using System.Net.WebSockets;
using System.Text;
using System.Collections.Concurrent;
using SusTwitchClient.Events;

namespace SusTwitchClient.IRC;

/// <summary>
/// Manages IRC connection via WebSocket with auto-reconnect and rate limiting.
/// </summary>
internal sealed class IrcConnection : IAsyncDisposable
{
    private readonly TwitchClientConfig _config;
    private ClientWebSocket _webSocket;
    private readonly RateLimiter _rateLimiter;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly ConcurrentDictionary<string, bool> _joinedChannels = new();
    
    private Task? _receiveTask;
    private int _reconnectAttempts;
    private bool _isDisposed;
    private bool _isConnected;

    public event AsyncEventHandler<TwitchClient, ConnectedEventArgs>? Connected;
    public event AsyncEventHandler<TwitchClient, DisconnectedEventArgs>? Disconnected;
    public event AsyncEventHandler<TwitchClient, MessageReceivedEventArgs>? MessageReceived;
    public event AsyncEventHandler<TwitchClient, TwitchErrorEventArgs>? Error;

    public bool IsConnected => _isConnected && _webSocket.State == WebSocketState.Open;

    public IrcConnection(TwitchClientConfig config)
    {
        _config = config;
        _webSocket = new ClientWebSocket();
        _rateLimiter = new RateLimiter(config.RateLimitMessages, config.RateLimitPeriodMs);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);

        await _webSocket.ConnectAsync(new Uri(_config.IrcServer), linkedCts.Token);

        // Authenticate
        await SendRawAsync($"PASS oauth:{_config.OAuthToken}", linkedCts.Token);
        await SendRawAsync($"NICK {_config.Username}", linkedCts.Token);

        // Request capabilities
        await SendRawAsync("CAP REQ :twitch.tv/tags twitch.tv/commands", linkedCts.Token);

        _isConnected = true;
        _reconnectAttempts = 0;

        // Start receive loop
        _receiveTask = ReceiveLoopAsync(_disposeCts.Token);

        await InvokeConnectedAsync();
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

            await InvokeDisconnectedAsync("Client requested disconnect", true);
        }
        catch (Exception ex)
        {
            await InvokeErrorAsync($"Error during disconnect: {ex.Message}", ex);
        }
    }

    public async Task JoinChannelAsync(string channelName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (string.IsNullOrWhiteSpace(channelName))
        {
            throw new ArgumentException("Channel name cannot be empty.", nameof(channelName));
        }

        var normalizedChannel = NormalizeChannelName(channelName);
        await SendRawAsync($"JOIN #{normalizedChannel}", cancellationToken);
        _joinedChannels[normalizedChannel] = true;
    }

    public async Task LeaveChannelAsync(string channelName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (string.IsNullOrWhiteSpace(channelName))
        {
            throw new ArgumentException("Channel name cannot be empty.", nameof(channelName));
        }

        var normalizedChannel = NormalizeChannelName(channelName);
        await SendRawAsync($"PART #{normalizedChannel}", cancellationToken);
        _joinedChannels.TryRemove(normalizedChannel, out _);
    }

    public async Task SendMessageAsync(string channelName, string message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (string.IsNullOrWhiteSpace(channelName))
        {
            throw new ArgumentException("Channel name cannot be empty.", nameof(channelName));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Message cannot be empty.", nameof(message));
        }

        var normalizedChannel = NormalizeChannelName(channelName);
        await _rateLimiter.WaitAsync(cancellationToken);
        await SendRawAsync($"PRIVMSG #{normalizedChannel} :{message}", cancellationToken);
    }

    private async Task SendRawAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Not connected to IRC.");
        }

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            var buffer = Encoding.UTF8.GetBytes(message + "\r\n");
            await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
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
                    var messages = messageBuilder.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    messageBuilder.Clear();

                    foreach (var message in messages)
                    {
                        await ProcessMessageAsync(message, cancellationToken);
                    }
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
            await InvokeErrorAsync($"Error in receive loop: {ex.Message}", ex);
            await HandleDisconnectAsync($"Unexpected error: {ex.Message}", false);
        }
    }

    private async Task ProcessMessageAsync(string rawMessage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawMessage)) return;

        // Handle PING
        if (rawMessage.StartsWith("PING", StringComparison.OrdinalIgnoreCase))
        {
            var pongMessage = rawMessage.Replace("PING", "PONG", StringComparison.OrdinalIgnoreCase);
            await SendRawAsync(pongMessage, cancellationToken);
            return;
        }

        // Parse and raise event
        var parsedMessage = IrcMessageParser.Parse(rawMessage);
        await InvokeMessageReceivedAsync(parsedMessage);
    }

    private async Task HandleDisconnectAsync(string reason, bool expected)
    {
        await InvokeDisconnectedAsync(reason, expected);

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
                await InvokeErrorAsync("Max reconnect attempts reached.", null);
                return;
            }

            _reconnectAttempts++;
            var delay = Math.Min(_config.ReconnectDelayMs * _reconnectAttempts, _config.MaxReconnectDelayMs);

            await Task.Delay(delay, _disposeCts.Token);

            // Dispose old WebSocket and create new one
            _webSocket.Dispose();
            _webSocket = new ClientWebSocket();

            await ConnectAsync(_disposeCts.Token);

            // Rejoin channels
            foreach (var channel in _joinedChannels.Keys)
            {
                await JoinChannelAsync(channel, _disposeCts.Token);
            }
        }
        catch (Exception ex)
        {
            await InvokeErrorAsync($"Reconnect failed: {ex.Message}", ex);
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    private static string NormalizeChannelName(string channelName)
    {
        return channelName.TrimStart('#').ToLowerInvariant();
    }

    private async Task InvokeConnectedAsync()
    {
        if (Connected != null)
        {
            await Connected.Invoke(null!, new ConnectedEventArgs());
        }
    }

    private async Task InvokeDisconnectedAsync(string reason, bool expected)
    {
        if (Disconnected != null)
        {
            await Disconnected.Invoke(null!, new DisconnectedEventArgs
            {
                Reason = reason,
                Expected = expected
            });
        }
    }

    private async Task InvokeMessageReceivedAsync(MessageReceivedEventArgs args)
    {
        if (MessageReceived != null)
        {
            await MessageReceived.Invoke(null!, args);
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
        _sendLock.Dispose();
        _reconnectLock.Dispose();
    }
}
