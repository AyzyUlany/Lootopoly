using UnityEngine;
using System;
using System.Runtime.InteropServices;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

// ============================================================
// LOOTOPOLY – Web3Manager  (v5.2 — CHAINLINK VRF SYNC)
// ============================================================
// Captures the vrfSeed from the Chainlink fulfillment via the 
// JS Bridge and passes it to the GameManager to seed Unity's
// RNG deterministically across all clients.
// ============================================================

public class Web3Manager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────
    // SINGLETON
    // ─────────────────────────────────────────────────────────
    public static Web3Manager Instance { get; private set; }

    // ─────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────[Header("Contract")]
    public string contractAddress = "0xDc6819e56d1970c824a32d6d1Fd6C0fF1C643F5f";

    [Header("Debug")]
    public bool verboseLogging = true;

    // ─────────────────────────────────────────────────────────
    // PUBLIC STATE
    // ─────────────────────────────────────────────────────────
    public bool   IsConnected    { get; private set; }
    public string WalletAddress  { get; private set; }
    public string BalanceEth     { get; private set; }
    public string ClaimableEth   { get; private set; }
    public string ActiveRoomId   { get; private set; }
    public int    ActiveRoomState{ get; private set; } = -1;
    public bool   IsHost         { get; private set; }

    // ─────────────────────────────────────────────────────────
    // EVENTS
    // ─────────────────────────────────────────────────────────
    public static event Action<bool, string>            OnBridgeReadyEvt;
    public static event Action<string, string>          OnWalletConnectedEvt;   
    public static event Action<string>                  OnWalletErrorEvt;
    public static event Action<LobbyData>               OnLobbyDataLoadedEvt;
    public static event Action<string>                  OnRoomCreatedEvt;       
    public static event Action<string>                  OnRoomJoinedEvt;        
    public static event Action<string>                  OnRosterLockedEvt;      
    public static event Action<string>                  OnEntryFeeCommittedEvt; 
    // UPDATED: Now passes the VRF seed down to the game manager
    public static event Action<string, string[], string> OnGameStartedEvt;       
    public static event Action<string>                  OnMoveRecordedEvt;      
    public static event Action<string, string>          OnWinnerDeclaredEvt;    
    public static event Action<string>                  OnRoomCancelledEvt;     
    public static event Action<string>                  OnWithdrawCompleteEvt;  
    public static event Action<RoomStateData>           OnRoomStateUpdatedEvt;
    public static event Action<ContractEvent>           OnContractEventEvt;     
    public static event Action<string>                  OnChainChangedEvt;      
    public static event Action<string>                  OnTransactionErrorEvt;  

    // ─────────────────────────────────────────────────────────
    // DATA STRUCTURES
    // ─────────────────────────────────────────────────────────
    [Serializable]
    public class RoomInfo
    {
        public string roomId;
        public string host;
        public string entryFeeEth;
        public int    maxPlayers;
        public int    currentPlayers;
        public int    state;
        public string prizePoolEth;
        public string bondEth;
    }

    [Serializable]
    public class LobbyData
    {
        public string     balance;
        public RoomInfo[] openRooms;
        public string     totalRooms;
        public string     claimable;
    }

    [Serializable]
    public class RoomStateData
    {
        public string roomId;
        public int    state;
        public int    currentPlayers;
        public int    maxPlayers;
        public string prizePoolEth;
        public long   commitDeadlineUnix;
        public string winner;
        public string[] players;
        public string vrfSeed; // Added VRF Seed
    }

    [Serializable]
    public class ContractEvent
    {
        public string @event;
        public string roomId;
        public string host;
        public string player;
        public string entryFeeEth;
        public string playerCount;
        public string commitDeadlineUnix;
        public string prizePoolEth;
        public string[] players;
        public string winner;
        public string prizeEth;
        public string reason;
        public string to;
        public string amountEth;
        public string vrfSeed; // Added VRF Seed
    }

    // UPDATED: Matches the new Solidity Enum exactly
    public enum RoomState { Open = 0, Committed = 1, AwaitingVRF = 2, Live = 3, Voided = 4, Finished = 5, Cancelled = 6 }

    // ─────────────────────────────────────────────────────────
    // JS IMPORTS
    // ─────────────────────────────────────────────────────────
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void Bridge_Init(string contractAddr);
    [DllImport("__Internal")] static extern void Bridge_ConnectWallet();
    [DllImport("__Internal")] static extern void Bridge_LoadLobbyData();
    [DllImport("__Internal")] static extern void Bridge_CreateRoom(string entryFeeEth, int maxPlayers);[DllImport("__Internal")] static extern void Bridge_JoinRoom(string roomId);
    [DllImport("__Internal")] static extern void Bridge_LockRoster(string roomId);
    [DllImport("__Internal")] static extern void Bridge_CommitEntryFee(string roomId);
    [DllImport("__Internal")] static extern void Bridge_RecordMove(string roomId, int diceRoll, int newPos);
    [DllImport("__Internal")] static extern void Bridge_DeclareWinner(string roomId, string winnerAddr);
    [DllImport("__Internal")] static extern void Bridge_CancelRoom(string roomId);
    [DllImport("__Internal")] static extern void Bridge_Withdraw();
    [DllImport("__Internal")] static extern void Bridge_GetRoomState(string roomId);
    [DllImport("__Internal")] static extern void JS_DispatchMockEvent(string method, string json);
