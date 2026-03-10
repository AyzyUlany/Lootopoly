using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public class GameUI : MonoBehaviour
{
    [Header("Dependencies")]
    public GameManager gameManager;

    // ── Serialisable sub-types for JSON ───────────────────────
    [System.Serializable]
    private class PlayerDTO
    {
        public string name;
        public int    hp;
        public int    maxHp;
        public int    gold;
        public bool   wanted;
        public string wpn;
        public string arm;
        public string bts;
        public string acc;
    }

    [System.Serializable]
    private class CardDTO
    {
        public string name;
        public string desc;
    }

    [System.Serializable]
    private class GameStateDTO
    {
        public PlayerDTO[] players;
        public int         activeIdx;
        public string      state;
        public string      turnName;
        public CardDTO[]   cards;
        // Combat
        public string      combatName;
        public int         combatHp;
        public int         combatDef;
        public string      combatFlavor;
        public bool        hasCrit;
        // Buy/Tile
        public string      tileInfo;
        // Equip
        public string      equipTitle;
        public string      equipNew;
        public string      equipOld;
        // Shop
        public string      shopInfo;
        public bool        canCraft;
        // Game Over
        public string      winnerName;
        public int         winnerGold;
        // Live feedback
        public string      roll;
        public string      log;
        public string      toast;
        public string      toastType;
        // Permissions
        public bool        isMyTurn;
    }

    [System.Serializable]
    private class ToastDTO
    {
        public string msg;
        public string type;
    }

    // ── State tracking ────────────────────────────────────────
    private GameState lastState = (GameState)(-1);

    // Pending card index selected by HTML (set via UI_PlayCard_Index)
    private int pendingCardIndex = -1;

    // ═════════════════════════════════════════════════════════
    // LIFECYCLE
    // ═════════════════════════════════════════════════════════

    private void OnEnable()
    {
        Subscribe();
        // Initial log
        SendLog("⚔️ <b>Welcome to Lootopoly!</b>");
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        // Push HUD updates every frame so gold/HP stay live.
        if (gameManager == null || gameManager.players.Count == 0) return;
        PushHUDOnly();
    }

    // ═════════════════════════════════════════════════════════
    // EVENT SUBSCRIPTIONS
    // ═════════════════════════════════════════════════════════

    private void Subscribe()
    {
        if (!gameManager) return;
        gameManager.onNotification.AddListener(OnNotification);
        gameManager.onStateChanged.AddListener(OnStateChanged);
        gameManager.onPlayerWin.AddListener(OnPlayerWin);
        gameManager.onEquipmentChanged.AddListener(_ => PushHUDOnly());
        gameManager.onPlayerDied.AddListener(p =>
            SendLog($"💀 <b>{p.playerName} has fallen!</b>"));
    }

    private void Unsubscribe()
    {
        if (!gameManager) return;
        gameManager.onNotification.RemoveListener(OnNotification);
        gameManager.onStateChanged.RemoveListener(OnStateChanged);
        gameManager.onPlayerWin.RemoveListener(OnPlayerWin);
        gameManager.onEquipmentChanged.RemoveListener(_ => PushHUDOnly());
    }

    // ═════════════════════════════════════════════════════════
    // STATE → HTML
    // ═════════════════════════════════════════════════════════

    private void OnStateChanged()
    {
        if (gameManager.currentState == lastState &&
            gameManager.currentState != GameState.CombatRollPhase) return;
        lastState = gameManager.currentState;

        PushFullState(log: null, toast: null);
    }

    private void OnNotification(string msg)
    {
        // Notifications appear as log entries AND brief toasts.
        PushFullState(log: msg, toast: msg, toastType: "info");
    }

    private void OnPlayerWin(Player winner)
    {
        if (winner == null) return;
        PushFullState(log: $"🏆 {winner.playerName} wins with {winner.gold}g!",
                      toast: $"🏆 {winner.playerName} wins!",
                      toastType: "success");
    }

    // ── Core push ─────────────────────────────────────────────

    private void PushHUDOnly()
    {
        // Lightweight update — only player data + active idx, no panel switches.
        var dto = new GameStateDTO
        {
            players   = BuildPlayerDTOs(),
            activeIdx = gameManager.currentPlayerIndex,
            state     = "",   // empty → HTML ignores panel switching
            turnName  = ActivePlayer()?.playerName ?? "",
            isMyTurn  = IsLocalPlayerTurn()
        };
        LootopolyUIBridge.UpdateGameState(JsonUtility.ToJson(dto));
    }

    private void PushFullState(string log, string toast, string toastType = "info")
    {
        if (gameManager == null) return;

        Player cp       = ActivePlayer();
        GameState state = gameManager.currentState;
        bool myTurn     = IsLocalPlayerTurn();

        var dto = new GameStateDTO
        {
            players   = BuildPlayerDTOs(),
            activeIdx = gameManager.currentPlayerIndex,
            state     = state.ToString(),
            turnName  = cp?.playerName ?? "",
            log       = log,
            toast     = toast,
            toastType = toastType,
            isMyTurn  = myTurn
        };

        // State-specific payload (Private info is ONLY sent if it is the local player's turn)
        switch (state)
        {
            case GameState.ActionPhase:
                if (myTurn) dto.cards = BuildCardDTOs(cp);
                break;

            case GameState.CombatRollPhase:
                dto.combatName   = gameManager.combatMonsterName;
                dto.combatHp     = gameManager.combatMonsterHP;
                dto.combatDef    = gameManager.combatMonsterDef;
                dto.combatFlavor = gameManager.combatMonsterFlavor;
                dto.hasCrit      = cp != null && cp.HasCriticalStrike();
                break;

            case GameState.BuyTileDecision:
                if (myTurn && gameManager.boardTiles != null && cp != null)
                {
                    Tile t = gameManager.boardTiles[cp.currentTileIndex];
                    dto.tileInfo = $"{t.tileName}\nCost: {t.baseCost}g";
                }
                break;

            case GameState.EquipDecision:
                LootData newItem = gameManager.pendingLootItem;
                if (myTurn && newItem != null && cp != null)
                {
                    dto.equipTitle = $"New {newItem.slot} Found!";
                    dto.equipNew   = FormatItemSheet(newItem);
                    LootData current = newItem.slot switch
                    {
                        EquipSlot.Weapon    => cp.equippedWeapon,
                        EquipSlot.Armor     => cp.equippedArmor,
                        EquipSlot.Boots     => cp.equippedBoots,
                        EquipSlot.Accessory => cp.equippedAccessory,
                        _                   => null
                    };
                    dto.equipOld = current != null ? FormatItemSheet(current) : "(slot empty)";
                }
                break;

            case GameState.ShopPhase:
                LootData shopItem = gameManager.currentShopItem;
                if (myTurn)
                {
                    if (shopItem != null && cp != null)
                    {
                        int cost = Mathf.Max(0, 200 - cp.GetShopDiscount());
                        dto.shopInfo = FormatItemSheet(shopItem) + $"\n\n💰 Price: {cost}g";
                    }
                    else
                    {
                        dto.shopInfo = "(Nothing in stock today)";
                    }
                    dto.canCraft = gameManager.canCraft;
                }
                break;

            case GameState.GameOver:
                Player winner = FindWinner();
                dto.winnerName = winner?.playerName ?? "—";
                dto.winnerGold = winner != null ? (int)winner.gold : 0;
                break;
        }

        LootopolyUIBridge.UpdateGameState(JsonUtility.ToJson(dto));
    }

    // ═════════════════════════════════════════════════════════
    // HTML → UNITY  (called by SendMessage from JS)
    // ═════════════════════════════════════════════════════════

    // These are the method names the HTML calls via:
    //   unityInstance.SendMessage('GameUI', '<Method>', '<param>')
    
    // Verifies permissions using IsLocalPlayerTurn() before resolving.
    public void UI_RollMovementDice()  { if (IsLocalPlayerTurn()) gameManager.UI_RollMovementDice(); }
    public void UI_RollCombatDice()    { if (IsLocalPlayerTurn()) gameManager.UI_RollCombatDice(); }
    public void UI_SkipActionPhase()   { if (IsLocalPlayerTurn()) gameManager.UI_SkipActionPhase(); }
    public void UI_Flee()              { if (IsLocalPlayerTurn()) gameManager.UI_Flee(); }
    public void UI_BuyTile()           { if (IsLocalPlayerTurn()) gameManager.UI_BuyTile(); }
    public void UI_SkipBuyTile()       { if (IsLocalPlayerTurn()) gameManager.UI_SkipBuyTile(); }
    public void UI_UpgradeTile()       { if (IsLocalPlayerTurn()) gameManager.UI_UpgradeTile(); }
    public void UI_SkipUpgrade()       { if (IsLocalPlayerTurn()) gameManager.UI_SkipUpgrade(); }
    public void UI_SetTrap()           { if (IsLocalPlayerTurn()) gameManager.UI_SetTrap(); }
    public void UI_SkipTrap()          { if (IsLocalPlayerTurn()) gameManager.UI_SkipTrap(); }
    public void UI_EquipPending()      { if (IsLocalPlayerTurn()) gameManager.UI_EquipPending(); }
    public void UI_DiscardPending()    { if (IsLocalPlayerTurn()) gameManager.UI_DiscardPending(); }
    public void UI_BuyShopItem()       { if (IsLocalPlayerTurn()) gameManager.UI_BuyShopItem(); }
    public void UI_CraftItems()        { if (IsLocalPlayerTurn()) gameManager.UI_CraftItems(); }
    public void UI_LeaveShop()         { if (IsLocalPlayerTurn()) gameManager.UI_LeaveShop(); }
    public void UI_RestartGame()       { /* Always allowed */ gameManager.UI_RestartGame(); }

    /// <summary>
    /// Called when HTML sends a card index string, e.g. "2".
    /// Resolves to the Player's handOfCards[index] and plays it.
    /// Auto-selects a random target player and the current tile.
    /// </summary>
    public void UI_PlayCard_Index(string indexStr)
    {
        if (!IsLocalPlayerTurn()) return;
        if (!int.TryParse(indexStr, out int idx)) return;

        Player cp = ActivePlayer();
        if (cp == null || idx < 0 || idx >= cp.handOfCards.Count) return;

        CardData card    = cp.handOfCards[idx];
        var others       = gameManager.players.Where(x => x != cp && x.currentHP > 0).ToList();
        Player target    = others.Count > 0 ? others[Random.Range(0, others.Count)] : null;
        Tile targetTile  = gameManager.boardTiles != null
                           ? gameManager.boardTiles[cp.currentTileIndex] : null;

        gameManager.UI_PlayCard(card, target, targetTile);

        // Refresh action panel after playing
        PushFullState(log: $"🃏 {cp.playerName} played <b>{card.cardName}</b>!", toast: null);
    }

    /// <summary>HTML sends "true" or "false" as a string.</summary>
    public void UI_ChooseMoveDirection(string backwardStr)
    {
        if (!IsLocalPlayerTurn()) return;
        bool backward = backwardStr?.ToLower() == "true";
        gameManager.UI_ChooseMoveDirection(backward);
    }

    // ═════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════

    private bool IsLocalPlayerTurn()
    {
        if (gameManager == null) return false;
        
        // Anyone can hit the restart button during game over
        if (gameManager.currentState == GameState.GameOver) return true;
        
        // Always allowed offline
        if (gameManager.fullDevTestMode) return true;
        
        Player cp = ActivePlayer();
        if (cp == null) return false;
        
        // If player has no wallet address, allow (offline mode)
        if (string.IsNullOrEmpty(cp.walletAddress)) return true;
        
        // Check Web3 connection
        if (Web3Manager.Instance == null || string.IsNullOrEmpty(Web3Manager.Instance.WalletAddress)) return true;
        
        return cp.walletAddress.Equals(Web3Manager.Instance.WalletAddress, System.StringComparison.OrdinalIgnoreCase);
    }

    private Player ActivePlayer()
    {
        if (gameManager == null || gameManager.players.Count == 0) return null;
        int idx = Mathf.Clamp(gameManager.currentPlayerIndex, 0, gameManager.players.Count - 1);
        return gameManager.players[idx];
    }

    private Player FindWinner()
    {
        if (gameManager == null || gameManager.players.Count == 0) return null;
        Player winner = gameManager.players.Where(p => p.currentHP > 0).OrderByDescending(p => p.gold).FirstOrDefault();
        return winner ?? gameManager.players.OrderByDescending(p => p.gold).FirstOrDefault();
    }

    private PlayerDTO[] BuildPlayerDTOs()
    {
        if (gameManager == null || gameManager.players == null) 
            return new PlayerDTO[0];

        var list = new PlayerDTO[gameManager.players.Count];
        for (int i = 0; i < gameManager.players.Count; i++)
        {
            Player p = gameManager.players[i];
            list[i] = new PlayerDTO
            {
                name   = p?.playerName ?? "Unknown",
                hp     = p?.currentHP ?? 0,
                maxHp  = p?.maxHP ?? 100,
                gold   = (int)(p?.gold ?? 0),
                wanted = gameManager.boardTiles != null && p != null && gameManager.IsWanted(p),
                wpn    = p?.equippedWeapon?.lootName,
                arm    = p?.equippedArmor?.lootName,
                bts    = p?.equippedBoots?.lootName,
                acc    = p?.equippedAccessory?.lootName
            };
        }
        return list;
    }

    private CardDTO[] BuildCardDTOs(Player p)
    {
        if (p == null || p.handOfCards == null) return new CardDTO[0];
        return p.handOfCards.Select(c => new CardDTO
        {
            name = c?.cardName ?? "Unknown",
            desc = c?.description ?? ""
        }).ToArray();
    }

    private string FormatItemSheet(LootData item)
    {
        if (item == null) return "(empty)";
        
        var sb = new StringBuilder();
        sb.AppendLine(item.lootName);
        sb.AppendLine($"[{item.rarity}]");
        sb.AppendLine();
        sb.Append(item.description);
        return sb.ToString().TrimEnd();
    }

    private void SendLog(string msg)
    {
        LootopolyUIBridge.LogEvent(msg);
    }
}