using System.Text;

namespace Pavlov.Rcon;

/// <summary>
/// Splits the RCON byte stream into replies. Replies are either a JSON object
/// (multi-line, tab-indented, may arrive split over several reads or several in
/// one read) or a single text line such as <c>Authenticated=1</c> or <c>Goodbye</c>.
/// </summary>
internal sealed class RconFrameReader(Stream stream)
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly byte[] _bytes = new byte[16 * 1024];
    private readonly char[] _chars = new char[Encoding.UTF8.GetMaxCharCount(16 * 1024)];
    private readonly StringBuilder _pending = new();

    /// <summary>Reads until <paramref name="token"/> was received and drops everything up to and including it.</summary>
    public async ValueTask ReadPastAsync(string token, CancellationToken cancellationToken)
    {
        while (true)
        {
            var text = _pending.ToString();
            var index = text.IndexOf(token, StringComparison.Ordinal);
            if (index >= 0)
            {
                _pending.Remove(0, index + token.Length);
                return;
            }

            await FillAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Returns the next complete reply.</summary>
    public async ValueTask<string> ReadFrameAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (TryExtractFrame(_pending, out var frame))
            {
                return frame;
            }

            await FillAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Drops any bytes already received but not consumed (e.g. a late reply).</summary>
    public void DiscardPending() => _pending.Clear();

    internal static bool TryExtractFrame(StringBuilder buffer, out string frame)
    {
        frame = string.Empty;
        var start = 0;
        while (start < buffer.Length && char.IsWhiteSpace(buffer[start]))
        {
            start++;
        }

        if (start == buffer.Length)
        {
            buffer.Clear();
            return false;
        }

        if (buffer[start] == '{')
        {
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start; i < buffer.Length; i++)
            {
                var c = buffer[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        break;
                    case '{':
                        depth++;
                        break;
                    case '}' when --depth == 0:
                        frame = buffer.ToString(start, i - start + 1);
                        buffer.Remove(0, i + 1);
                        return true;
                }
            }

            return false;
        }

        for (var i = start; i < buffer.Length; i++)
        {
            if (buffer[i] == '\n')
            {
                frame = buffer.ToString(start, i - start).TrimEnd('\r');
                buffer.Remove(0, i + 1);
                return true;
            }
        }

        return false;
    }

    private async ValueTask FillAsync(CancellationToken cancellationToken)
    {
        var read = await stream.ReadAsync(_bytes, cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            throw new RconConnectionException("The server closed the RCON connection.");
        }

        var chars = _decoder.GetChars(_bytes, 0, read, _chars, 0, flush: false);
        _pending.Append(_chars, 0, chars);
    }
}
