using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using DG.Tweening;
using Random = UnityEngine.Random;

public class Player : MonoBehaviour
{
    // ── Identity ──────────────────────────────────────────────
    [Header("Identity")]
    public int    playerID   = 0;
    public string playerName = "Hero";[Tooltip("The portrait used in the HUD and UI panels")]
    public Sprite playerPortrait;

    [HideInInspector] public string walletAddress = "";

    // ── References ────────────────────────────────────────────
    [Header("References")]
    public SpriteRenderer spriteRenderer;

    // ── Base Stats ────────────────────────────────────────────[Header("Base Stats")]
    public int  maxHP       = 20;
    public int  currentHP;
    public int  gold        = 500;
    public int  baseAttack  = 3;
    public int  baseDefense = 0;

    // ── Equipment Slots ───────────────────────────────────────
    [Header("Equipment (1 per slot — assigned at runtime)")]
    public LootData equippedWeapon;
    public LootData equippedArmor;
    public LootData equippedBoots;
    public LootData equippedAccessory;

    // ── Cards ─────────────────────────────────────────────────
    [Header("Cards")]
    public List<CardData> handOfCards = new List<CardData>();

    // ── Status Flags ──────────────────────────────────────────
    [Header("Status Flags (Runtime)")]
    public bool skipNextActionPhase = false;
    public bool hasGreasedShoes     = false;

    // ── Board Position ────────────────────────────────────────[Header("Board Position (Runtime)")]
    public int currentTileIndex = 0;

    // ── Animation Settings ────────────────────────────────────
    [Header("Animation")]
    public float hopDuration  = 0.30f;
    public float hopHeight    = 1.4f;

    private Tween idleTween;
    private Transform visualPivot; // The dynamically created bottom-anchor

    // ── Lifecycle ─────────────────────────────────────────────
    private void Start()
    {
        currentHP = maxHP;
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        SetupBottomPivot();
        StartIdleAnimation();
    }

    private void SetupBottomPivot()
    {
        if (spriteRenderer == null) return;

        // Create a pivot object exactly at the Player's origin (feet)
        GameObject pivotObj = new GameObject("VisualPivot");
        pivotObj.transform.SetParent(this.transform);
        pivotObj.transform.localPosition = Vector3.zero;
        visualPivot = pivotObj.transform;

        // Move the sprite renderer inside the pivot
        spriteRenderer.transform.SetParent(visualPivot);

        // Calculate the bottom offset so the feet touch the pivot origin
        if (spriteRenderer.sprite != null)
        {
            float offsetToBottom = spriteRenderer.sprite.bounds.extents.y - spriteRenderer.sprite.bounds.center.y;
            spriteRenderer.transform.localPosition = new Vector3(0, offsetToBottom, 0);
        }
        else
        {
            spriteRenderer.transform.localPosition = Vector3.zero;
        }
    }

    // ═════════════════════════════════════════════════════════
    // IDLE & FLIP ANIMATIONS
    // ═════════════════════════════════════════════════════════

    public void StartIdleAnimation()
    {
        if (visualPivot == null || currentHP <= 0) return;
        
        // Prevent double tweens
        if (idleTween != null && idleTween.IsActive()) return;

        visualPivot.localScale = Vector3.one;
        idleTween = visualPivot.DOScale(new Vector3(1.05f, 0.95f, 1f), 0.8f)
            .SetLoops(-1, LoopType.Yoyo)
            .SetEase(Ease.InOutSine);
    }

    public void StopIdleAnimation()
    {
        if (idleTween != null) idleTween.Kill();
        if (visualPivot != null)
        {
            visualPivot.DOComplete();
            visualPivot.localScale = Vector3.one;
            visualPivot.localRotation = Quaternion.identity;
        }
    }

    public void PlayHappyFlip()
    {
        if (currentHP <= 0 || visualPivot == null) return;
        
        StopIdleAnimation();
        Sequence seq = DOTween.Sequence();
        
        // A happy little jump and a full 360 spin!
        seq.Append(visualPivot.DOLocalJump(visualPivot.localPosition, 0.8f, 1, 0.5f));
        seq.Join(visualPivot.DOLocalRotate(new Vector3(0, 360, 0), 0.5f, RotateMode.FastBeyond360)
            .SetRelative(true)
            .SetEase(Ease.OutQuad));
        
        seq.OnComplete(StartIdleAnimation);
    }

    // ═════════════════════════════════════════════════════════
    // EQUIPMENT MANAGEMENT
    // ═════════════════════════════════════════════════════════

