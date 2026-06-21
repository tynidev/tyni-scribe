namespace TranscriptSummary.Core.Services;

public interface IOpenAiSummaryClient
{
    Task<OpenAiSummaryResult> SummarizeAsync(OpenAiSummaryRequest request, CancellationToken cancellationToken = default);
}