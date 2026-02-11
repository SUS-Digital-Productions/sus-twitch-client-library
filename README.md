# SUS Twitch Client Library

A .NET 10 class library for connecting to Twitch IRC and EventSub services. Designed to be event-driven, async-first, DI-friendly, and thread-safe, similar to DSharpPlus.

## Features

- **IRC via ClientWebSocket**: Connect to Twitch IRC using WebSocket
- **OAuth Authentication**: Secure authentication with OAuth tokens
- **Channel Management**: Join and leave channels easily
- **Message Parsing & Sending**: Parse incoming messages and send messages to channels
- **Auto-Reconnect**: Automatic reconnection with exponential backoff
- **Internal Rate Limiting**: Built-in rate limiting to prevent API throttling
- **EventSub WebSocket Support**: Subscribe to Twitch EventSub events
- **Event-Driven Architecture**: Async event handlers for all actions
- **Thread-Safe**: Safe to use from multiple threads
- **CancellationToken Support**: Proper cancellation throughout
- **Nullable Reference Types**: Enabled for better null safety

## Installation

Add the library to your project:

```bash
dotnet add reference /path/to/SusTwitchClient.csproj
```

Or build as a NuGet package:

```bash
dotnet pack
```

## Quick Start

### Basic Usage

```csharp
using SusTwitchClient;
using SusTwitchClient.Events;

// Configure the client
var config = new TwitchClientConfig
{
    OAuthToken = "your_oauth_token",
    Username = "your_bot_username",
    EnableIrc = true,
    EnableEventSub = false,
    AutoReconnect = true
};

// Create the client
using var client = new TwitchClient(config);

// Subscribe to events
client.Connected += async (sender, e) =>
{
    Console.WriteLine($"Connected at {e.Timestamp}");
};

client.MessageReceived += async (sender, e) =>
{
    Console.WriteLine($"[{e.Channel}] {e.Username}: {e.Message}");
    
    // Echo messages back
    if (e.Message == "!hello")
    {
        await sender.SendMessageAsync(e.Channel!, "Hello!");
    }
};

client.Disconnected += async (sender, e) =>
{
    Console.WriteLine($"Disconnected: {e.Reason} (Expected: {e.Expected})");
};

client.Error += async (sender, e) =>
{
    Console.WriteLine($"Error: {e.Message}");
    if (e.Exception != null)
    {
        Console.WriteLine(e.Exception);
    }
};

// Connect and join channels
await client.ConnectAsync();
await client.JoinChannelAsync("channelname");

// Keep the application running
await Task.Delay(Timeout.Infinite);
```

### EventSub Support

```csharp
var config = new TwitchClientConfig
{
    OAuthToken = "your_oauth_token",
    Username = "your_bot_username",
    EnableIrc = false,
    EnableEventSub = true
};

using var client = new TwitchClient(config);

client.EventSubNotification += async (sender, e) =>
{
    Console.WriteLine($"EventSub: {e.SubscriptionType}");
    Console.WriteLine($"Data: {e.EventData}");
};

await client.ConnectAsync();

// Keep the application running
await Task.Delay(Timeout.Infinite);
```

## Configuration Options

### TwitchClientConfig Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `OAuthToken` | string | "" | OAuth token for authentication |
| `Username` | string | "" | Bot username/nickname |
| `EnableIrc` | bool | true | Enable IRC connection |
| `EnableEventSub` | bool | false | Enable EventSub connection |
| `IrcServer` | string | wss://irc-ws.chat.twitch.tv:443 | IRC server address |
| `EventSubServer` | string | wss://eventsub.wss.twitch.tv/ws | EventSub server address |
| `AutoReconnect` | bool | true | Enable automatic reconnection |
| `MaxReconnectAttempts` | int | 0 | Max reconnect attempts (0 = unlimited) |
| `ReconnectDelayMs` | int | 1000 | Initial reconnect delay in ms |
| `MaxReconnectDelayMs` | int | 60000 | Maximum reconnect delay in ms |
| `RateLimitMessages` | int | 20 | Max messages per period |
| `RateLimitPeriodMs` | int | 30000 | Rate limit period in ms |
| `ReceiveBufferSize` | int | 8192 | WebSocket receive buffer size |
| `WebSocketTimeoutMs` | int | 30000 | WebSocket operation timeout in ms |

