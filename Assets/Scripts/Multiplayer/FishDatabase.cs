using System;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerFishing
{
    /// <summary>
    /// Maps loot names to their network display prefabs.
    /// Auto-populated by FishSetupEditor.
    /// </summary>
    [CreateAssetMenu(fileName = "FishDatabase", menuName = "Fishing/Fish Database")]
    public class FishDatabase : ScriptableObject
    {
        [Serializable]
        public struct FishEntry
        {
            public string lootName;
            public GameObject networkPrefab;
        }

        public List<FishEntry> entries = new List<FishEntry>();

        /// <summary>
        /// Find the network display prefab for a given loot name.
        /// </summary>
        public GameObject GetNetworkPrefab(string lootName)
        {
            foreach (var e in entries)
            {
                if (e.lootName == lootName)
                    return e.networkPrefab;
            }
            return null;
        }
    }
}
