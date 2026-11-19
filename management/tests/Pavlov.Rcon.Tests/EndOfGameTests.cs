using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Pavlov.Manager.Services.EndOfGame;

namespace Pavlov.Rcon.Tests;

public sealed class EndOfGameEvaluatorTests
{
    private static ServerInfo Teams(int blue, int red, string map = "datacenter", string mode = "TDM") =>
        new() { MapLabel = map, GameMode = mode, Teams = true, Team0Score = blue, Team1Score = red };

    [Theory]
    [InlineData(33, 30, true)]   // exactly 10% above
    [InlineData(32, 30, false)]  // 6.7%
    [InlineData(10, 0, true)]    // clean sheet once the minimum is reached
    [InlineData(9, 0, false)]    // below the minimum score of 10
    [InlineData(30, 30, false)]  // a tie is never a lead
    public void Lead_percentage(int top, int second, bool expected) =>
        Assert.Equal(expected, EndOfGameEvaluator.LeadReached(top, second, percent: 10, minimumScore: 10));

    [Fact]
    public void Lead_names_the_leading_team()
    {
        var rule = new EndOfGameRule { LeadPercent = 10 };

        var decision = EndOfGameEvaluator.Evaluate(rule, Teams(blue: 30, red: 44), [], TimeSpan.Zero);

        Assert.NotNull(decision);
        Assert.Equal("Red", decision.Winner);
    }

    [Fact]
    public void Score_limit_wins_before_anything_else()
    {
        var rule = new EndOfGameRule { ScoreLimit = 50, TimeLimitMinutes = 20 };

        var decision = EndOfGameEvaluator.Evaluate(rule, Teams(50, 49), [], TimeSpan.FromMinutes(25));

        Assert.Equal(new EndDecision("Blue", "Score limit 50 reached (50 pts)"), decision);
    }

    [Fact]
    public void Time_limit_ends_in_a_draw_when_tied()
    {
        var rule = new EndOfGameRule { TimeLimitMinutes = 20 };

        Assert.Null(EndOfGameEvaluator.Evaluate(rule, Teams(5, 5), [], TimeSpan.FromMinutes(19.9)));
        Assert.Equal("Draw", EndOfGameEvaluator.Evaluate(rule, Teams(5, 5), [], TimeSpan.FromMinutes(20))?.Winner);
    }

    [Fact]
    public void Free_for_all_compares_the_two_best_players()
    {
        var rule = new EndOfGameRule { ScoreLimit = 30 };
        var info = new ServerInfo { MapLabel = "sand", GameMode = "DM", Teams = false };
        PlayerDetail[] players =
        [
            new() { PlayerName = "mira", Score = 12 },
            new() { PlayerName = "neo", Score = 31 },
        ];

        Assert.Equal("neo", EndOfGameEvaluator.Evaluate(rule, info, players, TimeSpan.Zero)?.Winner);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("DATACENTER", null, true)]
    [InlineData("datacenter", "SND", false)]
    [InlineData("sand", null, false)]
    public void Rules_match_their_map_and_mode(string? map, string? mode, bool expected) =>
        Assert.Equal(expected, new EndOfGameRule { MapId = map, GameMode = mode, ScoreLimit = 1 }.Matches(Teams(0, 0)));

    [Fact]
    public void A_rule_needs_at_least_one_limit() =>
        Assert.Contains("Pick at least one limit: time, lead or score.", new EndOfGameRule().Validate());
}

public sealed class MatchTrackerTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-19T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    private static ServerInfo Info(string map, int blue, int red) =>
        new() { MapLabel = map, GameMode = "TDM", Teams = true, Team0Score = blue, Team1Score = red };

    [Fact]
    public void Map_change_or_score_reset_starts_a_new_match()
    {
        var tracker = new MatchTracker(_time, NullLogger<MatchTracker>.Instance);

        var first = tracker.Observe(Info("datacenter", 3, 2));
        _time.Advance(TimeSpan.FromMinutes(5));
        var same = tracker.Observe(Info("datacenter", 6, 4));
        _time.Advance(TimeSpan.FromMinutes(1));
        var reset = tracker.Observe(Info("datacenter", 0, 0));
        var nextMap = tracker.Observe(Info("sand", 0, 0));

        Assert.False(first.FromStart); // joined mid-match
        Assert.Equal(first.StartedAt, same.StartedAt);
        Assert.True(reset.FromStart);
        Assert.Equal(_time.GetUtcNow(), reset.StartedAt);
        Assert.Equal("sand", nextMap.MapId);
    }

    [Fact]
    public void Only_one_rule_can_end_a_match()
    {
        var tracker = new MatchTracker(_time, NullLogger<MatchTracker>.Instance);
        tracker.Observe(Info("datacenter", 50, 3));

        Assert.True(tracker.TryClaimEnd());
        Assert.False(tracker.TryClaimEnd());
        Assert.True(tracker.IsChangingMap);

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(tracker.IsChangingMap);
        Assert.True(tracker.Observe(Info("sand", 0, 0)).FromStart);
    }
}
