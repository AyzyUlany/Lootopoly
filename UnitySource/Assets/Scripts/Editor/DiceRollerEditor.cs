#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// ============================================================
// LOOTOPOLY – DiceRollerEditor v5.0
// ============================================================
// Face Setup workflow:
//   1. Physically rotate the die in the scene so the desired
//      face points upward.
//   2. Click "Capture Rotation" on the matching face row.
//   3. (Optional) Click "Preview" to snap the die back to that
//      saved rotation at any time to verify it looks correct.
// ============================================================

[CustomEditor(typeof(DiceRoller))]
public class DiceRollerEditor : Editor
{
    private DiceRoller dice;

    // Foldout state per face row
    private bool[] faceExpanded = new bool[6];

    // Colours
    private static readonly Color HeaderColor   = new Color(0.18f, 0.18f, 0.18f);
    private static readonly Color CapturedColor = new Color(0.18f, 0.45f, 0.18f);
    private static readonly Color MissingColor  = new Color(0.55f, 0.18f, 0.18f);
    private static readonly Color RowEvenColor  = new Color(0.22f, 0.22f, 0.22f);
    private static readonly Color RowOddColor   = new Color(0.26f, 0.26f, 0.26f);

    private static readonly Quaternion IdentityQ = Quaternion.identity;

    private void OnEnable()
    {
        dice = (DiceRoller)target;
        if (dice.faces == null || dice.faces.Length != 6)
        {
            dice.faces = new DiceFace[6];
            for (int i = 0; i < 6; i++)
                dice.faces[i] = new DiceFace { pipValue = i + 1, rotation = Quaternion.identity };
        }
    }

    // ─────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Draw everything except the faces array (we draw that ourselves)
        DrawPropertiesExcluding(serializedObject, "faces");

        GUILayout.Space(12);
        DrawFaceSetupPanel();

        GUILayout.Space(8);
        DrawValidationWarnings();

