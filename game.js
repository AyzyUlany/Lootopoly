// ═══════════════════════════════════════════════════════════
// LOOTOPOLY - game.js (v2.2)
// ═══════════════════════════════════════════════════════════
// Unity WebGL bridge + HTML lobby + in-game HUD
// Handles wallet connection, room management, and game state
// synchronization between Unity and the web interface.
// ═══════════════════════════════════════════════════════════

// ═══════════════════════════════════════════════════════════
// UNITY INSTANCE & LOADER
// ═══════════════════════════════════════════════════════════
let unityInstance = null;

// Send to GameUI (in-game buttons)
function unityCall(method, param) {
    if (unityInstance) {
        unityInstance.SendMessage('GameUI', method, param !== undefined ? String(param) : '');
    } else {
        console.warn('[Lootopoly] unityCall before ready:', method, param);
    }
}

// Send to any Unity object — used by lobby for Web3Manager callbacks
function SendMessage(obj, method, param) {
    if (unityInstance) {
        unityInstance.SendMessage(obj, method,
            typeof param === 'string' ? param : JSON.stringify(param));
    } else {
        console.warn('[Lootopoly] SendMessage before ready:', obj, method);
    }
}

(function loadUnity() {
    const buildName = 'BuildWeb';
    const buildUrl  = 'Build';
    const config = {
        dataUrl:            buildUrl + '/' + buildName + '.data',
        frameworkUrl:       buildUrl + '/' + buildName + '.framework.js',
        codeUrl:            buildUrl + '/' + buildName + '.wasm',
        streamingAssetsUrl: 'StreamingAssets',
        companyName:        'Florentopia',
        productName:        'Lootopoly',
        productVersion:     '0.1.0',
        matchWebGLToCanvasSize: true,
        devicePixelRatio: window.devicePixelRatio || 1
    };

    const canvas  = document.getElementById('unity-canvas');
    const bar     = document.getElementById('load-bar');
    const pct     = document.getElementById('load-pct');
    const errEl   = document.getElementById('load-err');
    const loading = document.getElementById('unity-loading-screen');

    if (!canvas) {
        console.error('[Lootopoly] Canvas element not found');
        return;
    }

    const script  = document.createElement('script');
    script.src    = buildUrl + '/' + buildName + '.loader.js';
    script.onload = () => {
        createUnityInstance(canvas, config, progress => {
            const p = Math.round(progress * 100);
            if (bar) bar.style.width = p + '%';
            if (pct) pct.textContent = p + '%';
        }).then(instance => {
            unityInstance = instance;
            if (loading) {
                loading.classList.add('fade');
                setTimeout(() => loading.classList.add('gone'), 520);
            }
            console.log('[Lootopoly] Unity instance loaded successfully');
        }).catch(msg => {
            if (errEl) {
                errEl.style.display = 'block';
                errEl.textContent   = 'Failed to load: ' + msg;
            }
            console.error('[Lootopoly] createUnityInstance error:', msg);
        });
    };
    script.onerror = () => {
        if (errEl) {
            errEl.style.display = 'block';
            errEl.textContent   = 'Could not load Unity build. Ensure the Build/ folder is deployed alongside this file.';
        }
    };
    document.body.appendChild(script);
})();

// ═══════════════════════════════════════════════════════════
// LOBBY STATE — all values come from the chain via callbacks
// ═══════════════════════════════════════════════════════════
const ROOM_STATES = ['OPEN', 'LOCKED', 'ACTIVE', 'CANCELLED', 'VOIDED'];

let S = {
    connected:   false,
    screen:      'connect',
    walletAddr:  null,
    roomId:      null,
    isHost:      false,
    maxPlayers:  3,
    fee:         0.1,
    entryFeeEth: '0',
    countTimer:  null,
    pendingJoin: null,   // { id, feeEth, bondEth }
    roomPlayers: [],
};

function showLobbyScreen(id) {
    ['connect', 'lobby', 'waiting'].forEach(s => {
        const el = document.getElementById('screen-' + s);
        if (el) el.classList.toggle('hidden', s !== id);
    });
    S.screen = id;
}

// ── Wallet ────────────────────────────────────────────────
function handleWalletBtn() {
    if (!S.connected) connectWallet();
}

function connectWallet() {
    const b = document.getElementById('btn-wallet');
    if (!b) return;
    b.innerHTML = '<span class="spinner"></span> Connecting…';
    b.disabled  = true;
    SendMessage('Web3Manager', 'ConnectWallet', '');
}

