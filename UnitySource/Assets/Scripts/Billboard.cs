using UnityEngine;

// ============================================================
// LOOTOPOLY – Billboard (v2.0)
// ============================================================
// Rotates this object's transform so it always faces the Main Camera.
// Separated axes locks allow the sprite to stay perfectly 
// upright (Paper Mario style) while preserving its initial tilt.
// ============================================================

public class Billboard : MonoBehaviour
{
    [Tooltip("Lock X rotation (Pitch) so the sprite doesn't tilt forward/backward.")]
    public bool lockX = false;

    [Tooltip("Lock Y rotation (Yaw). Rarely locked for billboarding.")]
    public bool lockY = false;

    [Tooltip("Lock Z rotation (Roll) so the sprite doesn't tilt side-to-side.")]
    public bool lockZ = true;

    private Transform cam;
    private Vector3 initialEulerAngles;

    private void Start()
    {
        // Store the initial rotation so we can preserve it on locked axes
        initialEulerAngles = transform.rotation.eulerAngles;

        Camera main = Camera.main;
        if (main != null)
            cam = main.transform;
        else
            Debug.LogWarning("[Billboard] No Main Camera found. Tag your camera 'MainCamera'.");
    }

    private void LateUpdate()
    {
        if (cam == null) return;

        // Get the target rotation to perfectly match the camera's viewing angle
        Vector3 targetEuler = Quaternion.LookRotation(cam.forward).eulerAngles;

        // Apply locks by overriding the target with the initial stored rotation
        if (lockX) targetEuler.x = initialEulerAngles.x;
        if (lockY) targetEuler.y = initialEulerAngles.y;
        if (lockZ) targetEuler.z = initialEulerAngles.z;

        transform.rotation = Quaternion.Euler(targetEuler);
    }
}