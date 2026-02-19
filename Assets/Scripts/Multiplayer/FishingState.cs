namespace MultiplayerFishing
{
    /// <summary>
    /// Core fishing state enum. Server-authoritative, synced to all clients.
    /// Charging is client-only and not included here.
    /// </summary>
    public enum FishingState
    {
        Idle,
        Casting,
        Floating,
        Hooked,
        LineBroken,
        Displaying
    }
}
