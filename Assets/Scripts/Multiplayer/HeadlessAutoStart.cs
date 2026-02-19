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
                nm.StartServer();
            }
            // Client: no longer auto-connects. LobbyUI handles manual connect.
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
