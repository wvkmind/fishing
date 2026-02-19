using UnityEngine;
using Mirror;

namespace MultiplayerFishing
{
    /// <summary>
    /// Attach to the player prefab. When the player enters a WaterZone trigger,
    /// they slowly sink for a moment, then teleport back to the last known
    /// ground position. Server-authoritative.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class WaterSinkHandler : NetworkBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float _sinkSpeed = 2f;
        [SerializeField] private float _sinkDuration = 1.0f;
        [SerializeField] private float _groundTrackInterval = 0.3f;

        private CharacterController _cc;
        private Vector3 _lastGroundPos;
        private float _groundTrackTimer;
        private bool _inWater;
        private float _sinkTimer;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
        }

        private void Start()
        {
            _lastGroundPos = transform.position;
        }

        private void Update()
        {
            if (!isServer) return;

            if (_inWater)
            {
                // Don't sink while fishing — player needs to stand near water
                var nfc = GetComponent<NetworkFishingController>();
                if (nfc != null && nfc.syncState != FishingState.Idle && nfc.syncState != FishingState.Displaying)
                {
                    _sinkTimer = 0f;
                    return;
                }

                // Sink the player
                _sinkTimer += Time.deltaTime;
                _cc.Move(Vector3.down * _sinkSpeed * Time.deltaTime);

                if (_sinkTimer >= _sinkDuration)
                {
                    TeleportToLand();
                }
            }
            else
            {
                // Track last ground position periodically
                _groundTrackTimer -= Time.deltaTime;
                if (_groundTrackTimer <= 0f)
                {
                    _groundTrackTimer = _groundTrackInterval;
                    if (IsGrounded())
                    {
                        _lastGroundPos = transform.position;
                    }
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isServer) return;
            if (other.GetComponent<WaterZone>() != null)
            {
                _inWater = true;
                _sinkTimer = 0f;
                Debug.Log($"[WaterSink] Player entered water netId={netId}");
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!isServer) return;
            if (other.GetComponent<WaterZone>() != null)
            {
                _inWater = false;
                Debug.Log($"[WaterSink] Player exited water netId={netId}");
            }
        }

        private void TeleportToLand()
        {
            _inWater = false;
            _sinkTimer = 0f;

            // Teleport: disable CC, move, re-enable
            _cc.enabled = false;
            transform.position = _lastGroundPos + Vector3.up * 0.5f;
            _cc.enabled = true;

            Debug.Log($"[WaterSink] Teleported to {_lastGroundPos} netId={netId}");
        }

        private bool IsGrounded()
        {
            return Physics.Raycast(transform.position + Vector3.up * 0.1f,
                Vector3.down, 0.3f);
        }
    }
}
