#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// ============================================================
// LOOTOPOLY – LootopolyDataManager  (Equipment v2.3 — BUILD FIX)
// ============================================================
// FIX v2.3: Entire file is now wrapped in #if UNITY_EDITOR.
// Previously, the bare `using UnityEditor;` at the top caused
// a compile error in WebGL / standalone builds because the
// UnityEditor assembly is stripped at build time.
// Placing an Editor-only class inside Assets/Editor/ folder OR
// wrapping it in #if UNITY_EDITOR are both valid solutions.
// We use the #if guard here so the file can stay in any folder.
// ============================================================

public class LootopolyDataManager : EditorWindow
{
    private Vector2 sidebarScroll;
    private Vector2 mainScroll;
    private ScriptableObject selectedAsset;
    private Editor cachedEditor;
    private string searchQuery = "";

    // Action tracking for generation
    private enum GenResult { Created, Updated, Skipped }

    // Caching system for performance
    private class AssetCategory
    {
        public string label;
        public string folder;
        public System.Type type;
        public bool isExpanded = true;
        public List<ScriptableObject> assets = new List<ScriptableObject>();
    }
    private List<AssetCategory> categories;

    [MenuItem("Lootopoly/Game Data Manager")]
    public static void Open()
    {
        var w = GetWindow<LootopolyDataManager>("Lootopoly DB");
        w.minSize = new Vector2(900, 650);
        w.Show();
    }

    private void OnEnable()
    {
        InitializeCategories();
        RefreshCache(false);
    }

    private void OnDisable()
    {
        if (cachedEditor != null) DestroyImmediate(cachedEditor);
    }

