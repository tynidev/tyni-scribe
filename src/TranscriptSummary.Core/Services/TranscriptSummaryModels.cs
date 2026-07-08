using System.Text.Json.Serialization;

namespace TranscriptSummary.Core.Services;

public enum TranscriptSummaryMode
{
    Hierarchical,
    SinglePass
}

public sealed record TranscriptSummaryRequest(
    string InputPath,
    string Prompt,
    string Model,
    Uri Endpoint,
    int ContextTokens,
    int ReservedOutputTokens,
    int MaxOutputTokens,
    double CharsPerToken,
    TranscriptSummaryMode Mode,
    int TimeoutSeconds,
    bool EstimateOnly,
    IProgress<SummaryProgress>? Progress = null);

public sealed record SummaryProgress(
    string Stage,
    int PassIndex,
    int ItemIndex,
    int ItemCount,
    string Status);

public sealed record TranscriptSummaryResult(
    string? SummaryText,
    int EstimatedTranscriptTokens,
    int ChunkCount,
    int MergePassCount,
    int LlmRequestCount,
    long TotalLlmMilliseconds,
    int? TotalPromptTokens,
    int? TotalCompletionTokens,
    int? TotalTokens,
    int EstimatedOutputTokens,
    double? PromptTokensPerSecond,
    double? CompletionTokensPerSecond,
    double? TotalTokensPerSecond,
    IReadOnlyList<SummaryPassMetric> Passes,
    IReadOnlyList<SummaryRequestMetric> Requests);

public sealed record SummaryPassMetric(
    int PassIndex,
    string Stage,
    int InputCount,
    int OutputCount,
    long Milliseconds);

public sealed record SummaryRequestMetric(
    int RequestIndex,
    int PassIndex,
    int? ChunkIndex,
    string Stage,
    int EstimatedInputTokens,
    int EstimatedOutputTokens,
    int OutputCharacters,
    long Milliseconds,
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens,
    double? PromptTokensPerSecond,
    double? CompletionTokensPerSecond,
    double? TotalTokensPerSecond);

public sealed record OpenAiSummaryRequest(
    Uri Endpoint,
    string Model,
    string SystemPrompt,
    string UserText,
    int MaxOutputTokens,
    int TimeoutSeconds);

public sealed record OpenAiSummaryResult(string Text, OpenAiTokenUsage? Usage);

public sealed record OpenAiTokenUsage(
    [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
    [property: JsonPropertyName("total_tokens")] int? TotalTokens);