// Unity → HTML  { ok, data:{ address, balanceEth } }
function OnWalletConnected(json) {
    const b = document.getElementById('btn-wallet');
    if (!b) return;

    try {
        const d = JSON.parse(json);
        if (!d.ok) {
            b.innerHTML = 'Connect Wallet';
            b.disabled = false;
            toast('Wallet error: ' + (d.error || 'Unknown error'), 'error');
            return;
        }
        S.connected  = true;
        S.walletAddr = d.data.address;
        b.textContent    = S.walletAddr.slice(0, 6) + '…' + S.walletAddr.slice(-4);
        b.style.fontSize = '.85rem';
        b.classList.replace('btn-gold', 'btn-ghost');
        b.disabled = false;
        toast('Wallet connected! 🎲 Welcome, adventurer.', 'success');
        loadLobbyData();
    } catch(e) {
        b.innerHTML = 'Connect Wallet';
        b.disabled = false;
        toast('Wallet connection failed.', 'error');
        console.error('[Lootopoly] OnWalletConnected parse error:', e);
    }
}

// ── Lobby data ────────────────────────────────────────────
function loadLobbyData() {
    SendMessage('Web3Manager', 'LoadLobbyData', '');
}

// Unity → HTML
// { ok, data:{ balance, openRooms:[{roomId,host,entryFeeEth,maxPlayers,currentPlayers,
//              state,prizePoolEth,bondEth}], totalRooms, claimable, contractAddr } }
function OnLobbyDataLoaded(json) {
    try {
        const d = JSON.parse(json);
        if (!d.ok) {
            toast('Lobby load failed: ' + (d.error || 'Unknown error'), 'error');
            return;
        }
        const data = d.data;
        setText('stat-balance',   (data.balance || '0')    + ' AVAX');
        setText('stat-open',      String((data.openRooms || []).length));
        setText('stat-total',     String(data.totalRooms || 0));
        setText('stat-claimable', (data.claimable || '0')  + ' AVAX');

        if (data.contractAddr) {
            const el = document.getElementById('contract-addr-link');
            if (el) {
                el.textContent = data.contractAddr.slice(0, 6) + '…' + data.contractAddr.slice(-4);
                el.href = 'https://testnet.snowtrace.io/address/' + data.contractAddr;
            }
        }
        renderRooms(data.openRooms || []);
        showLobbyScreen('lobby');
    } catch(e) {
        toast('Lobby parse error.', 'error');
        console.error('[Lootopoly] OnLobbyDataLoaded error:', e);
    }
}

function renderRooms(rooms) {
    const l = document.getElementById('room-list');
    if (!l) return;
    l.innerHTML = '';

    if (!rooms || rooms.length === 0) {
        l.innerHTML = '<div style="text-align:center;padding:48px;color:var(--fog);font-style:italic;font-size:1.1rem">No open rooms yet. Be the first to create one!</div>';
        setText('rooms-badge', '0 open');
        return;
    }

    rooms.forEach(r => {
        const full  = r.currentPlayers >= r.maxPlayers;
        const mine  = S.walletAddr && r.host && r.host.toLowerCase() === S.walletAddr.toLowerCase();
        const fee   = parseFloat(r.entryFeeEth) || 0;
        const prize = parseFloat(r.prizePoolEth) || 0;
        const bond  = parseFloat(r.bondEth) || 0;
        const stateLabel = ROOM_STATES[r.state] || 'UNKNOWN';
        const pips  = Array.from({length: r.maxPlayers}, (_, i) =>
            `<div class="pip ${i < r.currentPlayers ? 'on' : ''}"></div>`).join('');

        const c = document.createElement('div');
        c.className = 'room-card' + (full ? ' full' : '') + (mine ? ' mine' : '');
        c.innerHTML = `
            <div>
                <div class="room-id-txt">ROOM #${r.roomId} · HOST: ${(r.host || '0x0').slice(0,6)}…${(r.host || '0x0').slice(-4)}
                    ${mine ? "<span class='badge badge-gold' style='margin-left:8px'>YOU</span>" : ''}
                </div>
                <div class="room-fee">${fee.toFixed(3)}<span class="room-fee-unit">AVAX</span></div>
                <div class="room-tags">
                    <span class="room-tag tag-prize">🏆 ~${(prize > 0 ? prize : fee * r.maxPlayers * 0.95).toFixed(3)} AVAX</span>
                    <span class="room-tag tag-bond">Bond: ${bond.toFixed(4)}</span>
                    <span class="room-tag ${full ? 'tag-full' : 'tag-open'}">${stateLabel}</span>
                    ${mine ? "<span class='room-tag tag-yours'>YOUR ROOM</span>" : ''}
                </div>
            </div>
            <div class="room-right">
                <div class="player-pips">${pips}
                    <span style="font-size:.85rem;color:var(--fog);margin-left:6px">${r.currentPlayers}/${r.maxPlayers}</span>
                </div>
                ${mine
            ? `<button class="btn btn-teal" style="padding:10px 18px;font-size:.85rem"
                       onclick="goToWaiting('${r.roomId}',true,'${r.entryFeeEth}',${r.maxPlayers})">View →</button>`
            : (full || r.state !== 0)
                ? `<button class="btn btn-ghost" style="padding:10px 18px;font-size:.85rem" disabled>${full ? 'FULL' : stateLabel}</button>`
                : `<button class="btn btn-teal" style="padding:10px 18px;font-size:.85rem"
                         onclick="openJoinModal('${r.roomId}','${r.entryFeeEth}','${r.bondEth}')">JOIN →</button>`
        }
            </div>`;
        l.appendChild(c);
    });

    setText('rooms-badge',
        rooms.filter(r => r.currentPlayers < r.maxPlayers && r.state === 0).length + ' open');
}

