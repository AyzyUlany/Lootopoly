using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif

// ============================================================
// LOOTOPOLY – GameManager (v6.0 — VRF DETERMINISTIC SYNC)
// ============================================================
// The game now receives the true VRF seed from the smart contract
// when the Room hits the "Live" state. It hashes this seed to 
// initialize Unity's RNG, meaning all players perfectly compute
// the exact same dice rolls, card draws, and loot drops 
// without needing further network traffic!
// ============================================================

public enum GameState
{
    Setup, StartTurn, ActionPhase, RollPhase, MoveDirectionChoice, MovePhase,
    EncounterPhase, CombatRollPhase, BuyTileDecision, UpgradeTileDecision,
    TrapTileDecision, EquipDecision, ShopPhase, CraftingPhase, EndTurn, GameOver
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }[Header("Dev Test Mode")][Tooltip("Enable to bypass Web3 entirely, mock the HTML Lobby, and auto-simulate players joining.")]
    public bool fullDevTestMode = false;

    [Header("Events")]
    public UnityEvent<Player>       onPlayerWin;
    public UnityEvent<string>       onNotification;
    public UnityEvent               onStateChanged;
    public UnityEvent<Player, int>  onPlayerGoldChanged;    
    public UnityEvent<Player>       onPlayerDied;
    public UnityEvent<Player>       onEquipmentChanged;

    [Header("Win Condition")]
    public int goldToWin = 2000;

    [Header("Wanted System")]
    public int wantedTileThreshold = 3;
    public int wantedBounty        = 50;

    [Header("Scene References")]
    public Transform     boardParent;
    public DiceRoller    diceRoller;
    public List<Player>  players = new List<Player>();

    [Header("Data Pools")]
    public List<MonsterData> possibleMonsters  = new List<MonsterData>();
    public List<CardData>    allAvailableCards = new List<CardData>();
    public List<LootData>    shopLootPool      = new List<LootData>();
    public List<LootData>    dropLootPool      = new List<LootData>();
    public List<LootData>    craftingResults   = new List<LootData>();

    [Header("Prefabs")]
    public GameObject tier1MinionPrefab;
    public GameObject tier2MinionPrefab;
    public GameObject trapPrefab;

    [HideInInspector] public GameState   currentState;
    [HideInInspector] public int         currentPlayerIndex;
    [HideInInspector] public Tile[]      boardTiles;
    [HideInInspector] public LootData    currentShopItem;[HideInInspector] public LootData    pendingLootItem;        
    [HideInInspector] public bool        canCraft;              
    [HideInInspector] public bool        playerCanGoBackward;   
    
    // Combat State
    [HideInInspector] public Player      combatTargetPlayer;
    [HideInInspector] public string      combatMonsterName;
    [HideInInspector] public int         combatMonsterDef;[HideInInspector] public int         combatMonsterHP;
    [HideInInspector] public string      combatMonsterFlavor;
    
    [HideInInspector] public bool        goldRushActive;

    [HideInInspector] public int  lastDiceRoll = 0;
    private bool _web3GameReady = false;

    private Player CurrentPlayer => (players.Count > 0 && currentPlayerIndex >= 0 && currentPlayerIndex < players.Count) 
        ? players[currentPlayerIndex] 
        : null;
    private bool   isProcessing  = false;
    private bool   moveBackward  = false;

    // ─────────────────────────────────────────────────────────
    // LIFECYCLE
    // ─────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void Start()
    {
        // UPDATED: Listen for the new event signature which includes the VRF seed
        Web3Manager.OnGameStartedEvt += HandleWeb3GameStarted;

#if UNITY_EDITOR
        _web3GameReady = true;
#endif

        ChangeState(GameState.Setup);
    }

    private void Update()
    {
        // Dev Tool: Press K to force a random winner immediately
        if (fullDevTestMode && Input.GetKeyDown(KeyCode.K))
        {
            if (players.Count > 0)
            {
                Player randomWinner = players[UnityEngine.Random.Range(0, players.Count)];
                randomWinner.gold += 9999;
                Notify($"[DEV TOOL] {randomWinner.playerName} was granted immediate victory!");
                ChangeState(GameState.GameOver);
            }
        }
    }

    private void OnDestroy()
    {
        Web3Manager.OnGameStartedEvt -= HandleWeb3GameStarted;
    }

    // ─────────────────────────────────────────────────────────
    // WEB3 EVENT HANDLER (VRF SYNC)
    // ─────────────────────────────────────────────────────────

    // C# string.GetHashCode() can differ across platforms/sessions. 
    // This custom hash guarantees the exact same integer seed on every browser tab.
    private int GenerateDeterministicSeed(string vrfSeed)
    {
        if (string.IsNullOrEmpty(vrfSeed)) return 0;
        unchecked
        {
            int hash = 23;
            foreach (char c in vrfSeed)
            {
                hash = hash * 31 + c;
            }
            return hash;
        }
    }

    private void HandleWeb3GameStarted(string roomId, string[] playerAddrs, string vrfSeed)
    {
        Debug.Log($"[GameManager] Web3 GameStarted — room #{roomId}, " +
                  $"{(playerAddrs != null ? playerAddrs.Length : 0)} players. VRF: {vrfSeed}");
        
        // --- 1. SEED THE RNG DETERMINISTICALLY ---
        if (!string.IsNullOrEmpty(vrfSeed))
        {
            int safeSeed = GenerateDeterministicSeed(vrfSeed);
            UnityEngine.Random.InitState(safeSeed);
            Debug.Log($"<color=lime>[GameManager] Unity RNG Seeded: {safeSeed}. All events are now perfectly synced.</color>");
        }

        if (playerAddrs != null && playerAddrs.Length > 0)
        {
            // ── SPAWN MISSING PLAYERS ────────────────────────────
            while (players.Count < playerAddrs.Length && players.Count > 0)
            {
                Player template = players[players.Count - 1];
                Player newPlayer = Instantiate(template, template.transform.parent);
                
                newPlayer.playerName = "Player " + (players.Count + 1);
                newPlayer.playerID = players.Count;
                
                // Initialize fresh player data
                newPlayer.gold = 500;
                newPlayer.currentHP = newPlayer.maxHP;
                newPlayer.equippedWeapon = null;
                newPlayer.equippedArmor = null;
                newPlayer.equippedBoots = null;
                newPlayer.equippedAccessory = null;
                newPlayer.handOfCards = new List<CardData>();
                newPlayer.currentTileIndex = 0;
                newPlayer.skipNextActionPhase = false;
                newPlayer.hasGreasedShoes = false;

                if (boardTiles != null && boardTiles.Length > 0)
                {
                    newPlayer.transform.position = boardTiles[0].GetPlayerStandPosition(newPlayer.playerID);
                }

                players.Add(newPlayer);
            }

            // ── REMOVE EXCESS PLAYERS ────────────────────────────
            for (int i = players.Count - 1; i >= playerAddrs.Length; i--)
            {
                if (players[i] != null && players[i].gameObject != null)
                {
                    Destroy(players[i].gameObject);
                }
                players.RemoveAt(i);
            }

            // ── MAP WALLET ADDRESSES ─────────────────────────────
            for (int i = 0; i < playerAddrs.Length && i < players.Count; i++)
            {
                players[i].walletAddress = playerAddrs[i];
            }
        }

        _web3GameReady = true;
        
        if (currentState == GameState.Setup)
            ChangeState(GameState.StartTurn);
    }

    // ─────────────────────────────────────────────────────────
    // CHAIN RECORDING HELPERS
    // ─────────────────────────────────────────────────────────

    private void NotifyMoveOnChain(int diceRoll, int newPos)
    {
        if (Web3Manager.Instance != null && Web3Manager.Instance.IsConnected && !fullDevTestMode)
            Web3Manager.Instance.RecordMove(diceRoll, newPos);
    }

    private void NotifyGameOverOnChain(Player winner)
    {
        if (Web3Manager.Instance == null || !Web3Manager.Instance.IsConnected || fullDevTestMode) return;
        
        string addr = winner.walletAddress;
        if (string.IsNullOrEmpty(addr))
            addr = Web3Manager.Instance.WalletAddress;
            
        Web3Manager.Instance.DeclareWinnerNative(addr);
    }

    // ─────────────────────────────────────────────────────────
    // STATE MACHINE
    // ─────────────────────────────────────────────────────────

    public void ChangeState(GameState next)
    {
        if (isProcessing && next != GameState.GameOver) return;

        currentState = next;
        isProcessing = true;

        onStateChanged?.Invoke();

        switch (currentState)
        {
            case GameState.Setup:               HandleSetup();              break;
            case GameState.StartTurn:           HandleStartTurn();          break;
            case GameState.ActionPhase:         HandleActionPhase();        break;
            case GameState.RollPhase:           isProcessing = false;       break; 
            case GameState.MoveDirectionChoice: isProcessing = false;       break; 
            case GameState.MovePhase:                                       break; 
            case GameState.EncounterPhase:      HandleEncounter();          break;
            case GameState.CombatRollPhase:     HandleCombatSetup();        break;
            case GameState.BuyTileDecision:     isProcessing = false;       break;
            case GameState.UpgradeTileDecision: isProcessing = false;       break;
            case GameState.TrapTileDecision:    isProcessing = false;       break;
            case GameState.EquipDecision:       isProcessing = false;       break;
            case GameState.ShopPhase:           HandleShopOpen();           break;
            case GameState.CraftingPhase:       isProcessing = false;       break;
            case GameState.EndTurn:             HandleEndTurn();            break;
            case GameState.GameOver:            HandleGameOver();           break;
        }
    }

    private void HandleSetup()
    {
        boardTiles = boardParent.GetComponentsInChildren<Tile>();

        for (int i = 0; i < boardTiles.Length; i++)
        {
            boardTiles[i].tileIndex = i;
            if (boardTiles[i].tileType == TileType.Property && possibleMonsters.Count > 0)
                boardTiles[i].SetupResidentMonster(GetRandomMonster());
        }

        foreach (var p in players)
        {
            p.playerID          = players.IndexOf(p);
            p.currentTileIndex  = 0;
            p.gold              = 500;
            p.currentHP         = p.maxHP;
            p.transform.position = boardTiles[0].GetPlayerStandPosition(p.playerID);
        }

        currentPlayerIndex = 0;
        goldRushActive     = false;
        combatTargetPlayer = null;
        isProcessing       = false;

        if (_web3GameReady)
            ChangeState(GameState.StartTurn);
    }

    private void HandleStartTurn()
    {
        // Safety: ensure current player is alive
        if (CurrentPlayer == null || CurrentPlayer.currentHP <= 0)
        {
            // Find next alive player
            if (!TryFindNextAlivePlayer())
            {
                ChangeState(GameState.GameOver);
                return;
            }
        }

        CurrentPlayer.ClearGhostStep();
        goldRushActive = false;
        combatTargetPlayer = null;

        CurrentPlayer.PlayHappyFlip();

        int passive = CurrentPlayer.CollectPassiveGold();
        if (passive > 0)
        {
            Notify($"💎 {CurrentPlayer.playerName} earns {passive}g passive income!");
            onPlayerGoldChanged?.Invoke(CurrentPlayer, passive);
        }

        if (IsWanted(CurrentPlayer))
        {
            int bounty = Mathf.Min(wantedBounty, CurrentPlayer.gold);
            CurrentPlayer.gold -= bounty;
            Notify($"⚠️ WANTED! {CurrentPlayer.playerName} pays {bounty}g bounty.");
        }

        if (CheckWin()) return;

        isProcessing = false;
        ChangeState(GameState.ActionPhase);
    }

    private void HandleActionPhase()
    {
        if (CurrentPlayer == null)
        {
            ChangeState(GameState.EndTurn);
            return;
        }

        if (CurrentPlayer.skipNextActionPhase)
        {
            CurrentPlayer.skipNextActionPhase = false;
            Notify($"💨 {CurrentPlayer.playerName}'s Action Phase skipped! (Pocket Sand)");
            isProcessing = false;
            ChangeState(GameState.RollPhase);
            return;
        }
        isProcessing = false;
    }

    private void HandleEncounter()
    {
        if (CurrentPlayer == null)
        {
            ChangeState(GameState.EndTurn);
            return;
        }

        // 1. PvP Check
        Player pvpTarget = players.FirstOrDefault(p => 
            p != CurrentPlayer && 
            p.currentHP > 0 && 
            p.currentTileIndex == CurrentPlayer.currentTileIndex);

        if (pvpTarget != null)
        {
            combatTargetPlayer = pvpTarget;
            isProcessing = false;
            ChangeState(GameState.CombatRollPhase);
            return;
        }

        // 2. Normal Tile Encounter
        Tile tile = boardTiles[CurrentPlayer.currentTileIndex];

        switch (tile.tileType)
        {
            case TileType.Start:
                int heal = Mathf.FloorToInt(CurrentPlayer.maxHP * 0.5f);
                CurrentPlayer.currentHP = Mathf.Min(CurrentPlayer.maxHP, CurrentPlayer.currentHP + heal);
                GiveGold(CurrentPlayer, 200);
                Notify($"🏠 {CurrentPlayer.playerName} passes START! +200g and +{heal} HP!");
                StartCoroutine(AdvanceAfterDelay());
                break;

            case TileType.Property:
                HandlePropertyTile(tile);
                break;

            case TileType.Event:
                HandleEventTile();
                break;

            case TileType.Shop:
                isProcessing = false;
                ChangeState(GameState.ShopPhase);
                break;
        }
    }

    private void HandlePropertyTile(Tile tile)
    {
        if (CurrentPlayer == null) return;

        if (tile.ownerPlayerID == -1)
        {
            if (tile.residentMonster != null && tile.currentMonsterHP > 0)
            {
                isProcessing = false;
                ChangeState(GameState.CombatRollPhase);
            }
            else StartCoroutine(AdvanceAfterDelay(0.5f));
            return;
        }

        if (tile.ownerPlayerID == CurrentPlayer.playerID)
        {
            if (!tile.isTrap && tile.currentTier == 1 && CurrentPlayer.gold >= 300)
            {
                isProcessing = false;
                ChangeState(GameState.UpgradeTileDecision);
                return;
            }
            if (!tile.isTrap && tile.currentTier == 2 && CurrentPlayer.gold >= 200)
            {
                isProcessing = false;
                ChangeState(GameState.TrapTileDecision);
                return;
            }
            Notify($"🏡 {CurrentPlayer.playerName} rests in their territory.");
            StartCoroutine(AdvanceAfterDelay());
            return;
        }

        HandleEnemyTile(tile);
    }

    private void HandleEnemyTile(Tile tile)
    {
        if (CurrentPlayer == null) return;

        Player owner = players.FirstOrDefault(p => p.playerID == tile.ownerPlayerID);
        if (owner == null || owner.currentHP <= 0)
        {
            StartCoroutine(AdvanceAfterDelay(0.5f));
            return;
        }

        if (CurrentPlayer.HasGhostStep() && CurrentPlayer.equippedBoots != null && CurrentPlayer.equippedBoots.ghostStepActive)
        {
            CurrentPlayer.equippedBoots.ghostStepActive = false;
            Notify($"👻 GHOST STEP! {CurrentPlayer.playerName} slipped past {owner.playerName}'s tile unseen!");
            StartCoroutine(AdvanceAfterDelay());
            return;
        }

        int toll = tile.GetTollAmount();
        if (goldRushActive && tile.ownerPlayerID == owner.playerID) toll *= 2;

        int paid = Mathf.Min(CurrentPlayer.gold, toll);
        CurrentPlayer.gold -= paid;
        owner.gold         += paid;
        onPlayerGoldChanged?.Invoke(owner, paid);

        float ls = owner.GetTollLifesteal();
        if (ls > 0f && paid > 0)
        {
            int healAmt = Mathf.CeilToInt(paid * ls);
            owner.currentHP = Mathf.Min(owner.maxHP, owner.currentHP + healAmt);
            Notify($"🩸 {owner.playerName} leeches {healAmt} HP from toll income!");
        }

        bool wantedOwner = IsWanted(owner);
        int  baseDmg     = tile.isTrap ? tile.GetTrapDamage() : tile.GetTileDamage();
        int  finalDmg    = wantedOwner ? Mathf.CeilToInt(baseDmg * 1.5f) : baseDmg;
        int  actualDmg;

        if (tile.isTrap) 
        {
            tile.PlayTrapSpringAnimation();
            ShakeCamera(0.6f);
            actualDmg = CurrentPlayer.TakeTrapDamage(finalDmg, this);
        }
        else             
        {
            tile.PlayTollPaidAnimation();
            actualDmg = CurrentPlayer.TakeDamage(finalDmg, this);
            if (actualDmg > 0) ShakeCamera(0.2f);
        }

        if (!tile.isTrap && actualDmg > 0)
        {
            int thorns = CurrentPlayer.GetThornsDamage();
            if (thorns > 0)
            {
                owner.TakeDamage(thorns, this);
                Notify($"🌵 THORNS! {owner.playerName} takes {thorns} reflected damage!");
            }
        }

        string tollMsg = $"💸 {CurrentPlayer.playerName} paid {paid}g toll to {owner.playerName}";
        if (actualDmg > 0) tollMsg += $" and took {actualDmg} HP damage";
        tollMsg += "!";
        Notify(tollMsg);

        if (CurrentPlayer.currentHP > 0)
            StartCoroutine(AdvanceAfterDelay());
    }

    private void HandleEventTile()
    {
        if (CurrentPlayer == null) return;

        if (allAvailableCards.Count == 0)
        {
            GiveGold(CurrentPlayer, 75);
            Notify($"📦 {CurrentPlayer.playerName} found a stash! +75g.");
        }
        else
        {
            DrawCard(CurrentPlayer);
        }

        StartCoroutine(AdvanceAfterDelay());
    }

    private void DrawCard(Player p)
    {
        if (p == null || allAvailableCards.Count == 0) return;
        CardData drawn = allAvailableCards[UnityEngine.Random.Range(0, allAvailableCards.Count)];
        p.handOfCards.Add(drawn);

        if (p.HasDoubleCardDraw())
        {
            CardData bonus = allAvailableCards[UnityEngine.Random.Range(0, allAvailableCards.Count)];
            p.handOfCards.Add(bonus);
            Notify($"🃏 {p.playerName} drew {drawn.cardName} AND {bonus.cardName} (Double Draw!)");
        }
        else
        {
            Notify($"🃏 {p.playerName} drew: {drawn.cardName}");
        }
    }

    // ═════════════════════════════════════════════════════════
    // TURN-BASED COMBAT PIPELINE (PvE & PvP)
    // ═════════════════════════════════════════════════════════

    private void HandleCombatSetup()
    {
        if (CurrentPlayer == null || boardTiles == null)
        {
            ChangeState(GameState.EndTurn);
            return;
        }

        Tile tile = boardTiles[CurrentPlayer.currentTileIndex];

        if (combatTargetPlayer != null)
        {
            combatMonsterName   = combatTargetPlayer.playerName;
            combatMonsterDef    = combatTargetPlayer.GetTotalDefense();
            combatMonsterHP     = combatTargetPlayer.currentHP;
            combatMonsterFlavor = "A rival player stands in your way!";

            Vector3 pPos      = tile.transform.position + tile.transform.rotation * new Vector3(-0.35f, 0.15f, 0);
            Vector3 targetPos = tile.transform.position + tile.transform.rotation * new Vector3(0.35f, 0.15f, 0);

            CurrentPlayer.EnterCombatArena(pPos);
            if (combatTargetPlayer.currentHP > 0)
            {
                combatTargetPlayer.EnterCombatArena(targetPos);
            }
        }
        else if (tile.residentMonster != null)
        {
            combatMonsterName   = tile.residentMonster.monsterName;
            combatMonsterDef    = tile.residentMonster.defense;
            combatMonsterHP     = tile.currentMonsterHP;
            combatMonsterFlavor = tile.residentMonster.flavorText;

            tile.SetupCombatArena(out Vector3 pPos, out Vector3 mPos, out GameObject mVisual);
            CurrentPlayer.EnterCombatArena(pPos);
        }

        isProcessing = false;  
        onStateChanged?.Invoke(); // Ensures UI catches the correctly populated Monster HP/Defense immediately
    }

    public void UI_RollCombatDice()
    {
        if (currentState != GameState.CombatRollPhase || isProcessing) return;
        if (CurrentPlayer == null) return;
        
        isProcessing = true;

        int bestOf = CurrentPlayer.GetRerollBestOf();
        if (bestOf >= 2)
        {
            StartCoroutine(BestOfRollRoutine(bestOf));
            return;
        }

        Vector3 pos = CurrentPlayer.transform.position + Vector3.up * 3f;
        diceRoller.RollDice(pos, roll =>
        {
            HandleDoubleDownDamage();
            if (CurrentPlayer.currentHP > 0) {
                StartCoroutine(CombatRoundRoutineWrapper(roll));
            } else {
                EndCombat(false);
            }
        });
    }

    private IEnumerator BestOfRollRoutine(int times)
    {
        if (CurrentPlayer == null) yield break;

        int best = 0; bool done = false;
        Vector3 pos = CurrentPlayer.transform.position + Vector3.up * 3f;

        for (int i = 0; i < times; i++)
        {
            done = false;
            diceRoller.RollDice(pos, r => { if (r > best) best = r; done = true; });
            yield return new WaitUntil(() => done);
            if (i < times - 1) yield return new WaitForSeconds(0.6f);
        }

        Notify($"🎲 Best of {times}: {best}!");
        HandleDoubleDownDamage();
        
        if (CurrentPlayer.currentHP > 0) {
            StartCoroutine(CombatRoundRoutineWrapper(best));
        } else {
            EndCombat(false);
        }
    }

    private void HandleDoubleDownDamage()
    {
        if (CurrentPlayer == null || diceRoller == null) return;
        if (!diceRoller.lastRollWasDoubled) return;

        if (diceRoller.rawFaceValue <= 2)
        {
            CurrentPlayer.TakeDamage(2, this);
            Notify($"⚡ Double Down backfired! {CurrentPlayer.playerName} rolled a {diceRoller.rawFaceValue} and took 2 HP penalty!");
        }
    }

    private IEnumerator CombatRoundRoutineWrapper(int finalRoll)
    {
        if (combatTargetPlayer != null)
            yield return StartCoroutine(PvPRoundRoutine(finalRoll));
        else
            yield return StartCoroutine(PvERoundRoutine(finalRoll));
    }

    // ── PvE Logic ────────────────────────────────────────────
    private IEnumerator PvERoundRoutine(int finalRoll)
    {
        if (CurrentPlayer == null || boardTiles == null) 
        {
            EndCombat(false);
            yield break;
        }

        Tile tile = boardTiles[CurrentPlayer.currentTileIndex];
        MonsterData m = tile.residentMonster;
        if (m == null) { EndCombat(false); yield break; }

        bool isCrit    = CurrentPlayer.HasCriticalStrike() && finalRoll == 6;
        int  atkTotal  = finalRoll + CurrentPlayer.GetTotalAttack();
        
        bool hit = isCrit || atkTotal >= m.defense;
        int damageToMonster = 0;

        if (hit) {
            damageToMonster = isCrit ? atkTotal : Mathf.Max(1, atkTotal - m.defense);
        }

        bool turnDone = false;
        tile.SetupCombatArena(out Vector3 pPos, out Vector3 mPos, out GameObject mVisual);
        
        CurrentPlayer.AnimateAttackSequence(mPos, () => {
            if (hit) {
                tile.PlayMonsterDamageAnimation();
                ShakeCamera(isCrit ? 0.6f : 0.3f);
                tile.currentMonsterHP -= damageToMonster;
                combatMonsterHP = tile.currentMonsterHP; 
                
                string critStr = isCrit ? " (CRITICAL HIT!)" : "";
                Notify($"⚔️ {CurrentPlayer.playerName} struck {m.monsterName} for {damageToMonster} DMG!{critStr}");
            } else {
                Notify($"🛡️ {m.monsterName} deflected the attack! (ATK {atkTotal} vs DEF {m.defense})");
            }
        }, () => turnDone = true);

        yield return new WaitUntil(() => turnDone);
        yield return new WaitForSeconds(0.4f);

        if (tile.currentMonsterHP <= 0)
        {
            tile.PlayCombatHitAnimation();
            ShakeCamera(0.6f);
            
            float wantedMult = IsWanted(CurrentPlayer) ? 1.25f : 1f;
            float accMult    = CurrentPlayer.GetGoldMultiplier();
            int   reward     = Mathf.CeilToInt(m.goldReward * wantedMult * accMult);
            int   killBonus  = CurrentPlayer.GetKillBonusGold();

            GiveGold(CurrentPlayer, reward + killBonus);

            string msg = $"🏆 VICTORY vs {m.monsterName}! +{reward}g";
            if (killBonus > 0) msg += $" +{killBonus}g kill bonus";
            Notify(msg);

            float baseDrop  = m.lootDropChance;
            float extraDrop = CurrentPlayer.HasLootChanceBoost() ? 0.5f : 0f;
            float dropChance = Mathf.Min(1f, baseDrop + extraDrop);

            LootData drop = null;
            if (UnityEngine.Random.value <= dropChance) drop = GetRandomDrop(m);

            tile.residentMonster = null; 

            yield return new WaitForSeconds(0.6f);
            EndCombat(true, drop);
            yield break;
        }

        turnDone = false;
        tile.AnimateMonsterAttack(CurrentPlayer.transform.position, () => {
            ShakeCamera(0.4f);
            int dmg = CurrentPlayer.TakeDamage(m.attack, this);
            Notify($"💀 {m.monsterName} countered! {CurrentPlayer.playerName} took {dmg} DMG.");
        }, () => turnDone = true);

        yield return new WaitUntil(() => turnDone);
        yield return new WaitForSeconds(0.4f);

        // Battle ends after exactly one exchange. If no one died, combat ends and player rests on tile.
        EndCombat(false);
    }

    // ── PvP Logic ────────────────────────────────────────────
    private IEnumerator PvPRoundRoutine(int finalRoll)
    {
        if (CurrentPlayer == null || combatTargetPlayer == null)
        {
            EndCombat(false);
            yield break;
        }

        bool isCrit    = CurrentPlayer.HasCriticalStrike() && finalRoll == 6;
        int  atkTotal  = finalRoll + CurrentPlayer.GetTotalAttack();
        int  def       = combatTargetPlayer.GetTotalDefense();
        
        bool hit = isCrit || atkTotal >= def;
        int damageToTarget = 0;

        if (hit) {
            damageToTarget = isCrit ? atkTotal : Mathf.Max(1, atkTotal - def);
        }

        bool turnDone = false;
        Vector3 targetPos = combatTargetPlayer.transform.position;
        
        CurrentPlayer.AnimateAttackSequence(targetPos, () => {
            if (hit) {
                ShakeCamera(isCrit ? 0.6f : 0.3f);
                combatTargetPlayer.TakeDamage(damageToTarget, this);
                combatMonsterHP = combatTargetPlayer.currentHP;
                
                string critStr = isCrit ? " (CRITICAL HIT!)" : "";
                Notify($"⚔️ {CurrentPlayer.playerName} struck {combatTargetPlayer.playerName} for {damageToTarget} DMG!{critStr}");
            } else {
                Notify($"🛡️ {combatTargetPlayer.playerName} deflected the attack! (ATK {atkTotal} vs DEF {def})");
            }
        }, () => turnDone = true);

        yield return new WaitUntil(() => turnDone);
        yield return new WaitForSeconds(0.4f);

        if (combatTargetPlayer.currentHP <= 0)
        {
            int reward = 100 + CurrentPlayer.GetKillBonusGold();
            GiveGold(CurrentPlayer, reward);
            Notify($"🏆 PvP VICTORY! Collected {reward}g bounty from {combatTargetPlayer.playerName}!");

            yield return new WaitForSeconds(0.6f);
            EndCombat(true);
            yield break;
        }

        turnDone = false;
        combatTargetPlayer.AnimateAttackSequence(CurrentPlayer.transform.position, () => {
            ShakeCamera(0.4f);
            int rawCounterAtk = combatTargetPlayer.GetTotalAttack() + 3;
            int actualDmg = CurrentPlayer.TakeDamage(rawCounterAtk, this);
            Notify($"💀 {combatTargetPlayer.playerName} countered! {CurrentPlayer.playerName} took {actualDmg} DMG.");
        }, () => turnDone = true);

        yield return new WaitUntil(() => turnDone);
        yield return new WaitForSeconds(0.4f);

        // Battle ends after exactly one exchange. If no one died, combat ends and players rest on tile.
        EndCombat(false);
    }

    public void UI_Flee()
    {
        if (currentState != GameState.CombatRollPhase || isProcessing) return;
        if (CurrentPlayer == null) return;
        
        isProcessing = true;
        
        if (combatTargetPlayer != null)
            StartCoroutine(PvPFleeRoutine());
        else
            StartCoroutine(PvEFleeRoutine());
    }

    private IEnumerator PvEFleeRoutine()
    {
        if (CurrentPlayer == null || boardTiles == null)
        {
            EndCombat(false);
            yield break;
        }

        Tile tile = boardTiles[CurrentPlayer.currentTileIndex];
        MonsterData m = tile.residentMonster;

        if (UnityEngine.Random.value > 0.5f)
        {
            int fleeCost = Mathf.Min(30, CurrentPlayer.gold);
            CurrentPlayer.gold -= fleeCost;
            Notify($"💨 {CurrentPlayer.playerName} successfully fled! Cost: {fleeCost}g.");
            yield return new WaitForSeconds(0.5f);
            EndCombat(false);
        }
        else
        {
            Notify($"🚫 Flee failed! {m.monsterName} blocks the escape!");
            bool turnDone = false;
            tile.AnimateMonsterAttack(CurrentPlayer.transform.position, () => {
                int dmg = CurrentPlayer.TakeDamage(m.attack, this);
                ShakeCamera(0.5f);
                Notify($"💀 Took {dmg} DMG while trying to flee!");
            }, () => turnDone = true);
            
            yield return new WaitUntil(() => turnDone);
            yield return new WaitForSeconds(0.5f);
            
            EndCombat(false);
        }
    }

    private IEnumerator PvPFleeRoutine()
    {
        if (CurrentPlayer == null || combatTargetPlayer == null)
        {
            EndCombat(false);
            yield break;
        }

        if (UnityEngine.Random.value > 0.5f)
        {
            int fleeCost = Mathf.Min(30, CurrentPlayer.gold);
            CurrentPlayer.gold -= fleeCost;
            Notify($"💨 {CurrentPlayer.playerName} fled from {combatTargetPlayer.playerName}! Cost: {fleeCost}g.");
            yield return new WaitForSeconds(0.5f);
            EndCombat(false);
        }
        else
        {
            Notify($"🚫 Flee failed! {combatTargetPlayer.playerName} blocks the escape!");
            bool turnDone = false;
            combatTargetPlayer.AnimateAttackSequence(CurrentPlayer.transform.position, () => {
                int rawCounterAtk = combatTargetPlayer.GetTotalAttack() + 3;
                int dmg = CurrentPlayer.TakeDamage(rawCounterAtk, this);
                ShakeCamera(0.5f);
                Notify($"💀 Took {dmg} DMG while trying to flee!");
            }, () => turnDone = true);
            
            yield return new WaitUntil(() => turnDone);
            yield return new WaitForSeconds(0.5f);
            
            EndCombat(false);
        }
    }

    private void EndCombat(bool defeatedOpponent, LootData pendingDrop = null)
    {
        // ── CLEANUP COMBAT STATE ─────────────────────────────
        bool currentPlayerAlive = CurrentPlayer != null && CurrentPlayer.currentHP > 0;
        bool targetAlive = combatTargetPlayer != null && combatTargetPlayer.currentHP > 0;

        if (boardTiles == null || CurrentPlayer == null)
        {
            combatTargetPlayer = null;
            if (CheckWin()) return;
            isProcessing = false;
            ChangeState(GameState.EndTurn);
            return;
        }

        Tile tile = boardTiles[CurrentPlayer.currentTileIndex];
        Vector3 normalPos = tile.GetPlayerStandPosition(CurrentPlayer.playerID);
        
        if (currentPlayerAlive)
            CurrentPlayer.ExitCombatArena(normalPos);
            
        if (combatTargetPlayer != null)
        {
            if (targetAlive)
            {
                Vector3 targetNormalPos = tile.GetPlayerStandPosition(combatTargetPlayer.playerID);
                combatTargetPlayer.ExitCombatArena(targetNormalPos);
            }
            combatTargetPlayer = null;
            
            if (CheckWin()) return;
            isProcessing = false;
            ChangeState(GameState.EndTurn);
            return;
        }

        tile.ClearCombatMonster();

        if (defeatedOpponent)
        {
            if (pendingDrop != null)
            {
                GiveLootToPlayer(CurrentPlayer, pendingDrop, "Dropped by monster");
            }
            else
            {
                if (CheckWin()) return;
                isProcessing = false;
                ChangeState(CurrentPlayer.gold >= tile.baseCost ? GameState.BuyTileDecision : GameState.EndTurn);
            }
        }
        else
        {
            if (CheckWin()) return;

            if (currentPlayerAlive)
                StartCoroutine(AdvanceAfterDelay(0.3f));
            else
            {
                isProcessing = false;
                ChangeState(GameState.EndTurn);
            }
        }
    }

    // ═════════════════════════════════════════════════════════
    // SHOP & LOOT
    // ═════════════════════════════════════════════════════════

    private void HandleShopOpen()
    {
        if (CurrentPlayer == null)
        {
            ChangeState(GameState.EndTurn);
            return;
        }

        var nonCrafted = shopLootPool.Where(l => !l.isCraftedOnly).ToList();
        currentShopItem = nonCrafted.Count > 0
            ? nonCrafted[UnityEngine.Random.Range(0, nonCrafted.Count)]
            : null;
        canCraft = CurrentPlayer.EquippedCount() >= 2 && craftingResults.Count > 0;
        isProcessing = false;
    }

    private void GiveLootToPlayer(Player p, LootData item, string source)
    {
        if (p == null || item == null) return;

        Notify($"🎁 [{source}] {item.lootName}  [{item.slot} · {item.rarity}]");

        if (!p.HasSlotFilled(item.slot))
        {
            p.Equip(item);
            onEquipmentChanged?.Invoke(p);
            Notify($"✅ Auto-equipped {item.lootName} in empty {item.slot} slot!");
            if (CheckWin()) return;
            ContinueAfterLoot(p);
        }
        else
        {
            pendingLootItem = item;
            isProcessing    = false;
            ChangeState(GameState.EquipDecision);
        }
    }

    private void ContinueAfterLoot(Player p)
    {
        if (p == null || boardTiles == null) return;

        Tile tile = boardTiles[p.currentTileIndex];
        bool canBuy = p.gold >= tile.baseCost && tile.ownerPlayerID == -1 && tile.tileType == TileType.Property;

        if (currentState == GameState.ShopPhase || currentState == GameState.EquipDecision)
            StartCoroutine(AdvanceAfterDelay(0.5f));
        else if (canBuy)
        {
            isProcessing = false;
            ChangeState(GameState.BuyTileDecision);
        }
        else
            StartCoroutine(AdvanceAfterDelay());
    }

    // ═════════════════════════════════════════════════════════
    // UI BINDINGS (NON-COMBAT)
    // ═════════════════════════════════════════════════════════

    public void UI_SkipActionPhase()
    {
        if (currentState != GameState.ActionPhase || isProcessing) return;
        ChangeState(GameState.RollPhase);
    }

    public void UI_PlayCard(CardData card, Player targetPlayer, Tile targetTile)
    {
        if (currentState != GameState.ActionPhase || isProcessing) return;
        if (CurrentPlayer == null) return;

        if (targetPlayer != null && targetPlayer.HasCardReflect() && UnityEngine.Random.value < 0.30f)
        {
            Notify($"🔮 CARD REFLECT! {targetPlayer.playerName}'s amulet bounced {card.cardName}!");
            var others = players.Where(p => p != targetPlayer && p != CurrentPlayer).ToList();
            targetPlayer = others.Count > 0 ? others[UnityEngine.Random.Range(0, others.Count)] : null;
        }

        isProcessing = true;
        CurrentPlayer.handOfCards.Remove(card);

        switch (card.effectType)
        {
            case CardEffect.SwapMeet:
                if (targetPlayer != null)
                {
                    int myIdx    = CurrentPlayer.currentTileIndex;
                    int theirIdx = targetPlayer.currentTileIndex;
                    
                    Vector3 myDest = boardTiles[theirIdx].GetPlayerStandPosition(CurrentPlayer.playerID);
                    Vector3 theirDest = boardTiles[myIdx].GetPlayerStandPosition(targetPlayer.playerID);
                    
                    CurrentPlayer.AnimateTeleport(theirIdx, myDest);
                    targetPlayer.AnimateTeleport(myIdx, theirDest);
                    
                    Notify($"🔄 SWAP MEET! {CurrentPlayer.playerName} ↔ {targetPlayer.playerName}!");
                }
                break;

            case CardEffect.TaxFraud:
                var richest = players.Where(p => p != CurrentPlayer).OrderByDescending(p => p.gold).FirstOrDefault();
                if (richest != null)
                {
                    int stolen = Mathf.FloorToInt(richest.gold * 0.2f);
                    richest.gold        -= stolen;
                    CurrentPlayer.gold  += stolen;
                    onPlayerGoldChanged?.Invoke(CurrentPlayer, stolen);
                    Notify($"💰 TAX FRAUD! Stole {stolen}g from {richest.playerName}!");
                }
                break;

            case CardEffect.MeteorStrike:
                if (targetTile != null)
                {
                    ShakeCamera(1.2f);
                    targetTile.SetOwnership(-1, 0, null);
                    if (possibleMonsters.Count > 0)
                        targetTile.SetupResidentMonster(GetRandomMonster());
                    Notify($"☄️ METEOR STRIKE on {targetTile.tileName}! Ownership wiped.");
                }
                break;

            case CardEffect.GreasedShoes:
                if (targetPlayer != null)
                {
                    targetPlayer.hasGreasedShoes = true;
                    Notify($"👟 GREASED SHOES! {targetPlayer.playerName} takes 1 HP/space next move!");
                }
                break;

            case CardEffect.PocketSand:
                if (targetPlayer != null)
                {
                    targetPlayer.skipNextActionPhase = true;
                    Notify($"💨 POCKET SAND! {targetPlayer.playerName} loses their Action Phase!");
                }
                break;

            case CardEffect.DoubleDown:
                if (diceRoller != null)
                {
                    diceRoller.doubleNextRoll = true;
                    Notify($"🎲 DOUBLE DOWN! Next roll is doubled. Risk: rolling a 1 or 2 costs 2 HP!");
                }
                break;

            case CardEffect.Jackpot:
                int total = 0;
                foreach (var pl in players.Where(pl => pl != CurrentPlayer))
                {
                    int take = Mathf.FloorToInt(pl.gold * 0.1f);
                    pl.gold             -= take;
                    CurrentPlayer.gold  += take;
                    total               += take;
                }
                onPlayerGoldChanged?.Invoke(CurrentPlayer, total);
                Notify($"🎰 JACKPOT! Drained {total}g total from all opponents!");
                break;

            case CardEffect.GoldRush:
                goldRushActive = true;
                Notify($"💰 GOLD RUSH! {CurrentPlayer.playerName}'s tiles charge double toll this turn!");
                break;
        }

        isProcessing = false;
        ChangeState(GameState.RollPhase);
    }

    public void UI_RollMovementDice()
    {
        if (currentState != GameState.RollPhase || isProcessing) return;
        if (CurrentPlayer == null) return;
        
        isProcessing = true;

        if (CurrentPlayer.CanMoveBackward())
        {
            playerCanGoBackward = true;
            isProcessing = false;   
            ChangeState(GameState.MoveDirectionChoice);
            return;
        }

        moveBackward = false;
        ExecuteMovementRoll();
    }

    public void UI_ChooseMoveDirection(bool backward)
    {
        if (currentState != GameState.MoveDirectionChoice || isProcessing) return;
        isProcessing  = true;
        moveBackward  = backward;
        ExecuteMovementRoll();
    }

    private void ExecuteMovementRoll()
    {
        if (CurrentPlayer == null || boardTiles == null)
        {
            ChangeState(GameState.EndTurn);
            return;
        }

        currentState = GameState.MovePhase;

        if (CurrentPlayer.hasGreasedShoes)
        {
            StartCoroutine(GreasedShoesRollRoutine());
            return;
        }

        Vector3 pos = CurrentPlayer.transform.position + Vector3.up * 3f;
        diceRoller.RollDice(pos, result =>
        {
            lastDiceRoll = result;
            int finalMove = Mathf.Max(1, result + CurrentPlayer.GetMoveBonus());
            CurrentPlayer.MoveSpaces(finalMove, boardTiles, () =>
            {
                NotifyMoveOnChain(lastDiceRoll, CurrentPlayer.currentTileIndex);
                isProcessing = false;
                ChangeState(GameState.EncounterPhase);
            }, moveBackward);
        });
    }

    private IEnumerator GreasedShoesRollRoutine()
    {
        if (CurrentPlayer == null || boardTiles == null)
        {
            ChangeState(GameState.EndTurn);
            yield break;
        }

        int total = 0; bool done = false;
        Vector3 pos = CurrentPlayer.transform.position + Vector3.up * 3f;

        diceRoller.RollDice(pos, r => { total += r; done = true; });
        yield return new WaitUntil(() => done);
        yield return new WaitForSeconds(0.7f);

        done = false;
        diceRoller.RollDice(pos, r => { total += r; done = true; });
        yield return new WaitUntil(() => done);

        lastDiceRoll = total;
        int finalMove = Mathf.Max(1, total + CurrentPlayer.GetMoveBonus());
        Notify($"💨 Greased Shoes: rolled {total} total — {finalMove} spaces of pain!");
        CurrentPlayer.MoveSpaces(finalMove, boardTiles, () =>
        {
            NotifyMoveOnChain(lastDiceRoll, CurrentPlayer.currentTileIndex);
            isProcessing = false;
            ChangeState(GameState.EncounterPhase);
        }, moveBackward);
    }

    public void UI_BuyTile()
    {
        if (currentState != GameState.BuyTileDecision || isProcessing) return;
        if (CurrentPlayer == null || boardTiles == null) return;
        
        isProcessing = true;
        Tile t = boardTiles[CurrentPlayer.currentTileIndex];
        if (CurrentPlayer.gold < t.baseCost) { Notify("Not enough gold!"); isProcessing = false; return; }
        CurrentPlayer.gold -= t.baseCost;
        onPlayerGoldChanged?.Invoke(CurrentPlayer, -t.baseCost);
        t.SetOwnership(CurrentPlayer.playerID, 1, tier1MinionPrefab);
        Notify($"🏴 {CurrentPlayer.playerName} claimed {t.tileName} for {t.baseCost}g!");
        CurrentPlayer.PlayHappyFlip();
        StartCoroutine(AdvanceAfterDelay());
    }

    public void UI_SkipBuyTile()
    {
        if (currentState != GameState.BuyTileDecision || isProcessing) return;
        isProcessing = true;
        StartCoroutine(AdvanceAfterDelay(0.3f));
    }

    public void UI_UpgradeTile()
    {
        if (currentState != GameState.UpgradeTileDecision || isProcessing) return;
        if (CurrentPlayer == null || boardTiles == null) return;
        
        isProcessing = true;
        Tile t = boardTiles[CurrentPlayer.currentTileIndex];
        if (CurrentPlayer.gold < 300) { Notify("Need 300g!"); isProcessing = false; return; }
        CurrentPlayer.gold -= 300;
        onPlayerGoldChanged?.Invoke(CurrentPlayer, -300);
        t.SetOwnership(CurrentPlayer.playerID, 2, tier2MinionPrefab);
        Notify($"⬆️ {t.tileName} upgraded to Goblin Guard! Toll: 50g, Dmg: 2 HP");
        CurrentPlayer.PlayHappyFlip();
        StartCoroutine(AdvanceAfterDelay());
    }

    public void UI_SkipUpgrade()
    {
        if (currentState != GameState.UpgradeTileDecision || isProcessing) return;
        if (CurrentPlayer == null || boardTiles == null) return;
        
        Tile t = boardTiles[CurrentPlayer.currentTileIndex];
        if (!t.isTrap && t.currentTier == 2 && CurrentPlayer.gold >= 200)
        {
            ChangeState(GameState.TrapTileDecision);
            return;
        }
        isProcessing = true;
        StartCoroutine(AdvanceAfterDelay(0.3f));
    }

    public void UI_SetTrap()
    {
        if (currentState != GameState.TrapTileDecision || isProcessing) return;
        if (CurrentPlayer == null || boardTiles == null) return;
        
        isProcessing = true;
        Tile t = boardTiles[CurrentPlayer.currentTileIndex];
        if (CurrentPlayer.gold < 200) { Notify("Need 200g!"); isProcessing = false; return; }
        CurrentPlayer.gold -= 200;
        onPlayerGoldChanged?.Invoke(CurrentPlayer, -200);
        t.SetTrap(CurrentPlayer.playerID, trapPrefab);
        Notify($"💀 TRAP set on {t.tileName}! (3 DMG + 40g toll)");
        CurrentPlayer.PlayHappyFlip();
        StartCoroutine(AdvanceAfterDelay());
    }

    public void UI_SkipTrap()
    {
        if (currentState != GameState.TrapTileDecision || isProcessing) return;
        isProcessing = true;
        StartCoroutine(AdvanceAfterDelay(0.3f));
    }

    public void UI_EquipPending()
    {
        if (currentState != GameState.EquipDecision || isProcessing || pendingLootItem == null) return;
        if (CurrentPlayer == null) return;
        
        isProcessing = true;
        LootData displaced = CurrentPlayer.Equip(pendingLootItem);
        onEquipmentChanged?.Invoke(CurrentPlayer);
        Notify(displaced != null
            ? $"⚔️ Equipped {pendingLootItem.lootName} — discarded {displaced.lootName}."
            : $"⚔️ Equipped {pendingLootItem.lootName}!");
        pendingLootItem = null;
        if (CheckWin()) return;
        ContinueAfterLoot(CurrentPlayer);
    }

    public void UI_DiscardPending()
    {
        if (currentState != GameState.EquipDecision || isProcessing) return;
        isProcessing = true;
        Notify($"🗑️ Discarded {pendingLootItem?.lootName}.");
        pendingLootItem = null;
        if (CurrentPlayer != null)
            ContinueAfterLoot(CurrentPlayer);
        else
            StartCoroutine(AdvanceAfterDelay());
    }

    public void UI_BuyShopItem()
    {
        if (currentState != GameState.ShopPhase || isProcessing || currentShopItem == null) return;
        if (CurrentPlayer == null) return;
        
        isProcessing = true;
        int cost = Mathf.Max(0, 200 - CurrentPlayer.GetShopDiscount());
        if (CurrentPlayer.gold < cost) { Notify($"Need {cost}g!"); isProcessing = false; return; }
        CurrentPlayer.gold -= cost;
        onPlayerGoldChanged?.Invoke(CurrentPlayer, -cost);
        GiveLootToPlayer(CurrentPlayer, currentShopItem, "Purchased");
        CurrentPlayer.PlayHappyFlip();
    }

    public void UI_CraftItems()
    {
        if (currentState != GameState.ShopPhase || !canCraft || isProcessing) return;
        if (CurrentPlayer == null) return;
        
        isProcessing = true;

        var equipped = CurrentPlayer.GetAllEquipped();
        int count    = Mathf.Min(2, equipped.Count);
        string sacrificed = "";
        for (int i = 0; i < count; i++)
        {
            sacrificed += equipped[i].item.lootName + (i == 0 ? " + " : "");
            CurrentPlayer.Unequip(equipped[i].slot);
        }

        LootData crafted = craftingResults[UnityEngine.Random.Range(0, craftingResults.Count)];
        onEquipmentChanged?.Invoke(CurrentPlayer);
        Notify($"⚗️ CRAFTED! Sacrificed: {sacrificed} → {crafted.lootName}[{crafted.rarity}]!");
        GiveLootToPlayer(CurrentPlayer, crafted, "Crafted");
        CurrentPlayer.PlayHappyFlip();
    }

    public void UI_LeaveShop()
    {
        if (currentState != GameState.ShopPhase || isProcessing) return;
        isProcessing = true;
        StartCoroutine(AdvanceAfterDelay(0.3f));
    }

    public void UI_RestartGame()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }

    public void OnPlayerDeath(Player p)
    {
        if (p == null) return;

        onPlayerDied?.Invoke(p);
        onEquipmentChanged?.Invoke(p);
        Notify($"💀 {p.playerName} died and is ELIMINATED!");

        p.currentHP = 0;
        p.gameObject.SetActive(false);

        // Remove ownership from their tiles permanently
        if (boardTiles != null)
        {
            foreach (var tile in boardTiles)
            {
                if (tile.ownerPlayerID == p.playerID)
                {
                    tile.SetOwnership(-1, 0, null);
                    // Spawn a fresh monster back on the unclaimed tile
                    if (possibleMonsters.Count > 0)
                        tile.SetupResidentMonster(GetRandomMonster());
                }
            }
        }

        // Clear combat reference if the dead player was the target
        if (combatTargetPlayer == p)
        {
            combatTargetPlayer = null;
        }

        if (CheckWin()) return;

        // If current player died, advance turn
        if (p == CurrentPlayer)
        {
            if (currentState == GameState.CombatRollPhase)
            {
                // Exit combat first, then end turn
                EndCombat(false);
            }
            else
            {
                ChangeState(GameState.EndTurn);
            }
        }
    }

    // ═════════════════════════════════════════════════════════
    // HELPERS & JUICE
    // ═════════════════════════════════════════════════════════

    public bool IsWanted(Player p)
    {
        if (boardTiles == null || p == null) return false;
        if (p.equippedAccessory != null && p.equippedAccessory.lootName == "Greed Gem") return true;
        return boardTiles.Count(t => t.ownerPlayerID == p.playerID) >= wantedTileThreshold;
    }

    private void GiveGold(Player p, int amount)
    {
        if (p == null) return;
        p.gold += amount;
        onPlayerGoldChanged?.Invoke(p, amount);
    }

    private bool CheckWin()
    {
        int aliveCount = 0;
        Player lastAlive = null;

        foreach (var p in players)
        {
            if (p.currentHP > 0)
            {
                aliveCount++;
                lastAlive = p;

                if (p.gold >= goldToWin)
                {
                    ChangeState(GameState.GameOver);
                    return true;
                }
            }
        }

        // Elimination condition - only one player remains
        if (aliveCount <= 1 && players.Count > 1)
        {
            ChangeState(GameState.GameOver);
            return true;
        }

        return false;
    }

    private bool TryFindNextAlivePlayer()
    {
        for (int i = 0; i < players.Count; i++)
        {
            int idx = (currentPlayerIndex + i) % players.Count;
            if (players[idx].currentHP > 0)
            {
                currentPlayerIndex = idx;
                return true;
            }
        }
        return false;
    }

    private void HandleGameOver()
    {
        Player winner = players.Where(p => p.currentHP > 0).OrderByDescending(p => p.gold).FirstOrDefault();
        if (winner == null) winner = players.OrderByDescending(p => p.gold).FirstOrDefault();
        
        if (winner != null)
        {
            Notify($"🏆 {winner.playerName} WINS with {winner.gold}g!");
            onPlayerWin?.Invoke(winner);
            NotifyGameOverOnChain(winner);
        }
    }

    private void HandleEndTurn()
    {
        if (players.Count == 0)
        {
            ChangeState(GameState.GameOver);
            return;
        }

        int checkedCount = 0;
        int startIndex = currentPlayerIndex;
        
        do
        {
            currentPlayerIndex = (currentPlayerIndex + 1) % players.Count;
            checkedCount++;
            
            // Safety: prevent infinite loop if all players are dead
            if (checkedCount > players.Count)
            {
                Debug.LogWarning("[GameManager] HandleEndTurn: Cycled through all players, triggering GameOver.");
                ChangeState(GameState.GameOver);
                return;
            }
        } 
        while (players[currentPlayerIndex].currentHP <= 0);

        isProcessing = false;
        ChangeState(GameState.StartTurn);
    }

    private void Notify(string msg)
    {
        Debug.Log($"<color=yellow>[Notify]</color> {msg}");
        onNotification?.Invoke(msg);
    }

    private MonsterData GetRandomMonster()
        => possibleMonsters[UnityEngine.Random.Range(0, possibleMonsters.Count)];

    private LootData GetRandomDrop(MonsterData monster)
    {
        var pool = (monster?.specificLootDrops?.Count > 0)
            ? monster.specificLootDrops
            : dropLootPool;
        return (pool != null && pool.Count > 0)
            ? pool[UnityEngine.Random.Range(0, pool.Count)]
            : null;
    }

    private IEnumerator AdvanceAfterDelay(float delay = 1.4f)
    {
        yield return new WaitForSeconds(delay);
        isProcessing = false;
        ChangeState(GameState.EndTurn);
    }
    
    private void ShakeCamera(float strength)
    {
        CameraController.Instance?.AddShake(strength);
    }

