namespace TranscriptSummary.Core.Services;

public interface ITranscriptSummaryService
{
    Task<TranscriptSummaryResult> SummarizeAsync(TranscriptSummaryRequest request, CancellationToken cancellationToken = default);
}