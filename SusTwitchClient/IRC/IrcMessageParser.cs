using SusTwitchClient.Events;

namespace SusTwitchClient.IRC;

/// <summary>
/// Parses IRC messages according to Twitch IRC format.
/// </summary>
internal static class IrcMessageParser
{
    public static MessageReceivedEventArgs Parse(string rawMessage)
    {
        var tags = new Dictionary<string, string>();
        
        var parts = rawMessage.Split(' ');
        var index = 0;

        // Parse tags if present (starts with @)
        if (parts[index].StartsWith('@'))
        {
            var tagString = parts[index][1..]; // Remove @
            var tagPairs = tagString.Split(';');

            foreach (var tagPair in tagPairs)
            {
                var keyValue = tagPair.Split('=', 2);
                if (keyValue.Length == 2)
                {
                    tags[keyValue[0]] = UnescapeTagValue(keyValue[1]);
                }
            }

            index++;
        }

        // Parse prefix if present (starts with :)
        string? prefix = null;
        if (index < parts.Length && parts[index].StartsWith(':'))
        {
            prefix = parts[index][1..]; // Remove :
            index++;
        }

        // Parse command
        string? command = null;
        if (index < parts.Length)
        {
            command = parts[index];
            index++;
        }

        // Parse parameters
        var parameters = new List<string>();
        while (index < parts.Length)
        {
            if (parts[index].StartsWith(':'))
            {
                // Trailing parameter - join rest of message
                var trailing = string.Join(' ', parts.Skip(index))[1..];
                parameters.Add(trailing);
                break;
            }

            parameters.Add(parts[index]);
            index++;
        }

        // Extract username from prefix (format: username!username@username.tmi.twitch.tv)
        string? username = null;
        if (!string.IsNullOrEmpty(prefix))
        {
            var exclamationIndex = prefix.IndexOf('!');
            username = exclamationIndex > 0 ? prefix[..exclamationIndex] : prefix;
        }

        // Extract channel and message based on command
        string? channel = null;
        string? message = null;

        if (command == "PRIVMSG" && parameters.Count >= 2)
        {
            channel = parameters[0].TrimStart('#');
            message = parameters[1];
        }
        else if ((command == "JOIN" || command == "PART") && parameters.Count >= 1)
        {
            channel = parameters[0].TrimStart('#');
        }

        return new MessageReceivedEventArgs
        {
            RawMessage = rawMessage,
            Tags = tags,
            Username = username,
            Channel = channel,
            Message = message,
            Command = command
        };
    }

    private static string UnescapeTagValue(string value)
    {
        return value
            .Replace("\\s", " ")
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\:", ";")
            .Replace("\\\\", "\\");
    }
}