#if UNITY_EDITOR
    public void AutoAssignData()
    {
        Undo.RecordObject(this, "Auto-Assign Game Data");

        possibleMonsters.Clear();
        allAvailableCards.Clear();
        shopLootPool.Clear();
        dropLootPool.Clear();
        craftingResults.Clear();

        string[] monsterGuids = AssetDatabase.FindAssets("t:MonsterData");
        foreach (string guid in monsterGuids)
        {
            var asset = AssetDatabase.LoadAssetAtPath<MonsterData>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) possibleMonsters.Add(asset);
        }

        string[] cardGuids = AssetDatabase.FindAssets("t:CardData");
        foreach (string guid in cardGuids)
        {
            var asset = AssetDatabase.LoadAssetAtPath<CardData>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) allAvailableCards.Add(asset);
        }

        string[] lootGuids = AssetDatabase.FindAssets("t:LootData");
        foreach (string guid in lootGuids)
        {
            var asset = AssetDatabase.LoadAssetAtPath<LootData>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset == null) continue;

            if (asset.isCraftedOnly) craftingResults.Add(asset);
            else
            {
                dropLootPool.Add(asset);
                if (asset.rarity == LootRarity.Common || asset.rarity == LootRarity.Uncommon)
                    shopLootPool.Add(asset);
            }
        }

        EditorUtility.SetDirty(this);
    }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(GameManager))]
public class GameManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(20);
        
        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("⚡ Auto-Assign All Data from Project", GUILayout.Height(35)))
        {
            ((GameManager)target).AutoAssignData();
        }
        GUI.backgroundColor = Color.white;
        
        GUILayout.Space(10);
    }
}
#endif