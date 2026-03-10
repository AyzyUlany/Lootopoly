// SPDX-License-Identifier: MIT
pragma solidity ^0.8.26;

import {VRFConsumerBaseV2Plus} from "@chainlink/contracts/src/v0.8/vrf/dev/VRFConsumerBaseV2Plus.sol";
import {VRFV2PlusClient} from "@chainlink/contracts/src/v0.8/vrf/dev/libraries/VRFV2PlusClient.sol";

/**
 * ██╗      ██████╗  ██████╗ ████████╗ ██████╗ ██████╗  ██████╗ ██╗  ██╗   ██╗
 * ██║     ██╔═══██╗██╔═══██╗╚══██╔══╝██╔═══██╗██╔══██╗██╔═══██╗██║  ╚██╗ ██╔╝
 * ██║     ██║   ██║██║   ██║   ██║   ██║   ██║██████╔╝██║   ██║██║   ╚████╔╝
 * ██║     ██║   ██║██║   ██║   ██║   ██║   ██║██╔═══╝ ██║   ██║██║    ╚██╔╝
 * ███████╗╚██████╔╝╚██████╔╝   ██║   ╚██████╔╝██║     ╚██████╔╝███████╗██║
 * ╚══════╝ ╚═════╝  ╚═════╝    ╚═╝    ╚═════╝ ╚═╝      ╚═════╝ ╚══════╝╚═╝
 *
 * @title   Lootopoly v2.4 — VRF V2.5 Deterministic Sync
 * @notice  Chainlink VRF V2.5 seamlessly synchronized. When all players commit,
 *          the contract fetches a true random seed, emitting it to all clients.
 */

