using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 背包数据模型，存储玩家拥有的物品列表。
/// </summary>
[Serializable]
public class Inventory
{
    public List<InventoryItem> items = new List<InventoryItem>();

    /// <summary>
    /// 添加物品：已有相同 logicId 则数量增加，否则新建条目。
    /// </summary>
    public void AddItem(string logicId, int count = 1)
    {
        var existing = FindItem(logicId);
        if (existing != null)
        {
            existing.count += count;
        }
        else
        {
            items.Add(new InventoryItem { logicId = logicId, count = count });
        }
    }

    /// <summary>
    /// 按 LogicID 查找物品条目。
    /// </summary>
    public InventoryItem FindItem(string logicId)
    {
        return items.FirstOrDefault(item => item.logicId == logicId);
    }

    /// <summary>
    /// 获取物品总数量。
    /// </summary>
    public int TotalItemCount
    {
        get { return items.Sum(item => item.count); }
    }
}