    public LootData Equip(LootData item)
    {
        if (item == null) return null;
        item.ResetRuntimeState();

        LootData displaced = null;
        switch (item.slot)
        {
            case EquipSlot.Weapon:    displaced = equippedWeapon;    equippedWeapon    = item; break;
            case EquipSlot.Armor:     displaced = equippedArmor;     equippedArmor     = item; break;
            case EquipSlot.Boots:     displaced = equippedBoots;     equippedBoots     = item; break;
            case EquipSlot.Accessory: displaced = equippedAccessory; equippedAccessory = item; break;
        }
        return displaced;
    }

    public LootData Unequip(EquipSlot slot)
    {
        LootData removed = null;
        switch (slot)
        {
            case EquipSlot.Weapon:    removed = equippedWeapon;    equippedWeapon    = null; break;
            case EquipSlot.Armor:     removed = equippedArmor;     equippedArmor     = null; break;
            case EquipSlot.Boots:     removed = equippedBoots;     equippedBoots     = null; break;
            case EquipSlot.Accessory: removed = equippedAccessory; equippedAccessory = null; break;
        }
        return removed;
    }

    public LootData DropRandomItem()
    {
        var all = GetAllEquipped();
        if (all.Count == 0) return null;
        var pick = all[Random.Range(0, all.Count)];
        return Unequip(pick.slot);
    }

    public List<(EquipSlot slot, LootData item)> GetAllEquipped()
    {
        var list = new List<(EquipSlot, LootData)>();
        if (equippedWeapon    != null) list.Add((EquipSlot.Weapon,    equippedWeapon));
        if (equippedArmor     != null) list.Add((EquipSlot.Armor,     equippedArmor));
        if (equippedBoots     != null) list.Add((EquipSlot.Boots,     equippedBoots));
        if (equippedAccessory != null) list.Add((EquipSlot.Accessory, equippedAccessory));
        return list;
    }

    public int  EquippedCount()               => GetAllEquipped().Count;
    public bool HasSlotFilled(EquipSlot slot) =>
        slot switch {
            EquipSlot.Weapon    => equippedWeapon    != null,
            EquipSlot.Armor     => equippedArmor     != null,
            EquipSlot.Boots     => equippedBoots     != null,
            EquipSlot.Accessory => equippedAccessory != null,
            _ => false
        };

    // ═════════════════════════════════════════════════════════
    // STAT AGGREGATORS
    // ═════════════════════════════════════════════════════════

    public int   GetTotalAttack()        => baseAttack + (equippedWeapon?.attackBonus ?? 0);
    public int   GetRerollBestOf()       => equippedWeapon?.rerollBestOf ?? 0;
    public int   GetKillBonusGold()      => equippedWeapon?.killBonusGold ?? 0;
    public bool  HasCriticalStrike()     => equippedWeapon?.hasCriticalStrike ?? false;
    public bool  HasLootChanceBoost()    => equippedWeapon?.hasGuaranteedLootChance ?? false;

    public int   GetTotalDefense()       => baseDefense + (equippedArmor?.defenseBonus ?? 0);
    public int   GetThornsDamage()       => equippedArmor?.thornsDamage ?? 0;
    public bool  HasTrapShield()         => (equippedArmor?.trapShieldChargesRemaining ?? 0) > 0;
    public bool  HasLastStand()          => (equippedArmor?.hasLastStand ?? false) && !(equippedArmor?.lastStandUsed ?? true);

    public int   GetMoveBonus()          => equippedBoots?.moveBonus ?? 0;
    public bool  CanMoveBackward()       => equippedBoots?.canMoveBackward ?? false;
    public bool  HasTrapDodge()          => equippedBoots?.hasTrapDodge ?? false;
    public int   GetShopDiscount()       => equippedBoots?.shopDiscount ?? 0;
    public bool  HasGhostStep()          => equippedBoots?.hasGhostStep ?? false;

    public int   GetPassiveGoldPerTurn() => equippedAccessory?.passiveGoldPerTurn ?? 0;
    public float GetGoldMultiplier()     => equippedAccessory?.goldMultiplier ?? 1f;
    public float GetTollLifesteal()      => equippedAccessory?.tollLifestealPercent ?? 0f;
    public bool  HasCardReflect()        => equippedAccessory?.hasCardReflect ?? false;
    public bool  HasDoubleCardDraw()     => equippedAccessory?.hasDoubleCardDraw ?? false;

    // ═════════════════════════════════════════════════════════
    // PASSIVE TICK
    // ═════════════════════════════════════════════════════════

