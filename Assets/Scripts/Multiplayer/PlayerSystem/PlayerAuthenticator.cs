using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// 基于 Mirror NetworkAuthenticator 的玩家验证器。
/// 客户端连接时发送 AuthRequestMessage（包含 playerId 或 playerName），
/// 服务器验证身份并决定接受或拒绝连接。
/// </summary>
public class PlayerAuthenticator : NetworkAuthenticator
{
    #region Messages

    public struct AuthRequestMessage : NetworkMessage
    {
        public string playerId;    // 已有玩家，与 playerName 互斥
        public string playerName;  // 新玩家，与 playerId 互斥
    }

    public struct AuthResponseMessage : NetworkMessage
    {
        public bool success;
        public string playerDataJson;  // 成功时包含完整 PlayerData JSON
        public string errorMessage;    // 失败时包含错误信息
    }

    #endregion

    /// <summary>
    /// 缓存当前已验证玩家的 PlayerData（客户端使用）。
    /// </summary>
    public static PlayerData LocalPlayerData;

    /// <summary>
    /// 服务端 ServerStorage 的静态引用，供其他服务端组件（如 NetworkFishingController）访问。
    /// </summary>
    public static ServerStorage Storage { get; private set; }

    /// <summary>
    /// 服务端连接到玩家 ID 的映射，供其他服务端组件查找当前连接对应的玩家。
    /// </summary>
    public static readonly Dictionary<NetworkConnection, string> ConnectionPlayerMap
        = new Dictionary<NetworkConnection, string>();

    private ServerStorage _serverStorage;

    #region Server

    /// <summary>
    /// 服务器启动时初始化 ServerStorage 并注册消息处理器。
    /// </summary>
    public override void OnStartServer()
    {
        _serverStorage = new ServerStorage();
        _serverStorage.Load();
        Storage = _serverStorage;
        ConnectionPlayerMap.Clear();
        NetworkServer.RegisterHandler<AuthRequestMessage>(OnAuthRequestMessage, false);
    }

    /// <summary>
    /// 客户端连接时的服务端回调。处理器已在 OnStartServer 中注册。
    /// </summary>
    public override void OnServerAuthenticate(NetworkConnectionToClient conn)
    {
        // 消息处理器已在 OnStartServer 中注册，此处无需额外操作。
        // Mirror 会在收到 AuthRequestMessage 时调用 OnAuthRequestMessage。
    }

    /// <summary>
    /// 处理客户端发来的验证请求。
    /// playerId 非空时查找已有玩家；playerName 非空时创建新玩家；都为空则拒绝。
    /// </summary>
    public void OnAuthRequestMessage(NetworkConnectionToClient conn, AuthRequestMessage msg)
    {
        // 优先处理 playerId（已有玩家登录）
        if (!string.IsNullOrEmpty(msg.playerId))
        {
            PlayerData data = _serverStorage.FindPlayer(msg.playerId);
            if (data != null)
            {
                ConnectionPlayerMap[conn] = data.playerId;
                conn.Send(new AuthResponseMessage
                {
                    success = true,
                    playerDataJson = data.ToJson(),
                    errorMessage = ""
                });
                ServerAccept(conn);
            }
            else
            {
                conn.Send(new AuthResponseMessage
                {
                    success = false,
                    playerDataJson = "",
                    errorMessage = "玩家不存在"
                });
                ServerReject(conn);
            }
            return;
        }

        // 处理 playerName（新玩家注册）
        if (!string.IsNullOrEmpty(msg.playerName))
        {
            PlayerData data = _serverStorage.CreatePlayer(msg.playerName, "default_player");
            ConnectionPlayerMap[conn] = data.playerId;
            conn.Send(new AuthResponseMessage
            {
                success = true,
                playerDataJson = data.ToJson(),
                errorMessage = ""
            });
            ServerAccept(conn);
            return;
        }

        // playerId 和 playerName 都为空，拒绝连接
        conn.Send(new AuthResponseMessage
        {
            success = false,
            playerDataJson = "",
            errorMessage = "请求无效：缺少 playerId 或 playerName"
        });
        ServerReject(conn);
    }

    /// <summary>
    /// 服务器停止时注销消息处理器。
    /// </summary>
    public override void OnStopServer()
    {
        NetworkServer.UnregisterHandler<AuthRequestMessage>();
    }

    #endregion

    #region Client

    /// <summary>
    /// 客户端启动时注册响应消息处理器。
    /// </summary>
    public override void OnStartClient()
    {
        NetworkClient.RegisterHandler<AuthResponseMessage>(OnAuthResponseMessage, false);
    }

    /// <summary>
    /// 客户端连接时的回调。读取本地存档发送验证请求，
    /// 无存档时显示 NameInputUI 让玩家输入名字。
    /// </summary>
    public override void OnClientAuthenticate()
    {
        string playerId = LocalSaveManager.LoadPlayerId();

        if (!string.IsNullOrEmpty(playerId))
        {
            // 已有存档，发送 playerId 验证
            NetworkClient.Send(new AuthRequestMessage
            {
                playerId = playerId,
                playerName = ""
            });
        }
        else
        {
            // 无存档，显示 NameInputUI
            ShowNameInputUI();
        }
    }

    /// <summary>
    /// 处理服务器返回的验证响应。
    /// 成功时缓存 PlayerData 并保存本地存档；失败时显示 NameInputUI。
    /// </summary>
    public void OnAuthResponseMessage(AuthResponseMessage msg)
    {
        if (msg.success)
        {
            // 解析 PlayerData 并缓存
            PlayerData data = PlayerData.FromJson(msg.playerDataJson);
            LocalPlayerData = data;

            // 保存 playerId 到本地存档
            LocalSaveManager.SavePlayerId(data.playerId);

            // 通知 Mirror 验证成功
            ClientAccept();
        }
        else
        {
            Debug.LogWarning($"[PlayerAuthenticator] Auth failed: {msg.errorMessage}");
            // 验证失败，显示 NameInputUI 让玩家输入名字
            ShowNameInputUI();
        }
    }

    /// <summary>
    /// 客户端停止时注销消息处理器。
    /// </summary>
    public override void OnStopClient()
    {
        NetworkClient.UnregisterHandler<AuthResponseMessage>();
    }

    #endregion

    #region NameInputUI

    /// <summary>
    /// 显示名字输入界面。NameInputUI 将在 Task 6.1 中创建，
    /// 此处通过 FindAnyObjectByType 查找或动态创建。
    /// </summary>
    private void ShowNameInputUI()
    {
        var nameUI = FindAnyObjectByType<NameInputUI>();
        if (nameUI == null)
        {
            var go = new GameObject("NameInputUI");
            nameUI = go.AddComponent<NameInputUI>();
        }

        nameUI.OnNameConfirmed -= OnNameConfirmed;
        nameUI.OnNameConfirmed += OnNameConfirmed;
        nameUI.Show();
    }

    /// <summary>
    /// 玩家在 NameInputUI 中确认名字后的回调。
    /// 发送包含 playerName 的验证请求。
    /// </summary>
    private void OnNameConfirmed(string playerName)
    {
        var nameUI = FindAnyObjectByType<NameInputUI>();
        if (nameUI != null)
        {
            nameUI.OnNameConfirmed -= OnNameConfirmed;
            nameUI.Hide();
        }

        NetworkClient.Send(new AuthRequestMessage
        {
            playerId = "",
            playerName = playerName
        });
    }

    #endregion
}
