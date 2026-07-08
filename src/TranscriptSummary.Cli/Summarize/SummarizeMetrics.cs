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
    int? TotalPromptTokens,
    int? TotalCompletionTokens,
    int? TotalTokens,
    int? EstimatedOutputTokens,
    double? PromptTokensPerSecond,
    double? CompletionTokensPerSecond,
    double? TotalTokensPerSecond,
    IReadOnlyList<SummaryPassMetric>? Passes,
    IReadOnlyList<SummaryRequestMetric>? Requests,
    long TotalMilliseconds);