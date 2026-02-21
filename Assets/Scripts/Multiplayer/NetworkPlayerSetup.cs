using Mirror;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FishingGameTool.Example;

namespace MultiplayerFishing
{
    /// <summary>
    /// Configures local vs remote player component states on spawn.
    /// Local player: enables input, cameras, UI.
    /// Remote player: disables input, cameras, AudioListener; keeps Animator and renderers.
    /// Also creates a floating name label above every player's head visible to all clients.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class NetworkPlayerSetup : NetworkBehaviour
    {
        [Header("Component References")]
        [SerializeField] private CharacterMovement _characterMovement;
        [SerializeField] private InteractionSystem _interactionSystem;
        [SerializeField] private SimpleUIManager _simpleUIManager;
        [SerializeField] private Camera _tppCamera;
        [SerializeField] private Camera _fppCamera;
        [SerializeField] private AudioListener _audioListener;

        [SyncVar(hook = nameof(OnPlayerNameChanged))]
        public string syncPlayerName;

        private GameObject _nameLabelGO;

        public override void OnStartServer()
        {
            base.OnStartServer();
            Debug.Log($"[NPS][Server] OnStartServer: netId={netId} conn={connectionToClient}");
            // Look up player name from ConnectionPlayerMap → ServerStorage
            if (connectionToClient != null &&
                PlayerAuthenticator.ConnectionPlayerMap.TryGetValue(connectionToClient, out string playerId))
            {
                var storage = PlayerAuthenticator.Storage;
                if (storage != null)
                {
                    var data = storage.FindPlayer(playerId);
                    if (data != null)
                    {
                        syncPlayerName = data.playerName;
                        Debug.Log($"[NPS][Server] Set syncPlayerName='{syncPlayerName}' for netId={netId}");
                    }
                }
            }
        }

        public override void OnStartAuthority()
        {
            FindSceneCameras();

            // 玩家创建时始终在大厅，不启用移动和相机。
            // 进入游戏场景后由 NFC.EnterGameMode() 启用。
            ConfigurePlayerComponents(false,
                _characterMovement, _interactionSystem, _simpleUIManager,
                _tppCamera, _fppCamera, _audioListener);

            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
        }

        public override void OnStartClient()
        {
            Debug.Log($"[NPS][Client] OnStartClient: netId={netId} isOwned={isOwned} " +
                       $"syncPlayerName='{syncPlayerName}' scene='{gameObject.scene.name}'");
            if (isOwned)
                return;

            FindSceneCameras();

            ConfigurePlayerComponents(false,
                _characterMovement, _interactionSystem, _simpleUIManager,
                _tppCamera, _fppCamera, _audioListener);

            // Remote player: disable CharacterController, NetworkTransform handles position
            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
        }

        /// <summary>
        /// Called on all clients after OnStartClient. Create the floating name label here
        /// so it works for both local and remote players.
        /// </summary>
        private void Start()
        {
            if (Application.isBatchMode) return;
            Debug.Log($"[NPS] Start: netId={netId} isOwned={isOwned} syncPlayerName='{syncPlayerName}' " +
                       $"scene='{gameObject.scene.name}'");
            CreateNameLabel(syncPlayerName);
        }

        private void OnPlayerNameChanged(string oldName, string newName)
        {
            Debug.Log($"[NPS] OnPlayerNameChanged: netId={netId} '{oldName}' → '{newName}'");
            UpdateNameLabel(newName);
        }

        private void CreateNameLabel(string playerName)
        {
            if (_nameLabelGO != null) return;

            _nameLabelGO = new GameObject("PlayerNameLabel");
            _nameLabelGO.transform.SetParent(transform, false);
            // Position above head (adjust Y based on character height)
            _nameLabelGO.transform.localPosition = new Vector3(0f, 2.2f, 0f);

            var canvas = _nameLabelGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 200;

            var rt = _nameLabelGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(2f, 0.4f);
            rt.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            var textGo = new GameObject("NameText");
            textGo.transform.SetParent(_nameLabelGO.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = string.IsNullOrEmpty(playerName) ? "" : playerName;
            tmp.fontSize = 28;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
        }

        private void UpdateNameLabel(string playerName)
        {
            if (_nameLabelGO == null)
            {
                CreateNameLabel(playerName);
                return;
            }
            var tmp = _nameLabelGO.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null)
                tmp.text = string.IsNullOrEmpty(playerName) ? "" : playerName;
        }

        /// <summary>
        /// Billboard: make name label always face the camera.
        /// </summary>
        private void LateUpdate()
        {
            if (_nameLabelGO == null || Application.isBatchMode) return;
            var cam = Camera.main;
            if (cam == null) return;
            _nameLabelGO.transform.rotation = cam.transform.rotation;
        }

        /// <summary>
        /// Finds scene-level cameras and wires them into CharacterMovement.
        /// The TPP Camera lives in the scene (not in the prefab), so we need
        /// to locate it at runtime.
        /// </summary>
        private void FindSceneCameras()
        {
            if (_characterMovement == null) return;

            // Find TPP Camera in scene by component
            if (_tppCamera == null)
            {
                var tpp = Object.FindAnyObjectByType<TPPCamera>();
                if (tpp != null)
                {
                    _tppCamera = tpp.GetComponent<Camera>();
                    _characterMovement._tppCamera = tpp.transform;
                }
            }

            // FPP Camera is usually a child of the prefab, but check just in case
            if (_fppCamera == null)
            {
                var fpp = GetComponentInChildren<FPPCameraSystem>(true);
                if (fpp != null)
                {
                    _fppCamera = fpp.GetComponent<Camera>();
                    _characterMovement._fppCamera = fpp.transform;
                }
            }
        }

        /// <summary>
        /// Configures player components based on authority (ownership) state.
        /// Extracted as a static method for testability without Mirror networking.
        /// </summary>
        /// <param name="isLocal">True if the player has authority (is local), false for remote.</param>
        public static void ConfigurePlayerComponents(
            bool isLocal,
            CharacterMovement characterMovement,
            InteractionSystem interactionSystem,
            SimpleUIManager simpleUIManager,
            Camera tppCamera,
            Camera fppCamera,
            AudioListener audioListener)
        {
            if (characterMovement != null)
                characterMovement.enabled = isLocal;

            if (interactionSystem != null)
                interactionSystem.enabled = isLocal;

            // SimpleUIManager is replaced by FishingUI — destroy it and its Canvas
            if (simpleUIManager != null)
            {
                // Destroy the entire Canvas root (SimpleUIManager lives on a Canvas child)
                var canvas = simpleUIManager.GetComponentInParent<Canvas>();
                if (canvas != null)
                    Destroy(canvas.gameObject);
                else
                    Destroy(simpleUIManager.gameObject);
            }

            if (tppCamera != null)
                tppCamera.enabled = isLocal;

            if (fppCamera != null)
                fppCamera.enabled = isLocal;

            if (audioListener != null)
            {
                audioListener.enabled = isLocal;

                // When enabling local player's AudioListener, disable all others to avoid
                // "There are 2 audio listeners in the scene" warning
                if (isLocal)
                {
                    foreach (var al in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                    {
                        if (al != audioListener)
                            al.enabled = false;
                    }
                }
            }

            // Enable/disable camera control scripts
            if (tppCamera != null)
            {
                var tppScript = tppCamera.GetComponent<TPPCamera>();
                if (tppScript != null)
                    tppScript.enabled = isLocal;
            }

            if (fppCamera != null)
            {
                var fppScript = fppCamera.GetComponent<FPPCameraSystem>();
                if (fppScript != null)
                    fppScript.enabled = isLocal;
            }
        }
    }
}
