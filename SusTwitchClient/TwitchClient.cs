using System.Net.WebSockets;
using SusTwitchClient.Events;
using SusTwitchClient.IRC;
using SusTwitchClient.EventSub;

namespace SusTwitchClient;

/// <summary>
/// Main client for connecting to Twitch IRC and EventSub services.
/// Event-driven, async, DI-friendly, and thread-safe.
/// </summary>
public sealed class TwitchClient : IDisposable, IAsyncDisposable
{
    private readonly TwitchClientConfig _config;
    private readonly IrcConnection? _ircConnection;
    private readonly EventSubConnection? _eventSubConnection;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Fired when the client successfully connects to IRC.
    /// </summary>
    public event AsyncEventHandler<TwitchClient, ConnectedEventArgs>? Connected;

    /// <summary>
    /// Fired when the client disconnects from IRC.
    /// </summary>
    public event AsyncEventHandler<TwitchClient, DisconnectedEventArgs>? Disconnected;

    /// <summary>
    /// Fired when a message is received from IRC.
    /// </summary>
    public event AsyncEventHandler<TwitchClient, MessageReceivedEventArgs>? MessageReceived;

    /// <summary>
    /// Fired when an error occurs.
    /// </summary>
    public event AsyncEventHandler<TwitchClient, TwitchErrorEventArgs>? Error;

    /// <summary>
    /// Fired when EventSub receives a notification.
    /// </summary>
    public event AsyncEventHandler<TwitchClient, EventSubNotificationEventArgs>? EventSubNotification;

    /// <summary>
    /// Gets whether the client is currently connected to IRC.
    /// </summary>
    public bool IsConnected => _ircConnection?.IsConnected ?? false;

    /// <summary>
    /// Gets whether EventSub is currently connected.
    /// </summary>
    public bool IsEventSubConnected => _eventSubConnection?.IsConnected ?? false;

    /// <summary>
    /// Initializes a new instance of the TwitchClient.
    /// </summary>
    /// <param name="config">Configuration for the client.</param>
    public TwitchClient(TwitchClientConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        
        if (_config.EnableIrc)
        {
            _ircConnection = new IrcConnection(_config);
            _ircConnection.Connected += OnIrcConnected;
            _ircConnection.Disconnected += OnIrcDisconnected;
            _ircConnection.MessageReceived += OnIrcMessageReceived;
            _ircConnection.Error += OnIrcError;
        }

        if (_config.EnableEventSub)
        {
            _eventSubConnection = new EventSubConnection(_config);
            _eventSubConnection.Notification += OnEventSubNotification;
            _eventSubConnection.Error += OnEventSubError;
        }
    }

    /// <summary>
    /// Connects to Twitch services.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var tasks = new List<Task>();

        if (_ircConnection != null)
        {
            tasks.Add(_ircConnection.ConnectAsync(cancellationToken));
        }

        if (_eventSubConnection != null)
        {
            tasks.Add(_eventSubConnection.ConnectAsync(cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Disconnects from Twitch services.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var tasks = new List<Task>();

        if (_ircConnection != null)
        {
            tasks.Add(_ircConnection.DisconnectAsync(cancellationToken));
        }

        if (_eventSubConnection != null)
        {
            tasks.Add(_eventSubConnection.DisconnectAsync(cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Joins a channel.
    /// </summary>
    /// <param name="channelName">Name of the channel to join.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task JoinChannelAsync(string channelName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_ircConnection == null)
        {
            throw new InvalidOperationException("IRC is not enabled in the configuration.");
        }

        await _ircConnection.JoinChannelAsync(channelName, cancellationToken);
    }

    /// <summary>
    /// Leaves a channel.
    /// </summary>
    /// <param name="channelName">Name of the channel to leave.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task LeaveChannelAsync(string channelName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_ircConnection == null)
        {
            throw new InvalidOperationException("IRC is not enabled in the configuration.");
        }

        await _ircConnection.LeaveChannelAsync(channelName, cancellationToken);
    }

    /// <summary>
    /// Sends a message to a channel.
    /// </summary>
    /// <param name="channelName">Name of the channel.</param>
    /// <param name="message">Message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SendMessageAsync(string channelName, string message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_ircConnection == null)
        {
            throw new InvalidOperationException("IRC is not enabled in the configuration.");
        }

        await _ircConnection.SendMessageAsync(channelName, message, cancellationToken);
    }

    private async Task OnIrcConnected(TwitchClient sender, ConnectedEventArgs e)
    {
        if (Connected != null)
        {
            await Connected.Invoke(this, e);
        }
    }

    private async Task OnIrcDisconnected(TwitchClient sender, DisconnectedEventArgs e)
    {
        if (Disconnected != null)
        {
            await Disconnected.Invoke(this, e);
        }
    }

    private async Task OnIrcMessageReceived(TwitchClient sender, MessageReceivedEventArgs e)
    {
        if (MessageReceived != null)
        {
            await MessageReceived.Invoke(this, e);
        }
    }

    private async Task OnIrcError(TwitchClient sender, TwitchErrorEventArgs e)
    {
        if (Error != null)
        {
            await Error.Invoke(this, e);
        }
    }

    private async Task OnEventSubNotification(TwitchClient sender, EventSubNotificationEventArgs e)
    {
        if (EventSubNotification != null)
        {
            await EventSubNotification.Invoke(this, e);
        }
    }

    private async Task OnEventSubError(TwitchClient sender, TwitchErrorEventArgs e)
    {
        if (Error != null)
        {
            await Error.Invoke(this, e);
        }
    }

    /// <summary>
    /// Disposes the client synchronously. 
    /// Note: DisposeAsync() is preferred to avoid potential deadlocks.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Best effort cleanup
            _disposeLock.Dispose();
            _disposed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await _disposeLock.WaitAsync();
        try
        {
            if (_disposed) return;

            if (_ircConnection != null)
            {
                await _ircConnection.DisposeAsync();
            }

            if (_eventSubConnection != null)
            {
                await _eventSubConnection.DisposeAsync();
            }

            _disposeLock.Dispose();
            _disposed = true;
        }
        finally
        {
            if (!_disposed)
            {
                _disposeLock.Release();
            }
        }
    }
}
