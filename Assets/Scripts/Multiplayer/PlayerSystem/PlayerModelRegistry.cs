using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家模型注册表 ScriptableObject，将模型 LogicID 映射到玩家模型 Prefab。
/// </summary>
[CreateAssetMenu(fileName = "PlayerModelRegistry", menuName = "PlayerSystem/Player Model Registry")]
public class PlayerModelRegistry : ScriptableObject
{
    [Serializable]
    public struct ModelEntry
    {
        public string logicId;
        public GameObject prefab;
    }

    public List<ModelEntry> entries = new List<ModelEntry>();

    /// <summary>
    /// 按 LogicID 获取玩家模型 Prefab。
    /// </summary>
    /// <param name="logicId">要查找的模型 LogicID</param>
    /// <returns>匹配的 Prefab，未找到返回 null</returns>
    public GameObject GetPrefab(string logicId)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].logicId == logicId)
                return entries[i].prefab;
        }

        Debug.LogWarning($"[PlayerModelRegistry] GetPrefab: '{logicId}' not found");
        return null;
    }
}