function refreshRooms() {
    toast('Refreshing…', 'info');
    loadLobbyData();
}

function softRefresh() {
    if (!S.connected) {
        toast('Connect wallet first.', 'warning');
        return;
    }
    loadLobbyData();
}

// ── Create room ───────────────────────────────────────────
function createRoom() {
    const feeInput = document.getElementById('input-fee');
    const fee = feeInput ? parseFloat(feeInput.value) || 0 : 0;
    if (!fee || fee <= 0) {
        toast('Enter a valid entry fee.', 'warning');
        return;
    }
    const b = document.getElementById('btn-create');
    if (b) {
        b.innerHTML = '<span class="spinner"></span>Creating…';
        b.disabled = true;
    }
    S.fee = fee;
    S.entryFeeEth = fee.toString();
    SendMessage('Web3Manager', 'CreateRoom',
        JSON.stringify({ entryFeeEth: S.entryFeeEth, maxPlayers: S.maxPlayers }));
}

// Unity → HTML  { ok, data:{ roomId } }
function OnRoomCreated(json) {
    const b = document.getElementById('btn-create');
    if (b) {
        b.innerHTML = '🎲 Create Room';
        b.disabled = false;
    }
    try {
        const d = JSON.parse(json);
        if (!d.ok) {
            toast('Create room failed: ' + (d.error || 'Unknown error'), 'error');
            return;
        }
        S.isHost = true;
        toast('Room #' + d.data.roomId + ' created! Bond paid. 🎲', 'success');
        goToWaiting(d.data.roomId, true, S.entryFeeEth, S.maxPlayers);
    } catch(e) {
        toast('Create room response error.', 'error');
        console.error('[Lootopoly] OnRoomCreated error:', e);
    }
}

// ── Join room ─────────────────────────────────────────────
function openJoinModal(roomId, entryFeeEth, bondEth) {
    if (!S.connected) {
        toast('Connect wallet first.', 'warning');
        return;
    }
    S.pendingJoin = { id: roomId, feeEth: entryFeeEth, bondEth };

    const titleEl = document.getElementById('modal-join-title');
    const bondEl = document.getElementById('modal-join-bond');
    const feeEl = document.getElementById('modal-join-fee');

    if (titleEl) titleEl.textContent = 'Join Room #' + roomId;
    if (bondEl) bondEl.textContent = parseFloat(bondEth || 0).toFixed(4) + ' AVAX';
    if (feeEl) feeEl.textContent = (parseFloat(entryFeeEth || 0) - parseFloat(bondEth || 0)).toFixed(4) + ' AVAX';

    openModal('modal-join');
}

function confirmJoin() {
    if (!S.pendingJoin) return;
    const { id, feeEth } = S.pendingJoin;
    closeModal('modal-join');
    toast('Sending join bond…', 'info');
    S.fee = parseFloat(feeEth) || 0;
    S.entryFeeEth = feeEth;
    SendMessage('Web3Manager', 'JoinRoom', String(id));
}

// Unity → HTML  { ok, data:{ roomId } }
function OnRoomJoined(json) {
    try {
        const d = JSON.parse(json);
        if (!d.ok) {
            toast('Join failed: ' + (d.error || 'Unknown error'), 'error');
            return;
        }
        S.isHost = false;
        toast('Joined Room #' + d.data.roomId + '! 🎉', 'success');
        goToWaiting(d.data.roomId, false, S.entryFeeEth, S.maxPlayers);
    } catch(e) {
        toast('Join response error.', 'error');
        console.error('[Lootopoly] OnRoomJoined error:', e);
    }
}

