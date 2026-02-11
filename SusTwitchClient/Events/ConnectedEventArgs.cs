namespace SusTwitchClient.Events;

/// <summary>
/// Event args for connection established.
/// </summary>
public sealed class ConnectedEventArgs : EventArgs
{
    /// <summary>
    /// Timestamp when connected.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
