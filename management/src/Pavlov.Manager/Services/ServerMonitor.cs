using System.Threading.Channels;
using Pavlov.Manager.Services.EndOfGame;
using Pavlov.Rcon;

namespace Pavlov.Manager.Services;

public sealed record ServerSnapshot(ServerInfo? Info, IReadOnlyList<PlayerDetail> Players, DateTimeOffset? UpdatedAt, string? Error)
{
    public static ServerSnapshot Empty { get; } = new(null, [], null, null);

    public bool IsOnline => Info is not null && Error is null;
}

/// <summary>
/// Polls ServerInfo + InspectAll for every open page at once, so the RCON load
/// stays the same however many browsers are watching. Polls only while someone
/// is subscribed and the session is signed in.
/// </summary>
public sealed class ServerMonitor(RconSession session, MatchTracker tracker, ILogger<ServerMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private int _subscribers;

    public event Action? Changed;

    public ServerSnapshot Snapshot { get; private set; } = ServerSnapshot.Empty;

    public IDisposable Subscribe(Action onChanged)
    {
        Changed += onChanged;
        if (Interlocked.Increment(ref _subscribers) == 1)
        {
            RequestRefresh();
        }

        return new Subscription(this, onChanged);
    }

    /// <summary>Polls right away, e.g. after an action changed something.</summary>
    public void RequestRefresh() => _wake.Writer.TryWrite(true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (Volatile.Read(ref _subscribers) > 0 && session.IsActive)
            {
                await PollAsync(stoppingToken);
                await WaitAsync(Interval, stoppingToken);
            }
            else
            {
                await WaitAsync(Timeout.InfiniteTimeSpan, stoppingToken);
            }
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = session.Client;
            var info = await client.GetServerInfoAsync(cancellationToken);
            var players = await client.InspectAllAsync(cancellationToken);
            tracker.Observe(info);
            Snapshot = new ServerSnapshot(info, players, DateTimeOffset.UtcNow, null);
        }
        catch (RconAuthenticationException)
        {
            await session.InvalidateAsync();
            Snapshot = Snapshot with { Error = "The server no longer accepts the RCON password." };
        }
        catch (Exception ex) when (ex is RconException or RconSessionExpiredException)
        {
            Snapshot = Snapshot with { Error = ex.Message };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Polling the server failed");
            Snapshot = Snapshot with { Error = "Polling the server failed." };
        }

        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A ServerMonitor subscriber threw");
        }
    }

    private async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout != Timeout.InfiniteTimeSpan)
        {
            linked.CancelAfter(timeout);
        }

        try
        {
            await _wake.Reader.ReadAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Interval elapsed.
        }
    }

    private sealed class Subscription(ServerMonitor monitor, Action onChanged) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                monitor.Changed -= onChanged;
                Interlocked.Decrement(ref monitor._subscribers);
            }
        }
    }
}
