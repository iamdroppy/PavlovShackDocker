using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pavlov.Rcon;

public sealed class PavlovRconClientOptions
{
    public required string Host { get; init; }

    public int Port
    {
        get;
        init => field = value is > 0 and <= 65535
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Port), value, "Port must be 1-65535.");
    } = 9100;

    public required string Password { get; init; }

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long to wait for a reply. The server never answers unknown commands, or commands sent during a map change.</summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Minimum gap between commands; the wiki warns that back-to-back commands can be dropped.</summary>
    public TimeSpan CommandInterval { get; init; } = TimeSpan.FromMilliseconds(100);
}

/// <summary>
/// Thread-safe RCON client over one long-lived connection (the server prefers
/// that over a connection per command). Commands are queued, paced, and the
/// connection is re-opened transparently when the server dropped it while idle.
/// A connection is never reused after a timeout, so a late reply can't be
/// mistaken for the answer to the next command.
/// </summary>
public sealed class PavlovRconClient(PavlovRconClientOptions options, ILogger<PavlovRconClient>? logger = null) : IAsyncDisposable
{
    private static readonly HashSet<string> SensitiveCommands = new(StringComparer.OrdinalIgnoreCase) { "SetPin" };

    private readonly ILogger _logger = logger ?? NullLogger<PavlovRconClient>.Instance;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RconConnection? _connection;
    private long _lastCommandTimestamp;
    private bool _disposed;

    public string Host => options.Host;

    public int Port => options.Port;

    public bool IsConnected => _connection?.IsAlive == true;

    /// <summary>Opens and authenticates the connection now instead of on the first command.</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RconResponse> ExecuteAsync(RconCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            await PaceAsync(cancellationToken).ConfigureAwait(false);

            var line = command.ToString();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.CommandTimeout);
            try
            {
                var raw = await connection.ExchangeAsync(line, timeout.Token).ConfigureAwait(false);
                var response = RconResponse.Parse(line, raw);
                _logger.LogInformation("RCON {Command} -> {Result}", Describe(command), response.Successful ? "ok" : "failed");
                return response;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await DropConnectionAsync().ConfigureAwait(false);
                _logger.LogWarning("RCON {Command} timed out", Describe(command));
                throw new RconTimeoutException(
                    $"No reply to '{command.Name}' within {options.CommandTimeout.TotalSeconds:0.#}s. The server ignores unknown commands and doesn't run commands during a map change.");
            }
            catch (OperationCanceledException)
            {
                // Cancelled mid-exchange: the reply may still arrive, so don't reuse the socket.
                await DropConnectionAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or RconConnectionException)
            {
                await DropConnectionAsync().ConfigureAwait(false);
                throw ex as RconConnectionException ?? new RconConnectionException("Lost the RCON connection.", ex);
            }
            finally
            {
                _lastCommandTimestamp = Stopwatch.GetTimestamp();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DropConnectionAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task<RconConnection> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsAlive: true } alive)
        {
            return alive;
        }

        if (_connection is not null)
        {
            _logger.LogInformation("RCON connection to {Host}:{Port} was closed by the server, reconnecting", options.Host, options.Port);
            await DropConnectionAsync().ConfigureAwait(false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.ConnectTimeout);
        try
        {
            _connection = await RconConnection.OpenAsync(options.Host, options.Port, options.Password, timeout.Token).ConfigureAwait(false);
            _logger.LogInformation("RCON connected to {Host}:{Port}", options.Host, options.Port);
            return _connection;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RconConnectionException($"Timed out connecting to RCON at {options.Host}:{options.Port}.");
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            throw new RconConnectionException($"Can't connect to RCON at {options.Host}:{options.Port}: {ex.Message}", ex);
        }
    }

    private async Task PaceAsync(CancellationToken cancellationToken)
    {
        if (_lastCommandTimestamp == 0)
        {
            return;
        }

        var wait = options.CommandInterval - Stopwatch.GetElapsedTime(_lastCommandTimestamp);
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DropConnectionAsync()
    {
        if (_connection is { } connection)
        {
            _connection = null;
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string Describe(RconCommand command) =>
        SensitiveCommands.Contains(command.Name) && command.Arguments.Count > 0 ? $"{command.Name} ****" : command.ToString();
}
