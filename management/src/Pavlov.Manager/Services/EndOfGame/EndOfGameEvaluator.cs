using System.Globalization;
using Pavlov.Rcon;

namespace Pavlov.Manager.Services.EndOfGame;

public sealed record EndDecision(string Winner, string Reason);

/// <summary>How close the current match is to one of a rule's limits, for display.</summary>
public sealed record LimitProgress(string Kind, string Label, string Current, string Target, double Fraction);

/// <summary>
/// Pure end-of-game logic. Team modes compare Blue (team 0) and Red (team 1);
/// free-for-all modes compare the two best players.
/// </summary>
public static class EndOfGameEvaluator
{
    public static EndDecision? Evaluate(EndOfGameRule rule, ServerInfo info, IReadOnlyList<PlayerDetail> players, TimeSpan elapsed)
    {
        var (leader, top, second) = Standings(info, players);

        if (rule.ScoreLimit is { } limit && top >= limit)
        {
            return new EndDecision(leader, $"Score limit {limit} reached ({top} pts)");
        }

        if (rule.LeadPercent is { } percent && LeadReached(top, second, percent, rule.LeadMinimumScore))
        {
            var lead = second == 0 ? $"{top}–0" : $"{LeadPercentOf(top, second):0}%";
            return new EndDecision(leader, $"Led by {lead}, limit {percent}%");
        }

        if (rule.TimeLimitMinutes is { } minutes && elapsed >= TimeSpan.FromMinutes(minutes))
        {
            return new EndDecision(leader, $"Time limit {minutes} min reached");
        }

        return null;
    }

    /// <summary>
    /// True when <paramref name="top"/> is at least <paramref name="percent"/>% above <paramref name="second"/>,
    /// e.g. 10% means 33 vs 30. Integer math, so 10% of 30 is exactly 3.
    /// </summary>
    public static bool LeadReached(int top, int second, int percent, int minimumScore) =>
        top > second && top >= minimumScore && (top - second) * 100L >= (long)percent * second;

    public static double LeadPercentOf(int top, int second) =>
        second <= 0 ? double.PositiveInfinity : (top - second) * 100.0 / second;

    public static IReadOnlyList<LimitProgress> Progress(EndOfGameRule rule, ServerInfo info, IReadOnlyList<PlayerDetail> players, TimeSpan elapsed)
    {
        var (_, top, second) = Standings(info, players);
        var progress = new List<LimitProgress>(3);

        if (rule.TimeLimitMinutes is { } minutes)
        {
            var limit = TimeSpan.FromMinutes(minutes);
            progress.Add(new LimitProgress("time", "Time", Clock(elapsed), Clock(limit), Fraction(elapsed.TotalSeconds, limit.TotalSeconds)));
        }

        if (rule.LeadPercent is { } percent)
        {
            var current = top <= second ? 0 : LeadPercentOf(top, second);
            var label = top < rule.LeadMinimumScore ? $"Lead (from {rule.LeadMinimumScore} pts)" : "Lead";
            var shown = double.IsPositiveInfinity(current) ? "∞" : $"{current:0}%";
            var fraction = top < rule.LeadMinimumScore
                ? Fraction(top, rule.LeadMinimumScore) * 0.5
                : Fraction(current, percent);
            progress.Add(new LimitProgress("lead", label, shown, $"{percent}%", fraction));
        }

        if (rule.ScoreLimit is { } scoreLimit)
        {
            progress.Add(new LimitProgress("score", "Score", top.ToString(CultureInfo.InvariantCulture), scoreLimit.ToString(CultureInfo.InvariantCulture), Fraction(top, scoreLimit)));
        }

        return progress;
    }

    public static string Clock(TimeSpan time) =>
        time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    private static (string Leader, int Top, int Second) Standings(ServerInfo info, IReadOnlyList<PlayerDetail> players)
    {
        if (info.Teams)
        {
            var (blue, red) = (info.Team0Score, info.Team1Score);
            var leader = blue == red ? "Draw" : blue > red ? "Blue" : "Red";
            return (leader, Math.Max(blue, red), Math.Min(blue, red));
        }

        var ranked = players.OrderByDescending(p => p.Score).ToList();
        return ranked switch
        {
            [] => ("Nobody", 0, 0),
            [var only] => (only.DisplayName, only.Score, 0),
            [var first, var next, ..] => (first.Score == next.Score ? "Draw" : first.DisplayName, first.Score, next.Score),
        };
    }

    private static double Fraction(double current, double target) =>
        target <= 0 ? 1 : Math.Clamp(current / target, 0, 1);
}
