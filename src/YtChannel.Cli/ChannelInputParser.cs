namespace YtChannel.Cli;

internal static class ChannelInputParser
{
    internal static bool TryParseChannels(
        IReadOnlyList<string> positionalChannels,
        string? channelsFile,
        out string[] channels,
        out string? error)
    {
        channels = Array.Empty<string>();
        error = null;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        static void AddChannel(string? raw, HashSet<string> seenSet, List<string> output)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var value = raw.Trim();
            if (seenSet.Add(value))
            {
                output.Add(value);
            }
        }

        foreach (var channel in positionalChannels)
        {
            AddChannel(channel, seen, ordered);
        }

        if (!string.IsNullOrWhiteSpace(channelsFile))
        {
            try
            {
                foreach (var line in File.ReadLines(channelsFile))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    AddChannel(trimmed, seen, ordered);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"Failed to read channels file '{channelsFile}': {ex.Message}";
                return false;
            }
        }

        if (ordered.Count == 0)
        {
            error = "At least one channel must be provided via positional args or --channels-file.";
            return false;
        }

        channels = ordered.ToArray();
        return true;
    }
}
