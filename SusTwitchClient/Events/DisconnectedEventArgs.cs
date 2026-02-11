namespace SusTwitchClient.Events;

/// <summary>
/// Event args for disconnection.
/// </summary>
public sealed class DisconnectedEventArgs : EventArgs
{
    /// <summary>
    /// Timestamp when disconnected.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Reason for disconnection.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Whether the disconnection was expected.
    /// </summary>
    public bool Expected { get; init; }
}
