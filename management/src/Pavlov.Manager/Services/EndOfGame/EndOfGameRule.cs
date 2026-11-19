using System.Globalization;
using Pavlov.Rcon;

namespace Pavlov.Manager.Services.EndOfGame;

/// <summary>
/// Ends the match (rotates to the next map) as soon as any of its limits is reached:
/// a time limit, a lead of a given percentage over the other team, or a score limit.
/// Each enabled rule runs as its own Hangfire recurring job.
/// </summary>
public sealed record EndOfGameRule
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = string.Empty;

    /// <summary>Map id the rule applies to; null for every map.</summary>
    public string? MapId { get; init; }

    /// <summary>Game mode the rule applies to; null for every mode.</summary>
    public string? GameMode { get; init; }

    public int? TimeLimitMinutes { get; init; }

    /// <summary>The leader wins when their score is this many percent above the other side's.</summary>
    public int? LeadPercent { get; init; }

    /// <summary>The lead rule only applies once the leader has this many points (so 1–0 doesn't end a match).</summary>
    public int LeadMinimumScore { get; init; } = 10;

    public int? ScoreLimit { get; init; }

    public bool Enabled { get; init; } = true;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string JobId => $"end-of-game-{Id:N}";

    public string DisplayName => Name is { Length: > 0 } ? Name : ScopeLabel;

    public string ScopeLabel => (MapId, GameMode) switch
    {
        (null, null) => "Every map",
        (null, { } mode) => $"Every {PavlovCatalog.GameModeName(mode)} map",
        ({ } map, null) => PavlovCatalog.MapName(map),
        ({ } map, { } mode) => $"{PavlovCatalog.MapName(map)} · {mode}",
    };

    public bool Matches(ServerInfo info) =>
        (MapId is null || MapId.Equals(info.MapLabel, StringComparison.OrdinalIgnoreCase))
        && (GameMode is null || GameMode.Equals(info.GameMode, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> Conditions
    {
        get
        {
            var conditions = new List<string>(3);
            if (TimeLimitMinutes is { } minutes)
            {
                conditions.Add($"{minutes} min");
            }

            if (LeadPercent is { } percent)
            {
                conditions.Add(LeadMinimumScore > 0 ? $"{percent}% lead from {LeadMinimumScore} pts" : $"{percent}% lead");
            }

            if (ScoreLimit is { } limit)
            {
                conditions.Add($"{limit.ToString(CultureInfo.InvariantCulture)} pts");
            }

            return conditions;
        }
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (TimeLimitMinutes is null && LeadPercent is null && ScoreLimit is null)
        {
            errors.Add("Pick at least one limit: time, lead or score.");
        }

        if (TimeLimitMinutes is < 1 or > 600)
        {
            errors.Add("Time limit must be 1–600 minutes.");
        }

        if (LeadPercent is < 1 or > 1000)
        {
            errors.Add("Lead must be 1–1000 %.");
        }

        if (LeadMinimumScore is < 0 or > 100000)
        {
            errors.Add("The lead's minimum score must be 0–100000.");
        }

        if (ScoreLimit is < 1 or > 100000)
        {
            errors.Add("Score limit must be 1–100000.");
        }

        if (Name.Length > 40)
        {
            errors.Add("Keep the name under 40 characters.");
        }

        return errors;
    }
}

/// <summary>A match a rule ended.</summary>
public sealed record GameResult
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;

    public Guid RuleId { get; init; }

    public string RuleName { get; init; } = string.Empty;

    public string MapId { get; init; } = string.Empty;

    public string GameMode { get; init; } = string.Empty;

    public bool Teams { get; init; }

    public int BlueScore { get; init; }

    public int RedScore { get; init; }

    public string Winner { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public TimeSpan Elapsed { get; init; }

    /// <summary>Null when the map rotation was sent successfully.</summary>
    public string? Error { get; init; }
}
