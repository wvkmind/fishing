using Mirror;
using UnityEngine;

namespace MultiplayerFishing
{
    /// <summary>
    /// Auto-connect logic:
    /// - Batch mode (server build): auto StartServer, setup AdditiveSceneManager
    /// - Non-batch (client build): LobbyUI handles manual connect
    /// </summary>
    public class HeadlessAutoStart : MonoBehaviour
    {
        // Change this to your server IP
        public static string ServerAddress = "47.95.178.225";

        private void Start()
        {
            var nm = GetComponent<NetworkManager>();
            if (nm == null) return;

            Application.wantsToQuit += OnWantsToQuit;

            if (Application.isBatchMode)
            {
                Debug.Log("[HeadlessAutoStart] Batch mode — starting Server Only");

                // 清掉 onlineScene，服务器留在 LobbyScene 作为基础场景。
                // 游戏地图通过 AdditiveSceneManager 以 Additive 方式加载。
                nm.onlineScene = "";

                LogSpawnPrefabs(nm, "Server");
                nm.StartServer();

                // 初始化 AdditiveSceneManager
                var asm = GetComponent<AdditiveSceneManager>();
                if (asm == null)
                    asm = gameObject.AddComponent<AdditiveSceneManager>();
                asm.ServerSetup();

                // 监听玩家断开连接
                NetworkServer.OnDisconnectedEvent += OnServerDisconnect;

                Debug.Log("[HeadlessAutoStart] Server started with AdditiveSceneManager");
            }
        }

        private static void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (AdditiveSceneManager.Instance != null)
                AdditiveSceneManager.Instance.OnPlayerDisconnected(conn);
        }

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
            return true;
        }

        private void OnApplicationQuit()
        {
#if !UNITY_EDITOR
            System.Diagnostics.Process.GetCurrentProcess().Kill();
#endif
        }
    }
}
