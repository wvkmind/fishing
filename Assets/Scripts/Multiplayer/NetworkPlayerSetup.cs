using Mirror;
using UnityEngine;
using FishingGameTool.Example;

namespace MultiplayerFishing
{
    /// <summary>
    /// Configures local vs remote player component states on spawn.
    /// Local player: enables input, cameras, UI.
    /// Remote player: disables input, cameras, AudioListener; keeps Animator and renderers.
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

        public override void OnStartAuthority()
        {
            FindSceneCameras();

            ConfigurePlayerComponents(true,
                _characterMovement, _interactionSystem, _simpleUIManager,
                _tppCamera, _fppCamera, _audioListener);

            // Local player: CharacterController handles movement,
            // disable CharacterController on remote players to avoid conflicts
            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
        }

        public override void OnStartClient()
        {
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