    private void OnGUI()
    {
        DrawToolbar();

        EditorGUILayout.BeginHorizontal();

        // ── Sidebar ───────────────────────────────────────────
        EditorGUILayout.BeginVertical("box", GUILayout.Width(300), GUILayout.ExpandHeight(true));
        
        // Search Bar
        EditorGUILayout.BeginHorizontal();
        searchQuery = EditorGUILayout.TextField(searchQuery, EditorStyles.toolbarSearchField);
        if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(24))) {
            searchQuery = "";
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(5);

        sidebarScroll = EditorGUILayout.BeginScrollView(sidebarScroll);

        foreach (var category in categories)
        {
            DrawAssetSection(category);
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        // ── Inspector workspace ───────────────────────────────
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
        DrawWorkspace();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    // ═════════════════════════════════════════════════════════
    // UI DRAWING
    // ═════════════════════════════════════════════════════════

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label(" 🎲 Lootopoly Data Manager v2.3", EditorStyles.boldLabel);
        
        if (GUILayout.Button("🔄 Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
            RefreshCache(true);
            
        if (GUILayout.Button("💾 Save All", EditorStyles.toolbarButton, GUILayout.Width(70)))
        {
            AssetDatabase.SaveAssets();
            ShowNotification(new GUIContent("💾 All Data Saved!"));
            Debug.Log("<color=cyan>[DataManager]</color> All assets saved successfully.");
        }

        GUILayout.FlexibleSpace();

        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("⚡ Generate All MVP Data", EditorStyles.toolbarButton))
        {
            int choice = EditorUtility.DisplayDialogComplex("Generate Data",
                "This will generate the full MVP data set.\n\nIf assets already exist, do you want to overwrite their data or skip them?",
                "Overwrite Existing", "Cancel", "Skip Existing");

            if (choice == 0) GenerateAllData(true);
            else if (choice == 2) GenerateAllData(false);
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    private void DrawAssetSection(AssetCategory category)
    {
        GUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        
        string foldoutLabel = $"{category.label} ({category.assets.Count})";
        category.isExpanded = EditorGUILayout.Foldout(category.isExpanded, foldoutLabel, true, EditorStyles.foldoutHeader);
        
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(24)))
        {
            CreateNewAsset(category);
        }
        EditorGUILayout.EndHorizontal();

        if (!category.isExpanded) return;

        if (!AssetDatabase.IsValidFolder(category.folder) || category.assets.Count == 0)
        { 
            GUILayout.Label("   (Empty)", EditorStyles.miniLabel); 
            return; 
        }

        foreach (var asset in category.assets)
        {
            if (asset == null) continue;

            // Apply Search Filter
            if (!string.IsNullOrEmpty(searchQuery) && !asset.name.ToLower().Contains(searchQuery.ToLower()))
                continue;

            EditorGUILayout.BeginHorizontal();
            bool isSelected = selectedAsset == asset;
            GUI.backgroundColor = isSelected ? new Color(0.35f, 0.75f, 1f) : Color.white;
            
            if (GUILayout.Button($" {asset.name}", EditorStyles.miniButtonLeft, GUILayout.Height(22), GUILayout.ExpandWidth(true)))
                SelectAsset(asset);
                
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(22), GUILayout.Height(22)))
            {
                if (EditorUtility.DisplayDialog("Delete Asset", $"Are you sure you want to delete '{asset.name}'?\nThis cannot be undone.", "Delete", "Cancel"))
                {
                    string path = AssetDatabase.GetAssetPath(asset);
                    string nameStr = asset.name;
                    if (selectedAsset == asset) SelectAsset(null);
                    
                    AssetDatabase.DeleteAsset(path);
                    Debug.Log($"<color=red>[DataManager]</color> Deleted asset: {nameStr} at {path}");
                    
                    RefreshCache(false);
                    GUIUtility.ExitGUI();
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawWorkspace()
    {
        if (selectedAsset == null)
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("← Select or create an asset from the sidebar", EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
            return;
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(selectedAsset.name, EditorStyles.largeLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("🔍 Ping in Project", GUILayout.Width(120)))
            EditorGUIUtility.PingObject(selectedAsset);
        EditorGUILayout.EndHorizontal();
        
        GUILayout.Label($"Type: {selectedAsset.GetType().Name}   |   Path: {AssetDatabase.GetAssetPath(selectedAsset)}", EditorStyles.miniLabel);
        
        GUILayout.Space(6);
        EditorGUI.DrawRect(EditorGUILayout.GetControlRect(false, 1), Color.gray);
        GUILayout.Space(4);

        if (cachedEditor != null)
        {
            cachedEditor.serializedObject.Update();
            cachedEditor.OnInspectorGUI();
            cachedEditor.serializedObject.ApplyModifiedProperties();
        }

        GUILayout.Space(15);
        if (GUILayout.Button("🔄 Rename file to match internal Name field", GUILayout.Height(30)))
            RenameAsset();
    }

    // ═════════════════════════════════════════════════════════
    // DATA MANAGEMENT & CACHING
    // ═════════════════════════════════════════════════════════

    private void InitializeCategories()
    {
        categories = new List<AssetCategory>
        {
            new AssetCategory { label = "🃏 Cards",              folder = "Assets/Data/Cards",            type = typeof(CardData) },
            new AssetCategory { label = "⚔️ Weapons",            folder = "Assets/Data/Loot/Weapons",     type = typeof(LootData) },
            new AssetCategory { label = "🛡️ Armor",               folder = "Assets/Data/Loot/Armor",       type = typeof(LootData) },
            new AssetCategory { label = "👟 Boots",               folder = "Assets/Data/Loot/Boots",       type = typeof(LootData) },
            new AssetCategory { label = "💍 Accessories",         folder = "Assets/Data/Loot/Accessories", type = typeof(LootData) },
            new AssetCategory { label = "⚗️ Crafted (Legendary)", folder = "Assets/Data/Loot/Crafted",     type = typeof(LootData) },
            new AssetCategory { label = "👾 Monsters",            folder = "Assets/Data/Monsters",         type = typeof(MonsterData) }
        };
    }

    private void RefreshCache(bool notifyUser)
    {
        int totalFound = 0;
        foreach (var cat in categories)
        {
            cat.assets.Clear();
            if (AssetDatabase.IsValidFolder(cat.folder))
            {
                string[] guids = AssetDatabase.FindAssets($"t:{cat.type.Name}", new[] { cat.folder });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var asset = AssetDatabase.LoadAssetAtPath(path, cat.type) as ScriptableObject;
                    if (asset != null) cat.assets.Add(asset);
                }
                cat.assets = cat.assets.OrderBy(a => a.name).ToList();
                totalFound += cat.assets.Count;
            }
        }

        if (notifyUser)
        {
            ShowNotification(new GUIContent($"🔄 Refreshed Database ({totalFound} items)"));
            Debug.Log($"<color=cyan>[DataManager]</color> Database refreshed. Found {totalFound} total assets.");
        }
    }

    private void SelectAsset(ScriptableObject a)
    {
        selectedAsset = a;
        if (cachedEditor != null) DestroyImmediate(cachedEditor);
        if (a != null) { 
            cachedEditor = Editor.CreateEditor(a); 
            GUI.FocusControl(null);
        }
    }

    private void CreateNewAsset(AssetCategory category)
    {
        EnsureFolder(category.folder);
        var asset = ScriptableObject.CreateInstance(category.type);
        string path = AssetDatabase.GenerateUniqueAssetPath($"{category.folder}/New{category.type.Name}.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        
        Debug.Log($"<color=green>[DataManager]</color> Created new asset at {path}");
        
        RefreshCache(false);
        SelectAsset(asset);
        EditorGUIUtility.PingObject(asset);
    }

    private void RenameAsset()
    {
        if (selectedAsset == null) return;
        string path = AssetDatabase.GetAssetPath(selectedAsset);
        string oldName = selectedAsset.name;
        
        foreach (string field in new[] { "lootName", "cardName", "monsterName" })
        {
            var sp = new SerializedObject(selectedAsset).FindProperty(field);
            if (sp != null && !string.IsNullOrEmpty(sp.stringValue))
            {
                string safeName = string.Join("_", sp.stringValue.Split(Path.GetInvalidFileNameChars())).Replace(" ", "");
                
                if (safeName != oldName)
                {
                    AssetDatabase.RenameAsset(path, safeName);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"<color=yellow>[DataManager]</color> Renamed '{oldName}' ➔ '{safeName}'");
                    ShowNotification(new GUIContent($"Renamed to {safeName}"));
                    RefreshCache(false);
                }
                else
                {
                    ShowNotification(new GUIContent("Name is already matching."));
                }
                return;
            }
        }
        Debug.LogWarning("<color=orange>[DataManager]</color> Could not find a valid internal name field to rename the asset.");
    }

    // ═════════════════════════════════════════════════════════
    // DATA GENERATION FACTORY
    // ═════════════════════════════════════════════════════════

    private void GenerateAllData(bool overwrite)
    {
        int created = 0, updated = 0, skipped = 0;

        void Track(GenResult result)
        {
            if (result == GenResult.Created) created++;
            else if (result == GenResult.Updated) updated++;
            else if (result == GenResult.Skipped) skipped++;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Generating Data", "Creating Folders...", 0f);
            
            string[] folders = {
                "Assets/Data/Cards", "Assets/Data/Loot/Weapons", "Assets/Data/Loot/Armor",
                "Assets/Data/Loot/Boots", "Assets/Data/Loot/Accessories", "Assets/Data/Loot/Crafted", "Assets/Data/Monsters"
            };
            foreach (var f in folders) EnsureFolder(f);

            EditorUtility.DisplayProgressBar("Generating Data", "Creating Cards...", 0.2f);
            Track(MakeCard("SwapMeet",     CardEffect.SwapMeet,    true,  false, "Instantly swap board positions with any player.", overwrite));
            Track(MakeCard("TaxFraud",     CardEffect.TaxFraud,    false, false, "Steal exactly 20% of the richest player's current gold.", overwrite));
            Track(MakeCard("MeteorStrike", CardEffect.MeteorStrike,false, true,  "Destroy all ownership on a target tile. The monster respawns.", overwrite));
            Track(MakeCard("GreasedShoes", CardEffect.GreasedShoes,true,  false, "Target takes 1 HP damage for every space they move next turn.", overwrite));
            Track(MakeCard("PocketSand",   CardEffect.PocketSand,  true,  false, "Target player loses their entire Action Phase next turn.", overwrite));
            Track(MakeCard("DoubleDown",   CardEffect.DoubleDown,  false, false, "Your next combat or movement roll result is doubled. Risk: rolling 1-2 costs 2 HP.", overwrite));
            Track(MakeCard("Jackpot",      CardEffect.Jackpot,     false, false, "Drain 10% of gold from ALL other players simultaneously.", overwrite));
            Track(MakeCard("GoldRush",     CardEffect.GoldRush,    false, false, "Your owned tiles charge double toll until your next turn.", overwrite));

            EditorUtility.DisplayProgressBar("Generating Data", "Creating Loot...", 0.5f);
            Track(MakeLoot("RustyDagger",    "Assets/Data/Loot/Weapons",     EquipSlot.Weapon,    LootRarity.Common,    "Old but sharp.",                   l => { l.attackBonus = 1; }, overwrite));
            Track(MakeLoot("IronSword",      "Assets/Data/Loot/Weapons",     EquipSlot.Weapon,    LootRarity.Common,    "Standard issue.",                  l => { l.attackBonus = 2; }, overwrite));
            Track(MakeLoot("SteelBlade",     "Assets/Data/Loot/Weapons",     EquipSlot.Weapon,    LootRarity.Uncommon,  "Reliable in a fight.",             l => { l.attackBonus = 3; l.killBonusGold = 30; }, overwrite));
            Track(MakeLoot("VenomFang",      "Assets/Data/Loot/Weapons",     EquipSlot.Weapon,    LootRarity.Rare,      "Guaranteed loot on every kill.",   l => { l.attackBonus = 3; l.hasGuaranteedLootChance = true; l.rerollBestOf = 2; }, overwrite));
            Track(MakeLoot("DragonFang",     "Assets/Data/Loot/Crafted",     EquipSlot.Weapon,    LootRarity.Legendary, "Ultimate weapon.",                 l => { l.attackBonus = 5; l.hasCriticalStrike = true; l.killBonusGold = 120; l.isCraftedOnly = true; }, overwrite));
            Track(MakeLoot("LeatherJerkin",  "Assets/Data/Loot/Armor",       EquipSlot.Armor,     LootRarity.Common,    "Reduces damage by 1.",             l => { l.defenseBonus = 1; }, overwrite));
            Track(MakeLoot("ChainMail",      "Assets/Data/Loot/Armor",       EquipSlot.Armor,     LootRarity.Uncommon,  "Solid protection.",                l => { l.defenseBonus = 2; l.thornsDamage = 1; }, overwrite));
            Track(MakeLoot("TowerShield",    "Assets/Data/Loot/Armor",       EquipSlot.Armor,     LootRarity.Rare,      "Blocks the first trap.",           l => { l.defenseBonus = 2; l.trapShieldCharges = 2; }, overwrite));
            Track(MakeLoot("PhoenixPlate",   "Assets/Data/Loot/Crafted",     EquipSlot.Armor,     LootRarity.Legendary, "Impenetrable.",                    l => { l.defenseBonus = 3; l.hasLastStand = true; l.thornsDamage = 2; l.trapShieldCharges = 3; l.isCraftedOnly = true; }, overwrite));
            Track(MakeLoot("WornSandals",    "Assets/Data/Loot/Boots",       EquipSlot.Boots,     LootRarity.Common,    "+1 move.",                         l => { l.moveBonus = 1; }, overwrite));
            Track(MakeLoot("TravelBoots",    "Assets/Data/Loot/Boots",       EquipSlot.Boots,     LootRarity.Uncommon,  "+2 move and a shop discount.",     l => { l.moveBonus = 2; l.shopDiscount = 25; }, overwrite));
            Track(MakeLoot("ShadowSteps",    "Assets/Data/Loot/Boots",       EquipSlot.Boots,     LootRarity.Rare,      "Dodge traps on a 4+.",             l => { l.moveBonus = 1; l.hasTrapDodge = true; l.hasGhostStep = true; }, overwrite));
            Track(MakeLoot("HermesGreaves",  "Assets/Data/Loot/Crafted",     EquipSlot.Boots,     LootRarity.Legendary, "Master of the board.",             l => { l.moveBonus = 3; l.canMoveBackward = true; l.hasGhostStep = true; l.hasTrapDodge = true; l.shopDiscount = 50; l.isCraftedOnly = true; }, overwrite));
            Track(MakeLoot("FatCoinPouch",   "Assets/Data/Loot/Accessories", EquipSlot.Accessory, LootRarity.Common,    "+25g per turn.",                   l => { l.passiveGoldPerTurn = 25; }, overwrite));
            Track(MakeLoot("MerchantRing",   "Assets/Data/Loot/Accessories", EquipSlot.Accessory, LootRarity.Uncommon,  "Earn 50g per turn.",               l => { l.passiveGoldPerTurn = 50; l.goldMultiplier = 1.25f; }, overwrite));
            Track(MakeLoot("VampireAmulet",  "Assets/Data/Loot/Accessories", EquipSlot.Accessory, LootRarity.Rare,      "Lifesteal on tolls.",              l => { l.passiveGoldPerTurn = 25; l.tollLifestealPercent = 0.2f; l.hasCardReflect = true; }, overwrite));
            Track(MakeLoot("OuroborosRing",  "Assets/Data/Loot/Crafted",     EquipSlot.Accessory, LootRarity.Legendary, "Pure dominance.",                  l => { l.passiveGoldPerTurn = 100; l.goldMultiplier = 2.0f; l.tollLifestealPercent = 0.40f; l.hasCardReflect = true; l.hasDoubleCardDraw = true; l.isCraftedOnly = true; }, overwrite));

            EditorUtility.DisplayProgressBar("Generating Data", "Creating Monsters...", 0.8f);
            Track(MakeMonster("WildSlime",       "Wild Slime",       1, 2, 1, 0,  50,  0.20f, "It wiggles. That's about it.", overwrite));
            Track(MakeMonster("RatKing",         "Rat King",         1, 3, 1, 1,  75,  0.30f, "He wears a tiny crown.", overwrite));
            Track(MakeMonster("ThiefGoblin",     "Thief Goblin",     2, 4, 2, 1, 120,  0.45f, "Spent your gold already.", overwrite));
            Track(MakeMonster("BoulderTroll",    "Boulder Troll",    2, 6, 3, 2, 160,  0.40f, "Just wants a crushing hug.", overwrite));
            Track(MakeMonster("GeoffTheDragon",  "Geoff the Dragon", 3, 8, 4, 2, 260,  0.80f, "Hoard contains receipts.", overwrite));
            Track(MakeMonster("CorporateWizard", "Corporate Wizard", 3, 7, 5, 3, 310,  0.85f, "Has a 4:30 meeting.", overwrite));

            AssetDatabase.SaveAssets();
            RefreshCache(false);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<b>--- Generation Summary ---</b>");
            sb.AppendLine($"✨ Created new assets: <b>{created}</b>");
            if (overwrite) sb.AppendLine($"🔄 Updated existing assets: <b>{updated}</b>");
            if (!overwrite) sb.AppendLine($"⏭️ Skipped existing assets: <b>{skipped}</b>");
            
            sb.AppendLine("\n<b>--- Current Database Totals ---</b>");
            foreach (var cat in categories) {
                sb.AppendLine($"• {cat.label}: {cat.assets.Count}");
            }

            Debug.Log($"<color=lime>[DataManager]</color> MVP Data Generation Complete!\n{sb.ToString()}");

            string popupMsg = sb.ToString().Replace("<b>", "").Replace("</b>", "");
            EditorUtility.DisplayDialog("Generation Complete!", popupMsg, "Awesome!");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ═════════════════════════════════════════════════════════
    // FACTORY HELPERS (Safe Update Pattern)
    // ═════════════════════════════════════════════════════════

    private GenResult MakeCard(string assetName, CardEffect effect, bool targetPlayer, bool targetTile, string desc, bool overwrite)
    {
        string path = $"Assets/Data/Cards/{assetName}.asset";
        CardData a = AssetDatabase.LoadAssetAtPath<CardData>(path);
        bool isNew = (a == null);

        if (!isNew && !overwrite) return GenResult.Skipped;
        if (isNew) a = ScriptableObject.CreateInstance<CardData>();

        a.cardName             = assetName;
        a.effectType           = effect;
        a.description          = desc;
        a.requiresTargetPlayer = targetPlayer;
        a.requiresTargetTile   = targetTile;

        if (isNew) {
            AssetDatabase.CreateAsset(a, path);
            return GenResult.Created;
        } else {
            EditorUtility.SetDirty(a);
            return GenResult.Updated;
        }
    }

    private GenResult MakeLoot(string assetName, string folder, EquipSlot slot, LootRarity rarity, string desc, System.Action<LootData> configure, bool overwrite)
    {
        string path = $"{folder}/{assetName}.asset";
        LootData a = AssetDatabase.LoadAssetAtPath<LootData>(path);
        bool isNew = (a == null);

        if (!isNew && !overwrite) return GenResult.Skipped;
        if (isNew) a = ScriptableObject.CreateInstance<LootData>();

        a.lootName    = assetName;
        a.slot        = slot;
        a.rarity      = rarity;
        a.description = desc;
        configure(a);

        if (isNew) {
            AssetDatabase.CreateAsset(a, path);
            return GenResult.Created;
        } else {
            EditorUtility.SetDirty(a);
            return GenResult.Updated;
        }
    }

    private GenResult MakeMonster(string assetName, string displayName, int tier, int hp, int atk, int def, int gold, float dropChance, string flavor, bool overwrite)
    {
        string path = $"Assets/Data/Monsters/{assetName}.asset";
        MonsterData a = AssetDatabase.LoadAssetAtPath<MonsterData>(path);
        bool isNew = (a == null);

        if (!isNew && !overwrite) return GenResult.Skipped;
        if (isNew) a = ScriptableObject.CreateInstance<MonsterData>();

        a.monsterName     = displayName;
        a.difficultyTier  = tier;
        a.maxHP           = hp;
        a.attack          = atk;
        a.defense         = def;
        a.goldReward      = gold;
        a.lootDropChance  = dropChance;
        a.flavorText      = flavor;

        if (isNew) {
            AssetDatabase.CreateAsset(a, path);
            return GenResult.Created;
        } else {
            EditorUtility.SetDirty(a);
            return GenResult.Updated;
        }
    }

    private void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string[] folders = path.Split('/');
        string currentPath = folders[0];

        for (int i = 1; i < folders.Length; i++)
        {
            string nextPath = currentPath + "/" + folders[i];
            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                AssetDatabase.CreateFolder(currentPath, folders[i]);
            }
            currentPath = nextPath;
        }
    }
}
#endif // UNITY_EDITOR