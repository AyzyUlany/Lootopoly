mergeInto(LibraryManager.library, {

    Bridge_Init: function(contractAddrPtr) {
        if (typeof window._loot === "undefined") {
            window._loot = {
                ethers: null, provider: null, signer: null, contract: null,
                walletAddr: null, contractAddr: null, abi: null, gmObject: "Web3Manager",
            };

            window._lootSend = function(method, payload) {
                var jsonStr = JSON.stringify(payload);
                if (typeof window.SendMessage === "function") {
                    window.SendMessage(window._loot.gmObject, method, jsonStr);
                } else if (typeof unityInstance !== "undefined" && unityInstance.SendMessage) {
                    unityInstance.SendMessage(window._loot.gmObject, method, jsonStr);
                }
                if (typeof window[method] === "function") {
                    window[method](jsonStr);
                }
            };

            window._lootStartEventListeners = function() {
                if (!window._loot.contract) return;
                var c = window._loot.contract;
                var e = window._loot.ethers;

                c.on("RoomCreated", function(roomId, host, fee) {
                    window._lootSend("OnContractEvent", { event: "RoomCreated", roomId: roomId.toString(), host: host, entryFeeEth: parseFloat(e.formatEther(fee)).toFixed(4) });
                });
                c.on("PlayerJoined", function(roomId, player, count) {
                    window._lootSend("OnContractEvent", { event: "PlayerJoined", roomId: roomId.toString(), player: player, playerCount: count.toString() });
                });
                c.on("RosterLocked", function(roomId, deadline) {
                    window._lootSend("OnContractEvent", { event: "RosterLocked", roomId: roomId.toString(), commitDeadlineUnix: deadline.toString() });
                });
                c.on("PlayerCommitted", function(roomId, player) {
                    window._lootSend("OnContractEvent", { event: "PlayerCommitted", roomId: roomId.toString(), player: player });
                });
                // UPDATED: Now captures the vrfSeed emitted by the VRF fulfillment
                c.on("GameStarted", function(roomId, players, prizePool, vrfSeed) {
                    window._lootSend("OnContractEvent", { event: "GameStarted", roomId: roomId.toString(), players: players, prizePoolEth: parseFloat(e.formatEther(prizePool)).toFixed(4), vrfSeed: vrfSeed.toString() });
                });
                c.on("WinnerPaid", function(roomId, winner, prize) {
                    window._lootSend("OnContractEvent", { event: "WinnerPaid", roomId: roomId.toString(), winner: winner, prizeEth: parseFloat(e.formatEther(prize)).toFixed(4) });
                });
                c.on("RoomCancelled", function(roomId, reason) {
                    window._lootSend("OnContractEvent", { event: "RoomCancelled", roomId: roomId.toString(), reason: reason });
                });
                c.on("RoomVoided", function(roomId, reason) {
                    window._lootSend("OnContractEvent", { event: "RoomVoided", roomId: roomId.toString(), reason: reason });
                });
                c.on("Withdrawal", function(to, amount) {
                    window._lootSend("OnContractEvent", { event: "Withdrawal", to: to, amountEth: parseFloat(e.formatEther(amount)).toFixed(4) });
                });
            };

            // UPDATED ABI: Included getRoomFull to access the VRF Seed for reconnecting players
            window._lootABI =[
                "function createRoom(uint256 entryFeeInWei, uint8 maxPlayers) payable returns (uint256)",
                "function joinRoom(uint256 roomId) payable",
                "function lockRoster(uint256 roomId)",
                "function commit(uint256 roomId) payable",
                "function finaliseCommitWindow(uint256 roomId)",
                "function cancelRoom(uint256 roomId)",
                "function cancelStaleRoom(uint256 roomId)",
                "function recordMove(uint256 roomId, uint8 diceRoll, uint8 newPos)",
                "function eliminatePlayer(uint256 roomId, address target, string reason)",
                "function declareWinner(uint256 roomId, address winner)",
                "function withdraw()",
                "function getClaimable(address player) view returns (uint256)",
                "function getRoomBasic(uint256 roomId) view returns (uint256,address,uint256,uint8,uint8,uint8,uint256,address,uint64,uint256)",
                "function getRoomFull(uint256 roomId) view returns (uint256,address,uint8,uint8,uint32,uint256,address[],address,uint64,uint256,uint64,uint64,uint64,uint64,uint256)",
                "function getOpenRooms() view returns (uint256[])",
                "function totalRooms() view returns (uint256)",
                "function paused() view returns (bool)"
            ];
        }

        window._loot.contractAddr = UTF8ToString(contractAddrPtr);
        window._loot.abi          = window._lootABI;

        if (window.ethers) {
            window._loot.ethers = window.ethers;
            window._lootSend("OnBridgeReady", { ok: true, data: { hasMetaMask: !!window.ethereum } });
            return;
        }

        var script = document.createElement("script");
        script.src = "https://cdnjs.cloudflare.com/ajax/libs/ethers/6.7.0/ethers.umd.min.js";
        script.onload = function() {
            window._loot.ethers = window.ethers;
            window._lootSend("OnBridgeReady", { ok: true, data: { hasMetaMask: !!window.ethereum } });
        };
        script.onerror = function() { window._lootSend("OnBridgeReady", { ok: false, error: "Failed to load ethers.js." }); };
        document.head.appendChild(script);
    },

    Bridge_ConnectWallet: function() {
        if (!window.ethereum) return window._lootSend("OnWalletConnected", { ok: false, error: "MetaMask not found." });
        var FUJI = { chainId: "0xA869", chainName: "Avalanche Fuji Testnet", rpcUrls:["https://api.avax-test.network/ext/bc/C/rpc"], nativeCurrency: { name: "AVAX", symbol: "AVAX", decimals: 18 } };

        window.ethereum.request({ method: "eth_requestAccounts" })
            .then(function() { return window.ethereum.request({ method: "wallet_switchEthereumChain", params: [{ chainId: FUJI.chainId }] }).catch(function(err) {
                if (err.code === 4902) return window.ethereum.request({ method: "wallet_addEthereumChain", params: [FUJI] }); throw err; }); })
            .then(function() {
                window._loot.provider = new window._loot.ethers.BrowserProvider(window.ethereum);
                return window._loot.provider.getSigner();
            }).then(function(sgn) {
            window._loot.signer = sgn;
            window._loot.contract = new window._loot.ethers.Contract(window._loot.contractAddr, window._loot.abi, window._loot.signer);
            return window._loot.signer.getAddress();
        }).then(function(addr) {
            window._loot.walletAddr = addr;
            return window._loot.provider.getBalance(addr);
        }).then(function(bal) {
            window._lootSend("OnWalletConnected", { ok: true, data: { address: window._loot.walletAddr, balanceEth: parseFloat(window._loot.ethers.formatEther(bal)).toFixed(4) } });
            window._lootStartEventListeners();
        }).catch(function(err) { window._lootSend("OnWalletConnected", { ok: false, error: err.message || err }); });
    },

    Bridge_LoadLobbyData: function() {
        if (!window._loot.contract) return window._lootSend("OnLobbyDataLoaded", { ok: false, error: "Not connected" });
        Promise.all([
            window._loot.provider.getBalance(window._loot.walletAddr),
            window._loot.contract.getOpenRooms(),
            window._loot.contract.totalRooms(),
            window._loot.contract.getClaimable(window._loot.walletAddr),
        ]).then(function(res) {
            var openIds = res[1].map(function(id) { return id.toString(); });
            return Promise.all(openIds.map(function(id) { return window._loot.contract.getRoomBasic(id); })).then(function(roomsData) {
                var rooms = roomsData.map(function(rd, i) {
                    return { roomId: openIds[i], host: rd[1], entryFeeEth: parseFloat(window._loot.ethers.formatEther(rd[2])).toFixed(4), maxPlayers: Number(rd[3]), currentPlayers: Number(rd[4]), state: Number(rd[5]), prizePoolEth: parseFloat(window._loot.ethers.formatEther(rd[6])).toFixed(4), bondEth: parseFloat(window._loot.ethers.formatEther(rd[9])).toFixed(4) };
                });
                window._lootSend("OnLobbyDataLoaded", { ok: true, data: { balance: parseFloat(window._loot.ethers.formatEther(res[0])).toFixed(4), openRooms: rooms, totalRooms: res[2].toString(), claimable: parseFloat(window._loot.ethers.formatEther(res[3])).toFixed(4) } });
            });
        }).catch(function(err) { window._lootSend("OnLobbyDataLoaded", { ok: false, error: err.message || err }); });
    },

    Bridge_CreateRoom: function(entryFeeEthPtr, maxPlayers) {
        var feeWei = window._loot.ethers.parseEther(UTF8ToString(entryFeeEthPtr));
        window._loot.contract.createRoom(feeWei, maxPlayers, { value: (feeWei * 1000n) / 10000n })
            .then(function(tx) { return tx.wait(); })
            .then(function(receipt) {
                var id = "unknown";
                for (var i = 0; i < receipt.logs.length; i++) {
                    try { var p = window._loot.contract.interface.parseLog(receipt.logs[i]); if (p && p.name === "RoomCreated") id = p.args[0].toString(); } catch(e) {}
                }
                window._lootSend("OnRoomCreated", { ok: true, data: { roomId: id } });
            }).catch(function(err) { window._lootSend("OnRoomCreated", { ok: false, error: err.message || err }); });
    },

    Bridge_JoinRoom: function(roomIdPtr) {
        var roomId = UTF8ToString(roomIdPtr);
        window._loot.contract.getRoomBasic(roomId)
            .then(function(rd) { return window._loot.contract.joinRoom(roomId, { value: rd[9] }); })
            .then(function(tx) { return tx.wait(); })
            .then(function() { window._lootSend("OnRoomJoined", { ok: true, data: { roomId: roomId } }); })
            .catch(function(err) { window._lootSend("OnRoomJoined", { ok: false, error: err.message || err }); });
    },

    Bridge_LockRoster: function(roomIdPtr) {
        var roomId = UTF8ToString(roomIdPtr);
        window._loot.contract.lockRoster(roomId).then(function(tx) { return tx.wait(); }).then(function() { window._lootSend("OnRosterLocked", { ok: true, data: { roomId: roomId } }); }).catch(function(err) { window._lootSend("OnRosterLocked", { ok: false, error: err.message || err }); });
    },

    Bridge_CommitEntryFee: function(roomIdPtr) {
        var roomId = UTF8ToString(roomIdPtr);
        window._loot.contract.getRoomBasic(roomId).then(function(rd) { return window._loot.contract.commit(roomId, { value: rd[2] }); })
            .then(function(tx) { return tx.wait(); }).then(function() { window._lootSend("OnEntryFeeCommitted", { ok: true, data: { roomId: roomId } }); }).catch(function(err) { window._lootSend("OnEntryFeeCommitted", { ok: false, error: err.message || err }); });
    },

    Bridge_RecordMove: function(roomIdPtr, diceRoll, newPos) {
        var roomId = UTF8ToString(roomIdPtr);
        window._loot.contract.recordMove(roomId, diceRoll, newPos).then(function(tx) { return tx.wait(); }).then(function() { window._lootSend("OnMoveRecorded", { ok: true, data: { roomId: roomId, diceRoll: diceRoll, newPos: newPos } }); }).catch(function(err) { window._lootSend("OnMoveRecorded", { ok: false, error: err.message || err }); });
    },

    Bridge_DeclareWinner: function(roomIdPtr, winnerAddrPtr) {
        var roomId = UTF8ToString(roomIdPtr), winner = UTF8ToString(winnerAddrPtr);
        window._loot.contract.declareWinner(roomId, winner).then(function(tx) { return tx.wait(); }).then(function() { window._lootSend("OnWinnerDeclared", { ok: true, data: { roomId: roomId, winner: winner } }); }).catch(function(err) { window._lootSend("OnWinnerDeclared", { ok: false, error: err.message || err }); });
    },

    Bridge_CancelRoom: function(roomIdPtr) {
        var roomId = UTF8ToString(roomIdPtr);
        window._loot.contract.cancelRoom(roomId).then(function(tx) { return tx.wait(); }).then(function() { window._lootSend("OnRoomCancelled", { ok: true, data: { roomId: roomId } }); }).catch(function(err) { window._lootSend("OnRoomCancelled", { ok: false, error: err.message || err }); });
    },

    Bridge_Withdraw: function() {
        window._loot.contract.getClaimable(window._loot.walletAddr).then(function(bal) {
            if (bal === 0n) throw new Error("Nothing to withdraw");
            return window._loot.contract.withdraw().then(function(tx) { return tx.wait().then(function() { return bal; }); });
        }).then(function(bal) { window._lootSend("OnWithdrawComplete", { ok: true, data: { amountEth: parseFloat(window._loot.ethers.formatEther(bal)).toFixed(4) } }); })
            .catch(function(err) { window._lootSend("OnWithdrawComplete", { ok: false, error: err.message || err }); });
    },

    Bridge_GetRoomState: function(roomIdPtr) {
        var roomId = UTF8ToString(roomIdPtr);
        // UPDATED: Now uses getRoomFull to retrieve the VRF seed for state polling/reconnections
        window._loot.contract.getRoomFull(roomId).then(function(rd) {
            window._lootSend("OnRoomStateUpdated", {
                ok: true,
                data: {
                    roomId: roomId,
                    state: Number(rd[3]),
                    currentPlayers: rd[6].length,
                    maxPlayers: Number(rd[2]),
                    prizePoolEth: parseFloat(window._loot.ethers.formatEther(rd[9])).toFixed(4),
                    commitDeadlineUnix: Number(rd[10]),
                    winner: rd[7],
                    players: rd[6],
                    vrfSeed: rd[14].toString() // Pass the seed down to C#
                }
            });
        }).catch(function(err) { window._lootSend("OnRoomStateUpdated", { ok: false, error: err.message || err }); });
    },

    // ======== C# -> HTML GAME UI WRAPPERS ========
    JS_UpdateGameState: function(jsonPtr) { if (typeof window.UpdateGameState === 'function') window.UpdateGameState(UTF8ToString(jsonPtr)); },
    JS_LogEvent: function(msgPtr) { if (typeof window.LogEvent === 'function') window.LogEvent(UTF8ToString(msgPtr)); },
    JS_ShowToast: function(payloadPtr) { if (typeof window.ShowToast === 'function') window.ShowToast(UTF8ToString(payloadPtr)); },
    JS_OnDiceRolled: function(result) { if (typeof window.OnDiceRolled === 'function') window.OnDiceRolled(result); },

    // ======== C# -> HTML MOCK DISPATCHER (DEV MODE) ========
    JS_DispatchMockEvent: function(methodPtr, jsonPtr) {
        var method = UTF8ToString(methodPtr);
        var json = UTF8ToString(jsonPtr);
        if (typeof window[method] === 'function') {
            window[method](json);
        } else {
            console.warn("[Lootopoly] Mock Dispatcher missed function: " + method);
        }
    }
});