using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using YtScribe.Core.Model;

namespace YtScribe.Core.Services;

public sealed partial class VttTranscriptParser : IVttTranscriptParser
{
    public async Task<IReadOnlyList<TranscriptSegment>> ParseAsync(string vttPath, CancellationToken cancellationToken = default)
    {
        var lines = await File.ReadAllLinesAsync(vttPath, cancellationToken);
        var segments = new List<TranscriptSegment>();
        var accumulatedText = string.Empty;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (!line.Contains("-->", StringComparison.Ordinal))
            {
                continue;
            }

            var cueParts = line.Split("-->", StringSplitOptions.TrimEntries);
            if (cueParts.Length != 2 || !TryParseTimestamp(cueParts[0], out var startSeconds) || !TryParseTimestamp(cueParts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], out var endSeconds))
            {
                continue;
            }

            var textLines = new List<string>();
            index++;
            while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
            {
                var textLine = CleanText(lines[index]);
                if (!string.IsNullOrWhiteSpace(textLine))
                {
                    textLines.Add(textLine);
                }

                index++;
            }

            var text = string.Join(' ', textLines).Trim();
            var newText = GetNewCaptionText(accumulatedText, text);
            if (newText.Length > 0)
            {
                segments.Add(new TranscriptSegment(startSeconds, endSeconds, newText));
                accumulatedText = AppendText(accumulatedText, newText);
            }
        }

        return segments;
    }

    private static bool TryParseTimestamp(string value, out double seconds)
    {
        seconds = 0;
        var parts = value.Trim().Split(':');
        if (parts.Length is not 2 and not 3)
        {
            return false;
        }

        var offset = parts.Length == 3 ? 1 : 0;
        if (!double.TryParse(parts[offset + 1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSeconds))
        {
            return false;
        }

        if (!int.TryParse(parts[offset], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
        {
            return false;
        }

        var hours = 0;
        if (parts.Length == 3 && !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hours))
        {
            return false;
        }

        seconds = hours * 3600 + minutes * 60 + parsedSeconds;
        return true;
    }

    private static string CleanText(string value)
    {
        var withoutTags = VttTagRegex().Replace(value, string.Empty);
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    private static string GetNewCaptionText(string accumulatedText, string cueText)
    {
        if (string.IsNullOrWhiteSpace(cueText))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(accumulatedText))
        {
            return cueText.Trim();
        }

        var overlapLength = FindSuffixPrefixOverlap(accumulatedText, cueText);
        if (overlapLength >= cueText.Length)
        {
            return string.Empty;
        }

        return cueText[overlapLength..].Trim();
    }

    private static int FindSuffixPrefixOverlap(string accumulatedText, string cueText)
    {
        var maxLength = Math.Min(accumulatedText.Length, cueText.Length);
        for (var length = maxLength; length > 0; length--)
        {
            if (accumulatedText.AsSpan(accumulatedText.Length - length, length).Equals(cueText.AsSpan(0, length), StringComparison.OrdinalIgnoreCase))
            {
                return length;
            }
        }

        return 0;
    }

    private static string AppendText(string accumulatedText, string newText)
    {
        if (string.IsNullOrWhiteSpace(accumulatedText))
        {
            return newText.Trim();
        }

        return $"{accumulatedText.TrimEnd()} {newText.TrimStart()}";
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex VttTagRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();
}