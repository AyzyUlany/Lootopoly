using UnityEngine;

// ============================================================
// LOOTOPOLY – LootopolyBoardGenerator (URP COMPATIBLE)
// ============================================================
// Generates the perfect rectangular Lootopoly board.
// Safely applies URP _BaseColor to custom Prefabs using a
// MaterialPropertyBlock (prevents altering the original asset).
// ============================================================

[ExecuteInEditMode]
public class LootopolyBoardGenerator : MonoBehaviour
{
    [Header("Board Configuration")]
    [Tooltip("Number of tiles along the bottom/top edges (including the 2 corners)")]
    [Min(3)] public int tilesPerSideX = 10;
    
    [Tooltip("Number of tiles along the left/right edges (including the 2 corners)")]
    [Min(3)] public int tilesPerSideZ = 10;

    [Header("Tile Dimensions")]
    public float tileWidth = 1f;
    public float tileDepth = 1.5f;
    public float tileThickness = 0.2f;
    public float tileSpacing = 0.05f;

    [Header("Prefabs (Optional)")]
    [Tooltip("If left empty, standard Unity cubes will be generated.")]
    public GameObject edgeTilePrefab;
    public GameObject cornerTilePrefab;

    // ── Property names ────────────────────────────────────────
    private static readonly string[] PropertyNames =
    {
        "Goblin Gulch",    "Rat Hollow",       "Mudslide Crossing", "Toad's Rest",
        "Ember Peak",      "Dragon's Footpath", "Troll Bridge",      "Whispering Marsh",
        "Bandit Crossroads","Crypt Row",         "Wizard's Alley",   "Iron Rampart",
        "Serpent's Pass",  "Cursed Copse",      "Lich's Landing",   "Phantom Plaza",
        "Blightwood",      "Hellfire Hollow",   "Shadowfen Lane",   "Ruin's End",
        "Ashen Dale",      "Venom Veil",        "Abyssal Quay",     "Dreadspire Drive",
        "Golem Gate",      "Necropolis Nook",   "Infernal Inlet",   "Chimera Court",
        "Wyvern Way",      "Behemoth Boulevard","Titan Terrace",    "Colossus Corner"
    };

    public void Generate()
    {
        ClearBoard();

        float totalWidth = (2 * tileDepth) + ((tilesPerSideX - 2) * tileWidth) + ((tilesPerSideX - 1) * tileSpacing);
        float totalDepth = (2 * tileDepth) + ((tilesPerSideZ - 2) * tileWidth) + ((tilesPerSideZ - 1) * tileSpacing);

        int tileIndex = 0;
        int propertyNameIndex = 0;

        // 1. BOTTOM EDGE (Right to Left)
        for (int i = 0; i < tilesPerSideX; i++)
        {
            float xPos, zPos = (-totalDepth / 2f) + (tileDepth / 2f);
            bool isCorner = false;
            Quaternion rot = Quaternion.identity;

            if (i == 0) {
                xPos = (totalWidth / 2f) - (tileDepth / 2f);
                isCorner = true;
                rot = Quaternion.Euler(0, 0, 0); 
            } else if (i == tilesPerSideX - 1) {
                xPos = (-totalWidth / 2f) + (tileDepth / 2f);
                isCorner = true;
                rot = Quaternion.Euler(0, 90, 0);
            } else {
                xPos = (totalWidth / 2f) - (tileDepth + tileSpacing + (i - 1) * (tileWidth + tileSpacing) + (tileWidth / 2f));
                rot = Quaternion.Euler(0, 0, 0);
            }
            
            SpawnTile(new Vector3(xPos, 0, zPos), rot, isCorner, ref tileIndex, ref propertyNameIndex);
        }

        // 2. LEFT EDGE (Bottom to Top)
        for (int i = 1; i < tilesPerSideZ - 1; i++)
        {
            float xPos = (-totalWidth / 2f) + (tileDepth / 2f);
            float zPos = (-totalDepth / 2f) + (tileDepth + tileSpacing + (i - 1) * (tileWidth + tileSpacing) + (tileWidth / 2f));
            Quaternion rot = Quaternion.Euler(0, 90, 0);
            
            SpawnTile(new Vector3(xPos, 0, zPos), rot, false, ref tileIndex, ref propertyNameIndex);
        }

        // 3. TOP EDGE (Left to Right)
        for (int i = 0; i < tilesPerSideX; i++)
        {
            float xPos, zPos = (totalDepth / 2f) - (tileDepth / 2f);
            bool isCorner = false;
            Quaternion rot = Quaternion.identity;

            if (i == 0) {
                xPos = (-totalWidth / 2f) + (tileDepth / 2f);
                isCorner = true;
                rot = Quaternion.Euler(0, 180, 0);
            } else if (i == tilesPerSideX - 1) {
                xPos = (totalWidth / 2f) - (tileDepth / 2f);
                isCorner = true;
                rot = Quaternion.Euler(0, -90, 0);
            } else {
                xPos = (-totalWidth / 2f) + (tileDepth + tileSpacing + (i - 1) * (tileWidth + tileSpacing) + (tileWidth / 2f));
                rot = Quaternion.Euler(0, 180, 0);
            }
            
            SpawnTile(new Vector3(xPos, 0, zPos), rot, isCorner, ref tileIndex, ref propertyNameIndex);
        }

        // 4. RIGHT EDGE (Top to Bottom)
        for (int i = 1; i < tilesPerSideZ - 1; i++)
        {
            float xPos = (totalWidth / 2f) - (tileDepth / 2f);
            float zPos = (totalDepth / 2f) - (tileDepth + tileSpacing + (i - 1) * (tileWidth + tileSpacing) + (tileWidth / 2f));
            Quaternion rot = Quaternion.Euler(0, -90, 0);
            
            SpawnTile(new Vector3(xPos, 0, zPos), rot, false, ref tileIndex, ref propertyNameIndex);
        }
        
        Debug.Log($"[BoardGen] Generated {tileIndex} tiles.");
    }