// ── Waiting room ──────────────────────────────────────────
function goToWaiting(roomId, isHost, entryFeeEth, maxPlayers) {
    S.roomId = roomId;
    S.isHost = isHost;
    S.entryFeeEth = entryFeeEth || S.entryFeeEth;
    S.maxPlayers  = maxPlayers  || S.maxPlayers;
    S.fee = parseFloat(S.entryFeeEth) || S.fee;

    setText('w-room-id',          '#' + roomId);
    setText('big-room-id',        '#' + roomId);
    setText('w-prize',            '… AVAX');
    setText('w-players',          '…/' + S.maxPlayers);
    setText('room-state-label',   'WAITING');
    setText('commit-fee-display', S.fee.toFixed(4) + ' AVAX');

    const lockBtn = document.getElementById('btn-lock-roster');
    const cancelBtn = document.getElementById('btn-cancel-room');
    const declareBtn = document.getElementById('btn-declare-winner');
    const commitPanel = document.getElementById('commit-panel');
    const idlePanel = document.getElementById('waiting-idle-panel');

    if (lockBtn) lockBtn.style.display    = isHost ? '' : 'none';
    if (cancelBtn) cancelBtn.style.display    = isHost ? '' : 'none';
    if (declareBtn) declareBtn.style.display = 'none';
    if (commitPanel) commitPanel.style.display       = 'none';
    if (idlePanel) idlePanel.style.display = '';

    S.roomPlayers = [];
    renderSlots([]);
    showLobbyScreen('waiting');
    SendMessage('Web3Manager', 'PollRoomState', String(roomId));
}

// Unity → HTML
// { ok, data:{ roomId, state, currentPlayers, maxPlayers, prizePoolEth,
//              commitDeadlineUnix, winner, players:[] } }
function OnRoomStateUpdated(json) {
    try {
        const d = JSON.parse(json);
        if (!d.ok) return;
        const r = d.data;
        if (String(r.roomId) !== String(S.roomId)) return;

        setText('w-room-id',          '#' + r.roomId);
        setText('w-prize',            parseFloat(r.prizePoolEth || 0).toFixed(3) + ' AVAX');
        setText('w-players',          (r.currentPlayers || 0) + '/' + (r.maxPlayers || S.maxPlayers));
        setText('room-state-label',   ROOM_STATES[r.state] || String(r.state));
        setText('commit-fee-display', S.fee.toFixed(4) + ' AVAX');
        S.maxPlayers = r.maxPlayers || S.maxPlayers;

        if (r.state === 1) { // Locked
            const commitPanel = document.getElementById('commit-panel');
            const idlePanel = document.getElementById('waiting-idle-panel');
            const lockBtn = document.getElementById('btn-lock-roster');

            if (commitPanel) commitPanel.style.display       = '';
            if (idlePanel) idlePanel.style.display = 'none';
            if (lockBtn) lockBtn.style.display    = 'none';
            if (r.commitDeadlineUnix > 0) startCountdownFromUnix(r.commitDeadlineUnix);
        }
        if (r.state === 2) { // Active — game live
            if (S.countTimer) clearInterval(S.countTimer);
            startGame(r.roomId);
        }
        if (r.players && r.players.length) {
            S.roomPlayers = r.players;
            renderSlots(r.players);
        }
    } catch(e) {
        console.error('[Lootopoly] OnRoomStateUpdated:', e);
    }
}

// Unity → HTML: live contract events from Web3Manager
function OnContractEvent(json) {
    try {
        const d = JSON.parse(json);
        const ev = d.event;
        const myRoom = String(S.roomId);

        if (ev === 'PlayerJoined' && String(d.roomId) === myRoom) {
            setText('w-players', d.playerCount + '/' + S.maxPlayers);
            toast('A new player joined Room #' + d.roomId + '!', 'info');
            SendMessage('Web3Manager', 'PollRoomState', String(d.roomId));
        }
        if (ev === 'RosterLocked' && String(d.roomId) === myRoom) {
            setText('room-state-label', 'LOCKED');
            const commitPanel = document.getElementById('commit-panel');
            const idlePanel = document.getElementById('waiting-idle-panel');
            const lockBtn = document.getElementById('btn-lock-roster');

            if (commitPanel) commitPanel.style.display       = '';
            if (idlePanel) idlePanel.style.display = 'none';
            if (lockBtn) lockBtn.style.display    = 'none';
            setText('commit-fee-display', S.fee.toFixed(4) + ' AVAX');
            const dl = parseInt(d.commitDeadlineUnix);
            if (dl > 0) startCountdownFromUnix(dl);
            toast('🔒 Roster locked! 5-minute commit window open.', 'warning');
        }
        if (ev === 'PlayerCommitted' && String(d.roomId) === myRoom) {
            toast('A player committed their entry fee.', 'info');
            SendMessage('Web3Manager', 'PollRoomState', String(d.roomId));
        }
        if (ev === 'GameStarted' && String(d.roomId) === myRoom) {
            if (S.countTimer) clearInterval(S.countTimer);
            startGame(d.roomId);
        }
        if ((ev === 'RoomCancelled' || ev === 'RoomVoided') && String(d.roomId) === myRoom) {
            if (S.countTimer) clearInterval(S.countTimer);
            toast('Room #' + d.roomId + ' ' +
                (ev === 'RoomCancelled' ? 'cancelled' : 'voided') + '. Bonds refunded.', 'error');

            // Ensure lobby is visible again
            const lobbyOverlay = document.getElementById('lobby-overlay');
            if (lobbyOverlay) lobbyOverlay.classList.remove('hidden');

            showLobbyScreen('lobby');
            loadLobbyData();
        }
        if (ev === 'WinnerPaid') toast('🏆 Winner paid out ' + d.prizeEth + ' AVAX!', 'success');
        if (ev === 'Withdrawal') {
            setText('stat-claimable', '0.0000 AVAX');
            toast('💸 Withdrew ' + d.amountEth + ' AVAX!', 'success');
        }
    } catch(e) {
        console.error('[Lootopoly] OnContractEvent:', e);
    }
}

