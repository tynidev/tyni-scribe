using System.Diagnostics;
using TranscriptSummary.Core.Services;
using YtChannel.Core.Data;

namespace YtChannel.Core.Services;

public sealed class ChannelSummaryOptions
{
    public int? MaxVideos { get; init; }
    public bool IncludeShorts { get; init; }
    public bool Overwrite { get; init; }
    public bool EstimateOnly { get; init; }
    public string Prompt { get; init; } = TranscriptSummaryDefaults.Prompt;
    public string Model { get; init; } = TranscriptSummaryDefaults.Model;
    public Uri Endpoint { get; init; } = new(TranscriptSummaryDefaults.Endpoint);
    public int ContextTokens { get; init; } = TranscriptSummaryDefaults.ContextTokens;
    public int ReservedOutputTokens { get; init; } = TranscriptSummaryDefaults.ReservedOutputTokens;
    public int MaxOutputTokens { get; init; } = TranscriptSummaryDefaults.MaxOutputTokens;
    public double CharsPerToken { get; init; } = TranscriptSummaryDefaults.CharsPerToken;
    public int TimeoutSeconds { get; init; } = TranscriptSummaryDefaults.TimeoutSeconds;
    public TranscriptSummaryMode Mode { get; init; } = TranscriptSummaryMode.Hierarchical;
}

public sealed record ChannelSummaryResult(
    int Processed,
    int Succeeded,
    int Failed,
    int Skipped,
    TimeSpan Elapsed);

public sealed class ChannelSummaryProgress
{
    public required string VideoId { get; init; }
    public required string? Title { get; init; }
    public required int Position { get; init; }
    public required int Total { get; init; }
    public ChannelSummaryEventKind Kind { get; init; }
    public string? Stage { get; init; }
    public int? PassIndex { get; init; }
    public int? ItemIndex { get; init; }
    public int? ItemCount { get; init; }
    public long? ElapsedMs { get; init; }
    public string? ErrorCategory { get; init; }
    public bool EstimateOnly { get; init; }
}

public enum ChannelSummaryEventKind
{
    Started,
    Progress,
    Completed,
    Failed,
    Skipped,
}