    private void SpawnTile(Vector3 localPosition, Quaternion localRotation, bool isCorner, ref int tileIndex, ref int propertyNameIndex)
    {
        GameObject tileToSpawn = isCorner ? cornerTilePrefab : edgeTilePrefab;
        GameObject go;
        bool isPrimitive = false;

        if (tileToSpawn == null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            isPrimitive = true;
            if (isCorner) go.transform.localScale = new Vector3(tileDepth, tileThickness, tileDepth);
            else go.transform.localScale = new Vector3(tileWidth, tileThickness, tileDepth);
        }
        else
        {
#if UNITY_EDITOR
            // In the editor we use PrefabUtility so the instance stays linked
            // to the prefab asset and can receive prefab overrides correctly.
            go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(tileToSpawn);
#else
            // At runtime (WebGL / standalone build) PrefabUtility is not available,
            // so we fall back to a plain Instantiate.
            go = Instantiate(tileToSpawn);
#endif
        }

        go.transform.SetParent(this.transform);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = localRotation;

        // Tile component & Data Assignment
        Tile tile = go.GetComponent<Tile>() ?? go.AddComponent<Tile>();
        tile.tileIndex = tileIndex;

        // Tile Logic Rules
        if (tileIndex == 0)
        {
            tile.tileType = TileType.Start;
            tile.tileName = "START";
        }
        else if (tileIndex % 7 == 0)
        {
            tile.tileType = TileType.Shop;
            tile.tileName = $"Item Shop {tileIndex / 7}";
        }
        else if (isCorner || tileIndex % 5 == 0)
        {
            tile.tileType = TileType.Event;
            tile.tileName = $"Event Tile {tileIndex}";
        }
        else
        {
            tile.tileType = TileType.Property;
            tile.tileName = propertyNameIndex < PropertyNames.Length ? PropertyNames[propertyNameIndex++] : $"Property {tileIndex}";
            tile.baseCost = Mathf.Min(300, 80 + (tileIndex / 4) * 15);
        }

        // --- NEW: Generate standard editor-accessible player Standpoints immediately ---
        tile.EnsureStandPoints();

        go.name = $"Tile_{tileIndex:00}_{tile.tileName}";

        // --- URP Color coding for BOTH Primitives and Prefabs ---
        Renderer renderer = go.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            Color tileColor = tile.tileType switch {
                TileType.Start    => new Color(0.95f, 0.80f, 0.10f), // Gold
                TileType.Event    => new Color(0.85f, 0.45f, 0.10f), // Orange
                TileType.Shop     => new Color(0.15f, 0.70f, 0.30f), // Green
                _                 => new Color(0.20f, 0.70f, 0.85f), // Cyan
            };

            if (isPrimitive)
            {
                // Primitives need a URP material created for them
                Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
                Material mat = new Material(urpShader != null ? urpShader : Shader.Find("Standard"));
                
                if (urpShader != null) mat.SetColor("_BaseColor", tileColor);
                else mat.color = tileColor;
                
                renderer.sharedMaterial = mat;
            }
            else
            {
                // Prefabs use a MaterialPropertyBlock to safely override color
                // without permanently destroying/modifying the project's saved material asset!
                MaterialPropertyBlock propBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(propBlock);
                
                propBlock.SetColor("_BaseColor", tileColor); // URP
                propBlock.SetColor("_Color", tileColor);     // Standard Fallback
                
                renderer.SetPropertyBlock(propBlock);
            }
        }

        #if UNITY_EDITOR
        UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Generate Lootopoly Board");
        #endif

        tileIndex++;
    }

    public void ClearBoard()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            #if UNITY_EDITOR
            UnityEditor.Undo.DestroyObjectImmediate(child);
            #else
            DestroyImmediate(child);
            #endif
        }
    }
}