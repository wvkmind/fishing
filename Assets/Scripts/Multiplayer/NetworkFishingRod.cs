using Mirror;
using UnityEngine;
using FishingGameTool.Fishing.Rod;

namespace MultiplayerFishing
{
    /// <summary>
    /// Network wrapper for FishingRod bending and line rendering synchronization.
    /// 
    /// Local player (Authority): reads bend values from the FishingRod Animator each frame
    /// and syncs them to all clients via Syncnimator with synced values using Lerp smoothing.
    /// </summary>
    public class NetworkFishingRod : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private FishingRod _fishingRod;
        [SerializeField] private NetworkFishingController _networkFishingController;

        // --- SyncVars ---
        [SyncVar] public float syncHorizontalBend;
        [SyncVar] public float syncVerticalBend;
        [SyncVar] public bool syncLootCaught;

        /// <summary>
        /// Animator on the FishingRod GameObject, cached at startup.
        /// </summary>
        private Animator _animator;

        /// <summary>
        /// Smoothing speed for remote player Lerp interpolation.
        /// </summary>
        private const float LerpSpeed = 14f;

        // ───────────────────────── Lifecycle ─────────────────────────

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void Update()
        {
            if (isOwned)
            {
                ReadLocalBendValues();
            }
            else
            {
                ApplyRemoteBendValues();
            }
        }

        // ───────────────────────── Local Player (Authority) ─────────────────────────

        /// <summary>
        /// Reads the current HorizontalBend and VerticalBend Animator parameters
        /// set by FishingRod.CalculateBend() and sends them to the server.
        /// Also reads the _lootCaught flag for line tension sync.
        /// </summary>
        private void ReadLocalBendValues()
        {
            if (_animator == null)
                return;

            float h = _animator.GetFloat("HorizontalBend");
            float v = _animator.GetFloat("VerticalBend");
            bool loot = _fishingRod != null && _fishingRod._lootCaught;

            // Only send Command when values have actually changed to reduce traffic
            if (!Mathf.Approximately(h, syncHorizontalBend) ||
                !Mathf.Approximately(v, syncVerticalBend) ||
                loot != syncLootCaught)
            {
                CmdUpdateBendValues(h, v, loot);
            }
        }

        /// <summary>
        /// Command sent from the authority client to the server to update SyncVars.
        /// </summary>
        [Command]
        private void CmdUpdateBendValues(float horizontal, float vertical, bool lootCaught)
        {
            syncHorizontalBend = horizontal;
            syncVerticalBend = vertical;
            syncLootCaught = lootCaught;
        }

        // ───────────────────────── Remote Player ─────────────────────────

        /// <summary>
        /// Applies synced bend values to the Animator with Lerp smoothing
        /// so remote players see smooth rod bending transitions.
        /// Also syncs the _lootCaught flag on the FishingRod for line tension.
        /// </summary>
        private void ApplyRemoteBendValues()
        {
            if (_animator == null)
                return;

            float currentH = _animator.GetFloat("HorizontalBend");
            float currentV = _animator.GetFloat("VerticalBend");

            float smoothedH = Mathf.Lerp(currentH, syncHorizontalBend, Time.deltaTime * LerpSpeed);
            float smoothedV = Mathf.Lerp(currentV, syncVerticalBend, Time.deltaTime * LerpSpeed);

            _animator.SetFloat("HorizontalBend", smoothedH);
            _animator.SetFloat("VerticalBend", smoothedV);

            // Sync loot caught state for line tension rendering
            if (_fishingRod != null)
            {
                _fishingRod._lootCaught = syncLootCaught;
            }

            // Ensure remote client has the float reference for line rendering
            if (_fishingRod != null && _networkFishingController != null)
            {
                _fishingRod._fishingFloat = _networkFishingController.ActiveFloatTransform;
            }
        }
    }
}
