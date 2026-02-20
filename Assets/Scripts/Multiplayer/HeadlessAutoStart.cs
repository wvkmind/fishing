using Mirror;
using UnityEngine;

namespace MultiplayerFishing
{
    /// <summary>
    /// Auto-connect logic:
    /// - Batch mode (server build): auto StartServer
    /// - Non-batch (client build): auto connect to server IP
    /// </summary>
    public class HeadlessAutoStart : MonoBehaviour
    {
        // Change this to your server IP
        public static string ServerAddress = "47.95.178.225";

        private void Start()
        {
            var nm = GetComponent<NetworkManager>();
            if (nm == null) return;

            // Ensure the process actually terminates when the window is closed
            Application.wantsToQuit += OnWantsToQuit;

            if (Application.isBatchMode)
            {
                Debug.Log("[HeadlessAutoStart] Batch mode — starting Server Only");
                LogSpawnPrefabs(nm, "Server");
                nm.StartServer();
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
        }

        private static bool OnWantsToQuit()
        {
            Debug.Log("[HeadlessAutoStart] Application quitting — stopping network");
            if (NetworkClient.active) NetworkClient.Disconnect();
            if (NetworkServer.active) NetworkServer.Shutdown();
            return true; // allow quit to proceed
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
