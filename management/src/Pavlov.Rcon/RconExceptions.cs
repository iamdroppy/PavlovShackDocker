namespace Pavlov.Rcon;

/// <summary>Base type for every error raised by the RCON client.</summary>
public class RconException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>The server rejected the RCON password.</summary>
public sealed class RconAuthenticationException(string message)
    : RconException(message);

/// <summary>The TCP connection could not be opened or was lost.</summary>
public sealed class RconConnectionException(string message, Exception? innerException = null)
    : RconException(message, innerException);

/// <summary>The server did not answer a command in time.</summary>
public sealed class RconTimeoutException(string message)
    : RconException(message);

/// <summary>The server answered a command with <c>"Successful": false</c>.</summary>
public sealed class RconCommandFailedException(RconResponse response)
    : RconException(response.Comment is { Length: > 0 } comment
        ? $"'{response.CommandLine}' failed: {comment}"
        : $"'{response.CommandLine}' failed.")
{
    public RconResponse Response { get; } = response;
}
