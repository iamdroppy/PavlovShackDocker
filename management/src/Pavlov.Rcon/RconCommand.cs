using System.Buffers;
using System.Globalization;

namespace Pavlov.Rcon;

/// <summary>
/// One RCON command line: a command name followed by space-separated arguments.
/// Construction validates the input so user-supplied values can't inject a
/// second command (line breaks) or shift arguments (embedded whitespace).
/// </summary>
public sealed class RconCommand
{
    private static readonly SearchValues<char> Whitespace = SearchValues.Create(" \t\r\n\v\f");

    private RconCommand(string name, IReadOnlyList<string> arguments)
    {
        Name = name;
        Arguments = arguments;
    }

    public string Name { get; }

    public IReadOnlyList<string> Arguments { get; }

    /// <summary>Builds a command whose arguments are single tokens. Null arguments are skipped (optional trailing arguments).</summary>
    public static RconCommand Create(string name, params ReadOnlySpan<string?> arguments)
    {
        ValidateName(name);
        var args = new List<string>(arguments.Length);
        foreach (var argument in arguments)
        {
            if (argument is null)
            {
                continue;
            }

            if (argument.Length == 0)
            {
                throw new ArgumentException($"Arguments of '{name}' can't be empty.", nameof(arguments));
            }

            if (argument.AsSpan().ContainsAny(Whitespace) || HasControlCharacters(argument))
            {
                throw new ArgumentException($"'{argument}' isn't a valid argument for '{name}': it can't contain spaces or line breaks.", nameof(arguments));
            }

            args.Add(argument);
        }

        return new RconCommand(name, args);
    }

    /// <summary>Builds a command whose single argument is free text that may contain spaces (e.g. <c>UpdateServerName</c>).</summary>
    public static RconCommand CreateWithText(string name, string text)
    {
        ValidateName(name);
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException($"'{name}' needs a value.", nameof(text));
        }

        if (HasControlCharacters(trimmed))
        {
            throw new ArgumentException($"The value for '{name}' can't contain line breaks or control characters.", nameof(text));
        }

        return new RconCommand(name, [trimmed]);
    }

    /// <summary>Parses a raw console line such as <c>SwitchMap datacenter SND</c>.</summary>
    public static RconCommand Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (HasControlCharacters(line.Replace('\t', ' ')))
        {
            throw new ArgumentException("A command can't contain line breaks or control characters.", nameof(line));
        }

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new ArgumentException("Enter a command.", nameof(line));
        }

        ValidateName(parts[0]);
        return new RconCommand(parts[0], parts[1..]);
    }

    public static string Bool(bool value) => value ? "True" : "False";

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    public override string ToString() =>
        Arguments.Count == 0 ? Name : $"{Name} {string.Join(' ', Arguments)}";

    private static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsAsciiLetter(name[0]) || !name.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException($"'{name}' isn't a valid RCON command name.", nameof(name));
        }
    }

    private static bool HasControlCharacters(string value) => value.Any(char.IsControl);
}