function renderSlots(players) {
    const c = document.getElementById('player-slots');
    if (!c) return;
    c.innerHTML = '';

    const avatars = ['🧙', '⚔️', '🛡️', '🏹'];
    const total = Math.max(S.maxPlayers, players.length);

    for (let i = 0; i < total; i++) {
        const p    = players[i];
        const addr = p ? (p.wallet || p) : null;
        const mine = addr && S.walletAddr && addr.toLowerCase() === S.walletAddr.toLowerCase();
        const committed = p && p.hasCommitted;
        const displayAddr = addr ? (addr.slice(0, 6) + '…' + addr.slice(-4)) : 'Waiting for player…';
        const badge = mine
            ? `<span class="slot-status status-you">YOU</span>`
            : committed ? `<span class="slot-status status-committed">COMMITTED</span>`
                : addr      ? `<span class="slot-status status-joined">JOINED</span>`
                    :             `<span class="slot-status status-waiting">WAITING</span>`;
        const s = document.createElement('div');
        s.className = 'player-slot' + (mine ? ' you' : addr ? ' joined' : ' empty');
        s.innerHTML = `
            <div class="slot-avatar ${mine ? 'mine' : addr ? 'active' : ''}">${addr ? avatars[i % 4] : '·'}</div>
            <div class="slot-info">
                <div class="slot-addr">${displayAddr}</div>
                <div class="slot-sub">${addr && !mine ? (committed ? 'Fee paid' : 'Bond paid') : addr ? 'You · Bond paid' : 'Empty slot'}</div>
            </div>${badge}`;
        c.appendChild(s);
    }
}

// ── Lock roster ───────────────────────────────────────────
function lockRoster() {
    const b = document.getElementById('btn-lock-roster');
    if (b) {
        b.innerHTML = '<span class="spinner"></span>Locking…';
        b.disabled = true;
    }
    SendMessage('Web3Manager', 'LockRoster', String(S.roomId));
}

// Unity → HTML  { ok }
function OnRosterLocked(json) {
    const lockBtn = document.getElementById('btn-lock-roster');
    if (lockBtn) lockBtn.style.display = 'none';
    // RosterLocked contract event via OnContractEvent does the full UI update
}

// ── Commit ────────────────────────────────────────────────
function commitFee() {
    const b = document.getElementById('btn-commit');
    if (b) {
        b.innerHTML = '<span class="spinner"></span>Confirming…';
        b.disabled = true;
    }
    SendMessage('Web3Manager', 'CommitEntryFee', String(S.roomId));
}

// Unity → HTML  { ok }
function OnEntryFeeCommitted(json) {
    const b = document.getElementById('btn-commit');
    if (b) {
        b.innerHTML = '✅ Pay Entry Fee & Commit';
        b.disabled = false;
    }
    try {
        const d = JSON.parse(json);
        if (!d.ok) {
            toast('Commit failed: ' + (d.error || 'Unknown error'), 'error');
            return;
        }
        toast('✅ Committed! Waiting for all players…', 'success');
    } catch(e) {
        toast('Commit response error.', 'error');
    }
}

// ── Cancel ────────────────────────────────────────────────
function cancelRoom() {
    if (!confirm('Cancel this room? All bonds will be refunded.')) return;
    toast('Cancelling room…', 'info');
    SendMessage('Web3Manager', 'CancelRoom', String(S.roomId));
}

// Unity → HTML  { ok }
function OnRoomCancelled(json) {
    try {
        const d = JSON.parse(json);
        if (!d.ok) {
            toast('Cancel failed: ' + (d.error || 'Unknown error'), 'error');
            return;
        }
        if (S.countTimer) clearInterval(S.countTimer);
        toast('Room cancelled. Bonds refunded. 💸', 'success');
        showLobbyScreen('lobby');
        loadLobbyData();
    } catch(e) {
        toast('Cancel response error.', 'error');
    }
}

// ── Declare winner ────────────────────────────────────────
function submitDeclareWinner() {
    const input = document.getElementById('winner-addr-input');
    const v = input ? input.value.trim() : '';
    if (!v || !v.startsWith('0x')) {
        toast('Enter a valid 0x address.', 'warning');
        return;
    }
    closeModal('modal-declare');
    toast('Declaring winner on-chain…', 'info');
    SendMessage('Web3Manager', 'DeclareWinner',
        JSON.stringify({ roomId: String(S.roomId), winner: v }));
}

