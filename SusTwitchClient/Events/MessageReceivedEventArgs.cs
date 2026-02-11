namespace SusTwitchClient.Events;

/// <summary>
/// Event args for messages received from IRC.
/// </summary>
public sealed class MessageReceivedEventArgs : EventArgs
{
    /// <summary>
    /// The raw IRC message.
    /// </summary>
    public string RawMessage { get; init; } = string.Empty;

    /// <summary>
    /// Username of the sender.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// Channel where the message was sent.
    /// </summary>
    public string? Channel { get; init; }

    /// <summary>
    /// Message content.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// IRC tags (metadata).
    /// </summary>
    public Dictionary<string, string> Tags { get; init; } = new();

    /// <summary>
    /// IRC command (e.g., PRIVMSG, JOIN, PART).
    /// </summary>
    public string? Command { get; init; }

    /// <summary>
    /// Timestamp when received.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
