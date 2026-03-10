using UnityEngine;
using DG.Tweening;
using TMPro;
using System;

// ============================================================
// LOOTOPOLY – Tile (v4.7 — BILLBOARD SQUASH FIX)
// ============================================================
// Automatically wraps dynamically spawned sprites in a Pivot
// object, and provides distinct physical standing anchors for 
// up to 4 players, visible via Editor Gizmos.
//
// FIX: Monsters/Entities are now parented to the Board Root
// rather than the Tile to prevent non-uniform scale shearing 
// when the Billboard script attempts to track the active camera!
// ============================================================

public enum TileType { Property, Event, Shop, Start }

public class Tile : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────
    [Header("Identity")]
    public TileType   tileType    = TileType.Property;
    public string     tileName    = "Goblin Gulch";
    public int        tileIndex   = 0;

    [Header("Economics")]
    public int        baseCost    = 100;
    
    [Header("Stand Points")]
    public Transform  standPoint;
    [Tooltip("The 4 distinct positions for players to stand on this tile.")]
    public Transform[] playerStandPoints = new Transform[4];
    
    [Header("UI Overlay")]
    [Tooltip("Assign the TextMeshProUGUI component from your World Space Canvas here.")]
    public TMP_Text   overlayText;

    // ── Ownership State ───────────────────────────────────────
    [Header("Ownership (Runtime)")]
    public int        ownerPlayerID = -1;     
    public int        currentTier   = 0;      
    public bool       isTrap        = false;

    // ── Monster ───────────────────────────────────────────────
    [Header("Monster (Runtime)")]
    public MonsterData residentMonster;
    public int         currentMonsterHP;

    // ── Private references ────────────────────────────────────
    private GameObject spawnedEntity;
    private GameObject combatMonsterObj; // Visual used only during battles
    private Renderer   tileRenderer;

    // ── Colour Table ──────────────────────────────────────────
    private static readonly Color ColProperty = new Color(0.20f, 0.70f, 0.85f);
    private static readonly Color ColEvent    = new Color(0.85f, 0.45f, 0.10f);
    private static readonly Color ColShop     = new Color(0.15f, 0.70f, 0.30f);
    private static readonly Color ColStart    = new Color(0.95f, 0.80f, 0.10f);
    private static readonly Color ColOwned1   = new Color(0.55f, 0.85f, 0.55f);
    private static readonly Color ColOwned2   = new Color(0.30f, 0.70f, 0.30f);
    private static readonly Color ColTrap     = new Color(0.80f, 0.15f, 0.15f);

    private void Awake()
    {
        tileRenderer = GetComponent<Renderer>();
        EnsureStandPoints();
        ApplyBaseColour();
        
        UpdateOverlayText();
    }

    // ═════════════════════════════════════════════════════════
    // SETUP & OWNERSHIP
    // ═════════════════════════════════════════════════════════

    public void SetupResidentMonster(MonsterData monster)
    {
        residentMonster = monster;
        if (monster != null) currentMonsterHP = monster.maxHP;
        
        ownerPlayerID   = -1;
        currentTier     = 0;
        isTrap          = false;
        ApplyBaseColour();

        ClearEntityVisual();

        if (monster != null)
        {
            EnsureStandPoints();
            
            spawnedEntity = CreateBottomPivotSprite("BoardMonster", monster.monsterSprite);
            
            // SQUASH FIX: Parent to board root (which is uniform 1,1,1 scale) 
            // so the Billboard script doesn't shear/squash when rotating to the camera!
            Transform rootParent = transform.parent != null ? transform.parent : null;
            spawnedEntity.transform.SetParent(rootParent);
            spawnedEntity.transform.position = standPoint.position;
            
            spawnedEntity.AddComponent<Billboard>();

            spawnedEntity.transform.localScale = Vector3.zero;
            spawnedEntity.transform.DOScale(Vector3.one * 0.25f, 0.6f).SetEase(Ease.OutBack).OnComplete(() => {
                if (spawnedEntity != null)
                {
                    spawnedEntity.transform.DOScale(new Vector3(0.26f, 0.24f, 0.25f), 0.8f)
                        .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
                }
            });
        }

        UpdateOverlayText();
    }

    public void SetOwnership(int playerID, int tier, GameObject minionPrefab)
    {
        ownerPlayerID = playerID;
        currentTier   = tier;
        isTrap        = false;

        ClearEntityVisual();

        if (playerID < 0)
        {
            ApplyBaseColour();
        }
        else
        {
            if (minionPrefab != null) SpawnEntity(minionPrefab);
            if (tileRenderer != null) tileRenderer.material.color = tier >= 2 ? ColOwned2 : ColOwned1;
        }

        UpdateOverlayText();
    }

    public void SetTrap(int playerID, GameObject trapPrefab)
    {
        ownerPlayerID = playerID;
        isTrap        = true;
        currentTier   = 2;   

        ClearEntityVisual();

        if (trapPrefab != null) SpawnEntity(trapPrefab);
        if (tileRenderer != null) tileRenderer.material.color = ColTrap;

        UpdateOverlayText();
    }

    // ═════════════════════════════════════════════════════════
    // TEXT OVERLAY LOGIC
    // ═════════════════════════════════════════════════════════

    public void UpdateOverlayText()
    {
        if (overlayText == null) return;

        string text = $"<b>{tileName}</b>";

        if (ownerPlayerID >= 0)
        {
            GameManager gm = FindObjectOfType<GameManager>();
            string pName = gm != null && gm.players.Count > ownerPlayerID 
                ? gm.players[ownerPlayerID].playerName 
                : $"P{ownerPlayerID + 1}";

            string tierStr = currentTier > 0 ? $"\n<size=60%>Tier {currentTier}</size>" : "";
            string trapStr = isTrap ? "\n<color=#ff4444>[TRAP]</color>" : "";
            
            text += $"\n<size=70%><color=#ffffaa>{pName}</color></size>{tierStr}{trapStr}";
        }
        else if (tileType == TileType.Property)
        {
            text += $"\n<size=70%><color=#dddddd>{baseCost}g</color></size>";
        }

        overlayText.text = text;
    }

    // ═════════════════════════════════════════════════════════
    // COMBAT ARENA (JRPG STYLE)
    // ═════════════════════════════════════════════════════════

    public void SetupCombatArena(out Vector3 playerPos, out Vector3 monsterPos, out GameObject monsterVisual)
    {
        playerPos  = transform.position + transform.rotation * new Vector3(-0.35f, 0.15f, 0);
        monsterPos = transform.position + transform.rotation * new Vector3(0.35f, 0.15f, 0);

        if (spawnedEntity != null && combatMonsterObj == null)
        {
            spawnedEntity.SetActive(false); 
        }

        if (combatMonsterObj == null && residentMonster != null)
        {
            EnsureStandPoints();
            
            combatMonsterObj = CreateBottomPivotSprite("CombatMonster", residentMonster.monsterSprite);
            
            // SQUASH FIX: Parent to board root (which is uniform 1,1,1 scale) 
            // so the Billboard script doesn't shear/squash when rotating to the camera!
            Transform rootParent = transform.parent != null ? transform.parent : null;
            combatMonsterObj.transform.SetParent(rootParent);
            combatMonsterObj.transform.position = monsterPos;
            
            combatMonsterObj.AddComponent<Billboard>();

            combatMonsterObj.transform.localScale = Vector3.zero;
            combatMonsterObj.transform.DOScale(Vector3.one * 0.35f, 0.5f).SetEase(Ease.OutBack).OnComplete(() => {
                if (combatMonsterObj != null)
                {
                    combatMonsterObj.transform.DOScale(new Vector3(0.37f, 0.33f, 0.35f), 0.6f)
                        .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
                }
            });
        }

        monsterVisual = combatMonsterObj;
    }

    public void AnimateMonsterAttack(Vector3 targetPos, Action onHit, Action onComplete)
    {
        if (combatMonsterObj == null) { onComplete?.Invoke(); return; }

        Vector3 startPos = combatMonsterObj.transform.position;
        Sequence seq = DOTween.Sequence();
        
        seq.Append(combatMonsterObj.transform.DOJump(targetPos + (startPos - targetPos).normalized * 0.15f, 0.3f, 1, 0.3f));
        seq.AppendCallback(() => onHit?.Invoke());
        seq.Append(combatMonsterObj.transform.DOJump(startPos, 0.3f, 1, 0.3f));
        seq.OnComplete(() => onComplete?.Invoke());
    }

    public void PlayMonsterDamageAnimation()
    {
        if (combatMonsterObj != null)
        {
            combatMonsterObj.transform.DOKill();
            combatMonsterObj.transform.localScale = Vector3.one * 0.35f; 
            
            combatMonsterObj.transform.DOShakePosition(0.3f, 0.15f, 20, 90f);
            
            SpriteRenderer sr = combatMonsterObj.GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
            {
                sr.DOColor(Color.red, 0.1f).OnComplete(() => sr.DOColor(Color.white, 0.1f));
            }

            combatMonsterObj.transform.DOScale(new Vector3(0.37f, 0.33f, 0.35f), 0.6f)
                .SetDelay(0.3f)
                .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
        }
    }

    public void ClearCombatMonster()
    {
        if (combatMonsterObj != null)
        {
            combatMonsterObj.transform.DOKill();
            Destroy(combatMonsterObj);
            combatMonsterObj = null;
        }

        if (residentMonster == null && spawnedEntity != null)
        {
            ClearEntityVisual();
        }
        else if (spawnedEntity != null)
        {
            spawnedEntity.SetActive(true);
        }
    }

    // ═════════════════════════════════════════════════════════
    // QUERIES
    // ═════════════════════════════════════════════════════════

    public int GetTollAmount()
    {
        if (isTrap)           return 40;
        if (currentTier >= 2) return 50;
        if (currentTier == 1) return 20;
        return 0;
    }

    public int GetTileDamage()
    {
        if (currentTier >= 2) return 2;
        if (currentTier == 1) return 1;
        return 0;
    }

    public int GetTrapDamage() => 3;

    // ═════════════════════════════════════════════════════════
    // ANIMATIONS (JUICED)
    // ═════════════════════════════════════════════════════════

    public void PlayLandAnimation()
    {
        transform.DOPunchScale(new Vector3(0.15f, -0.1f, 0.15f), 0.3f, 5, 0.5f);
    }

    public void PlayCombatHitAnimation()
    {
        GameObject targetObj = combatMonsterObj != null ? combatMonsterObj : spawnedEntity;

        if (targetObj != null)
        {
            targetObj.transform.DOKill();
            
            targetObj.transform
                .DOShakePosition(0.4f, 0.3f, 25, 90f)
                .OnComplete(() =>
                {
                    targetObj.transform.DOScale(targetObj.transform.localScale * 1.5f, 0.1f).OnComplete(() =>
                    {
                        targetObj.transform.DOScale(Vector3.zero, 0.2f).SetEase(Ease.InBack)
                                     .OnComplete(() => {
                                         if (targetObj == spawnedEntity) ClearEntityVisual();
                                         else ClearCombatMonster();
                                     });
                    });
                });
        }
    }

    public void PlayTrapSpringAnimation()
    {
        if (spawnedEntity != null)
        {
            spawnedEntity.transform.DOKill();
            spawnedEntity.transform.DOPunchScale(new Vector3(-0.3f, 0.8f, -0.3f), 0.4f, 10, 1f).OnComplete(() => {
                spawnedEntity.transform.DOScale(new Vector3(1.05f, 0.95f, 1f), 0.8f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
            });
        }
        transform.DOComplete();
        transform.DOPunchPosition(Vector3.up * 0.4f, 0.3f, 15, 1f);
    }

    public void PlayTollPaidAnimation()
    {
        if (spawnedEntity != null)
        {
            spawnedEntity.transform.DOJump(spawnedEntity.transform.position, 0.6f, 1, 0.35f);
        }
        transform.DOComplete();
        transform.DOPunchScale(new Vector3(0.1f, -0.1f, 0.1f), 0.3f, 5, 0.5f);
    }

    // ═════════════════════════════════════════════════════════
    // INTERNAL HELPERS & GIZMOS
    // ═════════════════════════════════════════════════════════

    [ContextMenu("Generate Player Standpoints")]
    public void EnsureStandPoints()
    {
        if (standPoint == null)
        {
            GameObject sp = new GameObject("StandPoint");
            sp.transform.SetParent(transform);
            sp.transform.localPosition = Vector3.up * 0.6f;
            standPoint = sp.transform;

            Vector3 lossy = transform.lossyScale;
            float sx = lossy.x != 0f ? 1f / lossy.x : 1f;
            float sy = lossy.y != 0f ? 1f / lossy.y : 1f;
            float sz = lossy.z != 0f ? 1f / lossy.z : 1f;
            
            standPoint.localScale = new Vector3(sx, sy, sz);
        }

        if (playerStandPoints == null || playerStandPoints.Length != 4)
            playerStandPoints = new Transform[4];

        for (int i = 0; i < 4; i++)
        {
            if (playerStandPoints[i] == null)
            {
                Transform existing = standPoint.Find($"PlayerStand_{i + 1}");
                if (existing != null)
                {
                    playerStandPoints[i] = existing;
                }
                else
                {
                    GameObject psp = new GameObject($"PlayerStand_{i + 1}");
                    psp.transform.SetParent(standPoint);
                    
                    // Default 2x2 cluster layout
                    float xOff = (i % 2 == 0) ? -0.3f : 0.3f;
                    float zOff = (i < 2) ? 0.3f : -0.3f;
                    psp.transform.localPosition = new Vector3(xOff, 0, zOff);
                    psp.transform.localRotation = Quaternion.identity;
                    
                    playerStandPoints[i] = psp.transform;
                }
            }
        }
    }

    public Vector3 GetPlayerStandPosition(int playerID)
    {
        EnsureStandPoints();
        int index = Mathf.Clamp(playerID, 0, 3);
        if (playerStandPoints[index] != null)
            return playerStandPoints[index].position;
        return standPoint.position;
    }

    private GameObject CreateBottomPivotSprite(string name, Sprite sprite)
    {
        GameObject pivotObj = new GameObject(name + "_Pivot");
        
        GameObject spriteObj = new GameObject("SpriteVisual");
        spriteObj.transform.SetParent(pivotObj.transform);
        
        SpriteRenderer sr = spriteObj.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;

        float offsetToBottom = sr.sprite.bounds.extents.y - sr.sprite.bounds.center.y;
        spriteObj.transform.localPosition = new Vector3(0, offsetToBottom, 0);

        return pivotObj;
    }

    private void SpawnEntity(GameObject prefab)
    {
        EnsureStandPoints();
        
        // SQUASH FIX for Traps/Minions: Parent to board root
        Transform rootParent = transform.parent != null ? transform.parent : null;
        spawnedEntity = Instantiate(prefab, standPoint.position, Quaternion.identity, rootParent);
        
        spawnedEntity.transform.localScale = Vector3.zero;
        spawnedEntity.transform.DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack).OnComplete(() => {
            if (spawnedEntity != null)
            {
                spawnedEntity.transform.DOScale(new Vector3(1.05f, 0.95f, 1f), 0.8f)
                    .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
            }
        });
    }

    private void ClearEntityVisual()
    {
        if (spawnedEntity != null)
        {
            spawnedEntity.transform.DOKill();
            Destroy(spawnedEntity);
            spawnedEntity = null;
        }
    }

    private void ApplyBaseColour()
    {
        if (tileRenderer == null) return;
        tileRenderer.material.color = tileType switch {
            TileType.Event  => ColEvent,
            TileType.Shop   => ColShop,
            TileType.Start  => ColStart,
            _               => ColProperty,
        };
    }

    private void OnDrawGizmos()
    {
        if (playerStandPoints == null) return;
        
        Color[] pColors = { 
            new Color(1f, 0.2f, 0.2f, 0.8f),   // P1 Red
            new Color(0.2f, 0.4f, 1f, 0.8f),   // P2 Blue
            new Color(0.2f, 1f, 0.2f, 0.8f),   // P3 Green
            new Color(1f, 0.8f, 0.1f, 0.8f)    // P4 Yellow
        };

        for (int i = 0; i < playerStandPoints.Length; i++)
        {
            if (playerStandPoints[i] != null)
            {
                Gizmos.color = pColors[i % pColors.Length];
                Gizmos.DrawSphere(playerStandPoints[i].position, 0.15f);
                
                Gizmos.color = new Color(1, 1, 1, 0.2f);
                if (standPoint != null) Gizmos.DrawLine(standPoint.position, playerStandPoints[i].position);
            }
        }
    }
}