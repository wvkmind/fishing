using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家数据模型，包含玩家身份信息和背包数据。
/// 使用 JsonUtility 进行序列化/反序列化。
/// </summary>
[Serializable]
public class PlayerData
{
    public string playerId;
    public string playerName;
    public string modelLogicId;
    public Inventory inventory = new Inventory();

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