contract Lootopoly is VRFConsumerBaseV2Plus {

    // ═══════════════════════════════════════════════════════════════
    //  ERRORS
    // ═══════════════════════════════════════════════════════════════
    error Unauthorized();
    error PausedContract();
    error RoomDoesNotExist();
    error InvalidState();
    error InvalidFee();
    error InvalidPlayerCount();
    error AlreadyInRoom();
    error IncorrectBond();
    error RoomFull();
    error HostCannotJoin();
    error RoomExpired();
    error CommitWindowClosed();
    error CommitWindowOpen();
    error NotInRoom();
    error AlreadyCommitted();
    error IncorrectFee();
    error PlayerIsEliminated();
    error InvalidDiceRoll();
    error InvalidPosition();
    error MoveTimeoutNotReached();
    error NoAlivePlayer();
    error NothingToWithdraw();
    error TransferFailed();
    error RoomNotStale();
    error FallbackDisabled();

    // ═══════════════════════════════════════════════════════════════
    //  CONSTANTS & VRF VARIABLES
    // ═══════════════════════════════════════════════════════════════
    uint8   public constant MIN_PLAYERS      = 2;
    uint8   public constant MAX_PLAYERS      = 4;

    uint256 public constant PROTOCOL_FEE_BPS = 1000;
    uint256 public constant BASIS_POINTS     = 10_000;
    uint256 public constant BOND_BPS         = 1_000;

    uint256 public constant COMMIT_WINDOW    = 5 minutes;
    uint256 public constant ROOM_EXPIRY      = 24 hours;
    uint256 public constant MOVE_TIMEOUT     = 450; 

    uint256 public immutable subId; // V2.5 uses uint256
    bytes32 public immutable keyHash;

    // ═══════════════════════════════════════════════════════════════
    //  ENUMS & STRUCTS
    // ═══════════════════════════════════════════════════════════════
    enum RoomState { OPEN, COMMITTED, AWAITING_VRF, LIVE, VOIDED, FINISHED, CANCELLED }

    struct PlayerInfo {
        address wallet;
        bool    isAlive;
        bool    hasCommitted;
        uint8   position;
        uint8   health;
        uint256 joinBond;
    }

    struct Room {
        uint256   id;
        
        address   host;                  
        uint8     maxPlayers;            
        RoomState state;                 
        uint32    committedCount;        
        
        uint256   entryFee;
        address[] playerAddresses;
        
        address   winner;                
        uint64    createdAt;             

        uint256   prizePool;
        
        uint64    commitDeadline;        
        uint64    startedAt;             
        uint64    finishedAt;            
        uint64    lastMoveBlock;         

        uint256   vrfSeed;               // Determines all dice rolls in Unity
    }

    struct RoomBasicInfo {
        uint256   id;
        address   host;
        uint256   entryFee;
        uint8     maxPlayers;
        uint8     currentPlayers;
        uint8     state;
        uint256   prizePool;
        address   winner;
        uint64    commitDeadline;
        uint256   bondRequired;
    }

    // ═══════════════════════════════════════════════════════════════
    //  STATE
    // ═══════════════════════════════════════════════════════════════
    // Note: 'owner' is inherited from VRFConsumerBaseV2Plus automatically.
    bool    public paused;
    uint32  public nextRoomId;

    uint256 public totalFeesCollected;

    mapping(uint256 => Room)                           internal rooms;
    mapping(uint256 => mapping(address => PlayerInfo)) public players;

    mapping(address => uint256) public claimable;
    mapping(address => uint256) public playerCurrentRoom;
    mapping(address => bool)    public playerInRoom;
    mapping(address => uint256) public reputation;

    mapping(uint256 => uint256) public requestToRoom; // Maps VRF RequestID to RoomID

    uint256[] private _openRoomIds;
    mapping(uint256 => uint256) private _openRoomIndex; 
    mapping(uint256 => bool)    private _isOpenRoom;

    // ═══════════════════════════════════════════════════════════════
    //  EVENTS
    // ═══════════════════════════════════════════════════════════════
    event RoomCreated(uint256 indexed roomId, address indexed host, uint256 entryFee, uint8 maxPlayers);
    event PlayerJoined(uint256 indexed roomId, address indexed player, uint256 playerCount);
    event RosterLocked(uint256 indexed roomId, uint256 commitDeadline, uint256 playerCount);
    event PlayerCommitted(uint256 indexed roomId, address indexed player);
    event GameStarted(uint256 indexed roomId, address[] players, uint256 prizePool, uint256 vrfSeed);
    event PlayerMoved(uint256 indexed roomId, address indexed player, uint8 from, uint8 to, uint8 diceRoll);
    event PlayerEliminated(uint256 indexed roomId, address indexed player, string reason);
    event WinnerPaid(uint256 indexed roomId, address indexed winner, uint256 prize, uint256 fee);
    event RoomCancelled(uint256 indexed roomId, string reason);
    event RoomVoided(uint256 indexed roomId, string reason);
    event RefundQueued(uint256 indexed roomId, address indexed player, uint256 amount);
    event Withdrawal(address indexed to, uint256 amount);
    event ReputationUpdated(address indexed player, uint256 newScore);
    event Paused(address by);
    event Unpaused(address by);

    // ═══════════════════════════════════════════════════════════════
    //  MODIFIERS
    // ═══════════════════════════════════════════════════════════════
    // onlyOwner is implicitly provided by VRFConsumerBaseV2Plus

    modifier notPaused() {
        if (paused) revert PausedContract();
        _;
    }

    modifier roomExists(uint256 roomId) {
        if (roomId >= nextRoomId) revert RoomDoesNotExist();
        _;
    }

    modifier onlyHost(uint256 roomId) {
        if (msg.sender != rooms[roomId].host) revert Unauthorized();
        _;
    }

    modifier inState(uint256 roomId, RoomState expected) {
        if (rooms[roomId].state != expected) revert InvalidState();
        _;
    }

    // Fuji VRF V2.5 Config format
    constructor(address _vrfCoordinator, uint256 _subId, bytes32 _keyHash) VRFConsumerBaseV2Plus(_vrfCoordinator) {
        subId = _subId;
        keyHash = _keyHash;
    }

    // ═══════════════════════════════════════════════════════════════
    //  PHASE 1 — OPEN: CREATE & JOIN
    // ═══════════════════════════════════════════════════════════════
    function createRoom(uint256 entryFeeInWei, uint8 maxPlayers)
        external payable notPaused returns (uint256 roomId)
    {
        if (entryFeeInWei == 0) revert InvalidFee();
        if (maxPlayers < MIN_PLAYERS || maxPlayers > MAX_PLAYERS) revert InvalidPlayerCount();
        if (playerInRoom[msg.sender]) revert AlreadyInRoom();

        uint256 bond = _calcBond(entryFeeInWei);
        if (msg.value != bond) revert IncorrectBond();

        roomId = nextRoomId++;
        Room storage r = rooms[roomId];
        r.id          = roomId;
        r.host        = msg.sender;
        r.entryFee    = entryFeeInWei;
        r.maxPlayers  = maxPlayers;
        r.state       = RoomState.OPEN;
        r.createdAt   = uint64(block.timestamp);

        _openRoomIndex[roomId] = _openRoomIds.length;
        _openRoomIds.push(roomId);
        _isOpenRoom[roomId] = true;

        _initPlayer(roomId, msg.sender, bond);

        emit RoomCreated(roomId, msg.sender, entryFeeInWei, maxPlayers);
        emit PlayerJoined(roomId, msg.sender, 1);
    }

    function joinRoom(uint256 roomId) external payable notPaused roomExists(roomId) inState(roomId, RoomState.OPEN) {
        Room storage r = rooms[roomId];
        if (r.playerAddresses.length >= r.maxPlayers) revert RoomFull();
        if (playerInRoom[msg.sender]) revert AlreadyInRoom();
        if (msg.sender == r.host) revert HostCannotJoin();
        if (block.timestamp > r.createdAt + ROOM_EXPIRY) revert RoomExpired();

        uint256 bond = _calcBond(r.entryFee);
        if (msg.value != bond) revert IncorrectBond();

        _initPlayer(roomId, msg.sender, bond);

        emit PlayerJoined(roomId, msg.sender, r.playerAddresses.length);

        if (r.playerAddresses.length == r.maxPlayers) {
            _lockRoster(roomId);
        }
    }

    function _initPlayer(uint256 roomId, address player, uint256 bond) internal {
        rooms[roomId].playerAddresses.push(player);
        players[roomId][player] = PlayerInfo({
            wallet:       player,
            isAlive:      true,
            hasCommitted: false,
            position:     0,
            health:       100,
            joinBond:     bond
        });
        playerInRoom[player] = true;
        playerCurrentRoom[player] = roomId;

        if (reputation[player] == 0) reputation[player] = 50;
    }

    // ═══════════════════════════════════════════════════════════════
    //  PHASE 2 — COMMITTED: LOCK ROSTER & PAY ENTRY FEES
    // ═══════════════════════════════════════════════════════════════
    function lockRoster(uint256 roomId) external roomExists(roomId) onlyHost(roomId) inState(roomId, RoomState.OPEN) {
        if (rooms[roomId].playerAddresses.length < MIN_PLAYERS) revert InvalidPlayerCount();
        _lockRoster(roomId);
    }

    function _lockRoster(uint256 roomId) internal {
        Room storage r = rooms[roomId];
        r.state          = RoomState.COMMITTED;
        r.commitDeadline = uint64(block.timestamp + COMMIT_WINDOW);

        _removeFromOpenRooms(roomId);

        emit RosterLocked(roomId, r.commitDeadline, r.playerAddresses.length);
    }

    function commit(uint256 roomId) external payable notPaused roomExists(roomId) inState(roomId, RoomState.COMMITTED) {
        Room storage r = rooms[roomId];
        if (block.timestamp > r.commitDeadline) revert CommitWindowClosed();

        PlayerInfo storage p = players[roomId][msg.sender];
        if (p.wallet != msg.sender) revert NotInRoom();
        if (p.hasCommitted) revert AlreadyCommitted();
        if (msg.value != r.entryFee) revert IncorrectFee();

        p.hasCommitted  = true;
        r.prizePool    += msg.value;
        r.committedCount++;

        emit PlayerCommitted(roomId, msg.sender);

        if (r.committedCount == r.playerAddresses.length) {
            _requestGameVRF(roomId);
        }
    }

    function finaliseCommitWindow(uint256 roomId) external roomExists(roomId) inState(roomId, RoomState.COMMITTED) {
        Room storage r = rooms[roomId];
        if (block.timestamp <= r.commitDeadline) revert CommitWindowOpen();
        _voidRoom(roomId, "Commit window expired");
    }

    function _voidRoom(uint256 roomId, string memory reason) internal {
        Room storage r = rooms[roomId];
        r.state = RoomState.VOIDED;
        emit RoomVoided(roomId, reason);

        _collectForfeits(roomId);
        _queueVoidRefunds(roomId);
    }

    function _collectForfeits(uint256 roomId) internal {
        Room storage r = rooms[roomId];
        uint256 len = r.playerAddresses.length;
        for (uint256 i = 0; i < len; ++i) {
            address addr = r.playerAddresses[i];
            PlayerInfo storage p = players[roomId][addr];

            if (!p.hasCommitted) {
                totalFeesCollected += p.joinBond;
                p.joinBond = 0;
                _adjustReputation(addr, -10);
            }
        }
    }

    function _queueVoidRefunds(uint256 roomId) internal {
        Room storage r = rooms[roomId];
        uint256 len = r.playerAddresses.length;
        for (uint256 i = 0; i < len; ++i) {
            address addr = r.playerAddresses[i];
            PlayerInfo storage p = players[roomId][addr];

            if (p.hasCommitted) {
                uint256 refund = r.entryFee + p.joinBond;
                p.joinBond = 0;
                claimable[addr] += refund;
                emit RefundQueued(roomId, addr, refund);
                _adjustReputation(addr, 2);
            }
            playerInRoom[addr] = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  PHASE 3 — VRF / LIVE: IN-GAME ACTIONS
    // ═══════════════════════════════════════════════════════════════
    function _requestGameVRF(uint256 roomId) internal {
        Room storage r = rooms[roomId];
        r.state = RoomState.AWAITING_VRF;

        // Chainlink VRF V2.5 Request Syntax
        uint256 requestId = s_vrfCoordinator.requestRandomWords(
            VRFV2PlusClient.RandomWordsRequest({
                keyHash: keyHash,
                subId: subId,
                requestConfirmations: 3,
                callbackGasLimit: 200000,
                numWords: 1,
                extraArgs: VRFV2PlusClient._argsToBytes(
                    VRFV2PlusClient.ExtraArgsV1({nativePayment: false})
                )
            })
        );

        requestToRoom[requestId] = roomId;
    }

    function fulfillRandomWords(uint256 requestId, uint256[] calldata randomWords) internal override {
        uint256 roomId = requestToRoom[requestId];
        Room storage r = rooms[roomId];
        
        if (r.state != RoomState.AWAITING_VRF) return;

        r.vrfSeed       = randomWords[0];
        r.state         = RoomState.LIVE;
        r.startedAt     = uint64(block.timestamp);
        r.lastMoveBlock = uint64(block.number);

        uint256 len = r.playerAddresses.length;
        for (uint256 i = 0; i < len; ++i) {
            address addr = r.playerAddresses[i];
            PlayerInfo storage p = players[roomId][addr];

            if (p.joinBond > 0) {
                claimable[addr] += p.joinBond;
                p.joinBond = 0;
            }
        }

        emit GameStarted(roomId, r.playerAddresses, r.prizePool, r.vrfSeed);
    }

    function recordMove(uint256 roomId, uint8 diceRoll, uint8 newPos)
        external notPaused roomExists(roomId) inState(roomId, RoomState.LIVE)
    {
        PlayerInfo storage p = players[roomId][msg.sender];
        if (!p.isAlive) revert PlayerIsEliminated();
        if (!playerInRoom[msg.sender] || playerCurrentRoom[msg.sender] != roomId) revert NotInRoom();
        if (diceRoll < 1 || diceRoll > 12) revert InvalidDiceRoll();
        if (newPos > 35) revert InvalidPosition();

        uint8 oldPos    = p.position;
        p.position      = newPos;
        rooms[roomId].lastMoveBlock = uint64(block.number);

        emit PlayerMoved(roomId, msg.sender, oldPos, newPos, diceRoll);
    }

    function eliminatePlayer(uint256 roomId, address target, string calldata reason)
        external roomExists(roomId) onlyOwner inState(roomId, RoomState.LIVE)
    {
        PlayerInfo storage p = players[roomId][target];
        if (!p.isAlive) revert PlayerIsEliminated();

        p.isAlive = false;
        p.health  = 0;
        playerInRoom[target] = false;

        _adjustReputation(target, -5);
        emit PlayerEliminated(roomId, target, reason);

        if (_countAlive(roomId) == 1) {
            _payWinner(roomId, _findWinner(roomId));
        }
    }

    function declareWinner(uint256 roomId, address winner)
        external roomExists(roomId) onlyOwner inState(roomId, RoomState.LIVE)
    {
        if (!players[roomId][winner].isAlive) revert PlayerIsEliminated();
        _payWinner(roomId, winner);
    }

    function kickAFKPlayer(uint256 roomId, address target)
        external roomExists(roomId) inState(roomId, RoomState.LIVE)
    {
        Room storage r = rooms[roomId];
        if (block.number <= r.lastMoveBlock + MOVE_TIMEOUT) revert MoveTimeoutNotReached();

        PlayerInfo storage caller = players[roomId][msg.sender];
        if (!caller.isAlive) revert PlayerIsEliminated();

        PlayerInfo storage afk = players[roomId][target];
        if (!afk.isAlive) revert PlayerIsEliminated();
        if (target == msg.sender) revert InvalidState();

        afk.isAlive = false;
        afk.health  = 0;
        playerInRoom[target] = false;

        _adjustReputation(target, -5);
        emit PlayerEliminated(roomId, target, "AFK kick");

        if (_countAlive(roomId) == 1) {
            _payWinner(roomId, _findWinner(roomId));
        }

        r.lastMoveBlock = uint64(block.number);
    }

    // ═══════════════════════════════════════════════════════════════
    //  PAYOUT
    // ═══════════════════════════════════════════════════════════════
    function _payWinner(uint256 roomId, address winner) internal {
        Room storage r = rooms[roomId];
        r.state      = RoomState.FINISHED;
        r.winner     = winner;
        r.finishedAt = uint64(block.timestamp);

        uint256 pool = r.prizePool;

        uint256 protocolFee = (pool * PROTOCOL_FEE_BPS) / BASIS_POINTS;
        uint256 prize       = pool - protocolFee;

        totalFeesCollected += protocolFee;
        claimable[winner]  += prize;

        uint256 len = r.playerAddresses.length;
        for (uint256 i = 0; i < len; ++i) {
            playerInRoom[r.playerAddresses[i]] = false;
        }

        _adjustReputation(winner, 10);

        emit WinnerPaid(roomId, winner, prize, protocolFee);
    }

    // ═══════════════════════════════════════════════════════════════
    //  PULL PAYMENT — WITHDRAW
    // ═══════════════════════════════════════════════════════════════
    function withdraw() external notPaused {
        uint256 amount = claimable[msg.sender];
        if (amount == 0) revert NothingToWithdraw();

        claimable[msg.sender] = 0;

        (bool ok, ) = payable(msg.sender).call{value: amount}("");
        if (!ok) revert TransferFailed();

        emit Withdrawal(msg.sender, amount);
    }

    // ═══════════════════════════════════════════════════════════════
    //  CANCEL & REFUNDS
    // ═══════════════════════════════════════════════════════════════
    function cancelRoom(uint256 roomId) external roomExists(roomId) onlyHost(roomId) inState(roomId, RoomState.OPEN) {
        rooms[roomId].state = RoomState.CANCELLED;
        emit RoomCancelled(roomId, "Host cancelled");
        _removeFromOpenRooms(roomId);
        _refundAllBonds(roomId);
    }

    function cancelStaleRoom(uint256 roomId) external roomExists(roomId) inState(roomId, RoomState.OPEN) {
        Room storage r = rooms[roomId];
        if (block.timestamp <= r.createdAt + ROOM_EXPIRY) revert RoomNotStale();

        rooms[roomId].state = RoomState.CANCELLED;
        emit RoomCancelled(roomId, "Stale auto-cancel");
        _removeFromOpenRooms(roomId);

        uint256 len = r.playerAddresses.length;
        for (uint256 i = 0; i < len; ++i) {
            address addr = r.playerAddresses[i];
            PlayerInfo storage p = players[roomId][addr];

            uint256 refund = p.joinBond;
            p.joinBond = 0;
            if (refund > 0) {
                claimable[addr] += refund;
                emit RefundQueued(roomId, addr, refund);
            }
            playerInRoom[addr] = false;
        }
    }

    function _refundAllBonds(uint256 roomId) internal {
        Room storage r = rooms[roomId];
        uint256 len = r.playerAddresses.length;

        for (uint256 i = 0; i < len; ++i) {
            address addr = r.playerAddresses[i];
            PlayerInfo storage p = players[roomId][addr];
            uint256 refund = p.joinBond;
            p.joinBond = 0;
            if (refund > 0) {
                claimable[addr] += refund;
                emit RefundQueued(roomId, addr, refund);
            }
            playerInRoom[addr] = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  REPUTATION SYSTEM
    // ═══════════════════════════════════════════════════════════════
    function _adjustReputation(address player, int256 delta) internal {
        uint256 current = reputation[player];
        if (current == 0) current = 50;

        if (delta < 0) {
            uint256 sub = uint256(-delta);
            reputation[player] = current > sub ? current - sub : 0;
        } else {
            uint256 add = uint256(delta);
            uint256 next = current + add;
            reputation[player] = next > 100 ? 100 : next;
        }

        emit ReputationUpdated(player, reputation[player]);
    }

    // ═══════════════════════════════════════════════════════════════
    //  EMERGENCY CONTROLS
    // ═══════════════════════════════════════════════════════════════
    function pause() external onlyOwner {
        paused = true;
        emit Paused(msg.sender);
    }

    function unpause() external onlyOwner {
        paused = false;
        emit Unpaused(msg.sender);
    }

    function withdrawFees() external onlyOwner {
        uint256 amount = totalFeesCollected;
        if (amount == 0) revert NothingToWithdraw();
        totalFeesCollected = 0;
        (bool ok, ) = payable(owner()).call{value: amount}(""); // owner() provided by VRFConsumerBaseV2Plus
        if (!ok) revert TransferFailed();
    }

    // transferOwnership is inherited from ConfirmedOwner via VRFConsumerBaseV2Plus

    // ═══════════════════════════════════════════════════════════════
    //  VIEW FUNCTIONS
    // ═══════════════════════════════════════════════════════════════
    function getRoomPlayers(uint256 roomId) external view returns (address[] memory) {
        return rooms[roomId].playerAddresses;
    }

    function getPlayerInfo(uint256 roomId, address playerAddr) external view returns (PlayerInfo memory) {
        return players[roomId][playerAddr];
    }

    function getRoomFull(uint256 roomId) external view returns (Room memory) {
        return rooms[roomId];
    }

    function getRoomBasic(uint256 roomId) external view returns (RoomBasicInfo memory info) {
        Room storage r = rooms[roomId];

        info.id             = r.id;
        info.host           = r.host;
        info.entryFee       = r.entryFee;
        info.maxPlayers     = r.maxPlayers;
        info.currentPlayers = uint8(r.playerAddresses.length);
        info.state          = uint8(r.state);
        info.prizePool      = r.prizePool;
        info.winner         = r.winner;
        info.commitDeadline = r.commitDeadline;
        info.bondRequired   = _calcBond(r.entryFee);
    }

    function getOpenRooms() external view returns (uint256[] memory) {
        return _openRoomIds;
    }

    function getAliveCount(uint256 roomId) external view returns (uint256) {
        return _countAlive(roomId);
    }

    function totalRooms() external view returns (uint256) {
        return nextRoomId;
    }

    function getClaimable(address player) external view returns (uint256) {
        return claimable[player];
    }

    function getBondRequired(uint256 entryFee) external pure returns (uint256) {
        return _calcBond(entryFee);
    }

    // ═══════════════════════════════════════════════════════════════
    //  INTERNAL HELPERS
    // ═══════════════════════════════════════════════════════════════
    function _calcBond(uint256 entryFee) internal pure returns (uint256) {
        return (entryFee * BOND_BPS) / BASIS_POINTS;
    }

    function _countAlive(uint256 roomId) internal view returns (uint256 count) {
        Room storage r = rooms[roomId];
        uint256 len = r.playerAddresses.length;
        for (uint256 i = 0; i < len; ++i) {
            if (players[roomId][r.playerAddresses[i]].isAlive) count++;
        }
    }

    function _findWinner(uint256 roomId) internal view returns (address) {
        Room storage r = rooms[roomId];
        uint256 len = r.playerAddresses.length;
        for (uint256 i = 0; i < len; ++i) {
            if (players[roomId][r.playerAddresses[i]].isAlive) {
                return r.playerAddresses[i];
            }
        }
        revert NoAlivePlayer();
    }

    function _removeFromOpenRooms(uint256 roomId) internal {
        if (!_isOpenRoom[roomId]) return;

        uint256 idx  = _openRoomIndex[roomId];
        uint256 last = _openRoomIds[_openRoomIds.length - 1];

        _openRoomIds[idx]    = last;
        _openRoomIndex[last] = idx;
        _openRoomIds.pop();

        delete _openRoomIndex[roomId];
        delete _isOpenRoom[roomId];
    }

    // ═══════════════════════════════════════════════════════════════
    //  SAFETY: reject plain ETH sends
    // ═══════════════════════════════════════════════════════════════
    receive()  external payable { revert FallbackDisabled(); }
    fallback() external payable { revert FallbackDisabled(); }
}