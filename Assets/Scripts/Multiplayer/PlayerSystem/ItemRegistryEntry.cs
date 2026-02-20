using System;
using System.Collections.Generic;
using UnityEngine;
using FishingGameTool.Fishing.LootData;

/// <summary>
/// 单个物品条目数据结构，存储在 ItemRegistry 中。
/// 包含物品的完整属性：基础信息、钓鱼参数、可钓条件等。
/// </summary>
[Serializable]
public class ItemRegistryEntry
{
    public string logicId;                    // 唯一标识
    public ItemType itemType;                 // 物品类型
    public string displayName;                // 显示名称
    [TextArea] public string description;     // 描述
    public Sprite icon;                       // 图标
    public GameObject prefab;                 // Prefab 资源
    public bool isCatchable;                  // 是否可被钓到

    // 长度范围
    public float minLength;                   // 最小长度
    public float maxLength;                   // 最大长度

    // 重量范围
    public float minWeight;                   // 最小重量
    public float maxWeight;                   // 最大重量

    // 稀有度（复用 FishingGameTool 枚举）
    public LootTier lootTier;
    public float lootRarity;                  // 掉落概率

    public int price;                         // 价格
    public int experienceValue;               // 经验值

    // 可钓条件（CatchableFilter）
    public List<string> allowedRods = new List<string>();       // 可用鱼竿 LogicID 列表
    public List<string> allowedBaits = new List<string>();      // 可用鱼饵 LogicID 列表
    public List<string> allowedLocations = new List<string>();  // 可钓地点名称列表
}
