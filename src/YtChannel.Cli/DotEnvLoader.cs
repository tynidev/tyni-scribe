namespace YtChannel.Cli;

/// <summary>
/// Loads a .env file from the current working directory (or repo root search)
/// into process environment variables before the app reads them.
/// Only sets variables that are NOT already set — real env vars always win.
/// </summary>
internal static class DotEnvLoader
{
    internal static void Load(string fileName = ".env")
    {
        var path = FindEnvFile(fileName);
        if (path is null)
        {
            return;
        }

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();

            // Skip blank lines and comments.
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var eqIndex = line.IndexOf('=', StringComparison.Ordinal);
            if (eqIndex <= 0)
            {
                continue;
            }

            var key   = line[..eqIndex].Trim();
            var value = line[(eqIndex + 1)..].Trim();

            // Strip optional surrounding quotes.
            if (value.Length >= 2
                && ((value[0] == '"'  && value[^1] == '"')
                 || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            // Real environment variables take precedence.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static string? FindEnvFile(string fileName)
    {
        // Check CWD first, then walk up to repo root (up to 5 levels).
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 5; i++)
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir)
            {
                break;
            }

            dir = parent;
        }

        return null;
    }
}