public sealed class ChannelSummaryOrchestrator(
    ChannelRepository repository,
    ITranscriptSummaryService summaryService,
    ChannelSummaryPromptStore promptStore)
{
    public async Task<ChannelSummaryResult> SummarizeAsync(
        string? channelId,
        ChannelSummaryOptions options,
        Action<ChannelSummaryProgress>? onProgress,
        CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        var processed = 0;

        while (!options.MaxVideos.HasValue || processed < options.MaxVideos.Value)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidates = await repository.GetSummarizationCandidatesAsync(
                channelId,
                limit: 1,
                options.IncludeShorts,
                includeAlreadySummarized: options.Overwrite,
                cancellationToken);
            var candidate = candidates.FirstOrDefault();
            if (candidate is null)
            {
                break;
            }

            processed++;
            onProgress?.Invoke(new ChannelSummaryProgress
            {
                VideoId = candidate.VideoId,
                Title = candidate.Title,
                Position = processed,
                Total = options.MaxVideos ?? 0,
                Kind = ChannelSummaryEventKind.Started,
            });

            if (!options.EstimateOnly)
            {
                await repository.UpdateVideoSummaryStatusAsync(candidate.VideoId, ChannelSummaryStatuses.InProgress, cancellationToken);
            }

            var outcome = await SummarizeVideoAsync(candidate, options, processed, options.MaxVideos ?? 0, onProgress, cancellationToken);
            switch (outcome)
            {
                case ChannelSummaryOutcome.Succeeded:
                    succeeded++;
                    break;
                case ChannelSummaryOutcome.Failed:
                    failed++;
                    break;
                case ChannelSummaryOutcome.Skipped:
                    skipped++;
                    break;
            }
        }

        totalStopwatch.Stop();
        return new ChannelSummaryResult(
            Processed: processed,
            Succeeded: succeeded,
            Failed: failed,
            Skipped: skipped,
            Elapsed: totalStopwatch.Elapsed);
    }

    private async Task<ChannelSummaryOutcome> SummarizeVideoAsync(
        SummaryCandidateRecord candidate,
        ChannelSummaryOptions options,
        int position,
        int total,
        Action<ChannelSummaryProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        var videoStopwatch = Stopwatch.StartNew();
        var summaryPath = GetSummaryPath(candidate.TranscriptFilePath);

        try
        {
            var effectiveOptions = await ResolveOptionsAsync(options, candidate.TranscriptFilePath, cancellationToken);

            if (!File.Exists(candidate.TranscriptFilePath))
            {
                throw new FileNotFoundException("The transcript file was not found.");
            }

            if (File.Exists(summaryPath) && !effectiveOptions.Overwrite && !effectiveOptions.EstimateOnly)
            {
                videoStopwatch.Stop();
                await InsertExistingSummaryAsync(candidate, effectiveOptions, summaryPath, videoStopwatch.ElapsedMilliseconds, cancellationToken);
                await repository.UpdateVideoSummaryStatusAsync(candidate.VideoId, ChannelSummaryStatuses.Summarized, cancellationToken);
                onProgress?.Invoke(new ChannelSummaryProgress
                {
                    VideoId = candidate.VideoId,
                    Title = candidate.Title,
                    Position = position,
                    Total = total,
                    Kind = ChannelSummaryEventKind.Skipped,
                    ElapsedMs = videoStopwatch.ElapsedMilliseconds,
                    ErrorCategory = "summary-exists",
                });
                return ChannelSummaryOutcome.Skipped;
            }

            var progress = new Progress<SummaryProgress>(progress => onProgress?.Invoke(new ChannelSummaryProgress
            {
                VideoId = candidate.VideoId,
                Title = candidate.Title,
                Position = position,
                Total = total,
                Kind = ChannelSummaryEventKind.Progress,
                Stage = progress.Stage,
                PassIndex = progress.PassIndex,
                ItemIndex = progress.ItemIndex,
                ItemCount = progress.ItemCount,
            }));

            var result = await summaryService.SummarizeAsync(new TranscriptSummaryRequest(
                candidate.TranscriptFilePath,
                effectiveOptions.Prompt,
                effectiveOptions.Model,
                effectiveOptions.Endpoint,
                effectiveOptions.ContextTokens,
                effectiveOptions.ReservedOutputTokens,
                effectiveOptions.MaxOutputTokens,
                effectiveOptions.CharsPerToken,
                effectiveOptions.Mode,
                effectiveOptions.TimeoutSeconds,
                effectiveOptions.EstimateOnly,
                progress), cancellationToken);

            videoStopwatch.Stop();

            if (!effectiveOptions.EstimateOnly)
            {
                await WriteSummaryAtomicallyAsync(summaryPath, result.SummaryText ?? string.Empty, cancellationToken);
                await repository.InsertSummaryAsync(CreateSuccessRecord(candidate.VideoId, effectiveOptions, result, summaryPath, videoStopwatch.ElapsedMilliseconds), cancellationToken);
                await repository.UpdateVideoSummaryStatusAsync(candidate.VideoId, ChannelSummaryStatuses.Summarized, cancellationToken);
            }

            onProgress?.Invoke(new ChannelSummaryProgress
            {
                VideoId = candidate.VideoId,
                Title = candidate.Title,
                Position = position,
                Total = total,
                Kind = ChannelSummaryEventKind.Completed,
                ElapsedMs = videoStopwatch.ElapsedMilliseconds,
                EstimateOnly = effectiveOptions.EstimateOnly,
            });
            return ChannelSummaryOutcome.Succeeded;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            videoStopwatch.Stop();
            var errorCategory = CategorizeError(ex);
            if (!options.EstimateOnly)
            {
                await repository.InsertSummaryAsync(CreateFailureRecord(candidate.VideoId, options, errorCategory, ex), cancellationToken);
                await repository.UpdateVideoSummaryStatusAsync(candidate.VideoId, ChannelSummaryStatuses.Failed, cancellationToken);
            }

            onProgress?.Invoke(new ChannelSummaryProgress
            {
                VideoId = candidate.VideoId,
                Title = candidate.Title,
                Position = position,
                Total = total,
                Kind = ChannelSummaryEventKind.Failed,
                ElapsedMs = videoStopwatch.ElapsedMilliseconds,
                ErrorCategory = errorCategory,
            });
            return ChannelSummaryOutcome.Failed;
        }
    }

    private static string GetSummaryPath(string transcriptPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(transcriptPath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The transcript path does not have a parent directory.");
        }

        return Path.Combine(directory, "summary.md");
    }

    private async Task<ChannelSummaryOptions> ResolveOptionsAsync(
        ChannelSummaryOptions options,
        string transcriptPath,
        CancellationToken cancellationToken)
    {
        var settings = await promptStore.LoadForTranscriptAsync(transcriptPath, cancellationToken);
        var resolved = settings?.ApplyToDefaults(options) ?? options;
        ValidateOptions(resolved);
        return resolved;
    }

    private static void ValidateOptions(ChannelSummaryOptions options)
    {
        if (options.ContextTokens <= 0 || options.ReservedOutputTokens <= 0 || options.MaxOutputTokens <= 0 || options.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Channel summary token and timeout values must be greater than zero.");
        }

        if (options.ReservedOutputTokens >= options.ContextTokens)
        {
            throw new InvalidOperationException("Channel summary reserved output tokens must be lower than context tokens.");
        }

        if (options.CharsPerToken <= 0)
        {
            throw new InvalidOperationException("Channel summary chars-per-token must be greater than zero.");
        }
    }

    private static async Task WriteSummaryAtomicallyAsync(string summaryPath, string summaryText, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(summaryPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = summaryPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, summaryText, cancellationToken);
        if (File.Exists(summaryPath))
        {
            File.Delete(summaryPath);
        }

        File.Move(tempPath, summaryPath);
    }

    private async Task InsertExistingSummaryAsync(
        SummaryCandidateRecord candidate,
        ChannelSummaryOptions options,
        string summaryPath,
        long elapsedMilliseconds,
        CancellationToken cancellationToken)
    {
        await repository.InsertSummaryAsync(new SummaryRecord
        {
            VideoId = candidate.VideoId,
            SummaryFilePath = summaryPath,
            ModelId = options.Model,
            EndpointHost = options.Endpoint.Host,
            ContextTokens = options.ContextTokens,
            MaxOutputTokens = options.MaxOutputTokens,
            TotalDurationMs = elapsedMilliseconds,
            SummarizedAt = DateTimeOffset.UtcNow,
        }, cancellationToken);
    }

    private static SummaryRecord CreateSuccessRecord(
        string videoId,
        ChannelSummaryOptions options,
        TranscriptSummaryResult result,
        string summaryPath,
        long totalDurationMilliseconds)
    {
        return new SummaryRecord
        {
            VideoId = videoId,
            SummaryFilePath = summaryPath,
            ModelId = options.Model,
            EndpointHost = options.Endpoint.Host,
            ContextTokens = options.ContextTokens,
            MaxOutputTokens = options.MaxOutputTokens,
            EstimatedTranscriptTokens = result.EstimatedTranscriptTokens,
            ChunkCount = result.ChunkCount,
            MergePassCount = result.MergePassCount,
            LlmRequestCount = result.LlmRequestCount,
            TotalDurationMs = totalDurationMilliseconds,
            TotalLlmDurationMs = result.TotalLlmMilliseconds,
            SummarizedAt = DateTimeOffset.UtcNow,
        };
    }

    private static SummaryRecord CreateFailureRecord(
        string videoId,
        ChannelSummaryOptions options,
        string errorCategory,
        Exception exception)
    {
        return new SummaryRecord
        {
            VideoId = videoId,
            ModelId = options.Model,
            EndpointHost = options.Endpoint.Host,
            ContextTokens = options.ContextTokens,
            MaxOutputTokens = options.MaxOutputTokens,
            ErrorCategory = errorCategory,
            ErrorMessage = SanitizeErrorMessage(exception.Message),
        };
    }

    private static string SanitizeErrorMessage(string message)
    {
        return message.Length > 500 ? message[..500] : message;
    }

    private static string CategorizeError(Exception ex) => ex switch
    {
        FileNotFoundException => "transcript-not-found",
        DirectoryNotFoundException => "directory-not-found",
        TaskCanceledException => "timeout",
        TimeoutException => "timeout",
        UnauthorizedAccessException => "access-denied",
        IOException => "io-error",
        InvalidOperationException => "invalid-operation",
        _ => "summarization-error",
    };

    private enum ChannelSummaryOutcome
    {
        Succeeded,
        Failed,
        Skipped,
    }
}