        serializedObject.ApplyModifiedProperties();
    }

    // ─────────────────────────────────────────────────────────
    // FACE SETUP PANEL
    // ─────────────────────────────────────────────────────────

    private void DrawFaceSetupPanel()
    {
        // Header bar
        Rect headerRect = EditorGUILayout.BeginHorizontal();
        EditorGUI.DrawRect(headerRect, HeaderColor);
        GUILayout.Label("  ⚄  Face Setup", EditorStyles.whiteLargeLabel);
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "For each face:\n" +
            "  1. Rotate the die in the Scene so that face points UP.\n" +
            "  2. Click  ▶ Capture  to save the current rotation.\n" +
            "  3. Click  ◉ Preview  to verify the saved rotation later.",
            MessageType.Info);
        GUILayout.Space(6);

        for (int i = 0; i < dice.faces.Length; i++)
        {
            DrawFaceRow(i);
        }
    }

    private void DrawFaceRow(int i)
    {
        DiceFace face = dice.faces[i];
        bool isCaptured = face.rotation != IdentityQ;

        // Row background
        Rect rowRect = EditorGUILayout.BeginVertical();
        EditorGUI.DrawRect(rowRect, i % 2 == 0 ? RowEvenColor : RowOddColor);

        // ── Collapsed header ──────────────────────────────
        EditorGUILayout.BeginHorizontal();

        // Status dot
        Color dotColor = isCaptured ? CapturedColor : MissingColor;
        GUILayout.Label("  ●", new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = dotColor } }, GUILayout.Width(24));

        // Pip value field (compact)
        GUILayout.Label("Pip:", GUILayout.Width(28));
        EditorGUI.BeginChangeCheck();
        int newPip = EditorGUILayout.IntField(face.pipValue, GUILayout.Width(30));
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(dice, "Change Pip Value");
            dice.faces[i].pipValue = Mathf.Clamp(newPip, 1, 6);
            EditorUtility.SetDirty(dice);
        }

        // Note field
        GUILayout.Label("Note:", GUILayout.Width(36));
        EditorGUI.BeginChangeCheck();
        string newNote = EditorGUILayout.TextField(face.note);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(dice, "Edit Face Note");
            dice.faces[i].note = newNote;
            EditorUtility.SetDirty(dice);
        }

        GUILayout.FlexibleSpace();

        // ▶ Capture button
        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
        if (GUILayout.Button("▶ Capture", GUILayout.Width(80), GUILayout.Height(20)))
        {
            Undo.RecordObject(dice, "Capture Face Rotation");
            dice.faces[i].rotation = dice.transform.rotation;
            EditorUtility.SetDirty(dice);
            Debug.Log($"[DiceRoller] Captured rotation for pip {dice.faces[i].pipValue}: {dice.faces[i].rotation.eulerAngles}");
        }
        GUI.backgroundColor = Color.white;

        // ◉ Preview button (only active if rotation captured)
        GUI.enabled = isCaptured;
        GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
        if (GUILayout.Button("◉ Preview", GUILayout.Width(80), GUILayout.Height(20)))
        {
            Undo.RecordObject(dice, "Preview Face Rotation");
            dice.transform.rotation = face.rotation;
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;

        // Foldout arrow for expanded rotation details (Removed unsupported GUILayout.Width argument)
        faceExpanded[i] = EditorGUILayout.Foldout(faceExpanded[i], GUIContent.none, true);

        EditorGUILayout.EndHorizontal();

        // ── Expanded: show euler + reset ──────────────────
        if (faceExpanded[i])
        {
            EditorGUI.indentLevel++;

            // Read-only euler display
            Vector3 euler = face.rotation.eulerAngles;
            EditorGUI.BeginChangeCheck();
            Vector3 newEuler = EditorGUILayout.Vector3Field("Rotation (Euler)", euler);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(dice, "Edit Face Rotation");
                dice.faces[i].rotation = Quaternion.Euler(newEuler);
                EditorUtility.SetDirty(dice);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 14);
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕ Clear Rotation", GUILayout.Height(18)))
            {
                Undo.RecordObject(dice, "Clear Face Rotation");
                dice.faces[i].rotation = Quaternion.identity;
                EditorUtility.SetDirty(dice);
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
            GUILayout.Space(2);
        }

        EditorGUILayout.EndVertical();
        GUILayout.Space(2);
    }

    // ─────────────────────────────────────────────────────────
    // VALIDATION WARNINGS
    // ─────────────────────────────────────────────────────────

    private void DrawValidationWarnings()
    {
        // Check for duplicate pip values
        bool[] seen = new bool[7];
        bool hasDuplicates = false;
        bool hasUncaptured = false;

        foreach (var face in dice.faces)
        {
            int pip = Mathf.Clamp(face.pipValue, 1, 6);
            if (seen[pip]) hasDuplicates = true;
            seen[pip] = true;

            if (face.rotation == IdentityQ) hasUncaptured = true;
        }

        if (hasDuplicates)
            EditorGUILayout.HelpBox("⚠ Duplicate pip values detected! Each face should have a unique pip value (1–6).", MessageType.Warning);

        if (hasUncaptured)
            EditorGUILayout.HelpBox("ℹ Some faces still have the default (uncaptured) rotation. Capture them for accurate face snapping.", MessageType.Info);

        if (!hasDuplicates && !hasUncaptured)
        {
            GUI.color = new Color(0.6f, 1f, 0.6f);
            EditorGUILayout.LabelField("✔ All 6 faces captured with unique pip values.", EditorStyles.miniLabel);
            GUI.color = Color.white;
        }
    }

    // ─────────────────────────────────────────────────────────
    // SCENE GUI – Roll anchor handle + face rotation handles
    // ─────────────────────────────────────────────────────────

    private void OnSceneGUI()
    {
        // Roll anchor position handle
        if (dice.rollAnchor != null)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(
                dice.rollAnchor.position, Quaternion.identity);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(dice.rollAnchor, "Move Roll Anchor");
                dice.rollAnchor.position = newPos;
            }
        }

        // Face rotation handles (shown in a row in front of the die)
        if (dice.faces == null) return;

        for (int i = 0; i < dice.faces.Length; i++)
        {
            Vector3 previewPos =
                dice.transform.position +
                dice.transform.right * (i - (dice.faces.Length - 1) * 0.5f) * 1.5f +
                dice.transform.forward * 2f;

            // Rotation handle
            EditorGUI.BeginChangeCheck();
            Quaternion newRot = Handles.RotationHandle(dice.faces[i].rotation, previewPos);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(dice, "Rotate Face via Handle");
                dice.faces[i].rotation = newRot;
                EditorUtility.SetDirty(dice);
            }

            // Label
            Handles.color = Color.yellow;
            Handles.Label(previewPos + Vector3.up * 0.55f,
                $"Face {i + 1}  (pip {dice.faces[i].pipValue})\n" +
                (string.IsNullOrEmpty(dice.faces[i].note) ? "" : dice.faces[i].note));
        }
    }
}
#endif