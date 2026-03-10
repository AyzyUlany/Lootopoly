using UnityEngine;
using UnityEditor;

public class RandomizeScaleTool : EditorWindow
{
    private float minScale = 0.8f;
    private float maxScale = 1.2f;
    private bool uniformScale = true;

    [MenuItem("Tools/Randomize Scale")]
    public static void ShowWindow()
    {
        GetWindow<RandomizeScaleTool>("Randomize Scale");
    }

    private void OnGUI()
    {
        GUILayout.Label("Randomize Selected Object Scale", EditorStyles.boldLabel);

        minScale = EditorGUILayout.FloatField("Min Scale", minScale);
        maxScale = EditorGUILayout.FloatField("Max Scale", maxScale);
        uniformScale = EditorGUILayout.Toggle("Uniform Scale", uniformScale);

        EditorGUILayout.Space();

        if (GUILayout.Button("Randomize Scale"))
        {
            RandomizeSelectedObjects();
        }
    }

    private void RandomizeSelectedObjects()
    {
        if (Selection.transforms.Length == 0)
        {
            Debug.LogWarning("No GameObjects selected.");
            return;
        }

        foreach (Transform t in Selection.transforms)
        {
            Undo.RecordObject(t, "Randomize Scale");

            if (uniformScale)
            {
                float randomScale = Random.Range(minScale, maxScale);
                t.localScale = Vector3.one * randomScale;
            }
            else
            {
                t.localScale = new Vector3(
                    Random.Range(minScale, maxScale),
                    Random.Range(minScale, maxScale),
                    Random.Range(minScale, maxScale)
                );
            }

            EditorUtility.SetDirty(t);
        }
    }
}