using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Pavlov.Rcon;

namespace Pavlov.Manager.Services;

public sealed class RconSettings
{
    public const string Section = "Rcon";

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 9100;

    public int CommandTimeoutSeconds { get; set; } = 5;
}

public sealed record LoginResult(bool Succeeded, string? Error = null, string? Generation = null);

/// <summary>The manager has no RCON credentials (app restarted, or the password changed).</summary>
public sealed class RconSessionExpiredException() : Exception("The RCON session expired. Sign in again.");

/// <summary>
/// Holds the one shared RCON connection. The manager has no users of its own:
/// whoever knows the server's RCON password may manage it. The password is only
/// kept in memory; after a restart everyone signs in again.
/// </summary>
public sealed class RconSession(IOptions<RconSettings> settings, ILoggerFactory loggerFactory) : IAsyncDisposable
{
    public const string GenerationClaim = "rcon-generation";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger _logger = loggerFactory.CreateLogger<RconSession>();
    private PavlovRconClient? _client;
    private byte[]? _passwordHash;

    /// <summary>Changes whenever the credentials change; cookies from an older generation are rejected.</summary>
    public string Generation { get; private set; } = Guid.NewGuid().ToString("N");

    public string Endpoint => $"{settings.Value.Host}:{settings.Value.Port}";

    public bool IsActive => _client is not null;

    public PavlovRconClient Client => _client ?? throw new RconSessionExpiredException();

    public bool IsValid(ClaimsPrincipal? principal) =>
        _client is not null && principal?.FindFirst(GenerationClaim)?.Value == Generation;

    /// <summary>Checks the password against the server and, if it's right, keeps the connection.</summary>
    public async Task<LoginResult> LoginAsync(string password, CancellationToken cancellationToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_client is not null && _passwordHash is not null && CryptographicOperations.FixedTimeEquals(hash, _passwordHash))
            {
                return new LoginResult(true, Generation: Generation);
            }

            var candidate = new PavlovRconClient(
                new PavlovRconClientOptions
                {
                    Host = settings.Value.Host,
                    Port = settings.Value.Port,
                    Password = password,
                    CommandTimeout = TimeSpan.FromSeconds(settings.Value.CommandTimeoutSeconds),
                },
                loggerFactory.CreateLogger<PavlovRconClient>());

            try
            {
                await candidate.ConnectAsync(cancellationToken);
            }
            catch (RconAuthenticationException)
            {
                await candidate.DisposeAsync();
                return new LoginResult(false, "That's not the server's RCON password.");
            }
            catch (RconException ex)
            {
                await candidate.DisposeAsync();
                return new LoginResult(false, ex.Message);
            }

            var previous = _client;
            _client = candidate;
            _passwordHash = hash;
            Generation = Guid.NewGuid().ToString("N");
            if (previous is not null)
            {
                await previous.DisposeAsync();
            }

            _logger.LogInformation("Signed in to RCON at {Endpoint}", Endpoint);
            return new LoginResult(true, Generation: Generation);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Forgets the credentials, e.g. after the server stopped accepting them.</summary>
    public async Task InvalidateAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_client is { } client)
            {
                _client = null;
                _passwordHash = null;
                Generation = Guid.NewGuid().ToString("N");
                await client.DisposeAsync();
                _logger.LogWarning("RCON credentials were rejected; everyone has to sign in again");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is { } client)
        {
            await client.DisposeAsync();
        }

        _lock.Dispose();
    }
}
