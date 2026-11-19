using System.Globalization;
using Hangfire;
using Pavlov.Rcon;

namespace Pavlov.Manager.Services.EndOfGame;

/// <summary>
/// The Hangfire job behind one end-of-game rule. Runs every few seconds: reads the score,
/// and when a limit is reached rotates to the next map and records the result.
/// The returned text is shown as the job's result in the Hangfire dashboard.
/// </summary>
public sealed class EndOfGameJob(
    RuleStore rules,
    RconSession session,
    MatchTracker tracker,
    ServerMonitor monitor,
    ILogger<EndOfGameJob> logger)
{
    // A stale check is worthless; the next tick runs a few seconds later anyway.
    [AutomaticRetry(Attempts = 0)]
    [JobDisplayName("End of game · {1}")]
    public async Task<string> EvaluateAsync(Guid ruleId, string ruleName, CancellationToken cancellationToken)
    {
        if (rules.Find(ruleId) is not { Enabled: true } rule)
        {
            return "Rule is deleted or paused";
        }

        if (tracker.IsChangingMap)
        {
            return "Waiting for the next map to load";
        }

        var client = await session.GetBackgroundClientAsync(cancellationToken);
        if (client is null)
        {
            return "Waiting for RCON: sign in to the manager, or give it RCON_PASSWORD to run unattended";
        }

        MatchSnapshot snapshot;
        try
        {
            snapshot = await tracker.SnapshotAsync(client, cancellationToken);
        }
        catch (RconAuthenticationException)
        {
            await session.InvalidateAsync();
            return "The server rejected the RCON password";
        }
        catch (RconException ex)
        {
            return $"Server not reachable: {ex.Message}";
        }

        var info = snapshot.Info;
        var score = info.Teams ? $"{info.Team0Score}–{info.Team1Score}" : "free-for-all";
        if (!rule.Matches(info))
        {
            return $"Not active on {info.MapLabel} ({info.GameMode})";
        }

        var decision = EndOfGameEvaluator.Evaluate(rule, info, snapshot.Players, snapshot.Elapsed);
        if (decision is null)
        {
            return $"{info.MapLabel} {info.GameMode} {score} at {EndOfGameEvaluator.Clock(snapshot.Elapsed)}: playing on";
        }

        if (!tracker.TryClaimEnd())
        {
            return "Another rule already ended this match";
        }

        string? error = null;
        try
        {
            var response = await client.RotateMapAsync(cancellationToken);
            if (!response.Successful)
            {
                error = response.Comment ?? "RotateMap wasn't successful";
            }
        }
        catch (RconTimeoutException)
        {
            // The server often goes quiet while it loads the next map.
            error = null;
        }
        catch (RconException ex)
        {
            error = ex.Message;
        }

        rules.AddResult(new GameResult
        {
            RuleId = rule.Id,
            RuleName = rule.DisplayName,
            MapId = info.MapLabel,
            GameMode = info.GameMode,
            Teams = info.Teams,
            BlueScore = info.Team0Score,
            RedScore = info.Team1Score,
            Winner = decision.Winner,
            Reason = decision.Reason,
            Elapsed = snapshot.Elapsed,
            Error = error,
        });
        monitor.RequestRefresh();

        var outcome = decision.Winner is "Draw" or "Nobody" ? decision.Winner : $"{decision.Winner} wins";
        logger.LogInformation("End of game on {Map} {Mode}: {Outcome} {Score}, {Reason} (rule {Rule})", info.MapLabel, info.GameMode, outcome, score, decision.Reason, rule.DisplayName);
        return error is null
            ? $"Match ended: {outcome} {score}. {decision.Reason}. Rotated to the next map."
            : string.Create(CultureInfo.InvariantCulture, $"Match ended ({outcome}, {decision.Reason}) but rotating failed: {error}");
    }
}
