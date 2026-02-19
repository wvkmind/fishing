using UnityEngine;
using FishingGameTool.Example;

namespace MultiplayerFishing
{
    /// <summary>
    /// Placed in the Game scene. Disables all ItemInfo components on Awake
    /// (preventing NullRef), then rebinds them once a player spawns.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class ItemInfoBinder : MonoBehaviour
    {
        private ItemInfo[] _items;
        private bool _bound;

        private void Awake()
        {
            _items = FindObjectsByType<ItemInfo>(FindObjectsSortMode.None);
            foreach (var item in _items)
                item.enabled = false;

            // On dedicated server, ItemInfo (billboard) is useless — stay disabled
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }
        }

        private void Update()
        {
            if (_bound)
            {
                // Check if the bound player was destroyed (disconnect)
                // If so, re-disable items and wait for a new player
                if (_items != null && _items.Length > 0 && _items[0] != null
                    && _items[0]._characterMovement == null)
                {
                    foreach (var item in _items)
                    {
                        if (item != null) item.enabled = false;
                    }
                    _bound = false;
                }
                return;
            }

            var players = FindObjectsByType<CharacterMovement>(FindObjectsSortMode.None);
            if (players.Length == 0) return;

            CharacterMovement localPlayer = null;
            foreach (var p in players)
            {
                if (p.enabled)
                {
                    localPlayer = p;
                    break;
                }
            }
            if (localPlayer == null) return;

            foreach (var item in _items)
            {
                if (item == null) continue;
                item._characterMovement = localPlayer;
                item.enabled = true;
            }

            _bound = true;
        }
    }
}
