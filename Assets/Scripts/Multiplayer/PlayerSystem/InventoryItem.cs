using System;

/// <summary>
/// 背包物品条目，包含 LogicID 和数量。
/// </summary>
[Serializable]
public class InventoryItem
{
    public string logicId;
    public int count;
}