// Unity → HTML  { ok }
function OnWinnerDeclared(json) {
    try {
        const d = JSON.parse(json);
        if (!d.ok) {
            toast('Declare winner failed: ' + (d.error || 'Unknown error'), 'error');
            return;
        }
        toast('🏆 Winner declared! Prize distributed.', 'success');
        showLobbyScreen('lobby');
        loadLobbyData();
    } catch(e) {
        toast('Winner response error.', 'error');
    }
}

// ── Withdraw ──────────────────────────────────────────────
function claimWithdraw() {
    toast('Withdrawing…', 'info');
    SendMessage('Web3Manager', 'Withdraw', '');
}

// Unity → HTML  { ok, data:{ amountEth } }
function OnWithdrawComplete(json) {
    try {
        const d = JSON.parse(json);
        if (!d.ok) {
            toast('Withdraw failed: ' + (d.error || 'Unknown error'), 'error');
            return;
        }
        setText('stat-claimable', '0.0000 AVAX');
        toast('💸 Withdrew ' + d.data.amountEth + ' AVAX!', 'success');
    } catch(e) {
        toast('Withdraw response error.', 'error');
    }
}

// ── Countdown (on-chain unix deadline) ────────────────────
function startCountdownFromUnix(deadlineUnix) {
    if (S.countTimer) clearInterval(S.countTimer);
    S.countTimer = setInterval(() => {
        const left = deadlineUnix - Math.floor(Date.now() / 1000);
        const el   = document.getElementById('commit-countdown');
        if (!el) { clearInterval(S.countTimer); return; }
        if (left <= 0) {
            clearInterval(S.countTimer);
            el.textContent = 'EXPIRED';
            el.classList.add('urgent');
            toast('⚠️ Commit window expired!', 'error');
            return;
        }
        el.textContent = Math.floor(left / 60) + ':' + String(left % 60).padStart(2, '0');
        el.classList.toggle('urgent', left < 60);
    }, 1000);
}

// ── Fee / prize preview ───────────────────────────────────
function setFee(v, el) {
    const feeInput = document.getElementById('input-fee');
    if (feeInput) feeInput.value = v;
    S.fee = v;
    document.querySelectorAll('.preset-btn').forEach(b => b.classList.remove('active'));
    if (el) el.classList.add('active');
    updatePrizePreview();
}

function setPlayers(n, el) {
    S.maxPlayers = n;
    document.querySelectorAll('.player-opt-btn').forEach(b => b.classList.remove('active'));
    if (el) el.classList.add('active');
    updatePrizePreview();
}

function updatePrizePreview() {
    const feeInput = document.getElementById('input-fee');
    const fee = feeInput ? parseFloat(feeInput.value) || 0 : 0;
    const el  = document.getElementById('prize-preview');
    if (el) el.textContent = (fee * S.maxPlayers * 0.95).toFixed(3) + ' AVAX';
}

// ── Game start ────────────────────────────────────────────
function startGame(roomId) {
    if (roomId) S.roomId = roomId;
    const lobbyOverlay = document.getElementById('lobby-overlay');
    if (lobbyOverlay) lobbyOverlay.classList.add('hidden');
    setText('room-badge', 'ROOM #' + S.roomId + ' · LIVE');
    logEvent('🎮 <b>Game started!</b> Room #' + S.roomId);
    toast('🎮 Game is LIVE!', 'success');
}

// ── Game End / Return to Lobby ────────────────────────────
function returnToLobby() {
    // Tell Unity to reset the scene
    unityCall('UI_RestartGame');

    // Hide in-game panels cleanly
    hideAllPanels();
    document.getElementById('turn-banner').classList.add('hidden');

    // Show Lobby Overlay
    const lobbyOverlay = document.getElementById('lobby-overlay');
    if (lobbyOverlay) lobbyOverlay.classList.remove('hidden');

    // Reset to the main open rooms list
    showLobbyScreen('lobby');

    // Reset tracking vars
    S.roomId = null;
    S.isHost = false;
    setText('room-badge', '');

    // Load fresh data (so the user sees their new claimable prize!)
    loadLobbyData();
}

