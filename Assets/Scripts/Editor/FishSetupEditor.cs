using System;
using UnityEngine;
using UnityEditor;
using Mirror;
using FishingGameTool.Fishing.LootData;
using FishingGameTool.Fishing.Loot;
using MultiplayerFishing;

/// <summary>
/// One-click fish setup: creates Prefabs + LootData assets from FBX models,
/// then wires them into the scene's water FishingLoot component.
/// Also creates Network prefabs (with NetworkIdentity) for multiplayer catch display.
/// </summary>
[Obsolete("已由 ItemConfigEditor 替代，请使用 Tools/Item Config Editor 窗口")]
public static class FishSetupEditor
{
    private const string FishPrefabDir = "Assets/Prefabs/Fish";
    private const string LootDataDir = "Assets/Config/FishingLoot";
    private const string FishDatabasePath = "Assets/Config/FishDatabase.asset";

    // ── Fish definitions ──
    private struct FishDef
    {
        public string fbxPath;
        public string name;
        public LootTier tier;
        public LootType type;
        public float minWeight;
        public float maxWeight;
        public float rarity;
        public string description;
    }

    private static readonly FishDef[] Fishes = new FishDef[]
    {
        new FishDef
        {
            fbxPath = "Assets/Fishes/大眼金枪鱼/大眼金枪鱼.fbx",
            name = "Bigeye Tuna",
            tier = LootTier.Rare,
            type = LootType.Fish,
            minWeight = 15f,
            maxWeight = 40f,
            rarity = 20f,
            description = "A large deep-sea tuna with big eyes"
        },
        new FishDef
        {
            fbxPath = "Assets/Fishes/黄花鱼/黄花鱼.fbx",
            name = "Yellow Croaker",
            tier = LootTier.Common,
            type = LootType.Fish,
            minWeight = 0.3f,
            maxWeight = 2f,
            rarity = 20f,
            description = "A common coastal fish with golden scales"
        },
        new FishDef
        {
            fbxPath = "Assets/Fishes/多棘马夫鱼/多棘马夫鱼.fbx",
            name = "Moorish Idol",
            tier = LootTier.Uncommon,
            type = LootType.Fish,
            minWeight = 0.3f,
            maxWeight = 1.5f,
            rarity = 20f,
            description = "A striking reef fish with bold black and yellow stripes"
        },
        new FishDef
        {
            fbxPath = "Assets/Fishes/褐篮子鱼/褐篮子鱼.fbx",
            name = "Brown Rabbitfish",
            tier = LootTier.Common,
            type = LootType.Fish,
            minWeight = 0.2f,
            maxWeight = 1.0f,
            rarity = 20f,
            description = "A herbivorous reef fish with venomous dorsal spines"
        },
        new FishDef
        {
            fbxPath = "Assets/Fishes/厚头平鮋/厚头平鮋.fbx",
            name = "Thickhead Scorpionfish",
            tier = LootTier.Uncommon,
            type = LootType.Fish,
            minWeight = 0.5f,
            maxWeight = 3f,
            rarity = 20f,
            description = "A well-camouflaged bottom dweller with a large head"
        },
        new FishDef
        {
            fbxPath = "Assets/Fishes/千年笛鲷/千年笛鲷.fbx",
            name = "Emperor Snapper",
            tier = LootTier.Epic,
            type = LootType.Fish,
            minWeight = 5f,
            maxWeight = 25f,
            rarity = 20f,
            description = "A prized large snapper found near coral reefs"
        },
        new FishDef
        {
            fbxPath = "Assets/Fishes/日本鲐/日本鲐.fbx",
            name = "Chub Mackerel",
            tier = LootTier.Common,
            type = LootType.Fish,
            minWeight = 0.3f,
            maxWeight = 2.5f,
            rarity = 20f,
            description = "A fast-swimming pelagic fish common in coastal waters"
        }
    };

