using Hangfire;
using Hangfire.Dashboard;
using Hangfire.Storage;

namespace Pavlov.Manager.Services.EndOfGame;

/// <summary>
/// Keeps Hangfire's recurring jobs in step with the rules: creating or resuming a rule adds
/// its job, deleting or pausing it removes the job. Runs on startup and on every rule change.
/// </summary>
public sealed class EndOfGameScheduler(
    RuleStore rules,
    IRecurringJobManager recurringJobs,
    JobStorage storage,
    ILogger<EndOfGameScheduler> logger) : IHostedService
{
    /// <summary>Every 5 seconds (Hangfire 1.8 accepts a seconds field).</summary>
    public const string CheckEvery = "*/5 * * * * *";

    private const string JobPrefix = "end-of-game-";
    private readonly Lock _lock = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Sync();
        rules.Changed += Sync;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        rules.Changed -= Sync;
        return Task.CompletedTask;
    }

    private void Sync()
    {
        lock (_lock)
        {
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in rules.Rules.Where(rule => rule.Enabled))
            {
                wanted.Add(rule.JobId);
                var id = rule.Id;
                var name = rule.DisplayName;
                recurringJobs.AddOrUpdate<EndOfGameJob>(
                    rule.JobId,
                    job => job.EvaluateAsync(id, name, CancellationToken.None),
                    CheckEvery,
                    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc, MisfireHandling = MisfireHandlingMode.Ignorable });
            }

            using var connection = storage.GetConnection();
            foreach (var job in connection.GetRecurringJobs())
            {
                if (job.Id.StartsWith(JobPrefix, StringComparison.Ordinal) && !wanted.Contains(job.Id))
                {
                    recurringJobs.RemoveIfExists(job.Id);
                }
            }

            logger.LogInformation("End-of-game jobs in sync: {Count} active", wanted.Count);
        }
    }
}

/// <summary>The dashboard is only for people signed in with the RCON password (same cookie as the app).</summary>
public sealed class SignedInDashboardAuthorization : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) =>
        context.GetHttpContext().User.Identity?.IsAuthenticated == true;
}
