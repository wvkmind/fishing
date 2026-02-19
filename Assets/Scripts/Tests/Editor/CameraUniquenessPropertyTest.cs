using NUnit.Framework;
using UnityEngine;
using FishingGameTool.Example;
using MultiplayerFishing;

namespace MultiplayerFishing.Tests
{
    /// <summary>
    /// Property 2: 场景摄像机唯一性不变量
    /// For any client scene state (regardless of how many players are connected),
    /// the number of enabled AudioListener components should be exactly 1,
    /// and only the local player's cameras should be enabled.
    ///
    /// **Validates: Requirements 7.2, 7.3**
    /// </summary>
    [TestFixture]
    public class CameraUniquenessPropertyTest
    {
        private struct PlayerObjects
        {
            public GameObject playerGo;
            public GameObject tppCameraGo;
            public GameObject fppCameraGo;
            public CharacterMovement characterMovement;
            public InteractionSystem interactionSystem;
            public SimpleUIManager simpleUIManager;
            public Camera tppCamera;
            public Camera fppCamera;
            public AudioListener audioListener;
        }

        private PlayerObjects[] _players;

        [TearDown]
        public void TearDown()
        {
            if (_players == null) return;
            foreach (var p in _players)
            {
                if (p.playerGo != null) Object.DestroyImmediate(p.playerGo);
                if (p.tppCameraGo != null) Object.DestroyImmediate(p.tppCameraGo);
                if (p.fppCameraGo != null) Object.DestroyImmediate(p.fppCameraGo);
            }
            _players = null;
        }

        private PlayerObjects CreatePlayer(string name)
        {
            var p = new PlayerObjects();

            p.playerGo = new GameObject(name);
            p.playerGo.AddComponent<Animator>();
            p.playerGo.AddComponent<CharacterController>();
            p.characterMovement = p.playerGo.AddComponent<CharacterMovement>();
            p.interactionSystem = p.playerGo.AddComponent<InteractionSystem>();
            p.simpleUIManager = p.playerGo.AddComponent<SimpleUIManager>();
            p.audioListener = p.playerGo.AddComponent<AudioListener>();

            p.tppCameraGo = new GameObject(name + "_TPPCamera");
            p.tppCamera = p.tppCameraGo.AddComponent<Camera>();

            p.fppCameraGo = new GameObject(name + "_FPPCamera");
            p.fppCamera = p.fppCameraGo.AddComponent<Camera>();

            return p;
        }

        /// <summary>
        /// Property: For any random number of players (2-8) with exactly one local player,
        /// after configuring all players, the scene should have exactly 1 enabled AudioListener
        /// and only the local player's cameras should be enabled (all remote cameras disabled).
        ///
        /// **Validates: Requirements 7.2, 7.3**
        /// </summary>
        [Test, Repeat(100)]
        public void Property_SceneHasExactlyOneAudioListenerAndOnlyLocalCamerasEnabled()
        {
            // Generate random player count [2, 8]
            int playerCount = UnityEngine.Random.Range(2, 9);
            int localIndex = UnityEngine.Random.Range(0, playerCount);

            _players = new PlayerObjects[playerCount];

            // Create and configure all players
            for (int i = 0; i < playerCount; i++)
            {
                _players[i] = CreatePlayer($"Player_{i}");
                bool isLocal = (i == localIndex);

                NetworkPlayerSetup.ConfigurePlayerComponents(
                    isLocal,
                    _players[i].characterMovement,
                    _players[i].interactionSystem,
                    _players[i].simpleUIManager,
                    _players[i].tppCamera,
                    _players[i].fppCamera,
                    _players[i].audioListener);
            }

            // Count enabled AudioListeners across all players
            int enabledAudioListeners = 0;
            for (int i = 0; i < playerCount; i++)
            {
                if (_players[i].audioListener.enabled)
                    enabledAudioListeners++;
            }

            Assert.AreEqual(1, enabledAudioListeners,
                $"Expected exactly 1 enabled AudioListener with {playerCount} players " +
                $"(local index={localIndex}), but found {enabledAudioListeners}");

            // Verify only local player's cameras are enabled, all remote cameras disabled
            for (int i = 0; i < playerCount; i++)
            {
                bool isLocal = (i == localIndex);

                Assert.AreEqual(isLocal, _players[i].tppCamera.enabled,
                    $"Player {i} TPP Camera enabled should be {isLocal} " +
                    $"(isLocal={isLocal}, playerCount={playerCount})");

                Assert.AreEqual(isLocal, _players[i].fppCamera.enabled,
                    $"Player {i} FPP Camera enabled should be {isLocal} " +
                    $"(isLocal={isLocal}, playerCount={playerCount})");
            }
        }
    }
}