    [MenuItem("Tools/Fish Setup/Generate All Fish (Prefabs + LootData + Wire Water)")]
    public static void GenerateAllFish()
    {
        // Check correct scene is open
        var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (!activeScene.name.Contains("GameScene"))
        {
            EditorUtility.DisplayDialog("Fish Setup",
                "Please open GameScene first!\nCurrent scene: " + activeScene.name,
                "OK");
            return;
        }
        EnsureDir(FishPrefabDir);
        EnsureDir(LootDataDir);
        EnsureDir("Assets/Config");

        var lootDataAssets = new FishingLootData[Fishes.Length];
        var dbEntries = new System.Collections.Generic.List<FishDatabase.FishEntry>();

        for (int i = 0; i < Fishes.Length; i++)
        {
            var def = Fishes[i];

            // 1. Create base Prefab from FBX (for loot throw physics + display)
            string prefabPath = $"{FishPrefabDir}/{def.name}.prefab";
            GameObject prefab = CreateFishPrefab(def.fbxPath, prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[FishSetup] Failed to create prefab for {def.name}");
                continue;
            }

            // 2. Create LootData asset
            string lootPath = $"{LootDataDir}/{def.name}.asset";
            var lootData = CreateLootData(def, prefab, lootPath);
            lootDataAssets[i] = lootData;

            // 3. Track for database — use same base prefab for display
            dbEntries.Add(new FishDatabase.FishEntry
            {
                lootName = def.name,
                networkPrefab = prefab  // same prefab, correct scale
            });

            Debug.Log($"[FishSetup] Created: {def.name} → {prefabPath}, {lootPath}");
        }

        // 4. Create/update FishDatabase
        BuildFishDatabase(dbEntries);

        // 5. Wire into scene water
        WireWaterLoot(lootDataAssets);

        // 6. Register fish prefabs with NetworkManager for NetworkServer.Spawn
        RegisterNetworkPrefabs(dbEntries);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[FishSetup] All done. Save your scene (Ctrl+S).");
    }

