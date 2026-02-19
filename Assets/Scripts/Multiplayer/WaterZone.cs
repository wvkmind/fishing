using UnityEngine;

namespace MultiplayerFishing
{
    /// <summary>
    /// Attach to the water object as a child trigger collider.
    /// Detects when a player enters the water zone.
    /// The actual response (sinking + teleport) is handled by WaterSinkHandler on the player.
    /// </summary>
    public class WaterZone : MonoBehaviour
    {
        // This component just marks the trigger as a water zone.
        // WaterSinkHandler on the player uses OnTriggerEnter/Exit to detect it.
    }
}
