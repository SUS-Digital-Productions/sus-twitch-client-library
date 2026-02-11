using System.Collections.Concurrent;

namespace SusTwitchClient.IRC;

/// <summary>
/// Thread-safe rate limiter implementing sliding window algorithm.
/// </summary>
internal sealed class RateLimiter
{
    private readonly int _maxMessages;
    private readonly int _periodMs;
    private readonly ConcurrentQueue<DateTimeOffset> _timestamps = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public RateLimiter(int maxMessages, int periodMs)
    {
        if (maxMessages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessages), "Must be greater than zero.");
        }

        if (periodMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(periodMs), "Must be greater than zero.");
        }

        _maxMessages = maxMessages;
        _periodMs = periodMs;
    }

    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var cutoff = now.AddMilliseconds(-_periodMs);

            // Remove old timestamps
            while (_timestamps.TryPeek(out var timestamp) && timestamp < cutoff)
            {
                _timestamps.TryDequeue(out _);
            }

            // Wait if rate limit exceeded
            while (_timestamps.Count >= _maxMessages)
            {
                if (_timestamps.TryPeek(out var oldest))
                {
                    var waitTime = oldest.AddMilliseconds(_periodMs) - now;
                    if (waitTime > TimeSpan.Zero)
                    {
                        _lock.Release();
                        try
                        {
                            await Task.Delay(waitTime, cancellationToken);
                        }
                        finally
                        {
                            await _lock.WaitAsync(cancellationToken);
                        }

                        now = DateTimeOffset.UtcNow;
                        cutoff = now.AddMilliseconds(-_periodMs);

                        // Remove old timestamps again
                        while (_timestamps.TryPeek(out var timestamp) && timestamp < cutoff)
                        {
                            _timestamps.TryDequeue(out _);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            // Add current timestamp
            _timestamps.Enqueue(now);
        }
        finally
        {
            _lock.Release();
        }
    }
}
