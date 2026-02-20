using Mirror;
using UnityEngine;

namespace MultiplayerFishing
{
    /// <summary>
    /// 客户端请求服务器切换场景的网络消息。
    /// </summary>
    public struct SceneChangeRequestMessage : NetworkMessage
    {
        public string sceneName;
    }

    /// <summary>
    /// Auto-connect logic:
    /// - Batch mode (server build): auto StartServer
    /// - Non-batch (client build): auto connect to server IP
    /// </summary>
    public class HeadlessAutoStart : MonoBehaviour
    {
        // Change this to your server IP
        public static string ServerAddress = "47.95.178.225";

        /// <summary>
        /// 服务器是否已切换到游戏场景。防止重复切换。
        /// </summary>
        private static bool _serverInGameScene;

        private void Start()
        {
            var nm = GetComponent<NetworkManager>();
            if (nm == null) return;

            // Ensure the process actually terminates when the window is closed
            Application.wantsToQuit += OnWantsToQuit;

            if (Application.isBatchMode)
            {
                Debug.Log("[HeadlessAutoStart] Batch mode — starting Server Only");

                // 清掉 onlineScene，让服务器留在 LobbyScene。
                // 场景切换由客户端通过 SceneChangeRequestMessage 触发。
                // 如果不清，Mirror 会在 ServerAccept 后自动发 SceneMessage，
                // 导致客户端跳过大厅直接进入 GameScene。
                nm.onlineScene = "";

                LogSpawnPrefabs(nm, "Server");
                nm.StartServer();

                // 注册场景切换请求处理器
                NetworkServer.RegisterHandler<SceneChangeRequestMessage>(OnSceneChangeRequest);
                _serverInGameScene = false;
            }
            // Client: no longer auto-connects. LobbyUI handles manual connect.
        }

        /// <summary>
        /// Diagnostic: log all registered spawn prefabs and their assetIds.
        /// Helps debug "Failed to spawn server object" errors.
        /// </summary>
        public static void LogSpawnPrefabs(NetworkManager nm, string label)
        {
            Debug.Log($"[SpawnPrefabDiag][{label}] spawnPrefabs count={nm.spawnPrefabs.Count}");
            for (int i = 0; i < nm.spawnPrefabs.Count; i++)
            {
                var prefab = nm.spawnPrefabs[i];
                if (prefab == null)
                {
                    Debug.Log($"[SpawnPrefabDiag][{label}]   [{i}] NULL");
                    continue;
                }
                var ni = prefab.GetComponent<NetworkIdentity>();
                uint assetId = ni != null ? ni.assetId : 0;
                Debug.Log($"[SpawnPrefabDiag][{label}]   [{i}] {prefab.name} assetId={assetId}");
            }
            // Also log the player prefab
            if (nm.playerPrefab != null)
            {
                var pni = nm.playerPrefab.GetComponent<NetworkIdentity>();
                Debug.Log($"[SpawnPrefabDiag][{label}]   [player] {nm.playerPrefab.name} assetId={pni?.assetId ?? 0}");
            }
        }
        

        private static bool OnWantsToQuit()
        {
            Debug.Log("[HeadlessAutoStart] Application quitting — stopping network");
            if (NetworkClient.active) NetworkClient.Disconnect();
            if (NetworkServer.active) NetworkServer.Shutdown();
            return true; // allow quit to proceed
        }

        /// <summary>
        /// 服务端处理客户端的场景切换请求。
        /// 第一个已认证客户端请求时切换场景，后续客户端会被 Mirror 自动同步。
        /// </summary>
        private static void OnSceneChangeRequest(NetworkConnectionToClient conn, SceneChangeRequestMessage msg)
        {
            if (!conn.isAuthenticated)
            {
                Debug.LogWarning($"[HeadlessAutoStart] Unauthenticated scene change request from {conn}, ignoring");
                return;
            }

            if (_serverInGameScene)
            {
                // 服务器已在游戏场景，新连接的客户端会被 Mirror 自动同步场景
                Debug.Log($"[HeadlessAutoStart] Server already in game scene, client will sync automatically");
                return;
            }

            var nm = NetworkManager.singleton;
            if (nm == null) return;

            Debug.Log($"[HeadlessAutoStart] Scene change requested: {msg.sceneName}");
            _serverInGameScene = true;
            nm.ServerChangeScene(msg.sceneName);
        }

        private void OnApplicationQuit()
        {
            // Force kill the process in case Unity hangs during shutdown
            // (e.g. background threads from KCP transport)
#if !UNITY_EDITOR
            System.Diagnostics.Process.GetCurrentProcess().Kill();
#endif
        }
    }
}