// ═══════════════════════════════════════════════════════════
// IN-GAME HUD  — driven by Unity GameUI → UpdateGameState(json)
// GameStateDTO: { players:[{name,hp,maxHp,gold,wanted,wpn,arm,bts,acc}],
//   activeIdx, state, turnName, combatName, combatHp, combatDef, combatFlavor,
//   hasCrit, tileInfo, equipTitle, equipNew, equipOld, shopInfo, canCraft,
//   winnerName, winnerGold, roll, log, toast, toastType, cards:[{name,desc}], isMyTurn:bool }
// state="" means HUD-only update (no panel switch).
// ═══════════════════════════════════════════════════════════
function UpdateGameState(json) {
    try {
        const d = JSON.parse(json);

        // 1. Player HUDs
        for (let i = 0; i < 4; i++) {
            const hud = document.getElementById('hud-' + i);
            if (!hud) continue;
            if (i < d.players.length) {
                const p = d.players[i];
                hud.classList.remove('hidden');
                hud.classList.toggle('active', i === d.activeIdx);
                hud.classList.toggle('dead',   p.hp <= 0);
                setText('hud-name-' + i, p.name);
                setText('hud-hp-'   + i, p.hp + '/' + p.maxHp);
                setText('hud-gold-' + i, p.gold + 'g');
                setText('hud-wpn-'  + i, p.wpn  || '—');
                setText('hud-arm-'  + i, p.arm  || '—');
                setText('hud-bts-'  + i, p.bts  || '—');
                setText('hud-acc-'  + i, p.acc  || '—');
                const bar = document.getElementById('hud-hpbar-' + i);
                if (bar) bar.style.width =
                    Math.max(0, Math.min(100, p.maxHp > 0 ? (p.hp / p.maxHp) * 100 : 0)) + '%';
                const wanted = document.getElementById('hud-wanted-' + i);
                if (wanted) wanted.classList.toggle('show', !!p.wanted);
            } else {
                hud.classList.add('hidden');
            }
        }

        // 2. Turn banner
        const tb = document.getElementById('turn-banner');
        if (d.turnName) {
            tb.textContent = '▶ ' + d.turnName + "'s turn";
            tb.classList.remove('hidden');
        } else {
            tb.classList.add('hidden');
        }

        // 3. Action panels (skip if state is empty — HUD-only push)
        if (d.state) {
            hideAllPanels();

            // OVERRIDE: If it's not my turn (and the game is not over),
            // show the 'Please Wait' panel to hide private active-player UI.
            if (!d.isMyTurn && d.state !== 'GameOver') {
                setText('wait-player-name', d.turnName + "'s Turn");
                showPanel('panel-wait');

                // Allow non-active players to see dice rolls in the log via OnDiceRolled, 
                // but not click the button.
            }
            else
            {
                // It IS my turn (or game over), route normally:
                switch (d.state) {
                    case 'RollPhase':
                        setText('roll-player-name', d.turnName + "'s turn");
                        setText('dice-label', '');
                        showPanel('panel-roll');
                        break;
                    case 'MoveDirectionChoice':
                        showPanel('panel-movedir');
                        break;
                    case 'ActionPhase':
                        setText('action-header', '⚡ ' + d.turnName + "'s Action Phase");
                        buildCards(d.cards || []);
                        showPanel('panel-action');
                        break;
                    case 'CombatRollPhase':
                        setText('combat-monster-name',   d.combatName   || 'Monster');
                        setText('combat-monster-stats',  '❤️ ' + d.combatHp + ' HP  |  🛡️ ' + d.combatDef + ' DEF');
                        setText('combat-monster-flavor', '"' + (d.combatFlavor || '') + '"');
                        toggle('combat-crit', 'hidden', !d.hasCrit);
                        const btnAttack = document.getElementById('btn-attack');
                        const btnFlee = document.getElementById('btn-flee');
                        const combatRolling = document.getElementById('combat-rolling');
                        if (btnAttack) btnAttack.classList.remove('hidden');
                        if (btnFlee) btnFlee.classList.remove('hidden');
                        if (combatRolling) combatRolling.classList.add('hidden');
                        showPanel('panel-combat');
                        break;
                    case 'BuyTileDecision':
                        setText('buy-info', d.tileInfo || '—');
                        showPanel('panel-buy');
                        break;
                    case 'UpgradeTileDecision':
                        showPanel('panel-upgrade');
                        break;
                    case 'TrapTileDecision':
                        showPanel('panel-trap');
                        break;
                    case 'EquipDecision':
                        setText('equip-title', d.equipTitle || 'New Item!');
                        setText('equip-new',   d.equipNew   || '—');
                        setText('equip-old',   d.equipOld   || '(empty)');
                        showPanel('panel-equip');
                        break;
                    case 'ShopPhase': {
                        setText('shop-item-info', d.shopInfo || 'Nothing in stock');
                        const cb = document.getElementById('btn-craft');
                        if (cb) cb.style.display = d.canCraft ? '' : 'none';
                        showPanel('panel-shop');
                        break;
                    }
                    case 'GameOver':
                        setText('winner-name', '🏆 ' + d.winnerName + ' wins!');
                        setText('winner-gold', d.winnerGold + 'g');
                        showPanel('panel-gameover');
                        break;
                }
            }
        }

        // 4. Dice label 
        // (Even if non-active, they might see dice result sent natively through OnDiceRolled)
        if (d.roll && d.isMyTurn) setText('dice-label', 'Rolled: ' + d.roll);

        // 5. Log & toast
        if (d.log)   logEvent(d.log);
        if (d.toast) toast(d.toast, d.toastType || 'info');

    } catch(e) {
        console.error('[Lootopoly] UpdateGameState error:', e, json);
    }
}

