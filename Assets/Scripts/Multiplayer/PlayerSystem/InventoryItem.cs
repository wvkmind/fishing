using System;

/// <summary>
/// 背包物品条目，包含 LogicID、数量和累计重量。
/// </summary>
[Serializable]
public class InventoryItem
{
    public string logicId;
    public int count;
    public float totalWeight;
}
