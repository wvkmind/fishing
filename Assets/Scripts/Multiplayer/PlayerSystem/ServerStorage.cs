using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 服务器端 JSON 文件持久化层，负责读写所有玩家数据。
/// 内部使用 Dictionary 做快速查找，序列化时与 PlayerDataCollection 互转。
/// </summary>
public class ServerStorage
{
    private Dictionary<string, PlayerData> _players = new Dictionary<string, PlayerData>();

    public string StoragePath { get; private set; }

    /// <summary>
    /// 获取所有玩家数据的字典。
    /// </summary>
    public Dictionary<string, PlayerData> AllPlayers => _players;

    /// <summary>
    /// 使用默认路径构造。
    /// </summary>
    public ServerStorage()
    {
        StoragePath = Path.Combine(Application.persistentDataPath, "server_players.json");
    }

    /// <summary>
    /// 使用自定义路径构造（用于测试）。
    /// </summary>
    public ServerStorage(string storagePath)
    {
        StoragePath = storagePath;
    }

    /// <summary>
    /// 启动时加载 JSON 文件中的所有玩家数据。
    /// 文件不存在时创建空数据文件；JSON 解析失败时备份损坏文件并以空数据启动。
    /// </summary>
    public void Load()
    {
        _players.Clear();

        if (!File.Exists(StoragePath))
        {
            SaveToFile();
            return;
        }

        string json = null;
        try
        {
            json = File.ReadAllText(StoragePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[ServerStorage] Failed to read file: {e.Message}");
            BackupAndReset();
            return;
        }

        try
        {
            var collection = JsonUtility.FromJson<PlayerDataCollection>(json);
            if (collection != null && collection.players != null)
            {
                foreach (var player in collection.players)
                {
                    if (player != null && !string.IsNullOrEmpty(player.playerId))
                    {
                        _players[player.playerId] = player;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ServerStorage] JSON parse failed: {e.Message}");
            BackupAndReset();
        }
    }

    /// <summary>
    /// 按 playerId 查找玩家，未找到返回 null。
    /// </summary>
    public PlayerData FindPlayer(string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
            return null;

        _players.TryGetValue(playerId, out var data);
        return data;
    }

    /// <summary>
    /// 创建新玩家，生成 GUID 作为 playerId，分配默认模型和空背包，持久化到文件。
    /// </summary>
    public PlayerData CreatePlayer(string playerName, string defaultModelId)
    {
        var data = new PlayerData
        {
            playerId = Guid.NewGuid().ToString(),
            playerName = playerName,
            modelLogicId = defaultModelId,
            inventory = new Inventory()
        };

        _players[data.playerId] = data;
        SaveToFile();
        return data;
    }

    /// <summary>
    /// 更新玩家数据并持久化到文件。
    /// </summary>
    public void SavePlayer(PlayerData data)
    {
        if (data == null || string.IsNullOrEmpty(data.playerId))
            return;

        _players[data.playerId] = data;
        SaveToFile();
    }

    /// <summary>
    /// 将内存中的玩家数据序列化写入 JSON 文件。
    /// </summary>
    private void SaveToFile()
    {
        var collection = new PlayerDataCollection();
        foreach (var player in _players.Values)
        {
            collection.players.Add(player);
        }

        try
        {
            string directory = Path.GetDirectoryName(StoragePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonUtility.ToJson(collection, true);
            File.WriteAllText(StoragePath, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[ServerStorage] Failed to save file: {e.Message}");
        }
    }

    /// <summary>
    /// 备份损坏的 JSON 文件并以空数据重新开始。
    /// </summary>
    private void BackupAndReset()
    {
        try
        {
            if (File.Exists(StoragePath))
            {
                string backupPath = StoragePath + ".bak";
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
                File.Move(StoragePath, backupPath);
                Debug.LogWarning($"[ServerStorage] Corrupted file backed up to: {backupPath}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ServerStorage] Failed to backup corrupted file: {e.Message}");
        }

        _players.Clear();
        SaveToFile();
    }
}
