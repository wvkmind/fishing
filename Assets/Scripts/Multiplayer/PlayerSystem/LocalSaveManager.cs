using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 客户端本地存档管理，负责读写 LocalSaveFile（player_save.json）。
/// 文件不存在或无效时返回 null；IO 错误时 Debug.LogError 记录，不阻断流程。
/// </summary>
public static class LocalSaveManager
{
    [Serializable]
    private class SaveData
    {
        public string playerId;
    }

    /// <summary>
    /// 可覆盖的存档路径，用于测试。设为 null 时恢复默认路径。
    /// </summary>
    private static string _overridePath;

    /// <summary>
    /// 存档文件路径。默认为 Application.persistentDataPath + "/player_save.json"。
    /// </summary>
    public static string SaveFilePath
    {
        get
        {
            if (!string.IsNullOrEmpty(_overridePath))
                return _overridePath;
            return Path.Combine(Application.persistentDataPath, "player_save.json");
        }
    }

    /// <summary>
    /// 设置自定义存档路径（用于测试）。传入 null 恢复默认路径。
    /// </summary>
    public static void SetCustomPath(string path)
    {
        _overridePath = path;
    }

    /// <summary>
    /// 读取本地存档，返回 playerId。
    /// 文件不存在、内容为空、JSON 无效或 playerId 为空时返回 null。
    /// IO 错误时 Debug.LogError 记录并返回 null。
    /// </summary>
    public static string LoadPlayerId()
    {
        string filePath = SaveFilePath;

        if (!File.Exists(filePath))
            return null;

        string json;
        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[LocalSaveManager] Failed to read save file: {e.Message}");
            return null;
        }

        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            var saveData = JsonUtility.FromJson<SaveData>(json);
            if (saveData == null || string.IsNullOrEmpty(saveData.playerId))
                return null;
            return saveData.playerId;
        }
        catch (Exception e)
        {
            Debug.LogError($"[LocalSaveManager] Failed to parse save file: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 将 playerId 写入本地存档文件。
    /// IO 错误时 Debug.LogError 记录，不抛出异常。
    /// </summary>
    public static void SavePlayerId(string playerId)
    {
        string filePath = SaveFilePath;

        var saveData = new SaveData { playerId = playerId };
        string json = JsonUtility.ToJson(saveData);

        try
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[LocalSaveManager] Failed to write save file: {e.Message}");
        }
    }
}
