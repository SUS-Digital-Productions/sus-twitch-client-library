namespace SusTwitchClient.Events;

/// <summary>
/// Event args for EventSub notifications.
/// </summary>
public sealed class EventSubNotificationEventArgs : EventArgs
{
    /// <summary>
    /// Subscription type.
    /// </summary>
    public string SubscriptionType { get; init; } = string.Empty;

    /// <summary>
    /// Event data as JSON.
    /// </summary>
    public string EventData { get; init; } = string.Empty;

    /// <summary>
    /// Subscription ID.
    /// </summary>
    public string? SubscriptionId { get; init; }

    /// <summary>
    /// Timestamp when received.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
