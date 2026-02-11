using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SusTwitchClient.Events;

namespace SusTwitchClient.EventSub;

/// <summary>
/// Manages EventSub WebSocket connection with auto-reconnect.
/// </summary>
internal sealed class EventSubConnection : IAsyncDisposable
{
    private readonly TwitchClientConfig _config;
    private readonly ClientWebSocket _webSocket;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    
    private Task? _receiveTask;
    private string? _sessionId;
    private int _reconnectAttempts;
    private bool _isDisposed;
    private bool _isConnected;

    public event AsyncEventHandler<TwitchClient, EventSubNotificationEventArgs>? Notification;
    public event AsyncEventHandler<TwitchClient, TwitchErrorEventArgs>? Error;

    public bool IsConnected => _isConnected && _webSocket.State == WebSocketState.Open;

    public EventSubConnection(TwitchClientConfig config)
    {
        _config = config;
        _webSocket = new ClientWebSocket();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);

        await _webSocket.ConnectAsync(new Uri(_config.EventSubServer), linkedCts.Token);

        _isConnected = true;
        _reconnectAttempts = 0;

        // Start receive loop
        _receiveTask = ReceiveLoopAsync(_disposeCts.Token);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return;

        try
        {
            _isConnected = false;

            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", cancellationToken);
            }
        }
        catch (Exception ex)
        {
            await InvokeErrorAsync($"Error during EventSub disconnect: {ex.Message}", ex);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[_config.ReceiveBufferSize];
        var messageBuilder = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _isConnected = false;
                    await HandleDisconnectAsync("Server closed connection", false);
                    break;
                }

                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                messageBuilder.Append(text);

                if (result.EndOfMessage)
                {
                    var message = messageBuilder.ToString();
                    messageBuilder.Clear();

                    await ProcessMessageAsync(message, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (WebSocketException ex)
        {
            _isConnected = false;
            await HandleDisconnectAsync($"WebSocket error: {ex.Message}", false);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            await InvokeErrorAsync($"Error in EventSub receive loop: {ex.Message}", ex);
            await HandleDisconnectAsync($"Unexpected error: {ex.Message}", false);
        }
    }

    private async Task ProcessMessageAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            if (!root.TryGetProperty("metadata", out var metadata))
            {
                return;
            }

            var messageType = metadata.GetProperty("message_type").GetString();

            switch (messageType)
            {
                case "session_welcome":
                    await HandleSessionWelcomeAsync(root);
                    break;

                case "session_keepalive":
                    // No action needed, connection is alive
                    break;

                case "notification":
                    await HandleNotificationAsync(root);
                    break;

                case "session_reconnect":
                    await HandleSessionReconnectAsync(root);
                    break;

                case "revocation":
                    await HandleRevocationAsync(root);
                    break;

                default:
                    await InvokeErrorAsync($"Unknown EventSub message type: {messageType}", null);
                    break;
            }
        }
        catch (Exception ex)
        {
            await InvokeErrorAsync($"Error processing EventSub message: {ex.Message}", ex);
        }
    }

    private async Task HandleSessionWelcomeAsync(JsonElement root)
    {
        if (root.TryGetProperty("payload", out var payload) &&
            payload.TryGetProperty("session", out var session) &&
            session.TryGetProperty("id", out var id))
        {
            _sessionId = id.GetString();
        }

        await Task.CompletedTask;
    }

    private async Task HandleNotificationAsync(JsonElement root)
    {
        try
        {
            var payload = root.GetProperty("payload");
            var subscription = payload.GetProperty("subscription");
            var eventData = payload.GetProperty("event");

            var subscriptionType = subscription.GetProperty("type").GetString() ?? string.Empty;
            var subscriptionId = subscription.TryGetProperty("id", out var id) ? id.GetString() : null;
            var eventJson = eventData.GetRawText();

            if (Notification != null)
            {
                await Notification.Invoke(null!, new EventSubNotificationEventArgs
                {
                    SubscriptionType = subscriptionType,
                    EventData = eventJson,
                    SubscriptionId = subscriptionId
                });
            }
        }
        catch (Exception ex)
        {
            await InvokeErrorAsync($"Error handling EventSub notification: {ex.Message}", ex);
        }
    }

    private async Task HandleSessionReconnectAsync(JsonElement root)
    {
        try
        {
            if (root.TryGetProperty("payload", out var payload) &&
                payload.TryGetProperty("session", out var session) &&
                session.TryGetProperty("reconnect_url", out var reconnectUrl))
            {
                var url = reconnectUrl.GetString();
                if (!string.IsNullOrEmpty(url))
                {
                    _isConnected = false;
                    await DisconnectAsync();
                    
                    // Update config URL temporarily for reconnect
                    var originalUrl = _config.EventSubServer;
                    _config.EventSubServer = url;
                    
                    try
                    {
                        await ReconnectAsync();
                    }
                    finally
                    {
                        _config.EventSubServer = originalUrl;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            await InvokeErrorAsync($"Error handling session reconnect: {ex.Message}", ex);
        }
    }

    private async Task HandleRevocationAsync(JsonElement root)
    {
        try
        {
            if (root.TryGetProperty("payload", out var payload) &&
                payload.TryGetProperty("subscription", out var subscription))
            {
                var type = subscription.GetProperty("type").GetString();
                var status = subscription.GetProperty("status").GetString();
                await InvokeErrorAsync($"EventSub subscription revoked: {type} - Status: {status}", null);
            }
        }
        catch (Exception ex)
        {
            await InvokeErrorAsync($"Error handling revocation: {ex.Message}", ex);
        }
    }

    private async Task HandleDisconnectAsync(string reason, bool expected)
    {
        if (!expected && _config.AutoReconnect && !_isDisposed)
        {
            await ReconnectAsync();
        }
    }

    private async Task ReconnectAsync()
    {
        await _reconnectLock.WaitAsync();
        try
        {
            if (_config.MaxReconnectAttempts > 0 && _reconnectAttempts >= _config.MaxReconnectAttempts)
            {
                await InvokeErrorAsync("Max EventSub reconnect attempts reached.", null);
                return;
            }

            _reconnectAttempts++;
            var delay = Math.Min(_config.ReconnectDelayMs * _reconnectAttempts, _config.MaxReconnectDelayMs);

            await Task.Delay(delay, _disposeCts.Token);

            // Dispose old WebSocket and create new one
            _webSocket.Dispose();
            var newWebSocket = new ClientWebSocket();
            typeof(EventSubConnection).GetField("_webSocket", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(this, newWebSocket);

            await ConnectAsync(_disposeCts.Token);
        }
        catch (Exception ex)
        {
            await InvokeErrorAsync($"EventSub reconnect failed: {ex.Message}", ex);
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    private async Task InvokeErrorAsync(string message, Exception? exception)
    {
        if (Error != null)
        {
            await Error.Invoke(null!, new TwitchErrorEventArgs
            {
                Message = message,
                Exception = exception
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;

        _isDisposed = true;
        _disposeCts.Cancel();

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch
            {
                // Ignore exceptions during dispose
            }
        }

        _webSocket.Dispose();
        _disposeCts.Dispose();
        _reconnectLock.Dispose();
    }
}
