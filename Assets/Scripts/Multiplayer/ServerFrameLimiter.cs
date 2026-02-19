using Mirror;
using UnityEngine;

namespace MultiplayerFishing
{
    /// <summary>
    /// Limits dedicated server framerate. Clients use default Player Settings.
    /// </summary>
    public class ServerFrameLimiter : MonoBehaviour
    {
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            // Only touch framerate on dedicated server
            if (NetworkServer.active && !NetworkClient.active)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 30;
            }
        }
    }
}