// Unity → HTML: dice roll resolved (for animation sync)
function OnDiceRolled(result) {
    setText('dice-label', 'Rolled: ' + result + '!');
    const btn = document.getElementById('btn-roll-move');
    if (btn) {
        btn.disabled = false;
        btn.classList.remove('hidden');
        btn.innerHTML = '🎲 Roll Dice';
    }
}

// Unity → HTML: single log line
function LogEvent(msg) {
    logEvent(msg);
}

// Unity → HTML: toast from C#  (json string: { msg, type })
function ShowToast(payload) {
    try   { const d = JSON.parse(payload); toast(d.msg || payload, d.type || 'info'); }
    catch { toast(payload, 'info'); }
}

function onAttackClick() {
    const btnAttack = document.getElementById('btn-attack');
    const btnFlee = document.getElementById('btn-flee');
    const combatRolling = document.getElementById('combat-rolling');

    if (btnAttack) btnAttack.classList.add('hidden');
    if (btnFlee) btnFlee.classList.add('hidden');
    if (combatRolling) combatRolling.classList.remove('hidden');
    unityCall('UI_RollCombatDice');
}

// ═══════════════════════════════════════════════════════════
// PANEL / DOM HELPERS
// ═══════════════════════════════════════════════════════════
const ALL_PANELS = [
    'panel-wait','panel-roll','panel-movedir','panel-action','panel-combat',
    'panel-buy','panel-upgrade','panel-trap','panel-equip',
    'panel-shop','panel-gameover'
];

function hideAllPanels() {
    ALL_PANELS.forEach(id => {
        const el = document.getElementById(id);
        if (el) el.classList.add('hidden');
    });
}

function showPanel(id) {
    const el = document.getElementById(id);
    if (el) el.classList.remove('hidden');
}

function toggle(id, cls, condition) {
    const el = document.getElementById(id);
    if (el) el.classList.toggle(cls, condition);
}

function setText(id, text) {
    const el = document.getElementById(id);
    if (el) el.textContent = text;
}

function buildCards(cards) {
    const c = document.getElementById('card-container');
    if (!c) return;
    c.innerHTML = '';

    if (!cards.length) {
        c.innerHTML = '<div class="no-cards">No cards in hand</div>';
        return;
    }

    cards.forEach((card, i) => {
        const el = document.createElement('div');
        el.className = 'card-btn';
        el.innerHTML = `<div class="card-title">${card.name}</div><div class="card-desc">${card.desc}</div>`;
        el.onclick = () => unityCall('UI_PlayCard_Index', String(i));
        c.appendChild(el);
    });
}

// ═══════════════════════════════════════════════════════════
// EVENT LOG
// ═══════════════════════════════════════════════════════════
let logCollapsed = false;

function logEvent(msg) {
    const b = document.getElementById('log-body');
    if (!b) return;
    const el = document.createElement('div');
    el.className = 'log-entry';
    el.innerHTML = msg;
    b.appendChild(el);
    b.scrollTop = b.scrollHeight;
}

function toggleLog() {
    logCollapsed = !logCollapsed;
    const logBody = document.getElementById('log-body');
    const logToggle = document.getElementById('log-toggle-icon');
    if (logBody) logBody.classList.toggle('collapsed', logCollapsed);
    if (logToggle) logToggle.textContent = logCollapsed ? '▶' : '▼';
}

// ═══════════════════════════════════════════════════════════
// TOAST
// ═══════════════════════════════════════════════════════════
function toast(msg, type = 'info') {
    const w = document.getElementById('toast-wrap');
    if (!w) return;

    const el = document.createElement('div');
    el.className = 'toast toast-' + type;
    el.textContent = msg;
    w.appendChild(el);

    setTimeout(() => {
        el.style.animation = 'toastOut .3s ease forwards';
        setTimeout(() => el.remove(), 300);
    }, 4000);
}

// ═══════════════════════════════════════════════════════════
// MODALS
// ═══════════════════════════════════════════════════════════
function openModal(id) {
    const el = document.getElementById(id);
    if (el) el.classList.add('open');
}

function closeModal(id) {
    const el = document.getElementById(id);
    if (el) el.classList.remove('open');
}

document.querySelectorAll('.modal-overlay').forEach(m =>
    m.addEventListener('click', e => {
        if (e.target === m) m.classList.remove('open');
    }));

// ═══════════════════════════════════════════════════════════
// INIT
// ═══════════════════════════════════════════════════════════
document.addEventListener('DOMContentLoaded', () => {
    updatePrizePreview();
    logEvent('⚔️ <b>Welcome to Lootopoly!</b> Connect your wallet to begin.');
});
