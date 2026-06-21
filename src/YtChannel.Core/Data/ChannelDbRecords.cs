namespace YtChannel.Core.Data;

/// <summary>Row in the Channels table.</summary>
public sealed class ChannelRecord
{
    public string ChannelId { get; set; } = "";
    public string ChannelUrl { get; set; } = "";
    public string? ChannelName { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Row in the Videos table.</summary>
public sealed class VideoRecord
{
    public string VideoId { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public string? Title { get; set; }
    public double? DurationSeconds { get; set; }
    public bool? IsShortsPlaylistVideo { get; set; }
    public string? PublishedAt { get; set; }
    /// <summary>pending | completed | failed</summary>
    public string TranscriptStatus { get; set; } = "pending";
    /// <summary>pending | summarized | failed</summary>
    public string SummaryStatus { get; set; } = ChannelSummaryStatuses.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class ChannelSummaryStatuses
{
    public const string Pending = "pending";
    public const string Summarized = "summarized";
    public const string Failed = "failed";
}

/// <summary>Video metadata and latest successful transcript pointer for channel manifests.</summary>
public sealed class ChannelVideoIndexRecord
{
    public string VideoId { get; set; } = "";
    public string? Title { get; set; }
    public double? DurationSeconds { get; set; }
    public bool IsShort { get; set; }
    public string? PublishedAt { get; set; }
    public string TranscriptStatus { get; set; } = "pending";
    public string SummaryStatus { get; set; } = ChannelSummaryStatuses.Pending;
    public string? TranscriptOrigin { get; set; }
    public string? TranscriptFilePath { get; set; }
    public DateTimeOffset? TranscribedAt { get; set; }
    public string? SummaryFilePath { get; set; }
    public DateTimeOffset? SummarizedAt { get; set; }
}

/// <summary>Row in the Transcriptions table.</summary>
public sealed class TranscriptionRecord
{
    public long Id { get; set; }
    public string VideoId { get; set; } = "";
    /// <summary>captions | audio-transcription</summary>
    public string? TranscriptOrigin { get; set; }
    public string? TranscriptFilePath { get; set; }
    public string? ProviderId { get; set; }
    public string? ModelId { get; set; }
    public long? TotalDurationMs { get; set; }
    public long? CaptionDownloadTimeMs { get; set; }
    public DateTimeOffset? SucceededAt { get; set; }
    public string? ErrorCategory { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>Row in the Summaries table.</summary>
public sealed class SummaryRecord
{
    public long Id { get; set; }
    public string VideoId { get; set; } = "";
    public string? SummaryFilePath { get; set; }
    public string? ModelId { get; set; }
    public string? EndpointHost { get; set; }
    public int? ContextTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
    public int? EstimatedTranscriptTokens { get; set; }
    public int? ChunkCount { get; set; }
    public int? MergePassCount { get; set; }
    public int? LlmRequestCount { get; set; }
    public long? TotalDurationMs { get; set; }
    public long? TotalLlmDurationMs { get; set; }
    public DateTimeOffset? SummarizedAt { get; set; }
    public string? ErrorCategory { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>Video and transcript pointer selected for summarization.</summary>
public sealed class SummaryCandidateRecord
{
    public string VideoId { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public string? Title { get; set; }
    public double? DurationSeconds { get; set; }
    public bool? IsShortsPlaylistVideo { get; set; }
    public string SummaryStatus { get; set; } = ChannelSummaryStatuses.Pending;
    public string TranscriptFilePath { get; set; } = "";
}

/// <summary>Row in the RateLimitMetrics table.</summary>
public sealed class RateLimitMetricRecord
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public long CaptionDownloadDelayMs { get; set; }
    public long? CaptionDownloadDurationMs { get; set; }
    /// <summary>null = success, 429 = throttled, etc.</summary>
    public int? HttpStatus { get; set; }
    public long BackoffAppliedMs { get; set; }
}
