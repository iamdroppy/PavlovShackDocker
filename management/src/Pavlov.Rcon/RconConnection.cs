using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Pavlov.Rcon;

/// <summary>
/// One authenticated TCP connection. Handshake: the server sends
/// <c>"Password: "</c>, the client answers with the lowercase hex MD5 of the
/// password (no line break) and the server replies <c>Authenticated=1</c> or
/// <c>Authenticated=0</c>. Commands are LF-terminated lines.
/// </summary>
internal sealed class RconConnection : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly RconFrameReader _reader;

    private RconConnection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        _reader = new RconFrameReader(_stream);
    }

    /// <summary>False once the server closed the socket (e.g. after an idle timeout).</summary>
    public bool IsAlive
    {
        get
        {
            try
            {
                var socket = _client.Client;
                return socket.Connected && !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                return false;
            }
        }
    }

    public static async Task<RconConnection> OpenAsync(string host, int port, string password, CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            try
            {
                await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                throw new RconConnectionException($"Can't reach RCON at {host}:{port} ({ex.SocketErrorCode}). Is the server running with RCON_PASSWORD set?", ex);
            }

            var connection = new RconConnection(client);
            await connection._reader.ReadPastAsync("Password:", cancellationToken).ConfigureAwait(false);

            var hash = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(password)));
            await connection._stream.WriteAsync(Encoding.ASCII.GetBytes(hash), cancellationToken).ConfigureAwait(false);

            string reply;
            try
            {
                reply = (await connection._reader.ReadFrameAsync(cancellationToken).ConfigureAwait(false)).Trim();
            }
            catch (RconConnectionException)
            {
                throw new RconAuthenticationException("The server closed the connection after the password was sent.");
            }

            return reply switch
            {
                "Authenticated=1" => connection,
                "Authenticated=0" => throw new RconAuthenticationException("Wrong RCON password."),
                _ => throw new RconConnectionException($"Unexpected reply to the password: '{reply}'."),
            };
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Sends one command line and returns the raw reply.</summary>
    public async Task<string> ExchangeAsync(string commandLine, CancellationToken cancellationToken)
    {
        _reader.DiscardPending();
        await _stream.WriteAsync(Encoding.UTF8.GetBytes(commandLine + "\n"), cancellationToken).ConfigureAwait(false);
        return await _reader.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (IsAlive)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                await _stream.WriteAsync("Disconnect\n"u8.ToArray(), timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // Best effort: the socket is closed below either way.
        }
        finally
        {
            _client.Dispose();
        }
    }
}
