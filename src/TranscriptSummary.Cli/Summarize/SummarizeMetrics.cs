using TranscriptSummary.Core.Services;

namespace TranscriptSummary.Cli.Summarize;

internal sealed record SummarizeMetrics(
    string Status,
    string? ErrorCategory,
    string? InputFileName,
    string? InputParentDirectoryName,
    string? OutputPath,
    string Model,
    string? EndpointHost,
    int ContextTokens,
    int ReservedOutputTokens,
    int MaxOutputTokens,
    double CharsPerToken,
    int? EstimatedTranscriptTokens,
    string Mode,
    bool EstimateOnly,
    int? ChunkCount,
    int? MergePassCount,
    int? LlmRequestCount,
    long? TotalLlmMilliseconds,
    IReadOnlyList<SummaryPassMetric>? Passes,
    IReadOnlyList<SummaryRequestMetric>? Requests,
    long TotalMilliseconds);