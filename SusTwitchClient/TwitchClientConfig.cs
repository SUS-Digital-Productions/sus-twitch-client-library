namespace SusTwitchClient;

/// <summary>
/// Configuration for the Twitch client.
/// </summary>
public sealed class TwitchClientConfig
{
    /// <summary>
    /// OAuth token for authentication.
    /// </summary>
    public string OAuthToken { get; set; } = string.Empty;

    /// <summary>
    /// Username/nickname for the bot.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Enable IRC connection.
    /// </summary>
    public bool EnableIrc { get; set; } = true;

    /// <summary>
    /// Enable EventSub connection.
    /// </summary>
    public bool EnableEventSub { get; set; } = false;

    /// <summary>
    /// IRC server address.
    /// </summary>
    public string IrcServer { get; set; } = "wss://irc-ws.chat.twitch.tv:443";

    /// <summary>
    /// EventSub WebSocket server address.
    /// </summary>
    public string EventSubServer { get; set; } = "wss://eventsub.wss.twitch.tv/ws";

    /// <summary>
    /// Enable automatic reconnection on disconnect.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// Maximum reconnection attempts (0 for unlimited).
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 0;

    /// <summary>
    /// Initial delay before reconnecting (in milliseconds).
    /// </summary>
    public int ReconnectDelayMs { get; set; } = 1000;

    /// <summary>
    /// Maximum delay between reconnection attempts (in milliseconds).
    /// </summary>
    public int MaxReconnectDelayMs { get; set; } = 60000;

    /// <summary>
    /// Rate limit: maximum messages per period.
    /// </summary>
    public int RateLimitMessages { get; set; } = 20;

    /// <summary>
    /// Rate limit: period in milliseconds.
    /// </summary>
    public int RateLimitPeriodMs { get; set; } = 30000;

    /// <summary>
    /// WebSocket receive buffer size.
    /// </summary>
    public int ReceiveBufferSize { get; set; } = 8192;

    /// <summary>
    /// Timeout for WebSocket operations (in milliseconds).
    /// </summary>
    public int WebSocketTimeoutMs { get; set; } = 30000;
}
