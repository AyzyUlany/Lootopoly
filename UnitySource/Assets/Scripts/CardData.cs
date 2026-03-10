using UnityEngine;

// ============================================================
// LOOTOPOLY – CardData
// ============================================================
// ScriptableObject defining one card in the game.
// Cards are drawn from Events and played during Action Phase.
// All 8 effects are implemented in GameManager.UI_PlayCard().
// ============================================================

public enum CardEffect
{
    SwapMeet,       // Swap positions with any player
    TaxFraud,       // Steal 20% of the richest player's gold
    MeteorStrike,   // Destroy ownership on a target tile; respawn monster
    GreasedShoes,   // Target takes 1 HP per space moved next turn
    PocketSand,     // Target loses their entire Action Phase next turn
    DoubleDown,     // Your next roll is doubled; rolling 1-2 costs 2 HP
    Jackpot,        // Steal 10% from ALL other players simultaneously
    GoldRush,       // Your tiles charge double toll until your next turn
}

[CreateAssetMenu(fileName = "New Card", menuName = "Lootopoly/Card Data")]
public class CardData : ScriptableObject
{
    [Header("Identity")]
    public string     cardName    = "Mystery Card";
    [TextArea(2, 4)]
    public string     description = "Does something interesting.";
    public Sprite     cardArt;

    [Header("Behaviour")]
    public CardEffect effectType  = CardEffect.SwapMeet;

    [Tooltip("True if this card requires selecting a target player.")]
    public bool       requiresTargetPlayer = false;

    [Tooltip("True if this card requires selecting a target tile.")]
    public bool       requiresTargetTile   = false;
}