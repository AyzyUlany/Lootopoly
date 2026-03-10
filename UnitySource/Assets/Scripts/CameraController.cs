using UnityEngine;

// ============================================================
// LOOTOPOLY – CameraController (v5.0 — ORBITAL LANE UPDATE)
// ============================================================
// Camera now reads the rotation of the current board tile.
// It applies its positional/rotational offsets RELATIVE to 
// the lane direction, creating a beautiful sweeping orbital
// pan when the player rounds the corners of the board!
// ============================================================

public class CameraController : MonoBehaviour
{
    public static CameraController Instance;

    [Header("References")]
    public GameManager gameManager;

#if UNITY_EDITOR
    [Header("Editor Preview")]
    [Tooltip("Drop a Player or Tile here to act as the center when calculating offsets in the Editor.")]
    public Transform previewTarget;
#endif

    [Header("Positioning & Angles (Local to Lane)")]
    [Tooltip("Default wide view for rolling and moving around the board")]
    public Vector3 defaultOffset   = new Vector3(0, 11f, -8f);
    public Vector3 defaultRotation = new Vector3(50f, 0f, 0f);

    [Tooltip("Closer view for shopping, and decisions")]
    public Vector3 zoomOffset      = new Vector3(0, 5.5f, -5.5f);
    [Tooltip("Low, ground-level JRPG angle for combat")]
    public Vector3 combatOffset    = new Vector3(0, 0.8f, -2.5f);
    public Vector3 combatRotation  = new Vector3(15f, 0f, 0f);

    [Header("Speeds")]
    public float followSpeed = 6f;
    public float zoomSpeed   = 4f;

    [Header("Shake Settings")]
    public float shakeDecay = 1.5f;
    public float shakeMultiplier = 1.2f;

    // --- State Variables ---
    private Vector3    currentLocalOffset;
    private Quaternion currentLocalRotation;
    private Quaternion currentBoardRotation; // Smoothed rotation of the board's lane
    private Vector3    currentTargetPos;
    private float      shakeTrauma;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        currentLocalOffset   = defaultOffset;
        currentLocalRotation = Quaternion.Euler(defaultRotation);
        currentBoardRotation = Quaternion.identity;
    }

    private void LateUpdate()
    {
        if (gameManager == null || gameManager.players.Count == 0 || gameManager.boardTiles == null || gameManager.boardTiles.Length == 0) return;

        // ── 1. Determine Target & State ───────────────────────
        int targetIndex = gameManager.currentState == GameState.GameOver 
            ? GetWinnerIndex() 
            : gameManager.currentPlayerIndex;
            
        // Safety: clamp index to valid range
        targetIndex = Mathf.Clamp(targetIndex, 0, gameManager.players.Count - 1);
        
        Player activePlayer = gameManager.players[targetIndex];
        Tile currentTile    = gameManager.boardTiles[activePlayer.currentTileIndex];

        bool isCombat = gameManager.currentState == GameState.CombatRollPhase;
        bool shouldZoom = false;

        if (!isCombat)
        {
            switch (gameManager.currentState)
            {
                case GameState.ActionPhase:
                case GameState.BuyTileDecision:
                case GameState.UpgradeTileDecision:
                case GameState.TrapTileDecision:
                case GameState.EquipDecision:
                case GameState.ShopPhase:
                case GameState.MoveDirectionChoice:
                case GameState.GameOver:
                    shouldZoom = true;
                    break;
            }
        }

        // ── 2. Determine Look Targets ─────────────────────────
        Vector3 targetLocalOffset;
        Vector3 targetLocalRotAngles;
        Vector3 trackingPos;

        if (isCombat)
        {
            trackingPos          = currentTile.transform.position;
            targetLocalOffset    = combatOffset;
            targetLocalRotAngles = combatRotation;
        }
        else if (shouldZoom)
        {
            trackingPos          = activePlayer.transform.position;
            targetLocalOffset    = zoomOffset;
            targetLocalRotAngles = defaultRotation;
        }
        else
        {
            trackingPos          = activePlayer.transform.position;
            targetLocalOffset    = defaultOffset;
            targetLocalRotAngles = defaultRotation;
        }

        // ── 3. Smooth Orbital Interpolation ───────────────────
        // Target base rotation comes from the tile, so camera faces "inward" correctly on all 4 sides.
        Quaternion targetBoardRot = currentTile.transform.rotation;
        
        // Slerp the board rotation slightly slower than the position so it swings beautifully around corners
        currentBoardRotation = Quaternion.Slerp(currentBoardRotation, targetBoardRot, Time.deltaTime * (followSpeed * 0.75f));

        // Smooth position and local zoom states
        currentTargetPos     = Vector3.Lerp(currentTargetPos, trackingPos, Time.deltaTime * followSpeed);
        currentLocalOffset   = Vector3.Lerp(currentLocalOffset, targetLocalOffset, Time.deltaTime * zoomSpeed);
        currentLocalRotation = Quaternion.Slerp(currentLocalRotation, Quaternion.Euler(targetLocalRotAngles), Time.deltaTime * zoomSpeed);

        // ── 4. Calculate Shake ────────────────────────────────
        Vector3 shake = Vector3.zero;
        if (shakeTrauma > 0)
        {
            shake = new Vector3(
                Mathf.PerlinNoise(Time.time * 25f, 0f) * 2f - 1f,
                Mathf.PerlinNoise(0f, Time.time * 25f) * 2f - 1f,
                0f
            ) * (shakeTrauma * shakeMultiplier);

            shakeTrauma -= Time.deltaTime * shakeDecay;
            if (shakeTrauma < 0) shakeTrauma = 0f;
        }

        // ── 5. Combine & Apply Final Transform ────────────────
        // Convert local offset to world offset using the current lane rotation
        Vector3 finalWorldOffset = currentBoardRotation * currentLocalOffset;
        Quaternion finalWorldRotation = currentBoardRotation * currentLocalRotation;

        transform.position = currentTargetPos + finalWorldOffset + shake;
        transform.rotation = finalWorldRotation;
    }

    public void AddShake(float strength)
    {
        shakeTrauma = Mathf.Clamp(shakeTrauma + strength, 0f, 1.5f);
    }

    private int GetWinnerIndex()
    {
        float highestGold = -1;
        int winnerIdx = 0;
        
        for (int i = 0; i < gameManager.players.Count; i++)
        {
            // Only consider alive players for the winner camera focus
            if (gameManager.players[i].currentHP > 0 && gameManager.players[i].gold > highestGold)
            {
                highestGold = gameManager.players[i].gold;
                winnerIdx = i;
            }
        }
        
        // If no alive players have gold, fallback to first alive player
        if (highestGold < 0)
        {
            for (int i = 0; i < gameManager.players.Count; i++)
            {
                if (gameManager.players[i].currentHP > 0)
                {
                    return i;
                }
            }
        }
        
        return winnerIdx;
    }
}
