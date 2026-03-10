using UnityEngine;
using System;
using System.Collections;

#if UNITY_EDITOR
using UnityEditor;
#endif

// ============================================================
// LOOTOPOLY – DiceRoller (Visual-Sync v7.3 — DOUBLE DOWN FIX)
// ============================================================
// Fixed in v7.3:
//   • Added `lastRollWasDoubled` (bool) and `rawFaceValue` (int)
//     public fields. These are set before the callback fires so
//     GameManager.HandleDoubleDownDamage() can make an
//     authoritative decision instead of reverse-engineering the
//     result via arithmetic. This eliminates the false-positive
//     bug where any even roll of 2 or 4 triggered the penalty.
//
// Unchanged from v7.2:
//   • "Decaying Chaos" algorithm for smooth predetermined rolling.
//   • Result face rotates on ALL axes to point at the camera.
//   • After landing, the die CONTINUES to track the camera in
//     Update() until the next roll begins.
// ============================================================

[System.Serializable]
public struct DiceFace
{
    [Tooltip("Which pip value this face represents (1–6).")]
    [Range(1, 6)]
    public int pipValue;

    [Tooltip("The world rotation the die must be at so this face points up.")]
    public Quaternion rotation;

    [Tooltip("Optional label.")]
    public string note;
}

