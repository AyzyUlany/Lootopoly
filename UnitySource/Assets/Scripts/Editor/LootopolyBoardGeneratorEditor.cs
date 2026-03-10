using UnityEngine;
using UnityEditor;

// ============================================================
// LOOTOPOLY – LootopolyBoardGeneratorEditor
// ============================================================
// Custom Inspector for the LootopolyBoardGenerator component.
// Shows a colour legend and prominent Generate / Clear buttons.
// ============================================================

[CustomEditor(typeof(LootopolyBoardGenerator))]
public class LootopolyBoardGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(12);

        // Colour legend
        EditorGUILayout.HelpBox(
            "TILE COLOUR GUIDE:\n" +
            "  🟡 Gold       → START\n" +
            "  🔵 Cyan       → PROPERTY (buyable)\n" +
            "  🟠 Orange     → EVENT\n" +
            "  🟢 Green      → SHOP\n" +
            "\n" +
            "Tiles are auto-typed:\n" +
            "  Index 0 = Start | Every 7th = Shop | Every 5th / corners = Event | Rest = Property",
            MessageType.Info);

        GUILayout.Space(8);

        LootopolyBoardGenerator gen = (LootopolyBoardGenerator)target;

        GUI.backgroundColor = new Color(0.3f, 0.9f, 0.4f);
        if (GUILayout.Button("🎲  Generate Lootopoly Board", GUILayout.Height(36)))
        {
            // Clear existing board first to prevent overlapping cubes
            gen.ClearBoard();
            
            Undo.RegisterFullObjectHierarchyUndo(gen.gameObject, "Generate Board");
            gen.Generate();
        }

        GUILayout.Space(4);

        GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
        if (GUILayout.Button("🗑   Clear Board", GUILayout.Height(26)))
        {
            if (EditorUtility.DisplayDialog("Clear Board",
                    "Delete all child tiles? This cannot be undone.", "Yes, clear", "Cancel"))
                gen.ClearBoard();
        }

        GUI.backgroundColor = Color.white;
    }
}