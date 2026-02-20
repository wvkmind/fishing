#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Mirror;
using FishingGameTool.Fishing.LootData;
using FishingGameTool.Fishing.Loot;
using MultiplayerFishing;

/// <summary>
/// 物品配置编辑器窗口，通过 Tools/Item Config Editor 菜单打开。
/// 左右分栏布局：左侧物品列表面板，右侧详情编辑面板。
/// </summary>
public class ItemConfigEditor : EditorWindow
{
    private ItemRegistry _itemRegistry;
    private int _selectedIndex = -1;
    private Vector2 _leftScrollPos;
    private Vector2 _rightScrollPos;
    private string _searchText = "";
    private ItemType _filterType = (ItemType)(-1); // -1 表示"全部"

    private const float LeftPanelWidth = 250f;
    private const string RegistryAssetPath = "Assets/Config/ItemRegistry.asset";

    [MenuItem("Tools/Item Config Editor")]
    public static void ShowWindow()
    {
        var window = GetWindow<ItemConfigEditor>("Item Config Editor");
        window.minSize = new Vector2(600, 400);
        window.Show();
    }

    private void OnEnable()
    {
        _itemRegistry = AssetDatabase.LoadAssetAtPath<ItemRegistry>(RegistryAssetPath);
    }

    private void OnGUI()
    {
        DrawToolbar();

        EditorGUILayout.BeginHorizontal();
        {
            DrawItemListPanel();
            DrawItemDetailPanel();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        {
            if (_itemRegistry == null)
            {
                _itemRegistry = (ItemRegistry)EditorGUILayout.ObjectField(
                    "ItemRegistry", _itemRegistry, typeof(ItemRegistry), false);
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("同步到 FishingLootData", EditorStyles.toolbarButton))
            {
                SyncToFishingAssets();
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawItemListPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(LeftPanelWidth));
        {
            GUILayout.Label("物品列表", EditorStyles.boldLabel);

            if (_itemRegistry == null)
            {
                EditorGUILayout.HelpBox("请指定 ItemRegistry 资产", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            // 搜索框
            _searchText = EditorGUILayout.TextField("搜索", _searchText);

            // ItemType 筛选下拉菜单（包含"全部"选项）
            string[] filterOptions = BuildFilterOptions();
            int currentFilterIndex = _filterType == (ItemType)(-1) ? 0 : (int)_filterType + 1;
            int newFilterIndex = EditorGUILayout.Popup("类型筛选", currentFilterIndex, filterOptions);
            _filterType = newFilterIndex == 0 ? (ItemType)(-1) : (ItemType)(newFilterIndex - 1);

            // "添加物品"按钮
            if (GUILayout.Button("添加物品"))
            {
                Undo.RecordObject(_itemRegistry, "Add Item");
                var newEntry = new ItemRegistryEntry
                {
                    logicId = "new_item_" + _itemRegistry.entries.Count,
                    itemType = ItemType.Fish,
                    displayName = "新物品",
                    description = "",
                    isCatchable = false,
                    allowedRods = new List<string>(),
                    allowedBaits = new List<string>(),
                    allowedLocations = new List<string>()
                };
                _itemRegistry.entries.Add(newEntry);
                EditorUtility.SetDirty(_itemRegistry);
                _selectedIndex = _itemRegistry.entries.Count - 1;
            }

            EditorGUILayout.Space(4);

            // 物品列表（滚动区域）
            _leftScrollPos = EditorGUILayout.BeginScrollView(_leftScrollPos);
            {
                var entries = _itemRegistry.entries;
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];

                    // ItemType 筛选
                    if (_filterType != (ItemType)(-1) && entry.itemType != _filterType)
                        continue;

                    // 搜索过滤（按名称或 LogicID 模糊匹配）
                    if (!string.IsNullOrEmpty(_searchText))
                    {
                        string search = _searchText.ToLowerInvariant();
                        bool matchLogicId = !string.IsNullOrEmpty(entry.logicId) &&
                                            entry.logicId.ToLowerInvariant().Contains(search);
                        bool matchName = !string.IsNullOrEmpty(entry.displayName) &&
                                         entry.displayName.ToLowerInvariant().Contains(search);
                        if (!matchLogicId && !matchName)
                            continue;
                    }

                    // 绘制物品行
                    DrawItemRow(i, entry);
                }
            }
            EditorGUILayout.EndScrollView();
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawItemRow(int index, ItemRegistryEntry entry)
    {
        bool isSelected = (_selectedIndex == index);

        EditorGUILayout.BeginHorizontal(isSelected ? "SelectionRect" : "box");
        {
            // 可点击区域：LogicID 和 displayName
            if (GUILayout.Button(
                    $"{entry.logicId}\n{entry.displayName}",
                    EditorStyles.wordWrappedMiniLabel,
                    GUILayout.ExpandWidth(true)))
            {
                _selectedIndex = index;
            }

            // 删除按钮
            if (GUILayout.Button("X", GUILayout.Width(20), GUILayout.Height(20)))
            {
                if (EditorUtility.DisplayDialog(
                        "确认删除",
                        $"确定要删除物品 \"{entry.displayName}\"（{entry.logicId}）吗？",
                        "删除",
                        "取消"))
                {
                    Undo.RecordObject(_itemRegistry, "Delete Item");
                    _itemRegistry.entries.RemoveAt(index);
                    EditorUtility.SetDirty(_itemRegistry);

                    // 调整选中索引
                    if (_selectedIndex >= _itemRegistry.entries.Count)
                        _selectedIndex = _itemRegistry.entries.Count - 1;
                }
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private string[] BuildFilterOptions()
    {
        var itemTypeNames = System.Enum.GetNames(typeof(ItemType));
        var options = new string[itemTypeNames.Length + 1];
        options[0] = "全部";
        for (int i = 0; i < itemTypeNames.Length; i++)
        {
            options[i + 1] = itemTypeNames[i];
        }
        return options;
    }

    private void DrawItemDetailPanel()
    {
        EditorGUILayout.BeginVertical();
        {
            _rightScrollPos = EditorGUILayout.BeginScrollView(_rightScrollPos);
            {
                if (_selectedIndex < 0 || _itemRegistry == null ||
                    _selectedIndex >= _itemRegistry.entries.Count)
                {
                    GUILayout.Label("选择一个物品查看详情", EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    var entry = _itemRegistry.entries[_selectedIndex];

                    EditorGUI.BeginChangeCheck();

                    entry.logicId = EditorGUILayout.TextField("LogicID", entry.logicId);
                    entry.itemType = (ItemType)EditorGUILayout.EnumPopup("ItemType", entry.itemType);
                    entry.displayName = EditorGUILayout.TextField("显示名称", entry.displayName);

                    EditorGUILayout.LabelField("描述");
                    entry.description = EditorGUILayout.TextArea(entry.description, GUILayout.MinHeight(60));

                    entry.icon = (Sprite)EditorGUILayout.ObjectField("图标", entry.icon, typeof(Sprite), false);
                    entry.prefab = (GameObject)EditorGUILayout.ObjectField("Prefab", entry.prefab, typeof(GameObject), false);

                    entry.isCatchable = EditorGUILayout.Toggle("可被钓到", entry.isCatchable);

                    // 隐藏 lengthRange 和 weightRange（当 ItemType 为 FishingRod 或 Bait 时）
                    if (entry.itemType != ItemType.FishingRod && entry.itemType != ItemType.Bait)
                    {
                        entry.minLength = EditorGUILayout.FloatField("最小长度", entry.minLength);
                        entry.maxLength = EditorGUILayout.FloatField("最大长度", entry.maxLength);
                        entry.minWeight = EditorGUILayout.FloatField("最小重量", entry.minWeight);
                        entry.maxWeight = EditorGUILayout.FloatField("最大重量", entry.maxWeight);
                    }

                    entry.lootTier = (LootTier)EditorGUILayout.EnumPopup("LootTier", entry.lootTier);
                    entry.lootRarity = EditorGUILayout.FloatField("LootRarity", entry.lootRarity);
                    entry.price = EditorGUILayout.IntField("价格", entry.price);
                    entry.experienceValue = EditorGUILayout.IntField("经验值", entry.experienceValue);

                    // 隐藏 CatchableFilter（当 isCatchable 为 false 时）
                    if (entry.isCatchable)
                    {
                        EditorGUILayout.Space(8);
                        EditorGUILayout.LabelField("可钓条件 (CatchableFilter)", EditorStyles.boldLabel);
                        DrawStringList("可用鱼竿", entry.allowedRods);
                        DrawStringList("可用鱼饵", entry.allowedBaits);
                        DrawStringList("可钓地点", entry.allowedLocations);
                    }

                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorUtility.SetDirty(_itemRegistry);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
        EditorGUILayout.EndVertical();
    }


    private void DrawStringList(string label, List<string> list)
    {
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        for (int i = 0; i < list.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            {
                list[i] = EditorGUILayout.TextField(list[i]);
                if (GUILayout.Button("-", GUILayout.Width(20)))
                {
                    list.RemoveAt(i);
                    i--;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        if (GUILayout.Button("添加", GUILayout.Width(60)))
        {
            list.Add("");
        }

        EditorGUI.indentLevel--;
    }

    // ── Sync constants ──
    private const string FishPrefabDir = "Assets/Prefabs/Fish";
    private const string LootDataDir = "Assets/Config/FishingLoot";
    private const string FishDatabasePath = "Assets/Config/FishDatabase.asset";

    private void SyncToFishingAssets()
    {
        // 1. Validate registry
        if (_itemRegistry == null)
        {
            EditorUtility.DisplayDialog("同步失败", "请先指定 ItemRegistry 资产", "OK");
            return;
        }

        // 2. Filter catchable entries (Fish or WaterJunk with isCatchable=true)
        var allEntries = _itemRegistry.entries;
        var catchable = new List<ItemRegistryEntry>();
        foreach (var e in allEntries)
        {
            if (e.isCatchable && (e.itemType == ItemType.Fish || e.itemType == ItemType.WaterJunk))
                catchable.Add(e);
        }

        // 3. Validate: skip duplicates and empty displayNames
        var seenLogicIds = new HashSet<string>();
        var validEntries = new List<ItemRegistryEntry>();
        foreach (var e in catchable)
        {
            if (string.IsNullOrEmpty(e.logicId))
            {
                Debug.LogWarning($"[ItemConfigEditor] 跳过：logicId 为空的条目 (displayName=\"{e.displayName}\")");
                continue;
            }
            if (string.IsNullOrEmpty(e.displayName))
            {
                Debug.LogWarning($"[ItemConfigEditor] 跳过：displayName 为空的条目 (logicId=\"{e.logicId}\")");
                continue;
            }
            if (!seenLogicIds.Add(e.logicId))
            {
                Debug.LogWarning($"[ItemConfigEditor] 跳过：logicId 重复 \"{e.logicId}\" (displayName=\"{e.displayName}\")");
                continue;
            }
            validEntries.Add(e);
        }

        if (validEntries.Count == 0)
        {
            EditorUtility.DisplayDialog("同步", "没有符合条件的物品可同步。", "OK");
            return;
        }

        // 4. Confirmation dialog
        if (!EditorUtility.DisplayDialog(
                "同步到 FishingLootData",
                $"将同步 {validEntries.Count} 个物品到 FishingLootData、FishDatabase 和场景。\n继续？",
                "同步",
                "取消"))
        {
            return;
        }

        EnsureDir(FishPrefabDir);
        EnsureDir(LootDataDir);
        EnsureDir("Assets/Config");

        int prefabsUpdated = 0;
        int lootCreated = 0, lootUpdated = 0;

        var lootDataAssets = new List<FishingLootData>();
        var dbEntries = new List<FishDatabase.FishEntry>();

        // 5. Process each valid entry
        foreach (var entry in validEntries)
        {
            GameObject prefab = entry.prefab;

            // 5a. Handle prefab
            if (prefab != null)
            {
                // Ensure NetworkIdentity exists
                if (prefab.GetComponent<NetworkIdentity>() == null)
                {
                    string prefabPath = AssetDatabase.GetAssetPath(prefab);
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    inst.AddComponent<NetworkIdentity>();
                    PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                    Object.DestroyImmediate(inst);
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    entry.prefab = prefab;
                    EditorUtility.SetDirty(_itemRegistry);
                    prefabsUpdated++;
                }
            }
            else
            {
                // No prefab assigned — try to create from FBX by convention
                // Convention: Assets/Fishes/{ChineseName}/{ChineseName}.fbx — but we don't have
                // a mapping from logicId to FBX path. Log warning and skip prefab creation.
                Debug.LogWarning($"[ItemConfigEditor] Prefab 为空，跳过 Prefab 创建: {entry.displayName} ({entry.logicId})。请手动指定 Prefab 或先使用 FishSetupEditor。");
            }

            // 5b. Create/update FishingLootData
            string lootAssetPath = $"{LootDataDir}/{entry.displayName}.asset";
            var lootData = AssetDatabase.LoadAssetAtPath<FishingLootData>(lootAssetPath);
            LootType? mappedType = ItemTypeMapping.ToLootType(entry.itemType);

            if (lootData != null)
            {
                // Update existing
                lootData._lootName = entry.displayName;
                lootData._lootDescription = entry.description;
                lootData._weightRange = new LootWeightRange
                {
                    _minWeight = entry.minWeight,
                    _maxWeight = entry.maxWeight
                };
                lootData._lootTier = entry.lootTier;
                lootData._lootRarity = entry.lootRarity;
                if (prefab != null) lootData._lootPrefab = prefab;
                if (mappedType.HasValue) lootData._lootType = mappedType.Value;
                EditorUtility.SetDirty(lootData);
                lootUpdated++;
            }
            else
            {
                // Create new
                lootData = ScriptableObject.CreateInstance<FishingLootData>();
                lootData._lootName = entry.displayName;
                lootData._lootDescription = entry.description;
                lootData._weightRange = new LootWeightRange
                {
                    _minWeight = entry.minWeight,
                    _maxWeight = entry.maxWeight
                };
                lootData._lootTier = entry.lootTier;
                lootData._lootRarity = entry.lootRarity;
                if (prefab != null) lootData._lootPrefab = prefab;
                if (mappedType.HasValue) lootData._lootType = mappedType.Value;
                AssetDatabase.CreateAsset(lootData, lootAssetPath);
                lootCreated++;
            }

            lootDataAssets.Add(lootData);

            // 5c. Track for FishDatabase
            if (prefab != null)
            {
                dbEntries.Add(new FishDatabase.FishEntry
                {
                    lootName = entry.displayName,
                    networkPrefab = prefab
                });
            }
        }

        // 6. Update FishDatabase
        SyncFishDatabase(dbEntries);

        // 7. Wire water FishingLoot
        int waterWired = SyncWaterLoot(lootDataAssets);

        // 8. Register NetworkManager spawn prefabs
        SyncNetworkPrefabs(dbEntries);

        // 9. Save all
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 10. Summary
        Debug.Log($"[ItemConfigEditor] 同步完成:\n" +
                  $"  Prefab 更新: {prefabsUpdated}\n" +
                  $"  FishingLootData 新建: {lootCreated}, 更新: {lootUpdated}\n" +
                  $"  FishDatabase 条目: {dbEntries.Count}\n" +
                  $"  场景 FishingLoot 更新: {waterWired}");
    }

    private void SyncFishDatabase(List<FishDatabase.FishEntry> entries)
    {
        var db = AssetDatabase.LoadAssetAtPath<FishDatabase>(FishDatabasePath);
        if (db == null)
        {
            Debug.LogError($"[ItemConfigEditor] FishDatabase 未找到: {FishDatabasePath}，中止 FishDatabase 更新");
            return;
        }

        db.entries.Clear();
        db.entries.AddRange(entries);
        EditorUtility.SetDirty(db);
        Debug.Log($"[ItemConfigEditor] FishDatabase 已更新: {entries.Count} 条目");
    }

    private int SyncWaterLoot(List<FishingLootData> lootDataAssets)
    {
        var fishingLoot = Object.FindAnyObjectByType<FishingLoot>();
        if (fishingLoot == null)
        {
            Debug.LogWarning("[ItemConfigEditor] 场景中未找到 FishingLoot 组件。请打开 GameScene 后重新同步。");
            return 0;
        }

        fishingLoot._fishingLoot.Clear();
        foreach (var ld in lootDataAssets)
        {
            if (ld != null)
                fishingLoot._fishingLoot.Add(ld);
        }

        EditorUtility.SetDirty(fishingLoot);
        return fishingLoot._fishingLoot.Count;
    }

    private void SyncNetworkPrefabs(List<FishDatabase.FishEntry> entries)
    {
        var nm = Object.FindAnyObjectByType<NetworkManager>();
        bool loadedLobby = false;
        UnityEngine.SceneManagement.Scene lobbyScene = default;

        if (nm == null)
        {
            lobbyScene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/Scenes/LobbyScene.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Additive);
            loadedLobby = true;
            nm = Object.FindAnyObjectByType<NetworkManager>();
        }

        if (nm == null)
        {
            Debug.LogWarning("[ItemConfigEditor] 未找到 NetworkManager，Prefab 未注册到 spawnPrefabs。");
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
                Debug.Log($"[ItemConfigEditor] 注册 spawn prefab: {entry.lootName}");
                changed = true;
            }
        }

        if (changed)
        {
            EditorUtility.SetDirty(nm);
            if (loadedLobby)
            {
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(lobbyScene);
                Debug.Log("[ItemConfigEditor] 已保存 LobbyScene（更新 spawnPrefabs）");
            }
        }

        if (loadedLobby)
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(lobbyScene, true);
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
}
#endif