#else
    static void Bridge_Init(string a)                        => Debug.Log($"[Web3 STUB] Init({a})");
    static void Bridge_ConnectWallet()                       => Debug.Log("[Web3 STUB] ConnectWallet()");
    static void Bridge_LoadLobbyData()                       => Debug.Log("[Web3 STUB] LoadLobbyData()");
    static void Bridge_CreateRoom(string a, int b)           => Debug.Log($"[Web3 STUB] CreateRoom({a}, {b})");
    static void Bridge_JoinRoom(string a)                    => Debug.Log($"[Web3 STUB] JoinRoom({a})");
    static void Bridge_LockRoster(string a)                  => Debug.Log($"[Web3 STUB] LockRoster({a})");
    static void Bridge_CommitEntryFee(string a)              => Debug.Log($"[Web3 STUB] CommitEntryFee({a})");
    static void Bridge_RecordMove(string a, int b, int c)    => Debug.Log($"[Web3 STUB] RecordMove({a},{b},{c})");
    static void Bridge_DeclareWinner(string a, string b)     => Debug.Log($"[Web3 STUB] DeclareWinner({a},{b})");
    static void Bridge_CancelRoom(string a)                  => Debug.Log($"[Web3 STUB] CancelRoom({a})");
    static void Bridge_Withdraw()                            => Debug.Log("[Web3 STUB] Withdraw()");
    static void Bridge_GetRoomState(string a)                => Debug.Log($"[Web3 STUB] GetRoomState({a})");
    static void JS_DispatchMockEvent(string m, string j)     => Debug.Log($"[Web3 STUB] HTML JS Mock Triggered: {m}");
