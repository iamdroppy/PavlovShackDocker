using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;

namespace Pavlov.Manager.Components.UI;

/// <summary>
/// Colors indented JSON for display. Every piece of text is HTML-encoded before it
/// is wrapped in a span, because replies contain player-chosen names.
/// </summary>
public static class JsonHighlighter
{
    public static MarkupString Highlight(string json)
    {
        var html = new StringBuilder(json.Length * 2);
        var i = 0;
        while (i < json.Length)
        {
            var c = json[i];
            if (c == '"')
            {
                var end = i + 1;
                while (end < json.Length && json[end] != '"')
                {
                    end += json[end] == '\\' ? 2 : 1;
                }

                end = Math.Min(end + 1, json.Length);
                var next = end;
                while (next < json.Length && char.IsWhiteSpace(json[next]))
                {
                    next++;
                }

                var isKey = next < json.Length && json[next] == ':';
                Append(html, json[i..end], isKey ? "text-sky-300" : "text-signal-300");
                i = end;
            }
            else if (c == '-' || char.IsAsciiDigit(c))
            {
                var end = i + 1;
                while (end < json.Length && (char.IsAsciiDigit(json[end]) || json[end] is '.' or 'e' or 'E' or '+' or '-'))
                {
                    end++;
                }

                Append(html, json[i..end], "text-amber-300");
                i = end;
            }
            else if (TryWord(json, i, "true", out var word) || TryWord(json, i, "false", out word) || TryWord(json, i, "null", out word))
            {
                Append(html, word, word == "false" ? "text-rose-300" : "text-violet-300");
                i += word.Length;
            }
            else
            {
                html.Append(HtmlEncoder.Default.Encode(c.ToString()));
                i++;
            }
        }

        return new MarkupString(html.ToString());
    }

    private static bool TryWord(string json, int index, string word, out string match)
    {
        match = word;
        return string.CompareOrdinal(json, index, word, 0, word.Length) == 0;
    }

    private static void Append(StringBuilder html, string text, string cssClass) =>
        html.Append("<span class=\"").Append(cssClass).Append("\">").Append(HtmlEncoder.Default.Encode(text)).Append("</span>");
}