## Events

### Available Events

- **Connected**: Fired when successfully connected to IRC
- **Disconnected**: Fired when disconnected from IRC
- **MessageReceived**: Fired when a message is received from IRC
- **Error**: Fired when an error occurs
- **EventSubNotification**: Fired when an EventSub notification is received

### Event Args

#### ConnectedEventArgs
- `Timestamp`: When the connection was established

#### DisconnectedEventArgs
- `Timestamp`: When disconnected
- `Reason`: Reason for disconnection
- `Expected`: Whether the disconnect was expected

#### MessageReceivedEventArgs
- `RawMessage`: Raw IRC message
- `Username`: Sender's username
- `Channel`: Channel name
- `Message`: Message content
- `Tags`: IRC tags (metadata)
- `Command`: IRC command (PRIVMSG, JOIN, PART, etc.)
- `Timestamp`: When received

#### TwitchErrorEventArgs
- `Exception`: The exception that occurred
- `Message`: Error message
- `Timestamp`: When error occurred

#### EventSubNotificationEventArgs
- `SubscriptionType`: Type of subscription
- `EventData`: Event data as JSON
- `SubscriptionId`: Subscription ID
- `Timestamp`: When received

## API Methods

### TwitchClient Methods

```csharp
// Connect to Twitch services
Task ConnectAsync(CancellationToken cancellationToken = default);

// Disconnect from Twitch services
Task DisconnectAsync(CancellationToken cancellationToken = default);

// Join a channel
Task JoinChannelAsync(string channelName, CancellationToken cancellationToken = default);

// Leave a channel
Task LeaveChannelAsync(string channelName, CancellationToken cancellationToken = default);

// Send a message to a channel
Task SendMessageAsync(string channelName, string message, CancellationToken cancellationToken = default);
```

### Properties

```csharp
// Check if connected to IRC
bool IsConnected { get; }

// Check if EventSub is connected
bool IsEventSubConnected { get; }
```

## Advanced Usage

### Dependency Injection

```csharp
services.AddSingleton<TwitchClientConfig>(sp => new TwitchClientConfig
{
    OAuthToken = configuration["Twitch:OAuthToken"],
    Username = configuration["Twitch:Username"],
    EnableIrc = true,
    AutoReconnect = true
});

services.AddSingleton<TwitchClient>();
```

### Handling Rate Limiting

The library includes built-in rate limiting. By default, it allows 20 messages per 30 seconds (Twitch's default for regular users). You can configure this:

```csharp
var config = new TwitchClientConfig
{
    RateLimitMessages = 100,  // For moderators
    RateLimitPeriodMs = 30000
};
```

### Custom Reconnection Logic

```csharp
var config = new TwitchClientConfig
{
    AutoReconnect = true,
    MaxReconnectAttempts = 5,  // Try 5 times
    ReconnectDelayMs = 2000,   // Start with 2 seconds
    MaxReconnectDelayMs = 30000 // Max 30 seconds delay
};
```

## Architecture

The library follows SOLID principles:

- **Single Responsibility**: Each class has a focused purpose
  - `TwitchClient`: Main client interface
  - `IrcConnection`: IRC WebSocket management
  - `EventSubConnection`: EventSub WebSocket management
  - `RateLimiter`: Rate limiting logic
  - `IrcMessageParser`: IRC message parsing

- **Open/Closed**: Extensible through events and configuration

- **Liskov Substitution**: Proper inheritance hierarchies

- **Interface Segregation**: Clean event-driven interfaces

- **Dependency Inversion**: Depends on abstractions (events, configs)

## Thread Safety

All public methods are thread-safe. The library uses:
- `SemaphoreSlim` for critical sections
- `ConcurrentDictionary` for shared state
- Async/await throughout to avoid blocking
- Proper synchronization in rate limiter

## Requirements

- .NET 10.0 or later
- No external dependencies beyond .NET BCL

## License

[Add your license here]

## Contributing

[Add contribution guidelines here]