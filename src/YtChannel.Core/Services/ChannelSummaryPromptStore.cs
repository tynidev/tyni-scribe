using System.Text.Json;
using TranscriptSummary.Core.Services;

namespace YtChannel.Core.Services;

public sealed class ChannelSummaryPromptStore
{
    public const string FileName = "channel.summary.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async Task<ChannelSummarySettings?> LoadForTranscriptAsync(
        string transcriptPath,
        CancellationToken cancellationToken = default)
    {
        var configPath = GetConfigPathForTranscript(transcriptPath);
        if (configPath is null || !File.Exists(configPath))
        {
            return null;
        }

        await using var stream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        var settings = await JsonSerializer.DeserializeAsync<ChannelSummarySettings>(stream, JsonOptions, cancellationToken);
        return settings;
    }

    public static string? GetConfigPathForTranscript(string transcriptPath)
    {
        var videoDirectory = Path.GetDirectoryName(Path.GetFullPath(transcriptPath));
        if (string.IsNullOrWhiteSpace(videoDirectory))
        {
            return null;
        }

        var channelDirectory = Directory.GetParent(videoDirectory)?.FullName;
        return string.IsNullOrWhiteSpace(channelDirectory)
            ? null
            : Path.Combine(channelDirectory, FileName);
    }
}

public sealed class ChannelSummarySettings
{
    public int SchemaVersion { get; set; } = 1;

    public string? SummaryPrompt { get; set; }

    public string? Model { get; set; }

    public string? Endpoint { get; set; }

    public int? ContextTokens { get; set; }

    public int? ReservedOutputTokens { get; set; }

    public int? MaxOutputTokens { get; set; }

    public double? CharsPerToken { get; set; }

    public int? TimeoutSeconds { get; set; }

    public ChannelSummaryOptions ApplyToDefaults(ChannelSummaryOptions options)
    {
        var endpoint = options.Endpoint;
        if (IsDefaultEndpoint(options.Endpoint) && !string.IsNullOrWhiteSpace(Endpoint))
        {
            if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out endpoint!))
            {
                throw new InvalidOperationException("The channel summary endpoint must be an absolute URL.");
            }
        }

        return new ChannelSummaryOptions
        {
            MaxVideos = options.MaxVideos,
            IncludeShorts = options.IncludeShorts,
            Overwrite = options.Overwrite,
            EstimateOnly = options.EstimateOnly,
            Prompt = IsDefault(options.Prompt, TranscriptSummaryDefaults.Prompt) && !string.IsNullOrWhiteSpace(SummaryPrompt)
                ? SummaryPrompt
                : options.Prompt,
            Model = IsDefault(options.Model, TranscriptSummaryDefaults.Model) && !string.IsNullOrWhiteSpace(Model)
                ? Model
                : options.Model,
            Endpoint = endpoint,
            ContextTokens = IsDefault(options.ContextTokens, TranscriptSummaryDefaults.ContextTokens) && ContextTokens.HasValue
                ? ContextTokens.Value
                : options.ContextTokens,
            ReservedOutputTokens = IsDefault(options.ReservedOutputTokens, TranscriptSummaryDefaults.ReservedOutputTokens) && ReservedOutputTokens.HasValue
                ? ReservedOutputTokens.Value
                : options.ReservedOutputTokens,
            MaxOutputTokens = IsDefault(options.MaxOutputTokens, TranscriptSummaryDefaults.MaxOutputTokens) && MaxOutputTokens.HasValue
                ? MaxOutputTokens.Value
                : options.MaxOutputTokens,
            CharsPerToken = IsDefault(options.CharsPerToken, TranscriptSummaryDefaults.CharsPerToken) && CharsPerToken.HasValue
                ? CharsPerToken.Value
                : options.CharsPerToken,
            TimeoutSeconds = IsDefault(options.TimeoutSeconds, TranscriptSummaryDefaults.TimeoutSeconds) && TimeoutSeconds.HasValue
                ? TimeoutSeconds.Value
                : options.TimeoutSeconds,
            Mode = options.Mode,
        };
    }

    private static bool IsDefault(string value, string defaultValue)
    {
        return string.Equals(value, defaultValue, StringComparison.Ordinal);
    }

    private static bool IsDefault(int value, int defaultValue)
    {
        return value == defaultValue;
    }

    private static bool IsDefault(double value, double defaultValue)
    {
        return Math.Abs(value - defaultValue) < 0.0001;
    }

    private static bool IsDefaultEndpoint(Uri value)
    {
        return string.Equals(value.AbsoluteUri.TrimEnd('/'), TranscriptSummaryDefaults.Endpoint.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }
}