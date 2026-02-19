using UnityEngine;
using UnityEditor;
using MultiplayerFishing;
using FishingGameTool.Fishing.Loot;

/// <summary>
/// One-click setup: adds a WaterZone trigger collider to all water objects in the scene.
/// Creates a child object with a BoxCollider (trigger) sized to match the water mesh.
/// </summary>
public static class WaterZoneSetupEditor
{
    [MenuItem("Tools/Water Setup/Add WaterZone Triggers")]
    public static void AddWaterZoneTriggers()
    {
        var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (!activeScene.name.Contains("GameScene"))
        {
            EditorUtility.DisplayDialog("Water Setup",
                "Please open GameScene first!\nCurrent scene: " + activeScene.name,
                "OK");
            return;
        }
        // Find all FishingLoot components (they live on water objects)
        var waterObjects = Object.FindObjectsByType<FishingLoot>(FindObjectsSortMode.None);

        int count = 0;
        foreach (var fl in waterObjects)
        {
            var waterGO = fl.gameObject;

            // Skip if already has WaterZone child
            if (waterGO.GetComponentInChildren<WaterZone>() != null)
            {
                Debug.Log($"[WaterSetup] '{waterGO.name}' already has WaterZone, skipping");
                continue;
            }

            // Create child trigger object
            var triggerGO = new GameObject("WaterZoneTrigger");
            triggerGO.transform.SetParent(waterGO.transform, false);
            triggerGO.layer = waterGO.layer; // same Water layer

            // Size the trigger to match the water mesh
            var meshFilter = waterGO.GetComponent<MeshFilter>();
            Vector3 size = new Vector3(50f, 2f, 50f); // fallback
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                var bounds = meshFilter.sharedMesh.bounds;
                size = bounds.size;
                size.y = 3f; // thin trigger volume at water surface
            }

            var box = triggerGO.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            box.center = new Vector3(0f, -1.0f, 0f); // below surface to avoid triggering at shoreline

            triggerGO.AddComponent<WaterZone>();

            EditorUtility.SetDirty(waterGO);
            count++;
            Debug.Log($"[WaterSetup] Added WaterZone to '{waterGO.name}' size={size}");
        }

        Debug.Log($"[WaterSetup] Done. Added {count} WaterZone triggers. Save your scene (Ctrl+S).");
    }
}
