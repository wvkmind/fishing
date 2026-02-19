using Mirror;
using UnityEngine;
using FishingGameTool.Fishing.Float;

namespace MultiplayerFishing
{
    /// <summary>
    /// Network wrapper for the FishingFloat prefab.
    /// Manages server-authoritative physics and client-side kinematic state.
    ///
    /// Prefab Setup (Unity Editor):
    /// 1. Duplicate the existing FishingFloat prefab
    /// 2. Add NetworkIdentity component
    /// 3. Add NetworkTransform component (for position/rotation sync)
    /// 4. Add this NetworkFishingFloat script
    /// 5. Register the prefab in NetworkManager's Registered Spawnable Prefabs list
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class NetworkFishingFloat : NetworkBehaviour
    {
        private Rigidbody _rigidbody;
        private FishingFloat _fishingFloat;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _fishingFloat = GetComponent<FishingFloat>();
        }

        public override void OnStartServer()
        {
            // Server controls physics simulation
            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = false;
            }
        }

        public override void OnStartClient()
        {
            // Clients don't simulate physics — position comes from NetworkTransform
            if (!isServer && _rigidbody != null)
            {
                _rigidbody.isKinematic = true;
            }
        }

        /// <summary>
        /// Returns the underlying FishingFloat component for integration
        /// with the existing FishingSystem.
        /// </summary>
        public FishingFloat GetFishingFloat()
        {
            return _fishingFloat;
        }
    }
}
