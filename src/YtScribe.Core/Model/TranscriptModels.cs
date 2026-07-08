namespace YtScribe.Core.Model;

public sealed record TranscriptSegment(
    double? StartSeconds,
    double? EndSeconds,
    string Text);

public sealed record TranscriptDocument(
    int SchemaVersion,
    string SourceUrl,
    string VideoId,
    string? Title,
    string TranscriptOrigin,
    string TimingKind,
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<TranscriptSegment> Segments);

public sealed record VideoMetadataDocument(
    int SchemaVersion,
    string SourceUrl,
    string VideoId,
    string? Title,
    string? Channel,
    string? Uploader,
    string? UploadDate,
    double? DurationSeconds,
    string? WebpageUrl,
    string? ThumbnailUrl,
    string? CaptionLanguage,
    string TranscriptOrigin,
    DateTimeOffset IngestedUtc,
    string Status,
    string? TranscriptionProviderId,
    IReadOnlyDictionary<string, string>? TranscriptionSettings,
    long? MetadataMilliseconds,
    long? CaptionMilliseconds,
    long? AudioDownloadMilliseconds,
    long? MediaPreparationMilliseconds,
    long? AudioProcessingMilliseconds,
    long? TranscriptionMilliseconds);

public sealed record YouTubeVideoMetadata(
    string Id,
    string SourceUrl,
    string? Title,
    string? Channel,
    string? Uploader,
    string? UploadDate,
    double? DurationSeconds,
    string? WebpageUrl,
    string? ThumbnailUrl);

public sealed record ExportedTranscriptArtifacts(
    string OutputDirectory,
    string MetadataPath,
    string TranscriptJsonPath,
    string? TranscriptVttPath,
    string? TranscriptTextPath);

public sealed record YouTubeExportResult(
    string Status,
    string TranscriptOrigin,
    string OutputDirectory,
    string MetadataPath,
    string TranscriptJsonPath,
    string? TranscriptVttPath,
    string? TranscriptTextPath,
    string? ErrorCategory);

public sealed record YouTubeExportMetrics(
    string Status,
    string? ErrorCategory,
    string SourceUrl,
    string? VideoId,
    string? OutputDirectory,
    string? TranscriptOrigin,
    string? TranscriptionProviderId,
    IReadOnlyDictionary<string, string>? EffectiveSettings,
    long? MetadataMilliseconds,
    long? CaptionMilliseconds,
    long? AudioDownloadMilliseconds,
    long? MediaPreparationMilliseconds,
    long? AudioProcessingMilliseconds,
    long? TranscriptionMilliseconds,
    long TotalMilliseconds);