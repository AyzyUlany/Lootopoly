using UnityEngine;
using System.Collections.Generic;

// ============================================================
// LOOTOPOLY – MonsterData
// ============================================================
// ScriptableObject defining one monster type.
// Monsters guard unowned Property tiles. Defeating one is
// the only way to purchase that tile.
//
// Tiers:
//   1 = Early-game (Wild Slime, Rat King)
//   2 = Mid-game  (Thief Goblin, Boulder Troll)
//   3 = Boss      (Geoff the Dragon, Corporate Wizard)
// ============================================================

[CreateAssetMenu(fileName = "New Monster", menuName = "Lootopoly/Monster Data")]
public class MonsterData : ScriptableObject
{
    [Header("Identity")]
    public string monsterName  = "Wild Slime";
    [TextArea(1, 3)]
    public string flavorText   = "It wiggles. That's about it.";
    public Sprite monsterSprite;
    public int    difficultyTier = 1;   // 1, 2, or 3

    [Header("Combat Stats")]
    [Tooltip("Current HP of the monster (used for display; combat is resolved in one roll).")]
    public int    maxHP        = 2;
    [Tooltip("Damage dealt to the player on combat defeat.")]
    public int    attack       = 1;
    [Tooltip("Minimum attack total required for the player to win. Player ATK Total must be ≥ this.")]
    public int    defense      = 0;
    [Tooltip("Gold awarded to the player on winning combat (before multipliers).")]
    public int    goldReward   = 50;

    [Header("Loot Drop")]
    [Tooltip("Base probability (0–1) that defeating this monster drops a loot item.")]
    [Range(0f, 1f)]
    public float  lootDropChance = 0.2f;

    [Tooltip("Specific loot items this monster can drop. If empty, the global dropLootPool is used.")]
    public List<LootData> specificLootDrops = new List<LootData>();
}