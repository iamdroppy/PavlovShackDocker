using Pavlov.Rcon;

namespace Pavlov.Manager.Services.EndOfGame;

/// <summary>The match currently being played, as far as the manager has seen it.</summary>
/// <param name="FromStart">False when the manager first saw this match already running (e.g. after a restart), so elapsed time is a lower bound.</param>
public sealed record MatchState(string MapId, string GameMode, DateTimeOffset StartedAt, bool FromStart, int LastTotalScore)
{
    public string Key => $"{MapId}|{GameMode}";
}

public sealed record MatchSnapshot(ServerInfo Info, IReadOnlyList<PlayerDetail> Players, MatchState Match, DateTimeOffset At)
{
    public TimeSpan Elapsed => At - Match.StartedAt;
}

/// <summary>
/// RCON has no "match started at", so the manager tracks it: a new match starts when
/// the map or mode changes or the scores go back down. Also makes sure only one rule
/// ends a match, and shares one ServerInfo poll between rules checked at the same moment.
/// </summary>
public sealed class MatchTracker(TimeProvider time, ILogger<MatchTracker> logger)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MapChangeGrace = TimeSpan.FromSeconds(45);

    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _fetch = new(1, 1);
    private MatchSnapshot? _cached;
    private DateTimeOffset _endingUntil;
    private bool _afterEnd;

    public event Action? Changed;

    public MatchState? Current { get; private set; }

    /// <summary>True for a short while after a rule rotated the map, while the next map loads.</summary>
    public bool IsChangingMap => time.GetUtcNow() < _endingUntil;

    /// <summary>Records what the live poll saw (ServerMonitor calls this too, so tracking starts as soon as anyone looks).</summary>
    public MatchState Observe(ServerInfo info)
    {
        var now = time.GetUtcNow();
        var total = info.Team0Score + info.Team1Score;
        MatchState state;
        bool started;
        lock (_lock)
        {
            var previous = Current;
            started = previous is null
                || !previous.Key.Equals($"{info.MapLabel}|{info.GameMode}", StringComparison.OrdinalIgnoreCase)
                || total < previous.LastTotalScore;
            state = started
                ? new MatchState(info.MapLabel, info.GameMode, now, FromStart: previous is not null || _afterEnd, total)
                : previous! with { LastTotalScore = total };
            Current = state;
            _afterEnd = false;
        }

        if (started)
        {
            logger.LogInformation("Tracking a new match: {Map} {Mode}", info.MapLabel, info.GameMode);
            Changed?.Invoke();
        }

        return state;
    }

    /// <summary>ServerInfo (+ players for free-for-all modes), shared by rules checked within 2 s of each other.</summary>
    public async Task<MatchSnapshot> SnapshotAsync(PavlovRconClient client, CancellationToken cancellationToken)
    {
        await _fetch.WaitAsync(cancellationToken);
        try
        {
            var now = time.GetUtcNow();
            if (_cached is { } cached && now - cached.At < CacheFor)
            {
                return cached;
            }

            var info = await client.GetServerInfoAsync(cancellationToken);
            IReadOnlyList<PlayerDetail> players = info.Teams ? [] : await client.InspectAllAsync(cancellationToken);
            var match = Observe(info);
            return _cached = new MatchSnapshot(info, players, match, time.GetUtcNow());
        }
        finally
        {
            _fetch.Release();
        }
    }

    /// <summary>Claims the right to end the current match. Only the first rule to ask gets it.</summary>
    public bool TryClaimEnd()
    {
        lock (_lock)
        {
            var now = time.GetUtcNow();
            if (now < _endingUntil)
            {
                return false;
            }

            _endingUntil = now + MapChangeGrace;
            _cached = null;
            Current = null;
            _afterEnd = true;
        }

        Changed?.Invoke();
        return true;
    }
}
