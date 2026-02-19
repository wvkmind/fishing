using NUnit.Framework;
using UnityEngine;
using FishingGameTool.Example;
using MultiplayerFishing;

namespace MultiplayerFishing.Tests
{
    /// <summary>
    /// Property 1: Authority 控制输入与逻辑执行
    /// For any player object, all input-handling components' enabled state
    /// should equal whether the player has Authority (isOwned).
    /// 
    /// Validates: Requirements 2.3, 2.4, 6.3, 6.4
    /// </summary>
    [TestFixture]
    public class AuthorityControlPropertyTest
    {
        private GameObject _playerGo;
        private GameObject _tppCameraGo;
        private GameObject _fppCameraGo;

        private CharacterMovement _characterMovement;
        private InteractionSystem _interactionSystem;
        private SimpleUIManager _simpleUIManager;
        private Camera _tppCamera;
        private Camera _fppCamera;
        private AudioListener _audioListener;
        private TPPCamera _tppCameraScript;
        private FPPCameraSystem _fppCameraScript;

        [SetUp]
        public void SetUp()
        {
            _playerGo = new GameObject("TestPlayer");

            // CharacterMovement requires Animator and CharacterController
            _playerGo.AddComponent<Animator>();
            _playerGo.AddComponent<CharacterController>();
            _characterMovement = _playerGo.AddComponent<CharacterMovement>();
            _interactionSystem = _playerGo.AddComponent<InteractionSystem>();
            _simpleUIManager = _playerGo.AddComponent<SimpleUIManager>();
            _audioListener = _playerGo.AddComponent<AudioListener>();

            // TPP Camera with TPPCamera script
            _tppCameraGo = new GameObject("TPPCamera");
            _tppCamera = _tppCameraGo.AddComponent<Camera>();
            _tppCameraScript = _tppCameraGo.AddComponent<TPPCamera>();

            // FPP Camera with FPPCameraSystem script
            _fppCameraGo = new GameObject("FPPCamera");
            _fppCamera = _fppCameraGo.AddComponent<Camera>();
            _fppCameraScript = _fppCameraGo.AddComponent<FPPCameraSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_playerGo);
            Object.DestroyImmediate(_tppCameraGo);
            Object.DestroyImmediate(_fppCameraGo);
        }

        /// <summary>
        /// Property: For any random authority state, all input/camera components'
        /// enabled state must equal the authority state.
        /// 
        /// **Validates: Requirements 2.3, 2.4, 6.3, 6.4**
        /// </summary>
        [Test, Repeat(100)]
        public void Property_AuthorityControlsInputAndLogicExecution()
        {
            // Generate random authority state
            bool isLocal = UnityEngine.Random.value > 0.5f;

            // Randomize initial component states to ensure the method
            // actually sets them, not just relying on defaults
            _characterMovement.enabled = !isLocal;
            _interactionSystem.enabled = !isLocal;
            _simpleUIManager.enabled = !isLocal;
            _tppCamera.enabled = !isLocal;
            _fppCamera.enabled = !isLocal;
            _audioListener.enabled = !isLocal;
            _tppCameraScript.enabled = !isLocal;
            _fppCameraScript.enabled = !isLocal;

            // Act: configure components based on authority
            NetworkPlayerSetup.ConfigurePlayerComponents(
                isLocal,
                _characterMovement,
                _interactionSystem,
                _simpleUIManager,
                _tppCamera,
                _fppCamera,
                _audioListener);

            // Assert: all input components match authority state
            Assert.AreEqual(isLocal, _characterMovement.enabled,
                $"CharacterMovement.enabled should be {isLocal} when isLocal={isLocal}");
            Assert.AreEqual(isLocal, _interactionSystem.enabled,
                $"InteractionSystem.enabled should be {isLocal} when isLocal={isLocal}");
            Assert.AreEqual(isLocal, _simpleUIManager.enabled,
                $"SimpleUIManager.enabled should be {isLocal} when isLocal={isLocal}");

            // Assert: camera components match authority state
            Assert.AreEqual(isLocal, _tppCamera.enabled,
                $"TPPCamera (Camera).enabled should be {isLocal} when isLocal={isLocal}");
            Assert.AreEqual(isLocal, _fppCamera.enabled,
                $"FPPCamera (Camera).enabled should be {isLocal} when isLocal={isLocal}");
            Assert.AreEqual(isLocal, _audioListener.enabled,
                $"AudioListener.enabled should be {isLocal} when isLocal={isLocal}");

            // Assert: camera control scripts match authority state
            Assert.AreEqual(isLocal, _tppCameraScript.enabled,
                $"TPPCamera script.enabled should be {isLocal} when isLocal={isLocal}");
            Assert.AreEqual(isLocal, _fppCameraScript.enabled,
                $"FPPCameraSystem script.enabled should be {isLocal} when isLocal={isLocal}");
        }
    }
}
