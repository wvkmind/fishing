using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 物品注册表 ScriptableObject，物品数据的唯一真相源。
/// 通过 LogicID 或 displayName 查找物品条目，支持按 ItemType 筛选。
/// </summary>
[CreateAssetMenu(fileName = "ItemRegistry", menuName = "PlayerSystem/Item Registry")]
public class ItemRegistry : ScriptableObject
{
    public List<ItemRegistryEntry> entries = new List<ItemRegistryEntry>();

    /// <summary>
    /// 按 LogicID 查找物品条目。
    /// </summary>
    /// <param name="logicId">要查找的 LogicID</param>
    /// <returns>匹配的条目，未找到返回 null</returns>
    public ItemRegistryEntry FindByLogicId(string logicId)
    {
        if (string.IsNullOrEmpty(logicId))
        {
            Debug.LogWarning("[ItemRegistry] FindByLogicId: logicId is null or empty");
            return null;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].logicId == logicId)
                return entries[i];
        }

        Debug.LogWarning($"[ItemRegistry] FindByLogicId: '{logicId}' not found");
        return null;
    }

    /// <summary>
    /// 按 displayName 查找物品条目（用于 CmdPickupFish 时从鱼名映射到 LogicID）。
    /// </summary>
    /// <param name="displayName">要查找的显示名称</param>
    /// <returns>匹配的条目，未找到返回 null</returns>
    public ItemRegistryEntry FindByDisplayName(string displayName)
    {
        if (string.IsNullOrEmpty(displayName))
        {
            Debug.LogWarning("[ItemRegistry] FindByDisplayName: displayName is null or empty");
            return null;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].displayName == displayName)
                return entries[i];
        }

        Debug.LogWarning($"[ItemRegistry] FindByDisplayName: '{displayName}' not found");
        return null;
    }

    /// <summary>
    /// 获取所有指定 ItemType 的条目。
    /// </summary>
    /// <param name="type">要筛选的物品类型</param>
    /// <returns>匹配的条目列表</returns>
    public List<ItemRegistryEntry> GetByItemType(ItemType type)
    {
        var result = new List<ItemRegistryEntry>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].itemType == type)
                result.Add(entries[i]);
        }
        return result;
    }
}
