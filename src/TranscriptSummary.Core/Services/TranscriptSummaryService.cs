using System.Diagnostics;
using System.Text;
using YtScribe.Core.Model;

namespace TranscriptSummary.Core.Services;

public sealed class TranscriptSummaryService(
    ITranscriptArtifactReader transcriptReader,
    IOpenAiSummaryClient summaryClient) : ITranscriptSummaryService
{
    private const int MinimumInputBudgetTokens = 256;

    public async Task<TranscriptSummaryResult> SummarizeAsync(TranscriptSummaryRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var transcript = await transcriptReader.ReadAsync(request.InputPath, cancellationToken);
        var transcriptText = JoinSegments(transcript.Segments);
        var estimatedTranscriptTokens = EstimateTokens(transcriptText, request.CharsPerToken);
        var inputBudgetTokens = request.ContextTokens - request.ReservedOutputTokens;
        if (inputBudgetTokens < MinimumInputBudgetTokens)
        {
            throw new InvalidOperationException("The configured context budget leaves too little room for transcript text.");
        }

        if (request.Mode == TranscriptSummaryMode.SinglePass && estimatedTranscriptTokens > inputBudgetTokens)
        {
            throw new InvalidOperationException("The transcript does not fit in the configured single-pass context budget.");
        }

        var chunks = CreateChunks(transcript.Segments, request.CharsPerToken, inputBudgetTokens);
        var requests = new List<SummaryRequestMetric>();
        var passes = new List<SummaryPassMetric>();
        if (request.EstimateOnly)
        {
            var plannedMergePasses = EstimateMergePasses(chunks.Count, inputBudgetTokens, request.CharsPerToken);
            return new TranscriptSummaryResult(
                null,
                estimatedTranscriptTokens,
                chunks.Count,
                plannedMergePasses,
                0,
                0,
                passes,
                requests);
        }

        var requestIndex = 0;
        var summaries = new List<string>();
        var chunkPassStopwatch = Stopwatch.StartNew();
        for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
        {
            var chunk = chunks[chunkIndex];
            var prompt = CreateChunkPrompt(request.Prompt, chunkIndex + 1, chunks.Count, transcript);
            request.Progress?.Report(new SummaryProgress("chunk", 0, chunkIndex + 1, chunks.Count, "started"));
            var summary = await RunSummaryRequestAsync(
                request,
                prompt,
                chunk,
                0,
                chunkIndex + 1,
                "chunk",
                requestIndex++,
                requests,
                cancellationToken);
            summaries.Add(summary);
            request.Progress?.Report(new SummaryProgress("chunk", 0, chunkIndex + 1, chunks.Count, "completed"));
        }

        chunkPassStopwatch.Stop();
        passes.Add(new SummaryPassMetric(0, "chunk", chunks.Count, summaries.Count, ToMilliseconds(chunkPassStopwatch.Elapsed)));

        var mergePassIndex = 0;
        while (summaries.Count > 1 || EstimateTokens(summaries[0], request.CharsPerToken) > inputBudgetTokens)
        {
            mergePassIndex++;
            var mergeInputs = CreateTextChunks(summaries, request.CharsPerToken, inputBudgetTokens);
            var mergedSummaries = new List<string>();
            var mergeStopwatch = Stopwatch.StartNew();
            for (var mergeIndex = 0; mergeIndex < mergeInputs.Count; mergeIndex++)
            {
                var prompt = CreateMergePrompt(request.Prompt, mergeIndex + 1, mergeInputs.Count);
                request.Progress?.Report(new SummaryProgress("merge", mergePassIndex, mergeIndex + 1, mergeInputs.Count, "started"));
                var summary = await RunSummaryRequestAsync(
                    request,
                    prompt,
                    mergeInputs[mergeIndex],
                    mergePassIndex,
                    mergeIndex + 1,
                    "merge",
                    requestIndex++,
                    requests,
                    cancellationToken);
                mergedSummaries.Add(summary);
                request.Progress?.Report(new SummaryProgress("merge", mergePassIndex, mergeIndex + 1, mergeInputs.Count, "completed"));
            }

            mergeStopwatch.Stop();
            passes.Add(new SummaryPassMetric(mergePassIndex, "merge", summaries.Count, mergedSummaries.Count, ToMilliseconds(mergeStopwatch.Elapsed)));
            summaries = mergedSummaries;
        }

        var totalLlmMilliseconds = requests.Sum(metric => metric.Milliseconds);
        return new TranscriptSummaryResult(
            summaries[0],
            estimatedTranscriptTokens,
            chunks.Count,
            mergePassIndex,
            requests.Count,
            totalLlmMilliseconds,
            passes,
            requests);
    }

    private async Task<string> RunSummaryRequestAsync(
        TranscriptSummaryRequest request,
        string prompt,
        string text,
        int passIndex,
        int chunkIndex,
        string stage,
        int requestIndex,
        List<SummaryRequestMetric> metrics,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await summaryClient.SummarizeAsync(
            new OpenAiSummaryRequest(
                request.Endpoint,
                request.Model,
                prompt,
                text,
                request.MaxOutputTokens,
                request.TimeoutSeconds),
            cancellationToken);
        stopwatch.Stop();

        metrics.Add(new SummaryRequestMetric(
            requestIndex,
            passIndex,
            chunkIndex,
            stage,
            EstimateTokens(text, request.CharsPerToken),
            result.Text.Length,
            ToMilliseconds(stopwatch.Elapsed)));

        return result.Text;
    }

    private static void ValidateRequest(TranscriptSummaryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath))
        {
            throw new InvalidOperationException("An input transcript path is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new InvalidOperationException("A prompt is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new InvalidOperationException("A model is required.");
        }

        if (request.ContextTokens <= 0 || request.ReservedOutputTokens <= 0 || request.MaxOutputTokens <= 0 || request.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Token and timeout values must be greater than zero.");
        }

        if (request.CharsPerToken <= 0)
        {
            throw new InvalidOperationException("The chars-per-token value must be greater than zero.");
        }
    }

    private static string JoinSegments(IReadOnlyList<TranscriptSegment> segments)
    {
        return string.Join(Environment.NewLine, segments.Select(segment => segment.Text).Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static List<string> CreateChunks(IReadOnlyList<TranscriptSegment> segments, double charsPerToken, int inputBudgetTokens)
    {
        var segmentTexts = segments
            .Select(segment => segment.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text.Trim())
            .ToList();

        return CreateTextChunks(segmentTexts, charsPerToken, inputBudgetTokens);
    }

    private static List<string> CreateTextChunks(IReadOnlyList<string> texts, double charsPerToken, int inputBudgetTokens)
    {
        var chunks = new List<string>();
        var builder = new StringBuilder();

        foreach (var text in texts)
        {
            if (builder.Length == 0)
            {
                builder.Append(text);
                continue;
            }

            var candidate = builder.Length + Environment.NewLine.Length + text.Length;
            if (EstimateTokens(candidate, charsPerToken) > inputBudgetTokens)
            {
                chunks.Add(builder.ToString());
                builder.Clear();
                builder.Append(text);
            }
            else
            {
                builder.AppendLine();
                builder.Append(text);
            }
        }

        if (builder.Length > 0)
        {
            chunks.Add(builder.ToString());
        }

        return chunks.Count == 0 ? new List<string> { string.Empty } : chunks;
    }

    private static int EstimateMergePasses(int chunkCount, int inputBudgetTokens, double charsPerToken)
    {
        if (chunkCount <= 1)
        {
            return 0;
        }

        var estimatedSummaryChars = chunkCount * 1200;
        var mergeInputCapacity = Math.Max(1, inputBudgetTokens * (int)Math.Floor(charsPerToken));
        var passes = 0;
        var currentCount = chunkCount;
        while (currentCount > 1)
        {
            var summariesPerPass = Math.Max(2, mergeInputCapacity / 1200);
            currentCount = (int)Math.Ceiling(currentCount / (double)summariesPerPass);
            passes++;
            if (estimatedSummaryChars <= mergeInputCapacity)
            {
                break;
            }

            estimatedSummaryChars = currentCount * 1200;
        }

        return passes;
    }

    private static string CreateChunkPrompt(string basePrompt, int chunkIndex, int chunkCount, TranscriptDocument transcript)
    {
        var title = string.IsNullOrWhiteSpace(transcript.Title) ? transcript.VideoId : transcript.Title;
        return $"{basePrompt}{Environment.NewLine}{Environment.NewLine}This is part {chunkIndex} of {chunkCount} from transcript '{title}'. Summarize this part only, preserving details needed for a later whole-transcript synthesis.";
    }

    private static string CreateMergePrompt(string basePrompt, int chunkIndex, int chunkCount)
    {
        return $"{basePrompt}{Environment.NewLine}{Environment.NewLine}You are combining partial transcript summaries. This is summary group {chunkIndex} of {chunkCount}. Return a coherent higher-level summary without mentioning chunk boundaries.";
    }

    private static int EstimateTokens(string text, double charsPerToken)
    {
        return EstimateTokens(text.Length, charsPerToken);
    }

    private static int EstimateTokens(int characters, double charsPerToken)
    {
        return (int)Math.Ceiling(characters / charsPerToken);
    }

    private static long ToMilliseconds(TimeSpan duration)
    {
        return (long)Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero);
    }
}