    public int CollectPassiveGold()
    {
        int amount = GetPassiveGoldPerTurn();
        if (amount > 0) gold += amount;
        return amount;
    }

    public void ClearGhostStep()
    {
        if (equippedBoots != null)
            equippedBoots.ghostStepActive = false;
    }

    // ═════════════════════════════════════════════════════════
    // MOVEMENT & BOARD
    // ═════════════════════════════════════════════════════════

    public void MoveSpaces(int spaces, Tile[] boardTiles, System.Action onComplete, bool backward = false)
    {
        StartCoroutine(HopRoutine(spaces, boardTiles, onComplete, backward));
    }

    private IEnumerator HopRoutine(int spaces, Tile[] boardTiles, System.Action onComplete, bool backward)
    {
        int dir = backward ? -1 : 1;
        GameManager gm = FindObjectOfType<GameManager>();

        StopIdleAnimation();

        for (int i = 0; i < spaces; i++)
        {
            if (visualPivot != null)
            {
                DOTween.Kill(visualPivot);
                visualPivot.localScale = Vector3.one;
            }

            currentTileIndex = ((currentTileIndex + dir + boardTiles.Length) % boardTiles.Length);
            Tile next    = boardTiles[currentTileIndex];
            
            // Replaced offset with Tile Standpoint Anchor Request
            Vector3 dest = next.GetPlayerStandPosition(playerID);
            
            bool done    = false;

            Sequence seq = DOTween.Sequence();
            
            // Move the root transform
            seq.Append(transform.DOJump(dest, hopHeight, 1, hopDuration).SetEase(Ease.Linear));

            // Squash & stretch the visual pivot
            if (visualPivot != null)
            {
                seq.Insert(0f, visualPivot.DOScale(new Vector3(0.75f, 1.30f, 1f), hopDuration * 0.25f).SetEase(Ease.OutQuad));
                seq.Insert(hopDuration * 0.25f, visualPivot.DOScale(Vector3.one, hopDuration * 0.35f).SetEase(Ease.InOutSine));
                seq.Insert(hopDuration * 0.75f, visualPivot.DOScale(new Vector3(1.35f, 0.60f, 1f), hopDuration * 0.15f).SetEase(Ease.OutQuad));
                seq.Insert(hopDuration * 0.90f, visualPivot.DOScale(Vector3.one, hopDuration * 0.25f).SetEase(Ease.OutBounce));
            }

            seq.InsertCallback(hopDuration * 0.80f, () => next.PlayLandAnimation());
            seq.OnComplete(() => done = true);
            
            yield return new WaitUntil(() => done);

            if (visualPivot != null)
                visualPivot.localScale = Vector3.one;

            if (hasGreasedShoes && gm != null)
            {
                TakeDamage(1, gm);
                yield return new WaitForSeconds(0.12f);
            }
            else
            {
                yield return new WaitForSeconds(0.04f);
            }

            if (currentHP <= 0) { hasGreasedShoes = false; onComplete?.Invoke(); yield break; }
        }

        hasGreasedShoes = false;
        if (HasGhostStep()) equippedBoots.ghostStepActive = true;

        StartIdleAnimation();
        onComplete?.Invoke();
    }

    public void AnimateTeleport(int newTileIndex, Vector3 destPos)
    {
        currentTileIndex = newTileIndex;
        StopIdleAnimation();

        DOTween.Sequence()
            .Append(visualPivot.DOScale(Vector3.zero, 0.15f).SetEase(Ease.InBack))
            .AppendCallback(() => transform.position = destPos)
            .Append(visualPivot.DOScale(Vector3.one, 0.22f).SetEase(Ease.OutBack))
            .OnComplete(StartIdleAnimation);
    }

    // ═════════════════════════════════════════════════════════
    // COMBAT ARENA (JRPG STYLE)
    // ═════════════════════════════════════════════════════════

    public void EnterCombatArena(Vector3 arenaPos)
    {
        transform.DOComplete();
        StopIdleAnimation();

        transform.DOMove(arenaPos, 0.4f).SetEase(Ease.OutCubic);
        transform.DOScale(Vector3.one * 0.35f, 0.4f).SetEase(Ease.OutBack).OnComplete(StartIdleAnimation);
    }

