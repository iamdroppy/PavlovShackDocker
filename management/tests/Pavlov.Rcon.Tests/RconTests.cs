using System.Text;

namespace Pavlov.Rcon.Tests;

public sealed class FrameReaderTests
{
    [Fact]
    public void Extracts_json_objects_and_keeps_the_rest()
    {
        var buffer = new StringBuilder("{\r\n\t\"a\": \"}{\\\"\",\r\n\t\"b\": {\"c\": 1}\r\n}\r\n{\"Command\":");

        Assert.True(RconFrameReader.TryExtractFrame(buffer, out var first));
        Assert.Equal("{\r\n\t\"a\": \"}{\\\"\",\r\n\t\"b\": {\"c\": 1}\r\n}", first);
        Assert.False(RconFrameReader.TryExtractFrame(buffer, out _));
        Assert.StartsWith("\r\n{\"Command\":", buffer.ToString());
    }

    [Fact]
    public void Extracts_text_lines()
    {
        var buffer = new StringBuilder("Goodbye\r\n");

        Assert.True(RconFrameReader.TryExtractFrame(buffer, out var frame));
        Assert.Equal("Goodbye", frame);
        Assert.Equal(0, buffer.Length);
    }
}

public sealed class RconCommandTests
{
    [Theory]
    [InlineData("player\nShutdownServer")]
    [InlineData("two words")]
    [InlineData("")]
    public void Rejects_arguments_that_could_inject_or_shift(string argument) =>
        Assert.Throws<ArgumentException>(() => RconCommand.Create("Kick", argument));

    [Fact]
    public void Skips_null_optional_arguments() =>
        Assert.Equal("SetPin", RconCommand.Create("SetPin", (string?)null).ToString());

    [Fact]
    public void Text_commands_keep_spaces_but_not_line_breaks()
    {
        Assert.Equal("UpdateServerName My Shack Server", RconCommand.CreateWithText("UpdateServerName", "  My Shack Server ").ToString());
        Assert.Throws<ArgumentException>(() => RconCommand.CreateWithText("UpdateServerName", "a\r\nShutdownServer"));
    }

    [Fact]
    public void Parses_console_lines() =>
        Assert.Equal("SwitchMap datacenter SND", RconCommand.Parse("  SwitchMap   datacenter SND ").ToString());

    [Theory]
    [InlineData("3267926", "UGC3267926")]
    [InlineData("ugc3267926", "UGC3267926")]
    public void Normalizes_ugc_ids(string input, string expected) =>
        Assert.Equal(expected, PavlovRconCommands.NormalizeUgc(input));
}

public sealed class PavlovRconClientTests
{
    private const string Password = "s3cret pass";

    [Fact]
    public async Task Authenticates_and_reads_a_split_reply()
    {
        var body = "\t\"ServerInfo\": {\r\n\t\t\"MapLabel\": \"datacenter\",\r\n\t\t\"GameMode\": \"TDM\",\r\n\t\t\"Teams\": true,\r\n\t\t\"Team0Score\": \"4\",\r\n\t\t\"PlayerCount\": \"3/10\"\r\n\t},\r\n";
        var json = FakeRconServer.Json("ServerInfo", body);
        await using var server = new FakeRconServer(Password, _ => [json[..25], json[25..60], json[60..]]);
        await using var client = server.CreateClient(Password);

        var info = await client.GetServerInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal("datacenter", info.MapLabel);
        Assert.Equal(4, info.Team0Score);
        Assert.Equal(3, info.CurrentPlayers);
        Assert.Equal(10, info.MaxPlayers);
    }

    [Fact]
    public async Task Wrong_password_throws_authentication_error()
    {
        await using var server = new FakeRconServer(Password, _ => null);
        await using var client = server.CreateClient("wrong");

        await Assert.ThrowsAsync<RconAuthenticationException>(() => client.ConnectAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Timeout_drops_the_connection_and_the_next_command_reconnects()
    {
        await using var server = new FakeRconServer(Password, line => line == "RotateMap" ? null : [FakeRconServer.Json(line)]);
        await using var client = server.CreateClient(Password, TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAsync<RconTimeoutException>(() => client.RotateMapAsync(TestContext.Current.CancellationToken));
        var reply = await client.ResetSndAsync(TestContext.Current.CancellationToken);

        Assert.True(reply.Successful);
        Assert.Equal("ResetSND", reply.Command);
        Assert.Equal(2, server.Connections);
    }

    [Fact]
    public async Task Empty_lists_parse_even_when_unsuccessful()
    {
        await using var server = new FakeRconServer(Password, _ => [FakeRconServer.Json("BanList", "\t\"BanList\": [],\r\n", successful: false)]);
        await using var client = server.CreateClient(Password);

        var bans = await client.GetBanListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(bans);
    }

    [Fact]
    public async Task Inspect_list_parses_string_numbers()
    {
        var body = "\t\"InspectList\": [\r\n\t\t{\r\n\t\t\t\"PlayerName\": \"neo\",\r\n\t\t\t\"UniqueId\": \"neo_vr\",\r\n\t\t\t\"KDA\": \"3/1/2\",\r\n\t\t\t\"Score\": \"120\",\r\n\t\t\t\"Dead\": false,\r\n\t\t\t\"Cash\": \"800\",\r\n\t\t\t\"TeamId\": \"1\",\r\n\t\t\t\"Ping\": 23.5,\r\n\t\t\t\"Gag\": false\r\n\t\t}\r\n\t],\r\n";
        await using var server = new FakeRconServer(Password, _ => [FakeRconServer.Json("InspectAll", body)]);
        await using var client = server.CreateClient(Password);

        var player = Assert.Single(await client.InspectAllAsync(TestContext.Current.CancellationToken));

        Assert.Equal((3, 1, 2), (player.Kills, player.Deaths, player.Assists));
        Assert.Equal(800, player.Cash);
        Assert.Equal(PavlovCatalog.RedTeam, player.TeamId);
        Assert.Equal(23.5, player.Ping);
    }
}
