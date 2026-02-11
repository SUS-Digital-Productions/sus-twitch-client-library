using SusTwitchClient;
using SusTwitchClient.Events;

// Example: Basic IRC Bot
Console.WriteLine("=== SUS Twitch Client Library Example ===");
Console.WriteLine();

// Note: Replace with your actual OAuth token and username
// Get token from: https://twitchapps.com/tmi/
var config = new TwitchClientConfig
{
    OAuthToken = Environment.GetEnvironmentVariable("TWITCH_OAUTH") ?? "your_oauth_token_here",
    Username = Environment.GetEnvironmentVariable("TWITCH_USERNAME") ?? "your_bot_username",
    EnableIrc = true,
    EnableEventSub = false,
    AutoReconnect = true,
    RateLimitMessages = 20,
    RateLimitPeriodMs = 30000
};

// Validate configuration
if (config.OAuthToken == "your_oauth_token_here" || config.Username == "your_bot_username")
{
    Console.WriteLine("Please set TWITCH_OAUTH and TWITCH_USERNAME environment variables");
    Console.WriteLine("Or modify the config in Program.cs");
    Console.WriteLine();
    Console.WriteLine("Get OAuth token from: https://twitchapps.com/tmi/");
    return;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    Console.WriteLine("\nShutting down...");
    e.Cancel = true;
    cts.Cancel();
};

try
{
    using var client = new TwitchClient(config);

    // Event: Connected
    client.Connected += async (sender, e) =>
    {
        Console.WriteLine($"[{e.Timestamp:HH:mm:ss}] ✓ Connected to Twitch IRC");
    };

    // Event: Disconnected
    client.Disconnected += async (sender, e) =>
    {
        Console.WriteLine($"[{e.Timestamp:HH:mm:ss}] ✗ Disconnected: {e.Reason}");
        if (!e.Expected)
        {
            Console.WriteLine("     Will attempt to reconnect...");
        }
    };

    // Event: Message Received
    client.MessageReceived += async (sender, e) =>
    {
        if (e.Command == "PRIVMSG" && e.Channel != null && e.Username != null && e.Message != null)
        {
            Console.WriteLine($"[{e.Channel}] {e.Username}: {e.Message}");

            // Example: Respond to commands
            if (e.Message.StartsWith("!hello"))
            {
                await sender.SendMessageAsync(e.Channel, $"Hello @{e.Username}! 👋");
            }
            else if (e.Message.StartsWith("!time"))
            {
                var time = DateTime.Now.ToString("HH:mm:ss");
                await sender.SendMessageAsync(e.Channel, $"Current time: {time}");
            }
            else if (e.Message.StartsWith("!about"))
            {
                await sender.SendMessageAsync(e.Channel, "I'm a bot built with SUS Twitch Client Library!");
            }
        }
        else if (e.Command == "JOIN" && e.Channel != null && e.Username != null)
        {
            Console.WriteLine($"[{e.Channel}] >> {e.Username} joined");
        }
        else if (e.Command == "PART" && e.Channel != null && e.Username != null)
        {
            Console.WriteLine($"[{e.Channel}] << {e.Username} left");
        }
    };

    // Event: Error
    client.Error += async (sender, e) =>
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[{e.Timestamp:HH:mm:ss}] ⚠ Error: {e.Message}");
        if (e.Exception != null)
        {
            Console.WriteLine($"     Exception: {e.Exception.Message}");
        }
        Console.ResetColor();
    };

    // Connect to Twitch
    Console.WriteLine("Connecting to Twitch...");
    await client.ConnectAsync(cts.Token);

    // Join a channel
    Console.Write("Enter channel name to join: ");
    var channel = Console.ReadLine();
    if (!string.IsNullOrWhiteSpace(channel))
    {
        Console.WriteLine($"Joining #{channel}...");
        await client.JoinChannelAsync(channel, cts.Token);
        Console.WriteLine();
        Console.WriteLine("Bot is running! Try these commands in chat:");
        Console.WriteLine("  !hello - Get a greeting");
        Console.WriteLine("  !time - Get current time");
        Console.WriteLine("  !about - Learn about the bot");
        Console.WriteLine();
        Console.WriteLine("Press Ctrl+C to stop.");
    }

    // Wait until cancellation
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Shutdown complete.");
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Fatal error: {ex.Message}");
    Console.WriteLine(ex);
    Console.ResetColor();
}
