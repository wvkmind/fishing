/// <summary>
/// 上层物品分类枚举，属于玩家系统/背包系统层级。
/// 与 FishingGameTool 的 LootType（下层钓鱼行为枚举）分层独立。
/// </summary>
public enum ItemType
{
    Fish,        // 鱼
    WaterJunk,   // 水里垃圾
    FishingRod,  // 鱼竿
    Bait,        // 鱼饵
    Beverage     // 饮品
}
