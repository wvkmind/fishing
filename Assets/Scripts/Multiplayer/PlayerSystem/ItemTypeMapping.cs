using FishingGameTool.Fishing.LootData;

/// <summary>
/// ItemType（上层物品分类）与 LootType（下层钓鱼行为枚举）之间的静态映射。
/// Fish → LootType.Fish，WaterJunk → LootType.Item，其他不可映射返回 null。
/// </summary>
public static class ItemTypeMapping
{
    /// <summary>
    /// 将 ItemType 映射到 LootType。
    /// Fish → LootType.Fish，WaterJunk → LootType.Item，其他返回 null。
    /// </summary>
    public static LootType? ToLootType(ItemType itemType)
    {
        switch (itemType)
        {
            case ItemType.Fish:
                return LootType.Fish;
            case ItemType.WaterJunk:
                return LootType.Item;
            default:
                return null;
        }
    }

    /// <summary>
    /// 将 LootType 反向映射到 ItemType。
    /// LootType.Fish → Fish，LootType.Item → WaterJunk。
    /// </summary>
    public static ItemType ToItemType(LootType lootType)
    {
        switch (lootType)
        {
            case LootType.Fish:
                return ItemType.Fish;
            case LootType.Item:
                return ItemType.WaterJunk;
            default:
                return ItemType.WaterJunk;
        }
    }
}
