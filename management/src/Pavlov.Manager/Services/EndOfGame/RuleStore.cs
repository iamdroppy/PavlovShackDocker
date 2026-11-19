using System.Text.Json;

namespace Pavlov.Manager.Services.EndOfGame;

/// <summary>
/// End-of-game rules and recent results, saved as JSON under <c>Data:Path</c>
/// (a volume in the container). Rules are the source of truth; the Hangfire
/// recurring jobs are rebuilt from them on startup and on every change.
/// </summary>
public sealed class RuleStore
{
    private const int HistorySize = 30;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Lock _lock = new();
    private readonly string _path;
    private readonly ILogger<RuleStore> _logger;
    private List<EndOfGameRule> _rules = [];
    private List<GameResult> _history = [];

    public RuleStore(IConfiguration configuration, IHostEnvironment environment, ILogger<RuleStore> logger)
    {
        _logger = logger;
        var directory = configuration["Data:Path"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "end-of-game.json");
        Load();
    }

    public event Action? Changed;

    public IReadOnlyList<EndOfGameRule> Rules
    {
        get
        {
            lock (_lock)
            {
                return [.. _rules];
            }
        }
    }

    public IReadOnlyList<GameResult> History
    {
        get
        {
            lock (_lock)
            {
                return [.. _history];
            }
        }
    }

    public EndOfGameRule? Find(Guid id)
    {
        lock (_lock)
        {
            return _rules.Find(rule => rule.Id == id);
        }
    }

    public void Save(EndOfGameRule rule) => Update(() =>
    {
        var index = _rules.FindIndex(existing => existing.Id == rule.Id);
        if (index >= 0)
        {
            _rules[index] = rule;
        }
        else
        {
            _rules.Add(rule);
        }
    });

    public void Delete(Guid id) => Update(() => _rules.RemoveAll(rule => rule.Id == id));

    public void SetEnabled(Guid id, bool enabled) => Update(() =>
    {
        var index = _rules.FindIndex(rule => rule.Id == id);
        if (index >= 0)
        {
            _rules[index] = _rules[index] with { Enabled = enabled };
        }
    });

    public void AddResult(GameResult result) => Update(() =>
    {
        _history.Insert(0, result);
        if (_history.Count > HistorySize)
        {
            _history.RemoveRange(HistorySize, _history.Count - HistorySize);
        }
    });

    private void Update(Action change)
    {
        lock (_lock)
        {
            change();
            Persist();
        }

        Changed?.Invoke();
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var state = JsonSerializer.Deserialize<StoreState>(File.ReadAllText(_path), JsonOptions);
            _rules = state?.Rules ?? [];
            _history = state?.History ?? [];
            _logger.LogInformation("Loaded {Count} end-of-game rule(s) from {Path}", _rules.Count, _path);
        }
        catch (JsonException ex)
        {
            var backup = _path + ".broken";
            File.Copy(_path, backup, overwrite: true);
            _logger.LogError(ex, "Couldn't read {Path}; starting empty and kept the file as {Backup}", _path, backup);
        }
    }

    private void Persist()
    {
        // Write to a temp file and swap it in, so a crash can't leave half a file.
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new StoreState(_rules, _history), JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }

    private sealed record StoreState(List<EndOfGameRule> Rules, List<GameResult> History);
}
