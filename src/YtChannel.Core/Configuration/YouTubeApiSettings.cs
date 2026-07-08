namespace YtChannel.Core.Configuration;

/// <summary>
/// Configuration for the YouTube Data API v3 client.
/// Load from appsettings or environment variable YOUTUBE_API_KEY.
/// </summary>
public sealed class YouTubeApiSettings
{
    /// <summary>
    /// YouTube Data API v3 key.
    /// Can also be supplied via the YOUTUBE_API_KEY environment variable.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Number of results per API request page (max 50).</summary>
    public int MaxResultsPerPage { get; set; } = 50;

    /// <summary>Returns the API key, preferring the environment variable over the config value.</summary>
    public string? ResolvedApiKey =>
        Environment.GetEnvironmentVariable("YOUTUBE_API_KEY")
        ?? ApiKey;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ResolvedApiKey);
}
