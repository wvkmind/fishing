#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using FishingGameTool.Fishing.LootData;

/// <summary>
/// Editor utility to create and configure the ItemRegistry.asset with the 7 fish entries.
/// Run via menu: Tools/Setup Item Registry
/// </summary>
public static class ItemRegistrySetup
{
    private const string AssetPath = "Assets/Config/ItemRegistry.asset";
    private const string ConfigDir = "Assets/Config";

    [MenuItem("Tools/Setup Item Registry")]
    public static void SetupItemRegistry()
    {
        // Ensure Config directory exists
        if (!AssetDatabase.IsValidFolder(ConfigDir))
        {
            AssetDatabase.CreateFolder("Assets", "Config");
        }

        // Load existing or create new
        var registry = AssetDatabase.LoadAssetAtPath<ItemRegistry>(AssetPath);
        bool isNew = registry == null;

        if (isNew)
        {
            registry = ScriptableObject.CreateInstance<ItemRegistry>();
        }

        // Clear existing entries and configure 7 fish
        registry.entries = new List<ItemRegistryEntry>();

        registry.entries.Add(CreateFishEntry(
            "fish_bigeye_tuna", "Bigeye Tuna",
            "A large deep-sea tuna with big eyes",
            15f, 40f, 20f, 50f, LootTier.Rare, 20f, 200, 25
        ));

        registry.entries.Add(CreateFishEntry(
            "fish_yellow_croaker", "Yellow Croaker",
            "A common coastal fish with golden scales",
            0.3f, 2f, 10f, 30f, LootTier.Common, 20f, 50, 5
        ));

        registry.entries.Add(CreateFishEntry(
            "fish_moorish_idol", "Moorish Idol",
            "A striking reef fish with bold black and yellow stripes",
            0.3f, 1.5f, 10f, 25f, LootTier.Uncommon, 20f, 80, 10
        ));

        registry.entries.Add(CreateFishEntry(
            "fish_brown_rabbitfish", "Brown Rabbitfish",
            "A herbivorous reef fish with venomous dorsal spines",
            0.2f, 1.0f, 8f, 20f, LootTier.Common, 20f, 40, 5
        ));

        registry.entries.Add(CreateFishEntry(
            "fish_thickhead_scorpionfish", "Thickhead Scorpionfish",
            "A well-camouflaged bottom dweller with a large head",
            0.5f, 3f, 10f, 35f, LootTier.Uncommon, 20f, 90, 10
        ));

        registry.entries.Add(CreateFishEntry(
            "fish_emperor_snapper", "Emperor Snapper",
            "A prized large snapper found near coral reefs",
            5f, 25f, 30f, 80f, LootTier.Epic, 20f, 500, 50
        ));

        registry.entries.Add(CreateFishEntry(
            "fish_chub_mackerel", "Chub Mackerel",
            "A fast-swimming pelagic fish common in coastal waters",
            0.3f, 2.5f, 10f, 35f, LootTier.Common, 20f, 45, 5
        ));

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

        Debug.Log($"[ItemRegistrySetup] ItemRegistry saved at {AssetPath} with {registry.entries.Count} fish entries.");
    }

    private static ItemRegistryEntry CreateFishEntry(
        string logicId, string displayName, string description,
        float minWeight, float maxWeight,
        float minLength, float maxLength,
        LootTier lootTier, float lootRarity,
        int price, int experienceValue)
    {
        return new ItemRegistryEntry
        {
            logicId = logicId,
            itemType = ItemType.Fish,
            displayName = displayName,
            description = description,
            icon = null,
            prefab = null,
            isCatchable = true,
            minLength = minLength,
            maxLength = maxLength,
            minWeight = minWeight,
            maxWeight = maxWeight,
            lootTier = lootTier,
            lootRarity = lootRarity,
            price = price,
            experienceValue = experienceValue,
            allowedRods = new List<string>(),
            allowedBaits = new List<string>(),
            allowedLocations = new List<string>()
        };
    }
}
#endif
