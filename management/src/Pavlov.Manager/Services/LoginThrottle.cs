using System.Collections.Concurrent;
using System.Net;

namespace Pavlov.Manager.Services;

/// <summary>Slows down password guessing: 5 failures per address in 10 minutes locks it out for 10 minutes.</summary>
public sealed class LoginThrottle(TimeProvider time)
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Attempts> _attempts = new();

    public TimeSpan? RetryAfter(IPAddress? address)
    {
        if (!_attempts.TryGetValue(Key(address), out var attempts))
        {
            return null;
        }

        var now = time.GetUtcNow();
        lock (attempts)
        {
            attempts.Failures.RemoveAll(at => now - at > Window);
            return attempts.Failures.Count >= MaxFailures ? attempts.Failures[0] + Window - now : null;
        }
    }

    public void RecordFailure(IPAddress? address)
    {
        var attempts = _attempts.GetOrAdd(Key(address), _ => new Attempts());
        lock (attempts)
        {
            attempts.Failures.Add(time.GetUtcNow());
        }
    }

    public void Reset(IPAddress? address) => _attempts.TryRemove(Key(address), out _);

    private static string Key(IPAddress? address) => address?.ToString() ?? "unknown";

    private sealed class Attempts
    {
        public List<DateTimeOffset> Failures { get; } = [];
    }
}
