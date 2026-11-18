using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Pavlov.Rcon;

/// <summary>
/// A reply from the server. Every JSON reply carries <c>Command</c>,
/// <c>Successful</c> and optionally <c>Comment</c>, plus command-specific fields.
/// Note: list commands (Banlist, ModeratorList) report <c>Successful: false</c>
/// when the list is empty, so typed readers ignore the flag.
/// </summary>
public sealed class RconResponse
{
    // Relaxed escaping keeps player names readable (no \u003C or \u00E9). This is display text:
    // whoever renders it as HTML must encode it, as the manager's JSON highlighter does.
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private RconResponse(string commandLine, string raw, JsonElement? json)
    {
        CommandLine = commandLine;
        Raw = raw;
        Json = json;
        if (json is { ValueKind: JsonValueKind.Object } root)
        {
            Command = TryGetProperty(root, "Command", out var command) && command.ValueKind == JsonValueKind.String
                ? command.GetString() ?? string.Empty
                : string.Empty;
            Successful = TryGetProperty(root, "Successful", out var successful) && successful.ValueKind == JsonValueKind.True;
            Comment = TryGetProperty(root, "Comment", out var comment) && comment.ValueKind == JsonValueKind.String
                ? comment.GetString()
                : null;
        }
        else
        {
            Command = string.Empty;
            Successful = raw.Equals("Goodbye", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>The command line that was sent.</summary>
    public string CommandLine { get; }

    /// <summary>The <c>Command</c> field of the reply.</summary>
    public string Command { get; }

    public bool Successful { get; }

    public string? Comment { get; }

    /// <summary>The reply exactly as received.</summary>
    public string Raw { get; }

    public JsonElement? Json { get; }

    public DateTimeOffset ReceivedAt { get; } = DateTimeOffset.UtcNow;

    public static RconResponse Parse(string commandLine, string raw)
    {
        if (raw.StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                return new RconResponse(commandLine, raw, document.RootElement.Clone());
            }
            catch (JsonException)
            {
                // Fall through and keep it as text.
            }
        }

        return new RconResponse(commandLine, raw, null);
    }

    /// <summary>Deserializes a command-specific property (case-insensitive name).</summary>
    public T? Read<T>(string propertyName, JsonTypeInfo<T> typeInfo) =>
        Json is { ValueKind: JsonValueKind.Object } root && TryGetProperty(root, propertyName, out var value)
            ? value.Deserialize(typeInfo)
            : default;

    public RconResponse EnsureSuccess() => Successful ? this : throw new RconCommandFailedException(this);

    /// <summary>The reply formatted for display.</summary>
    public string ToDisplayString() =>
        Json is { } json ? JsonSerializer.Serialize(json, IndentedOptions) : Raw;

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(name) || string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
