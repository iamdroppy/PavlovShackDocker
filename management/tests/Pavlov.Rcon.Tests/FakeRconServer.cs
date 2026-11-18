using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Pavlov.Rcon.Tests;

/// <summary>Minimal stand-in for the Pavlov RCON listener.</summary>
internal sealed class FakeRconServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly string _passwordHash;
    private readonly Func<string, IReadOnlyList<string>?> _reply;
    private readonly Task _loop;

    /// <param name="reply">Maps a command line to reply chunks (sent with small gaps), or null for no reply.</param>
    public FakeRconServer(string password, Func<string, IReadOnlyList<string>?> reply)
    {
        _passwordHash = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(password)));
        _reply = reply;
        _listener.Start();
        _loop = Task.Run(AcceptLoopAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int Connections { get; private set; }

    public List<string> Received { get; } = [];

    public PavlovRconClient CreateClient(string password, TimeSpan? commandTimeout = null) =>
        new(new PavlovRconClientOptions
        {
            Host = "127.0.0.1",
            Port = Port,
            Password = password,
            CommandTimeout = commandTimeout ?? TimeSpan.FromSeconds(2),
            CommandInterval = TimeSpan.Zero,
        });

    public static string Json(string command, string body = "", bool successful = true) =>
        "{\r\n\t\"Command\": \"" + command + "\",\r\n" + body + "\t\"Successful\": " + (successful ? "true" : "false") + "\r\n}\r\n";

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            Connections++;
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            await stream.WriteAsync("Password: "u8.ToArray(), _stop.Token);

            var hash = new byte[32];
            await stream.ReadExactlyAsync(hash, _stop.Token);
            if (Encoding.ASCII.GetString(hash) != _passwordHash)
            {
                await stream.WriteAsync("Authenticated=0\r\n"u8.ToArray(), _stop.Token);
                return;
            }

            await stream.WriteAsync("Authenticated=1\r\n"u8.ToArray(), _stop.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                lock (Received)
                {
                    Received.Add(line);
                }

                if (line == "Disconnect")
                {
                    await stream.WriteAsync("Goodbye\r\n"u8.ToArray(), _stop.Token);
                    return;
                }

                foreach (var chunk in _reply(line) ?? [])
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(chunk), _stop.Token);
                    await stream.FlushAsync(_stop.Token);
                    await Task.Delay(20, _stop.Token);
                }
            }
        }
    }
}
