namespace SusTwitchClient.Events;

/// <summary>
/// Event args for errors.
/// </summary>
public sealed class TwitchErrorEventArgs : EventArgs
{
    /// <summary>
    /// The exception that occurred.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Error message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Timestamp when error occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