public class DiceRoller : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────

    [Header("Motion Settings")]
    [Tooltip("Total time from launch to resting.")]
    public float rollDuration = 1.5f;

    [Tooltip("Intensity of the mid-air spin.")]
    [Range(2, 20)]
    public int tumblingIntensity = 8;

    [Tooltip("If true, the die will tilt and rotate to face the camera perfectly.")]
    public bool faceCameraResult = true;

    [Tooltip("Controls the flow of time. Use an 'EaseOut' or 'Bounce' curve.")]
    public AnimationCurve motionCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.7f, 0.8f),
        new Keyframe(1f, 1f)
    );

    [Header("Face Mapping")]
    public DiceFace[] faces = new DiceFace[6];

    [Header("Scene References")]
    [Tooltip("If assigned, die teleports here before each roll.")]
    public Transform rollAnchor;

    [Header("Editor Gizmos")]
    [SerializeField] private float gizmoSize = 0.4f;
    [SerializeField] private float facePreviewSpacing = 1.5f;

    // ─────────────────────────────────────────────────────────
    // RUNTIME STATE
    // ─────────────────────────────────────────────────────────

    /// <summary>Set by UI_PlayCard(DoubleDown). Consumed once per roll.</summary>
    [HideInInspector] public bool doubleNextRoll = false;

    /// <summary>
    /// FIX v7.3: True when the most recent roll was doubled by the DoubleDown card.
    /// Read this AFTER the RollDice callback fires to determine the penalty.
    /// Reset to false at the start of every new RollDice call.
    /// </summary>
    [HideInInspector] public bool lastRollWasDoubled = false;

    /// <summary>
    /// FIX v7.3: The raw face value (1–6) before any doubling is applied.
    /// GameManager uses this to check if the raw value was 1 or 2 (penalty threshold).
    /// Set before the callback fires. Reset to 0 at the start of every new roll.
    /// </summary>
    [HideInInspector] public int rawFaceValue = 0;

    private int? forcedResult = null;
    
    private bool isRolling = false;
    private Quaternion? finishedFaceBaseRotation = null;

    // ─────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────

    private void Awake()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (faces == null || faces.Length == 0) InitializeDefaultFaces();
    }

    private void InitializeDefaultFaces()
    {
        faces = new DiceFace[6];
        for (int i = 0; i < 6; i++)
            faces[i] = new DiceFace { pipValue = i + 1, rotation = Quaternion.identity };
    }

    private void Update()
    {
        // PERSISTENT LOOK-AT LOGIC
        if (!isRolling && faceCameraResult && finishedFaceBaseRotation.HasValue && Camera.main != null)
        {
            UpdateFacingLogic();
        }
    }

    private void UpdateFacingLogic()
    {
        Vector3 dirToCam = (Camera.main.transform.position - transform.position).normalized;

        if (dirToCam != Vector3.zero)
        {
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, dirToCam);
            Quaternion lookRot = tilt * finishedFaceBaseRotation.Value;
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 5f);
        }
    }

    // ─────────────────────────────────────────────────────────
    // PUBLIC API
    // ─────────────────────────────────────────────────────────

    public void RollDice(Vector3 launchPosition, Action<int> callback)
    {
        // Reset diagnostic fields at the start of every roll.
        isRolling            = true;
        finishedFaceBaseRotation = null;
        lastRollWasDoubled   = false;
        rawFaceValue         = 0;

        int raw = forcedResult.HasValue
            ? Mathf.Clamp(forcedResult.Value, 1, 6)
            : UnityEngine.Random.Range(1, 7);

        forcedResult = null;

        // FIX v7.3: Record the raw face BEFORE doubling, then set the public
        // diagnostic fields so GameManager can read them in the callback.
        rawFaceValue = raw;

        bool wasDouble = doubleNextRoll;
        doubleNextRoll     = false;
        lastRollWasDoubled = wasDouble;

        int result = wasDouble ? raw * 2 : raw;

        if (wasDouble) Debug.Log($"[DiceRoller] Double Down! rawFace:{raw} → result:{result}");
        else           Debug.Log($"[DiceRoller] Result: {result}");

        StartCoroutine(AnimatePredeterminedRoll(raw, result, callback));
    }

    public void ForceResult(int value)
    {
        forcedResult = Mathf.Clamp(value, 1, 6);
    }

    public bool TryGetFaceRotation(int pip, out Quaternion rotation)
    {
        if (faces != null)
        {
            foreach (var face in faces)
            {
                if (face.pipValue == pip)
                {
                    rotation = face.rotation;
                    return true;
                }
            }
        }
        rotation = Quaternion.identity;
        return false;
    }

    // ─────────────────────────────────────────────────────────
    // ANIMATION LOGIC
    // ─────────────────────────────────────────────────────────

    private IEnumerator AnimatePredeterminedRoll(int rawFace, int callbackResult, Action<int> callback)
    {
        // 1. Setup Start
        if (rollAnchor != null) transform.position = rollAnchor.position;
        Quaternion startRotation = UnityEngine.Random.rotation;

        // 2. Determine Target Base Rotation (From Config)
        if (!TryGetFaceRotation(rawFace, out Quaternion baseTargetRotation))
        {
            baseTargetRotation = Quaternion.identity;
        }

        // 3. Calculate "Landing" Rotation
        Quaternion landingRotation = baseTargetRotation;
        
        if (faceCameraResult && Camera.main != null)
        {
            Vector3 dirToCam = (Camera.main.transform.position - transform.position).normalized;
            if (dirToCam != Vector3.zero)
            {
                Quaternion tiltRotation = Quaternion.FromToRotation(Vector3.up, dirToCam);
                landingRotation = tiltRotation * baseTargetRotation;
            }
        }

        // 4. Chaos Vector (Tumbling)
        Vector3 chaosVector = new Vector3(
            UnityEngine.Random.Range(360, 720), 
            UnityEngine.Random.Range(360, 720), 
            UnityEngine.Random.Range(360, 720)
        ) * tumblingIntensity;

        // 5. Animation Loop
        float timer = 0f;
        while (timer < rollDuration)
        {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / rollDuration);
            
            float curveT = motionCurve.Evaluate(progress);

            Quaternion baseRot = Quaternion.Slerp(startRotation, landingRotation, curveT);

            float noiseFactor = 1f - curveT; 
            Quaternion noiseRot = Quaternion.Euler(chaosVector * noiseFactor);

            transform.rotation = baseRot * noiseRot;

            yield return null;
        }

        // 6. Hard Snap & Enable Persistent Tracking
        transform.rotation = landingRotation;
        finishedFaceBaseRotation = baseTargetRotation; 
        isRolling = false;

        yield return new WaitForSeconds(0.2f);
        callback?.Invoke(callbackResult);
    }

#if UNITY_EDITOR
    // ─────────────────────────────────────────────────────────
    // GIZMOS
    // ─────────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        if (rollAnchor != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(rollAnchor.position, 0.25f);
            Gizmos.DrawLine(transform.position, rollAnchor.position);
        }

        if (faces == null) return;

        for (int i = 0; i < faces.Length; i++)
        {
            Vector3 offset = transform.position + transform.right * (i - (faces.Length - 1) * 0.5f) * facePreviewSpacing;
            Gizmos.color = (faces[i].rotation == Quaternion.identity) ? new Color(1,0,0,0.5f) : Color.yellow;
            Gizmos.DrawWireCube(offset, Vector3.one * gizmoSize);
            Handles.Label(offset + Vector3.up * 0.6f, $"● {faces[i].pipValue}");
        }
    }
#endif
}