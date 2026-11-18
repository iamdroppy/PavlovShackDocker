using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pavlov.Rcon;

/// <summary>Reply of <c>ServerInfo</c>. Scores arrive as strings; the converters accept both.</summary>
public sealed record ServerInfo
{
    public string MapLabel { get; init; } = string.Empty;

    public string MapName { get; init; } = string.Empty;

    public string GameMode { get; init; } = string.Empty;

    public string GameModeName { get; init; } = string.Empty;

    public string ServerName { get; init; } = string.Empty;

    [JsonConverter(typeof(LenientBooleanConverter))]
    public bool Teams { get; init; }

    [JsonConverter(typeof(LenientInt32Converter))]
    public int Team0Score { get; init; }

    [JsonConverter(typeof(LenientInt32Converter))]
    public int Team1Score { get; init; }

    [JsonConverter(typeof(LenientInt32Converter))]
    public int Round { get; init; }

    public string RoundState { get; init; } = string.Empty;

    /// <summary>"current/max", e.g. <c>3/10</c>.</summary>
    public string PlayerCount { get; init; } = string.Empty;

    [JsonIgnore]
    public int CurrentPlayers => CountPart(0);

    [JsonIgnore]
    public int MaxPlayers => CountPart(1);

    /// <summary>Friendly map name, falling back to the map id.</summary>
    [JsonIgnore]
    public string DisplayMap => MapName is { Length: > 0 } ? MapName : MapLabel;

    private int CountPart(int index)
    {
        var parts = PlayerCount.Split('/');
        return parts.Length == 2 && int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }
}

/// <summary>Entry of <c>RefreshList</c>.</summary>
public sealed record PlayerListEntry
{
    public string Username { get; init; } = string.Empty;

    public string UniqueId { get; init; } = string.Empty;
}

/// <summary>Entry of <c>InspectAll</c>/<c>InspectTeam</c>, or <c>InspectPlayer</c>'s PlayerInfo.</summary>
public sealed record PlayerDetail
{
    public string PlayerName { get; init; } = string.Empty;

    public string UniqueId { get; init; } = string.Empty;

    /// <summary>"kills/deaths/assists".</summary>
    public string KDA { get; init; } = string.Empty;

    [JsonConverter(typeof(LenientInt32Converter))]
    public int Score { get; init; }

    [JsonConverter(typeof(LenientBooleanConverter))]
    public bool Dead { get; init; }

    [JsonConverter(typeof(LenientInt32Converter))]
    public int Cash { get; init; }

    [JsonConverter(typeof(LenientInt32Converter))]
    public int TeamId { get; init; }

    [JsonConverter(typeof(LenientDoubleConverter))]
    public double Ping { get; init; }

    [JsonConverter(typeof(LenientBooleanConverter))]
    public bool Gag { get; init; }

    [JsonIgnore]
    public string DisplayName => PlayerName is { Length: > 0 } ? PlayerName : UniqueId;

    [JsonIgnore]
    public int Kills => KdaPart(0);

    [JsonIgnore]
    public int Deaths => KdaPart(1);

    [JsonIgnore]
    public int Assists => KdaPart(2);

    private int KdaPart(int index)
    {
        var parts = KDA.Split('/');
        return parts.Length > index && int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }
}

/// <summary>Entry of <c>MapList</c> (the map rotation).</summary>
public sealed record MapRotationEntry
{
    public string MapId { get; init; } = string.Empty;

    public string GameMode { get; init; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ServerInfo))]
[JsonSerializable(typeof(PlayerDetail))]
[JsonSerializable(typeof(List<PlayerDetail>))]
[JsonSerializable(typeof(List<PlayerListEntry>))]
[JsonSerializable(typeof(List<MapRotationEntry>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<JsonElement>))]
internal sealed partial class PavlovJsonContext : JsonSerializerContext;

/// <summary>Accepts <c>3</c>, <c>"3"</c>, <c>"3.0"</c> and <c>""</c> (0).</summary>
public sealed class LenientInt32Converter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.Number when reader.TryGetInt32(out var value) => value,
        JsonTokenType.Number => (int)reader.GetDouble(),
        JsonTokenType.String => double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? (int)parsed : 0,
        _ => 0,
    };

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

/// <summary>Accepts numbers and numeric strings.</summary>
public sealed class LenientDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.Number => reader.GetDouble(),
        JsonTokenType.String => double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0,
        _ => 0,
    };

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

/// <summary>Accepts <c>true</c>, <c>"true"</c>, <c>"True"</c> and <c>1</c>.</summary>
public sealed class LenientBooleanConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.Number => reader.TryGetInt32(out var value) && value != 0,
        JsonTokenType.String => reader.GetString() is { } text && (bool.TryParse(text, out var parsed) ? parsed : text == "1"),
        _ => false,
    };

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}