#endif

    // ─────────────────────────────────────────────────────────
    // DEV TEST SIMULATOR STATE
    // ─────────────────────────────────────────────────────────
    private bool IsDevTestMode() => GameManager.Instance != null && GameManager.Instance.fullDevTestMode;
    
    private string _mockRoomId = "777";
    private int _mockRoomState = 0;
    private int _mockMaxPlayers = 4;
    private List<string> _mockPlayers = new List<string>();
    private string _mockFee = "0.1";
    private string _mockVrfSeed = "834789127398127398127398127";
    private Coroutine _mockJoinCoroutine;

    private void DispatchMock(string method, string json) {
        if (verboseLogging) Debug.Log($"<color=magenta>[DEV MOCK]</color> -> {method}: {json}");
        
        #if UNITY_WEBGL && !UNITY_EDITOR
        JS_DispatchMockEvent(method, json); // Push to HTML UI
        #endif

        // Process locally in Unity to keep native state accurate
        switch(method) {
            case "OnBridgeReady": OnBridgeReady(json); break;
            case "OnWalletConnected": OnWalletConnected(json); break;
            case "OnLobbyDataLoaded": OnLobbyDataLoaded(json); break;
            case "OnRoomCreated": OnRoomCreated(json); break;
            case "OnRoomJoined": OnRoomJoined(json); break;
            case "OnRosterLocked": OnRosterLocked(json); break;
            case "OnEntryFeeCommitted": OnEntryFeeCommitted(json); break;
            case "OnRoomStateUpdated": OnRoomStateUpdated(json); break;
            case "OnContractEvent": OnContractEvent(json); break;
            case "OnWinnerDeclared": OnWinnerDeclared(json); break;
            case "OnRoomCancelled": OnRoomCancelled(json); break;
            case "OnWithdrawComplete": OnWithdrawComplete(json); break;
        }
    }

    // ─────────────────────────────────────────────────────────
    // LIFECYCLE
    // ─────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        gameObject.name = "Web3Manager";
        StartCoroutine(RoomStatePoller());
    }

    private void Start()
    {
        if (IsDevTestMode()) {
            DispatchMock("OnBridgeReady", "{\"ok\":true,\"data\":{\"hasMetaMask\":true}}");
            return;
        }
        Bridge_Init(contractAddress);
    }

    // ─────────────────────────────────────────────────────────
    // C# / NATIVE / UI INTERCEPTORS
    // ─────────────────────────────────────────────────────────
    public void ConnectWallet() {
        if (IsDevTestMode()) {
            string mockWallet = "0xDEV" + UnityEngine.Random.Range(1000, 9999) + "TEST";
            DispatchMock("OnWalletConnected", "{\"ok\":true,\"data\":{\"address\":\"" + mockWallet + "\",\"balanceEth\":\"100.0000\"}}");
            return;
        }
        Bridge_ConnectWallet();
    }

    public void LoadLobbyData() {
        if (IsDevTestMode()) {
            string mockLobby = "{\"ok\":true,\"data\":{\"balance\":\"100.0000\",\"openRooms\":[{\"roomId\":\"888\",\"host\":\"0xHOST_TEST_ADDR\",\"entryFeeEth\":\"0.5000\",\"maxPlayers\":4,\"currentPlayers\":2,\"state\":0,\"prizePoolEth\":\"1.9000\",\"bondEth\":\"0.0500\"}],\"totalRooms\":\"1\",\"claimable\":\"0.0000\"}}";
            DispatchMock("OnLobbyDataLoaded", mockLobby);
            return;
        }
        Bridge_LoadLobbyData();
    }

    [Serializable] public class CreateRoomPayload { public string entryFeeEth; public int maxPlayers; }
    
    public void CreateRoom(string json) {
        if (IsDevTestMode()) {
            var p = JsonUtility.FromJson<CreateRoomPayload>(json);
            MockCreateRoom(p.entryFeeEth, p.maxPlayers);
            return;
        }
        var p2 = JsonUtility.FromJson<CreateRoomPayload>(json);
        Bridge_CreateRoom(p2.entryFeeEth, p2.maxPlayers);
    }

    public void CreateRoom(string entryFeeEth, int maxPlayers) {
        if (IsDevTestMode()) { MockCreateRoom(entryFeeEth, maxPlayers); return; }
        Bridge_CreateRoom(entryFeeEth, maxPlayers);
    }

    private void MockCreateRoom(string fee, int max) {
        _mockRoomId = UnityEngine.Random.Range(100, 999).ToString();
        _mockFee = fee;
        _mockMaxPlayers = max;
        _mockRoomState = (int)RoomState.Open;
        _mockPlayers.Clear();
        _mockPlayers.Add(WalletAddress); 
        
        DispatchMock("OnRoomCreated", "{\"ok\":true,\"data\":{\"roomId\":\"" + _mockRoomId + "\"}}");
        
        if (_mockJoinCoroutine != null) StopCoroutine(_mockJoinCoroutine);
        _mockJoinCoroutine = StartCoroutine(MockPlayersJoining());
    }

    private IEnumerator MockPlayersJoining() {
        int needed = _mockMaxPlayers - 1;
        float delayPerPlayer = 8f / needed;

        for (int i = 0; i < needed; i++) {
            yield return new WaitForSeconds(delayPerPlayer);
            if (_mockRoomState != (int)RoomState.Open) break; 
            
            string mockPlayer = "0xBOT_" + (i+2) + "_ADDRESS";
            _mockPlayers.Add(mockPlayer);
            
            string eventJson = $"{{\"event\":\"PlayerJoined\",\"roomId\":\"{_mockRoomId}\",\"player\":\"{mockPlayer}\",\"playerCount\":\"{_mockPlayers.Count}\"}}";
            DispatchMock("OnContractEvent", eventJson);
        }
    }

    public void JoinRoomNative(string roomId) { 
        if (IsDevTestMode()) { MockJoinRoom(roomId); return; }
        Bridge_JoinRoom(roomId); 
    }
    public void JoinRoom(string roomId) { 
        if (IsDevTestMode()) { MockJoinRoom(roomId); return; }
        Bridge_JoinRoom(roomId); 
    }

    private void MockJoinRoom(string roomId) {
        _mockRoomId = roomId;
        _mockFee = "0.5";
        _mockMaxPlayers = 4;
        _mockRoomState = (int)RoomState.Open;
        _mockPlayers.Clear();
        _mockPlayers.Add("0xHOST_TEST_ADDR");
        _mockPlayers.Add("0xOTHER_PLAYER");
        _mockPlayers.Add(WalletAddress);

        DispatchMock("OnRoomJoined", "{\"ok\":true,\"data\":{\"roomId\":\"" + roomId + "\"}}");
        
        string eventJson = $"{{\"event\":\"PlayerJoined\",\"roomId\":\"{roomId}\",\"player\":\"{WalletAddress}\",\"playerCount\":\"{_mockPlayers.Count}\"}}";
        DispatchMock("OnContractEvent", eventJson);

        StartCoroutine(MockHostLockingAndStarting(roomId));
    }

    private IEnumerator MockHostLockingAndStarting(string roomId) {
        yield return new WaitForSeconds(3.5f);
        _mockRoomState = (int)RoomState.Committed;
        long deadline = ((System.DateTimeOffset)System.DateTime.UtcNow).ToUnixTimeSeconds() + 300;
        string eventJson = $"{{\"event\":\"RosterLocked\",\"roomId\":\"{roomId}\",\"commitDeadlineUnix\":\"{deadline}\"}}";
        DispatchMock("OnContractEvent", eventJson);
    }

    public void LockRosterNative() { 
        if (IsDevTestMode()) { MockLockRoster(_mockRoomId); return; }
        if (!string.IsNullOrEmpty(ActiveRoomId)) Bridge_LockRoster(ActiveRoomId); 
    }
    public void LockRoster(string roomId) { 
        if (IsDevTestMode()) { MockLockRoster(roomId); return; }
        Bridge_LockRoster(roomId); 
    }

    private void MockLockRoster(string roomId) {
        _mockRoomState = (int)RoomState.Committed;
        DispatchMock("OnRosterLocked", "{\"ok\":true,\"data\":{\"roomId\":\"" + roomId + "\"}}");
        
        long deadline = ((System.DateTimeOffset)System.DateTime.UtcNow).ToUnixTimeSeconds() + 300;
        string eventJson = $"{{\"event\":\"RosterLocked\",\"roomId\":\"{roomId}\",\"commitDeadlineUnix\":\"{deadline}\"}}";
        DispatchMock("OnContractEvent", eventJson);
    }

    public void CommitEntryFeeNative() { 
        if (IsDevTestMode()) { MockCommit(_mockRoomId); return; }
        if (!string.IsNullOrEmpty(ActiveRoomId)) Bridge_CommitEntryFee(ActiveRoomId); 
    }
    public void CommitEntryFee(string roomId) { 
        if (IsDevTestMode()) { MockCommit(roomId); return; }
        Bridge_CommitEntryFee(roomId); 
    }

    private void MockCommit(string roomId) {
        DispatchMock("OnEntryFeeCommitted", "{\"ok\":true,\"data\":{\"roomId\":\"" + roomId + "\"}}");
        
        string eventJson = $"{{\"event\":\"PlayerCommitted\",\"roomId\":\"{roomId}\",\"player\":\"{WalletAddress}\"}}";
        DispatchMock("OnContractEvent", eventJson);

        StartCoroutine(MockBotsCommitting(roomId));
    }

    private IEnumerator MockBotsCommitting(string roomId) {
        yield return new WaitForSeconds(1.5f);
        
        for(int i = 0; i < _mockPlayers.Count; i++) {
            if (_mockPlayers[i] == WalletAddress) continue;
            string eventJson = $"{{\"event\":\"PlayerCommitted\",\"roomId\":\"{roomId}\",\"player\":\"{_mockPlayers[i]}\"}}";
            DispatchMock("OnContractEvent", eventJson);
            yield return new WaitForSeconds(0.6f);
        }

        // Simulating the delay for VRF Fulfillment!
        _mockRoomState = (int)RoomState.AwaitingVRF; 
        
        Debug.Log("<color=cyan>[DEV MOCK]</color> All players committed! Awaiting Chainlink VRF Fulfillment...");
        yield return new WaitForSeconds(3.0f);

        _mockRoomState = (int)RoomState.Live; 
        
        float feeFloat = 0.1f;
        float.TryParse(_mockFee, NumberStyles.Float, CultureInfo.InvariantCulture, out feeFloat);
        float prize = feeFloat * _mockPlayers.Count * 0.95f;
        string prizeStr = prize.ToString("F3", CultureInfo.InvariantCulture);
        string playersArr = "[" + string.Join(",", _mockPlayers.ConvertAll(p => $"\"{p}\"")) + "]";
        
        string startEvent = $"{{\"event\":\"GameStarted\",\"roomId\":\"{roomId}\",\"prizePoolEth\":\"{prizeStr}\",\"players\":{playersArr},\"vrfSeed\":\"{_mockVrfSeed}\"}}";
        
        DispatchMock("OnContractEvent", startEvent);
    }

    public void PollRoomState(string roomId) {
        if (IsDevTestMode()) { MockPollState(roomId); return; }
        Bridge_GetRoomState(roomId);
    }
    public void RefreshRoomState() {
        if (IsDevTestMode()) { if (!string.IsNullOrEmpty(ActiveRoomId)) MockPollState(ActiveRoomId); return; }
        if (!string.IsNullOrEmpty(ActiveRoomId)) Bridge_GetRoomState(ActiveRoomId);
    }

    private void MockPollState(string roomId) {
        float feeFloat = 0.1f;
        float.TryParse(_mockFee, NumberStyles.Float, CultureInfo.InvariantCulture, out feeFloat);
        
        float prize = feeFloat * _mockPlayers.Count * 0.95f;
        string prizeStr = prize.ToString("F3", CultureInfo.InvariantCulture);

        long deadline = _mockRoomState == (int)RoomState.Committed ? ((System.DateTimeOffset)System.DateTime.UtcNow).ToUnixTimeSeconds() + 250 : 0;
        string playersArr = "[" + string.Join(",", _mockPlayers.ConvertAll(p => $"\"{p}\"")) + "]";
        
        string json = $@"{{
            ""ok"": true, ""data"": {{ ""roomId"": ""{roomId}"", ""state"": {_mockRoomState}, ""currentPlayers"": {_mockPlayers.Count},
                ""maxPlayers"": {_mockMaxPlayers}, ""prizePoolEth"": ""{prizeStr}"", ""commitDeadlineUnix"": {deadline},
                ""winner"": """", ""players"": {playersArr}, ""vrfSeed"": ""{_mockVrfSeed}"" }} }}";
        
        DispatchMock("OnRoomStateUpdated", json);
    }

    public void RecordMove(int diceRoll, int newPos) { 
        if (IsDevTestMode()) { DispatchMock("OnMoveRecorded", "{\"ok\":true,\"data\":{\"roomId\":\"" + _mockRoomId + "\"}}"); return; }
        if (!string.IsNullOrEmpty(ActiveRoomId)) Bridge_RecordMove(ActiveRoomId, diceRoll, newPos); 
    }

    [Serializable] public class DeclareWinnerPayload { public string roomId; public string winner; }
    
    public void DeclareWinnerNative(string winnerAddress) { 
        if (IsDevTestMode()) { DispatchMock("OnWinnerDeclared", "{\"ok\":true,\"data\":{\"roomId\":\"" + _mockRoomId + "\",\"winner\":\"" + winnerAddress + "\"}}"); return; }
        if (!string.IsNullOrEmpty(ActiveRoomId)) Bridge_DeclareWinner(ActiveRoomId, winnerAddress); 
    }
    
    public void DeclareWinner(string payload) {
        if (IsDevTestMode()) {
            string winner = payload.StartsWith("{") ? JsonUtility.FromJson<DeclareWinnerPayload>(payload).winner : payload;
            DispatchMock("OnWinnerDeclared", "{\"ok\":true,\"data\":{\"roomId\":\"" + _mockRoomId + "\",\"winner\":\"" + winner + "\"}}");
            return;
        }
        if (payload.StartsWith("{")) {
            var p = JsonUtility.FromJson<DeclareWinnerPayload>(payload);
            Bridge_DeclareWinner(p.roomId, p.winner);
        } else {
            if (string.IsNullOrEmpty(ActiveRoomId)) return;
            Bridge_DeclareWinner(ActiveRoomId, payload);
        }
    }

    public void CancelRoomNative() { 
        if (IsDevTestMode()) {
            DispatchMock("OnRoomCancelled", "{\"ok\":true,\"data\":{\"roomId\":\"" + _mockRoomId + "\"}}");
            DispatchMock("OnContractEvent", $"{{\"event\":\"RoomCancelled\",\"roomId\":\"{_mockRoomId}\",\"reason\":\"Host cancelled\"}}");
            return;
        }
        if (!string.IsNullOrEmpty(ActiveRoomId)) Bridge_CancelRoom(ActiveRoomId); 
    }
    public void CancelRoom(string roomId) { 
        if (IsDevTestMode()) { CancelRoomNative(); return; }
        Bridge_CancelRoom(roomId); 
    }

    public void Withdraw() {
        if (IsDevTestMode()) { DispatchMock("OnWithdrawComplete", "{\"ok\":true,\"data\":{\"amountEth\":\"0.0000\"}}"); return; }
        Bridge_Withdraw();
    }

    // ─────────────────────────────────────────────────────────
    // AUTO POLL
    // ─────────────────────────────────────────────────────────
    private IEnumerator RoomStatePoller()
    {
        while (true)
        {
            yield return new WaitForSeconds(5f);
            if (IsConnected && !string.IsNullOrEmpty(ActiveRoomId)) RefreshRoomState();
        }
    }

    // ─────────────────────────────────────────────────────────
    // CALLBACK RECEIVERS (called by JS bridge OR Mock Dispatcher)
    // ─────────────────────────────────────────────────────────
    [Serializable] class BridgeResult       { public bool ok; public string error; }
    [Serializable] class BridgeReady        { public bool ok; public BridgeReadyData data; public string error; }
    [Serializable] class BridgeReadyData    { public bool hasMetaMask; }[Serializable] class WalletResult       { public bool ok; public WalletData data; public string error; }[Serializable] class WalletData         { public string address; public string balanceEth; }
    [Serializable] class LobbyResult        { public bool ok; public LobbyData data; public string error; }
    [Serializable] class RoomCreatedResult  { public bool ok; public RoomCreatedData data; public string error; }
    [Serializable] class RoomCreatedData    { public string roomId; }
    [Serializable] class StringResult       { public bool ok; public StringData data; public string error; }
    [Serializable] class StringData         { public string roomId; public string amountEth; }
    [Serializable] class RoomStateResult    { public bool ok; public RoomStateData data; public string error; }
    [Serializable] class ChainChangedResult { public bool ok; public ChainChangedData data; }
    [Serializable] class ChainChangedData   { public string reason; }[Serializable] class WinnerResult       { public bool ok; public WinnerData data; public string error; }[Serializable] class WinnerData         { public string roomId; public string winner; }

    public void OnBridgeReady(string json)
    {
        Log("OnBridgeReady", json);
        var r = JsonUtility.FromJson<BridgeReady>(json);
        OnBridgeReadyEvt?.Invoke(r.ok, r.ok ? null : r.error);
    }

    public void OnWalletConnected(string json)
    {
        Log("OnWalletConnected", json);
        var r = JsonUtility.FromJson<WalletResult>(json);
        if (r.ok) { IsConnected = true; WalletAddress = r.data.address; BalanceEth = r.data.balanceEth; OnWalletConnectedEvt?.Invoke(r.data.address, r.data.balanceEth); }
        else OnWalletErrorEvt?.Invoke(r.error);
    }

    public void OnLobbyDataLoaded(string json)
    {
        Log("OnLobbyDataLoaded", json);
        var r = JsonUtility.FromJson<LobbyResult>(json);
        if (r.ok) { ClaimableEth = r.data.claimable; BalanceEth = r.data.balance; OnLobbyDataLoadedEvt?.Invoke(r.data); }
    }

    public void OnRoomCreated(string json)
    {
        Log("OnRoomCreated", json);
        var r = JsonUtility.FromJson<RoomCreatedResult>(json);
        if (r.ok) { ActiveRoomId = r.data.roomId; IsHost = true; OnRoomCreatedEvt?.Invoke(r.data.roomId); }
        else OnTransactionErrorEvt?.Invoke("Create room failed: " + r.error);
    }

    public void OnRoomJoined(string json)
    {
        Log("OnRoomJoined", json);
        var r = JsonUtility.FromJson<StringResult>(json);
        if (r.ok) { ActiveRoomId = r.data.roomId; IsHost = false; OnRoomJoinedEvt?.Invoke(r.data.roomId); }
        else OnTransactionErrorEvt?.Invoke("Join room failed: " + r.error);
    }

    public void OnRosterLocked(string json)
    {
        Log("OnRosterLocked", json);
        var r = JsonUtility.FromJson<StringResult>(json);
        if (r.ok) OnRosterLockedEvt?.Invoke(r.data.roomId);
        else OnTransactionErrorEvt?.Invoke("Lock roster failed: " + r.error);
    }

    public void OnEntryFeeCommitted(string json)
    {
        Log("OnEntryFeeCommitted", json);
        var r = JsonUtility.FromJson<StringResult>(json);
        if (r.ok) OnEntryFeeCommittedEvt?.Invoke(r.data.roomId);
        else OnTransactionErrorEvt?.Invoke("Commit failed: " + r.error);
    }

    public void OnMoveRecorded(string json)
    {
        Log("OnMoveRecorded", json);
        var r = JsonUtility.FromJson<StringResult>(json);
        if (r.ok) OnMoveRecordedEvt?.Invoke(r.data.roomId);
    }

    public void OnWinnerDeclared(string json)
    {
        Log("OnWinnerDeclared", json);
        var r = JsonUtility.FromJson<WinnerResult>(json);
        if (r.ok) { ActiveRoomId = null; OnWinnerDeclaredEvt?.Invoke(r.data.roomId, r.data.winner); }
        else OnTransactionErrorEvt?.Invoke("Declare winner failed: " + r.error);
    }

    public void OnRoomCancelled(string json)
    {
        Log("OnRoomCancelled", json);
        var r = JsonUtility.FromJson<StringResult>(json);
        if (r.ok) { ActiveRoomId = null; IsHost = false; OnRoomCancelledEvt?.Invoke(r.data.roomId); }
        else OnTransactionErrorEvt?.Invoke("Cancel room failed: " + r.error);
    }

    public void OnWithdrawComplete(string json)
    {
        Log("OnWithdrawComplete", json);
        var r = JsonUtility.FromJson<StringResult>(json);
        if (r.ok) OnWithdrawCompleteEvt?.Invoke(r.data.amountEth);
        else OnTransactionErrorEvt?.Invoke("Withdraw failed: " + r.error);
    }

    public void OnRoomStateUpdated(string json)
    {
        Log("OnRoomStateUpdated", json);
        var r = JsonUtility.FromJson<RoomStateResult>(json);
        if (r.ok) { ActiveRoomState = r.data.state; OnRoomStateUpdatedEvt?.Invoke(r.data); }
    }

    public void OnContractEvent(string json)
    {
        Log("OnContractEvent", json);
        var e = JsonUtility.FromJson<ContractEvent>(json);
        OnContractEventEvt?.Invoke(e);

        if (e.@event == "GameStarted" && e.roomId == ActiveRoomId)
        {
            ActiveRoomState = (int)RoomState.Live;
            // UPDATED: Now passing the VRF seed to whoever is listening (GameManager)
            OnGameStartedEvt?.Invoke(e.roomId, e.players, e.vrfSeed);
        }
    }

    public void OnChainChanged(string json)
    {
        Log("OnChainChanged", json);
        var r = JsonUtility.FromJson<ChainChangedResult>(json);
        IsConnected = false; WalletAddress = null; ActiveRoomId = null;
        OnChainChangedEvt?.Invoke(r.data?.reason ?? "unknown");
    }

    private void Log(string method, string json) { if (verboseLogging) Debug.Log($"[Web3Manager] {method} ← {json}"); }
}