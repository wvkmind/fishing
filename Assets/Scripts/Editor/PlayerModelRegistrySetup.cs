#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Editor utility to create and configure the PlayerModelRegistry.asset with a default model entry.
/// Run via menu: Tools/Setup Player Model Registry
/// </summary>
public static class PlayerModelRegistrySetup
{
    private const string AssetPath = "Assets/Config/PlayerModelRegistry.asset";
    private const string ConfigDir = "Assets/Config";

    [MenuItem("Tools/Setup Player Model Registry")]
    public static void SetupPlayerModelRegistry()
    {
        // Ensure Config directory exists
        if (!AssetDatabase.IsValidFolder(ConfigDir))
        {
            AssetDatabase.CreateFolder("Assets", "Config");
        }

        // Load existing or create new
        var registry = AssetDatabase.LoadAssetAtPath<PlayerModelRegistry>(AssetPath);
        bool isNew = registry == null;

        if (isNew)
        {
            registry = ScriptableObject.CreateInstance<PlayerModelRegistry>();
        }

        // Configure default model entry
        registry.entries = new List<PlayerModelRegistry.ModelEntry>
        {
            new PlayerModelRegistry.ModelEntry
            {
                logicId = "default_player",
                prefab = null
            }
        };

        // Save asset
        if (isNew)
        {
            AssetDatabase.CreateAsset(registry, AssetPath);
        }
        else
        {
            EditorUtility.SetDirty(registry);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[PlayerModelRegistrySetup] PlayerModelRegistry saved at {AssetPath}. Please assign the player model Prefab to the 'default_player' entry in the Inspector.");
    }
}
#endif
