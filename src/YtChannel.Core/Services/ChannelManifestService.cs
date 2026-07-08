using System.Diagnostics;
using System.Text.Json;
using YtChannel.Core.Data;

namespace YtChannel.Core.Services;

public sealed class ChannelManifestService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly ChannelRepository _repository;

    public ChannelManifestService(ChannelRepository repository)
    {
        _repository = repository;
    }

    public async Task<ChannelManifestWriteResult> WriteAsync(
        string outputRootDirectory,
        ChannelSyncResult syncResult,
        CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var channelOutputDirectory = GetChannelOutputDirectory(outputRootDirectory, syncResult.ChannelId);
        Directory.CreateDirectory(channelOutputDirectory);

        var queryStopwatch = Stopwatch.StartNew();
        var videos = await _repository.GetChannelVideoIndexAsync(syncResult.ChannelId, cancellationToken);
        queryStopwatch.Stop();

        var totalShorts = videos.Count(v => v.IsShort);
        var totalTranscribed = videos.Count(v => string.Equals(v.TranscriptStatus, "completed", StringComparison.OrdinalIgnoreCase));
        var totalFailed = videos.Count(v => string.Equals(v.TranscriptStatus, "failed", StringComparison.OrdinalIgnoreCase));
        var totalPending = videos.Count(v => string.Equals(v.TranscriptStatus, "pending", StringComparison.OrdinalIgnoreCase));
        var mostRecentVideoDate = videos
            .Select(v => ParseDateTimeOffsetOrNull(v.PublishedAt))
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .DefaultIfEmpty()
            .Max();
        var mostRecentTranscribedVideoDate = videos
            .Where(v => string.Equals(v.TranscriptStatus, "completed", StringComparison.OrdinalIgnoreCase))
            .Select(v => ParseDateTimeOffsetOrNull(v.PublishedAt))
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .DefaultIfEmpty()
            .Max();

        var metadata = new ChannelMetadataDocument(
            SchemaVersion: 1,
            ChannelId: syncResult.ChannelId,
            ChannelUrl: syncResult.ChannelUrl,
            ChannelName: syncResult.ChannelName,
            Description: syncResult.Description,
            ThumbnailUrl: syncResult.ThumbnailUrl,
            SyncedAt: syncResult.SyncedAt,
            TotalVideos: videos.Count,
            TotalVideosInChannel: syncResult.TotalVideosInChannel,
            TotalShorts: totalShorts,
            TotalTranscribed: totalTranscribed,
            TotalFailed: totalFailed,
            TotalPending: totalPending,
            MostRecentVideoPublishedAt: mostRecentVideoDate == default ? null : mostRecentVideoDate,
            MostRecentTranscribedVideoPublishedAt: mostRecentTranscribedVideoDate == default ? null : mostRecentTranscribedVideoDate,
            UpdatedAt: DateTimeOffset.UtcNow,
            Videos: videos.Select(v => new ChannelVideoMetadataDocument(
                VideoId: v.VideoId,
                Url: $"https://www.youtube.com/watch?v={v.VideoId}",
                Title: v.Title,
                DurationSeconds: v.DurationSeconds,
                IsShort: v.IsShort,
                PublishedAt: v.PublishedAt,
                TranscriptStatus: v.TranscriptStatus,
                TranscriptOrigin: v.TranscriptOrigin,
                TranscriptJsonPath: ToRelativePath(channelOutputDirectory, v.TranscriptFilePath),
                TranscribedAt: v.TranscribedAt)).ToList());

        var path = Path.Combine(channelOutputDirectory, "channel.json");
        var writeStopwatch = Stopwatch.StartNew();
        await using (var stream = File.Create(path))
        {
            await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken);
        }
        writeStopwatch.Stop();
        totalStopwatch.Stop();

        var fileSizeBytes = new FileInfo(path).Length;
        return new ChannelManifestWriteResult(
            OutputPath: path,
            VideoCount: videos.Count,
            FileSizeBytes: fileSizeBytes,
            QueryDurationMs: queryStopwatch.ElapsedMilliseconds,
            WriteDurationMs: writeStopwatch.ElapsedMilliseconds,
            TotalDurationMs: totalStopwatch.ElapsedMilliseconds,
            IncludesVideoIndex: true);
    }

    public static string GetChannelOutputDirectory(string rootDirectory, string channelId)
    {
        var channelIdSegment = SanitizePathSegment(channelId);
        return Path.Combine(Path.GetFullPath(rootDirectory), channelIdSegment);
    }

    private static DateTimeOffset? ParseDateTimeOffsetOrNull(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static string? ToRelativePath(string rootDirectory, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetRelativePath(rootDirectory, path).Replace(Path.DirectorySeparatorChar, '/');
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = new List<char>(value.Length);
        var previousWasSeparator = false;

        foreach (var c in value.Trim())
        {
            if (invalidChars.Contains(c) || char.IsWhiteSpace(c) || c is '/' or '\\')
            {
                if (!previousWasSeparator)
                {
                    chars.Add('-');
                    previousWasSeparator = true;
                }

                continue;
            }

            chars.Add(c);
            previousWasSeparator = false;
        }

        var sanitized = new string(chars.ToArray()).Trim('-', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? "channel" : sanitized;
    }

    private sealed record ChannelMetadataDocument(
        int SchemaVersion,
        string ChannelId,
        string ChannelUrl,
        string? ChannelName,
        string? Description,
        string? ThumbnailUrl,
        DateTimeOffset SyncedAt,
        int TotalVideos,
        int TotalVideosInChannel,
        int TotalShorts,
        int TotalTranscribed,
        int TotalFailed,
        int TotalPending,
        DateTimeOffset? MostRecentVideoPublishedAt,
        DateTimeOffset? MostRecentTranscribedVideoPublishedAt,
        DateTimeOffset UpdatedAt,
        IReadOnlyList<ChannelVideoMetadataDocument> Videos);

    private sealed record ChannelVideoMetadataDocument(
        string VideoId,
        string Url,
        string? Title,
        double? DurationSeconds,
        bool IsShort,
        string? PublishedAt,
        string TranscriptStatus,
        string? TranscriptOrigin,
        string? TranscriptJsonPath,
        DateTimeOffset? TranscribedAt);
}

public sealed record ChannelManifestWriteResult(
    string OutputPath,
    int VideoCount,
    long FileSizeBytes,
    long QueryDurationMs,
    long WriteDurationMs,
    long TotalDurationMs,
    bool IncludesVideoIndex);