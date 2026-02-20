using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单条鱼的个人纪录（按 logicId 记录最大重量）。
/// </summary>
[Serializable]
public class FishRecord
{
    public string logicId;
    public float maxWeight;
}

/// <summary>
/// 玩家数据模型，包含玩家身份信息、背包数据和钓鱼纪录。
/// 使用 JsonUtility 进行序列化/反序列化。
/// </summary>
[Serializable]
public class PlayerData
{
    public string playerId;
    public string playerName;
    public string modelLogicId;
    public Inventory inventory = new Inventory();
    public List<FishRecord> fishRecords = new List<FishRecord>();

    /// <summary>
    /// 查找指定鱼的个人纪录，未找到返回 null。
    /// </summary>
    public FishRecord FindFishRecord(string logicId)
    {
        for (int i = 0; i < fishRecords.Count; i++)
            if (fishRecords[i].logicId == logicId) return fishRecords[i];
        return null;
    }

    /// <summary>
    /// 尝试更新鱼的纪录。如果 weight 超过当前纪录则更新并返回 true（新纪录）。
    /// 如果没有纪录则创建并返回 true。
    /// </summary>
    public bool TryUpdateFishRecord(string logicId, float weight)
    {
        var record = FindFishRecord(logicId);
        if (record == null)
        {
            fishRecords.Add(new FishRecord { logicId = logicId, maxWeight = weight });
            return true;
        }
        if (weight > record.maxWeight)
        {
            record.maxWeight = weight;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 将 PlayerData 序列化为 JSON 字符串。
    /// </summary>
    public string ToJson()
    {
        return JsonUtility.ToJson(this);
    }

    /// <summary>
    /// 从 JSON 字符串反序列化为 PlayerData 对象。
    /// 缺失字段使用默认值（JsonUtility 的默认行为）。
    /// </summary>
    public static PlayerData FromJson(string json)
    {
        return JsonUtility.FromJson<PlayerData>(json);
    }
}

/// <summary>
/// PlayerData 集合包装类，用于 ServerStorage 的 JSON 序列化。
/// JsonUtility 不支持直接序列化 Dictionary，因此使用 List 包装。
/// </summary>
[Serializable]
public class PlayerDataCollection
{
    public List<PlayerData> players = new List<PlayerData>();
}
