using UnityEngine;

// ============================================================
// LOOTOPOLY – LootData (Equipment Overhaul v2.0)
// ============================================================
// Four distinct equipment slots, each with unique identity:
//
//   WEAPON    → Offensive. Combat rolls, kill bonuses, crits.
//   ARMOR     → Defensive. Damage reduction, thorns, shields.
//   BOOTS     → Mobility. Move augmentation, board traversal.
//   ACCESSORY → Economy. Passive gold, lifesteal, card chaos.
//
// Rarity: Common → Uncommon → Rare → Legendary (crafted only)
// ============================================================

public enum EquipSlot  { Weapon, Armor, Boots, Accessory }
public enum LootRarity { Common, Uncommon, Rare, Legendary }

[CreateAssetMenu(fileName = "New Loot", menuName = "Lootopoly/Loot Data")]
public class LootData : ScriptableObject
{
    // ── Identity ──────────────────────────────────────────────
    [Header("Identity")]
    public string      lootName    = "Iron Sword";
    [TextArea(2, 4)]
    public string      description = "A sturdy blade.";
    public Sprite      lootSprite;
    public EquipSlot   slot        = EquipSlot.Weapon;
    public LootRarity  rarity      = LootRarity.Common;

    // ══════════════════════════════════════════════════════════
    // WEAPON STATS
    // ══════════════════════════════════════════════════════════
    [Header("— Weapon: Combat —")]
    [Tooltip("Flat bonus added to every combat attack roll (dice result + this + base 3 = total attack).")]
    public int  attackBonus            = 0;

    [Tooltip("Roll the combat die this many times and keep the single highest result. 0 = roll once normally.")]
    public int  rerollBestOf           = 0;

    [Tooltip("On a killing blow, earn this extra gold on top of the monster's normal gold reward.")]
    public int  killBonusGold          = 0;

    [Tooltip("If true: rolling a natural 6 on the combat die bypasses ALL monster defense (auto-win regardless of DEF).")]
    public bool hasCriticalStrike      = false;

    [Tooltip("If true: monster loot drop chance is raised by +50% after winning combat (additive with the monster's base rate).")]
    public bool hasGuaranteedLootChance = false;

    // ══════════════════════════════════════════════════════════
    // ARMOR STATS
    // ══════════════════════════════════════════════════════════
    [Header("— Armor: Defense —")]
    [Tooltip("Flat damage reduction on every incoming HP hit. Minimum 1 damage always dealt.")]
    public int  defenseBonus           = 0;

    [Tooltip("When this armor absorbs HP damage from a tile or trap, reflect this many HP back to the tile owner.")]
    public int  thornsDamage           = 0;

    [Tooltip("Number of times trap HP damage is fully negated. Trap gold toll is still paid. Charges deplete on each trigger.")]
    public int  trapShieldCharges      = 0;

    [Tooltip("One-time: survive a killing blow at exactly 1 HP instead of dying. The item is not destroyed but the effect cannot trigger again.")]
    public bool hasLastStand           = false;

    // ══════════════════════════════════════════════════════════
    // BOOTS STATS
    // ══════════════════════════════════════════════════════════
    [Header("— Boots: Mobility —")]
    [Tooltip("Flat bonus added to every movement die roll result (after dice are read, before clamping).")]
    public int  moveBonus              = 0;

    [Tooltip("Each turn, the player may choose to move BACKWARD by the rolled amount instead of forward.")]
    public bool canMoveBackward        = false;

    [Tooltip("When landing on an enemy trap: roll 1d6. On 4+ result, negate all trap HP damage. Gold toll is still paid.")]
    public bool hasTrapDodge           = false;

    [Tooltip("Gold discount applied to Shop tile item purchases. Deducted from the 200g base price.")]
    public int  shopDiscount           = 0;

    [Tooltip("After movement dice are rolled this turn, the player passes through the first enemy tile encountered without paying toll or taking damage.")]
    public bool hasGhostStep           = false;

    // ══════════════════════════════════════════════════════════
    // ACCESSORY STATS
    // ══════════════════════════════════════════════════════════
    [Header("— Accessory: Economy & Chaos —")]
    [Tooltip("Earn this gold at the START of every turn (fires before Action Phase, after Wanted tax).")]
    public int   passiveGoldPerTurn    = 0;

    [Tooltip("Multiply all combat kill gold rewards by this factor (applied after Wanted bonus). 1.0 = no change.")]
    public float goldMultiplier        = 1.0f;

    [Tooltip("Heal this fraction of every toll payment collected from your tiles as HP (e.g. 0.2 = heal 20% of toll as HP).")]
    [Range(0f, 1f)]
    public float tollLifestealPercent  = 0f;

    [Tooltip("When any card is played targeting this player: 30% chance to redirect it to a random different opponent.")]
    public bool  hasCardReflect        = false;

    [Tooltip("When landing on a negative Event tile and drawing a card: draw one additional card automatically.")]
    public bool  hasDoubleCardDraw     = false;

    // ══════════════════════════════════════════════════════════
    // FLAGS
    // ══════════════════════════════════════════════════════════
    [Header("— Flags —")]
    [Tooltip("Legendary items cannot appear in the Shop or drop from monsters. They are produced exclusively by crafting.")]
    public bool  isCraftedOnly         = false;

    // ══════════════════════════════════════════════════════════
    // RUNTIME STATE  (not serialised; reset via ResetRuntimeState)
    // ══════════════════════════════════════════════════════════
    [HideInInspector] public int  trapShieldChargesRemaining;
    [HideInInspector] public bool lastStandUsed;
    [HideInInspector] public bool ghostStepActive;   // Set after movement roll; cleared at next StartTurn

    /// <summary>Call when the item is equipped. Resets all runtime fields to their asset defaults.</summary>
    public void ResetRuntimeState()
    {
        trapShieldChargesRemaining = trapShieldCharges;
        lastStandUsed              = false;
        ghostStepActive            = false;
    }
}