    private static GameObject CreateFishPrefab(string fbxPath, string prefabPath)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbx == null)
        {
            Debug.LogError($"[FishSetup] FBX not found: {fbxPath}");
            return null;
        }

        // If prefab already exists, reuse it to preserve GUID (= Mirror assetId).
        // Deleting and recreating would change the GUID, causing client/server mismatch.
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null)
        {
            Debug.Log($"[FishSetup] Prefab already exists, reusing: {prefabPath}");
            // Ensure it has NetworkIdentity
            if (existing.GetComponent<NetworkIdentity>() == null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(existing);
                inst.AddComponent<NetworkIdentity>();
                PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                UnityEngine.Object.DestroyImmediate(inst);
                existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }
            return existing;
        }

        var newInst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        PrefabUtility.UnpackPrefabInstance(newInst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        // ── Offset mesh so fish head (X+ max) is at origin ──
        // Wrap all children under a pivot parent so we can shift the mesh
        var pivot = new GameObject("MeshPivot");
        pivot.transform.SetParent(newInst.transform, false);

        // Reparent all original children under pivot
        var children = new System.Collections.Generic.List<Transform>();
        foreach (Transform child in newInst.transform)
        {
            if (child != pivot.transform)
                children.Add(child);
        }
        foreach (var child in children)
            child.SetParent(pivot.transform, true);

        // Calculate combined bounds of all renderers
        var renderers = newInst.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++)
                bounds.Encapsulate(renderers[r].bounds);

            // Fish head = X+ max. Shift so X+ max is at world origin (newInst.transform.position)
            // pivot offset = -(bounds.max.x) along local X
            Vector3 offset = new Vector3(-bounds.max.x, -bounds.center.y, -bounds.center.z);
            pivot.transform.localPosition = offset;

            Debug.Log($"[FishSetup] Pivot offset for {fbxPath}: bounds.max.x={bounds.max.x:F3} offset={offset}");
        }

        // Add Rigidbody
        if (!newInst.GetComponent<Rigidbody>())
        {
            var rb = newInst.AddComponent<Rigidbody>();
            rb.mass = 1f;
        }

        // Add collider if none exists
        if (!newInst.GetComponent<Collider>())
            newInst.AddComponent<BoxCollider>();

        // Add NetworkIdentity for NetworkServer.Spawn
        if (!newInst.GetComponent<NetworkIdentity>())
            newInst.AddComponent<NetworkIdentity>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(newInst, prefabPath);
        UnityEngine.Object.DestroyImmediate(newInst);
        return prefab;
    }

    private static FishingLootData CreateLootData(FishDef def, GameObject prefab, string assetPath)
    {
        // Check if already exists
        var existing = AssetDatabase.LoadAssetAtPath<FishingLootData>(assetPath);
        if (existing != null)
        {
            // Update existing
            existing._lootTier = def.tier;
            existing._lootType = def.type;
            existing._weightRange = new LootWeightRange { _minWeight = def.minWeight, _maxWeight = def.maxWeight };
            existing._lootRarity = def.rarity;
            existing._lootName = def.name;
            existing._lootDescription = def.description;
            existing._lootPrefab = prefab;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        var lootData = ScriptableObject.CreateInstance<FishingLootData>();
        lootData._lootTier = def.tier;
        lootData._lootType = def.type;
        lootData._weightRange = new LootWeightRange { _minWeight = def.minWeight, _maxWeight = def.maxWeight };
        lootData._lootRarity = def.rarity;
        lootData._lootName = def.name;
        lootData._lootDescription = def.description;
        lootData._lootPrefab = prefab;

        AssetDatabase.CreateAsset(lootData, assetPath);
        return lootData;
    }


    private static void BuildFishDatabase(System.Collections.Generic.List<FishDatabase.FishEntry> entries)
    {
        var db = AssetDatabase.LoadAssetAtPath<FishDatabase>(FishDatabasePath);
        if (db == null)
        {
            db = ScriptableObject.CreateInstance<FishDatabase>();
            AssetDatabase.CreateAsset(db, FishDatabasePath);
        }

        db.entries.Clear();
        db.entries.AddRange(entries);
        EditorUtility.SetDirty(db);
        Debug.Log($"[FishSetup] FishDatabase updated: {entries.Count} entries → {FishDatabasePath}");
    }


    private static void WireWaterLoot(FishingLootData[] lootDataAssets)
    {
        // Find the FishingLoot component in the current scene (on the water object)
        var fishingLoot = UnityEngine.Object.FindAnyObjectByType<FishingLoot>();
        if (fishingLoot == null)
        {
            Debug.LogWarning("[FishSetup] No FishingLoot found in scene. Open GameScene first, then re-run.");
            return;
        }

        // Clear old loot list and add new fish
        fishingLoot._fishingLoot.Clear();
        foreach (var ld in lootDataAssets)
        {
            if (ld != null)
                fishingLoot._fishingLoot.Add(ld);
        }

        EditorUtility.SetDirty(fishingLoot);
        Debug.Log($"[FishSetup] Water loot updated: {fishingLoot._fishingLoot.Count} fish configured");
    }

    private static void EnsureDir(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        var cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    private static void RegisterNetworkPrefabs(System.Collections.Generic.List<MultiplayerFishing.FishDatabase.FishEntry> entries)
    {
        // NetworkManager lives in LobbyScene (not GameScene).
        // Try to find it in currently loaded scenes first, then load LobbyScene additively if needed.
        var nm = UnityEngine.Object.FindAnyObjectByType<Mirror.NetworkManager>();
        bool loadedLobby = false;
        UnityEngine.SceneManagement.Scene lobbyScene = default;

        if (nm == null)
        {
            // Load LobbyScene additively to access its NetworkManager
            lobbyScene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/Scenes/LobbyScene.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Additive);
            loadedLobby = true;
            nm = UnityEngine.Object.FindAnyObjectByType<Mirror.NetworkManager>();
        }

        if (nm == null)
        {
            Debug.LogWarning("[FishSetup] No NetworkManager found. Fish prefabs not registered for spawning.");
            if (loadedLobby)
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(lobbyScene, true);
            return;
        }

        bool changed = false;
        foreach (var entry in entries)
        {
            if (entry.networkPrefab == null) continue;
            if (!nm.spawnPrefabs.Contains(entry.networkPrefab))
            {
                nm.spawnPrefabs.Add(entry.networkPrefab);
                Debug.Log($"[FishSetup] Registered spawn prefab: {entry.lootName}");
                changed = true;
            }
        }

        // Also register the float prefab if not already there
        var floatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Multiplayer/NetworkFishingFloat.prefab");
        if (floatPrefab != null && !nm.spawnPrefabs.Contains(floatPrefab))
        {
            nm.spawnPrefabs.Add(floatPrefab);
            Debug.Log("[FishSetup] Registered spawn prefab: NetworkFishingFloat");
            changed = true;
        }

        if (changed)
        {
            EditorUtility.SetDirty(nm);
            if (loadedLobby)
            {
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(lobbyScene);
                Debug.Log("[FishSetup] Saved LobbyScene with updated spawnPrefabs");
            }
        }

        if (loadedLobby)
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(lobbyScene, true);
    }
}