    public void ExitCombatArena(Vector3 boardPos)
    {
        transform.DOComplete();
        StopIdleAnimation();

        transform.DOMove(boardPos, 0.4f).SetEase(Ease.OutCubic);
        transform.DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack).OnComplete(StartIdleAnimation);
    }

    public void AnimateAttackSequence(Vector3 targetPos, Action onHit, Action onComplete)
    {
        StopIdleAnimation();
        Vector3 startPos = transform.position;
        Sequence seq = DOTween.Sequence();
        
        Vector3 hitPos = targetPos + (startPos - targetPos).normalized * 0.15f; 

        seq.Append(transform.DOJump(hitPos, 0.3f, 1, 0.3f));
        seq.AppendCallback(() => {
            onHit?.Invoke();
            if (visualPivot != null)
                visualPivot.DOPunchScale(new Vector3(0.1f, -0.1f, 0f), 0.15f, 5, 1f);
        });

        seq.Append(transform.DOJump(startPos, 0.3f, 1, 0.3f));
        seq.OnComplete(() => {
            StartIdleAnimation();
            onComplete?.Invoke();
        });
    }

    // ═════════════════════════════════════════════════════════
    // DAMAGE PIPELINE (Juiced)
    // ═════════════════════════════════════════════════════════

    public int TakeDamage(int rawAmount, GameManager gm)
    {
        int mitigated = Mathf.Max(1, rawAmount - GetTotalDefense());
        currentHP -= mitigated;

        StopIdleAnimation();
        StartCoroutine(HitStopRoutine(0.08f));
        FlashRed();

        if (visualPivot != null)
        {
            visualPivot.DOShakePosition(0.25f, 0.4f, 25, 90f).OnComplete(() => {
                if (currentHP > 0) StartIdleAnimation();
            });
        }

        if (currentHP <= 0 && TryTriggerLastStand(gm))
            return mitigated;

        if (currentHP <= 0)
            Die(gm);

        return mitigated;
    }

    public int TakeTrapDamage(int rawAmount, GameManager gm)
    {
        if (HasTrapShield())
        {
            equippedArmor.trapShieldChargesRemaining--;
            gm?.onNotification.Invoke($"🛡️ {playerName}'s shield blocked the trap! ({equippedArmor.trapShieldChargesRemaining} charges left)");
            return 0;
        }

        if (HasTrapDodge())
        {
            int roll = Random.Range(1, 7);
            if (roll >= 4)
            {
                gm?.onNotification.Invoke($"👟 {playerName} dodged the trap! (Rolled {roll})");
                return 0;
            }
        }

        return TakeDamage(rawAmount, gm);
    }

    private bool TryTriggerLastStand(GameManager gm)
    {
        if (!HasLastStand()) return false;
        currentHP = 1;
        equippedArmor.lastStandUsed = true;
        
        if (visualPivot != null)
            visualPivot.DOPunchScale(Vector3.one * 0.5f, 0.5f, 5, 1f);

        gm?.onNotification.Invoke($"⚡ LAST STAND! {playerName} refuses to die!");
        return true;
    }

    private IEnumerator HitStopRoutine(float duration)
    {
        Time.timeScale = 0.1f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1f;
    }

    // ═════════════════════════════════════════════════════════
    // DEATH (Juiced)
    // ═════════════════════════════════════════════════════════

    private void Die(GameManager gm)
    {
        Debug.Log($"<color=red>💀 {playerName} DIED</color>");

        StopIdleAnimation();

        if (visualPivot != null)
        {
            if (spriteRenderer != null) spriteRenderer.color = Color.white;
            visualPivot.DOLocalRotate(new Vector3(0, 0, 720), 0.6f, RotateMode.FastBeyond360).SetEase(Ease.InQuad);
            visualPivot.DOScale(Vector3.zero, 0.6f).SetEase(Ease.InBack);
        }

        gold = Mathf.FloorToInt(gold * 0.5f);
        LootData dropped = DropRandomItem();
        handOfCards.Clear();
        
        StartCoroutine(DeathCallbackRoutine(gm));
    }

    private IEnumerator DeathCallbackRoutine(GameManager gm)
    {
        yield return new WaitForSeconds(0.6f);
        
        if (visualPivot != null)
            visualPivot.localRotation = Quaternion.identity;
        
        transform.localScale = Vector3.one;
        gm?.OnPlayerDeath(this);
    }

    // ═════════════════════════════════════════════════════════
    // VFX
    // ═════════════════════════════════════════════════════════

    private void FlashRed()
    {
        if (spriteRenderer == null) return;
        DOTween.Kill(spriteRenderer); 
        spriteRenderer.DOColor(Color.red, 0.10f)
                      .OnComplete(() => spriteRenderer.DOColor(Color.white, 0.14f));
    }
}