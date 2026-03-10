#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

// ============================================================
// LOOTOPOLY – CameraController Editor Tool (ORBITAL UPDATE)
// ============================================================
// Custom inspector to visually preview, edit, and save the 
// different Camera States. Now fully supports Inverse-Rotation
// so calculating offsets against turned board edges works flawlessly.
// ============================================================

[CustomEditor(typeof(CameraController))]
public class CameraControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CameraController cam = (CameraController)target;

        GUILayout.Space(20);
        
        GUI.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
        EditorGUILayout.BeginVertical("box");
        GUI.backgroundColor = Color.white;

        EditorGUILayout.LabelField("🎥 Camera Setup Tools", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1. Assign a Preview Target (like a Player or a Tile).\n" +
            "2. Fly around in the Scene View to find the perfect angle.\n" +
            "3. Click 'Align Camera to Scene View'.\n" +
            "4. Click 'Save Current as...' to calculate the LOCAL offset automatically.", MessageType.Info);

        // Extract positional and rotational bases
        Vector3 targetPos = cam.previewTarget != null ? cam.previewTarget.position : Vector3.zero;
        Quaternion baseRot = cam.previewTarget != null ? cam.previewTarget.rotation : Quaternion.identity;

        GUILayout.Space(10);
        
        // --- SCENE VIEW ALIGNMENT TOOLS ---
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("📷 Align Camera to Scene View", GUILayout.Height(30)))
        {
            if (SceneView.lastActiveSceneView != null)
            {
                Undo.RecordObject(cam.transform, "Align Camera to Scene");
                cam.transform.position = SceneView.lastActiveSceneView.camera.transform.position;
                cam.transform.rotation = SceneView.lastActiveSceneView.camera.transform.rotation;
            }
        }
        if (GUILayout.Button("👁 Align Scene View to Camera", GUILayout.Height(30)))
        {
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.AlignViewToObject(cam.transform);
            }
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(15);

        // --- STATE BUTTONS ---
        DrawStateRow("Default View (Movement)", 
            () => { // Snap Preview
                Undo.RecordObject(cam.transform, "Preview Default View");
                cam.transform.position = targetPos + (baseRot * cam.defaultOffset);
                cam.transform.rotation = baseRot * Quaternion.Euler(cam.defaultRotation);
            }, 
            () => { // Save Current
                Undo.RecordObject(cam, "Save Default View");
                // Convert world position back to local offset relative to the target's rotation
                cam.defaultOffset = Quaternion.Inverse(baseRot) * (cam.transform.position - targetPos);
                cam.defaultRotation = (Quaternion.Inverse(baseRot) * cam.transform.rotation).eulerAngles;
                EditorUtility.SetDirty(cam);
                Debug.Log("<color=cyan>[Camera Setup]</color> Default View Saved!");
            });

        DrawStateRow("Zoom View (Decisions)", 
            () => { // Snap Preview
                Undo.RecordObject(cam.transform, "Preview Zoom View");
                cam.transform.position = targetPos + (baseRot * cam.zoomOffset);
                cam.transform.rotation = baseRot * Quaternion.Euler(cam.defaultRotation);
            }, 
            () => { // Save Current
                Undo.RecordObject(cam, "Save Zoom View");
                cam.zoomOffset = Quaternion.Inverse(baseRot) * (cam.transform.position - targetPos);
                // Zoom uses the Default Rotation in code, so we only save the offset.
                EditorUtility.SetDirty(cam);
                Debug.Log("<color=cyan>[Camera Setup]</color> Zoom View Saved!");
            });

        DrawStateRow("Combat View (JRPG Arena)", 
            () => { // Snap Preview
                Undo.RecordObject(cam.transform, "Preview Combat View");
                cam.transform.position = targetPos + (baseRot * cam.combatOffset);
                cam.transform.rotation = baseRot * Quaternion.Euler(cam.combatRotation);
            }, 
            () => { // Save Current
                Undo.RecordObject(cam, "Save Combat View");
                cam.combatOffset = Quaternion.Inverse(baseRot) * (cam.transform.position - targetPos);
                cam.combatRotation = (Quaternion.Inverse(baseRot) * cam.transform.rotation).eulerAngles;
                EditorUtility.SetDirty(cam);
                Debug.Log("<color=cyan>[Camera Setup]</color> Combat View Saved!");
            });

        EditorGUILayout.EndVertical();
    }

    private void DrawStateRow(string label, System.Action onSnap, System.Action onSave)
    {
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        
        GUI.backgroundColor = new Color(0.7f, 0.9f, 1f);
        if (GUILayout.Button("👁 Snap Here")) onSnap?.Invoke();
        
        GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
        if (GUILayout.Button("💾 Save Current")) onSave?.Invoke();
        
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(8);
    }
}
#endif