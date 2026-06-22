using System.Diagnostics;
using Tts.Core.Configuration;
using YtChannel.Core.Data;
using YtScribe.Core.Model;
using YtScribe.Core.Services;

namespace YtChannel.Core.Services;

/// <summary>
/// Options for a yt-channel process run.
/// </summary>
public sealed class ProcessOptions
{
    /// <summary>Root directory where per-video transcript directories are written.</summary>
    public string OutputDirectory { get; init; } = string.Empty;

    /// <summary>Maximum number of pending videos to process. Null means all.</summary>
    public int? MaxVideos { get; init; }

    /// <summary>Caption language preference passed to yt-scribe.</summary>
    public string CaptionLanguage { get; init; } = "en";

    /// <summary>Force audio transcription even if captions are available.</summary>
    public bool ForceAudio { get; init; }

    /// <summary>App settings passed through to the transcription provider.</summary>
    public AppSettings? Settings { get; init; }

    /// <summary>Transcription provider ID override.</summary>
    public string? ProviderId { get; init; }

    /// <summary>
    /// When false (default), videos are skipped as Shorts only when duration <= 180 seconds
    /// and they appear in the channel's UUSH Shorts playlist.
    /// When true, Shorts are included in processing.
    /// </summary>
    public bool IncludeShorts { get; init; }

    /// <summary>Maximum retries per video on rate-limit errors before marking failed.</summary>
    public int MaxRateLimitRetries { get; init; } = 3;
}

/// <summary>
/// Summary result returned after <see cref="ChannelOrchestrator.ProcessChannelAsync"/> completes.
/// </summary>
public sealed record ProcessingResult(
    int Processed,
    int Succeeded,
    int Failed,
    int Skipped,
    TimeSpan Elapsed,
    ChannelManifestWriteResult? InitialManifestWrite,
    ChannelManifestWriteResult? FinalManifestWrite);

public sealed record TranscribeOneResult(
    bool HadWork,
    bool Succeeded,
    bool Failed,
    bool Skipped,
    TimeSpan Elapsed);

/// <summary>
/// Orchestrates serial transcription of all pending videos in a channel.
/// Applies adaptive rate-limit delays between caption download attempts.
/// </summary>
public sealed class ChannelOrchestrator
{
    private readonly ChannelSyncService _syncService;
    private readonly ChannelManifestService _manifestService;
    private readonly RateLimitTracker _rateLimitTracker;
    private readonly IYouTubeExportService _exportService;
    private readonly ChannelRepository _repository;
    private readonly ChannelRetentionService _retentionService;

    public ChannelOrchestrator(
        ChannelSyncService syncService,
        ChannelManifestService manifestService,
        RateLimitTracker rateLimitTracker,
        IYouTubeExportService exportService,
        ChannelRepository repository,
        ChannelRetentionService retentionService)
    {
        _syncService = syncService;
        _manifestService = manifestService;
        _rateLimitTracker = rateLimitTracker;
        _exportService = exportService;
        _repository = repository;
        _retentionService = retentionService;
    }

    /// <summary>
    /// Syncs the channel, then serially transcribes all pending videos.
    /// Progress events are emitted via <paramref name="onProgress"/> (may be null).
    /// </summary>
    public async Task<ProcessingResult> ProcessChannelAsync(
        string channelUrl,
        ProcessOptions options,
        Action<ProcessingProgress>? onProgress,
        CancellationToken cancellationToken = default)
    {
        // 1. Sync channel to discover any new videos.
        var syncResult = await _syncService.SyncChannelAsync(channelUrl, cancellationToken);
        await _retentionService.PruneAsync(syncResult.ChannelId, options.OutputDirectory, cancellationToken);
        var channelOutputDirectory = ChannelManifestService.GetChannelOutputDirectory(
            options.OutputDirectory,
            syncResult.ChannelId);
        var initialManifestWrite = await _manifestService.WriteAsync(options.OutputDirectory, syncResult, cancellationToken);

        await _rateLimitTracker.WarmUpAsync(cancellationToken);

        // 2. Fetch pending videos from DB.
        var pendingVideos = await _repository.GetPendingVideosAsync(
            syncResult.ChannelId, options.MaxVideos, options.IncludeShorts, cancellationToken);

        var totalStopwatch = Stopwatch.StartNew();
        int succeeded = 0, failed = 0, skipped = 0;

        for (int i = 0; i < pendingVideos.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var video = pendingVideos[i];
            var position = i + 1;

            onProgress?.Invoke(new ProcessingProgress
            {
                VideoId  = video.VideoId,
                Title    = video.Title,
                Position = position,
                Total    = pendingVideos.Count,
                Kind     = ProcessingEventKind.Started,
            });

            var (outcome, origin, errorCategory) = await ProcessVideoWithRetryAsync(
                video, options, channelOutputDirectory, position, pendingVideos.Count, onProgress, cancellationToken);

            switch (outcome)
            {
                case VideoOutcome.Succeeded:
                    succeeded++;
                    break;
                case VideoOutcome.Failed:
                    failed++;
                    break;
                case VideoOutcome.Skipped:
                    skipped++;
                    break;
            }
        }

        var finalManifestWrite = await _manifestService.WriteAsync(options.OutputDirectory, syncResult, cancellationToken);

        return new ProcessingResult(
            Processed: pendingVideos.Count,
            Succeeded: succeeded,
            Failed: failed,
            Skipped: skipped,
            Elapsed: totalStopwatch.Elapsed,
            InitialManifestWrite: initialManifestWrite,
            FinalManifestWrite: finalManifestWrite);
    }

    public async Task<TranscribeOneResult> TranscribeNextAsync(
        string? channelId,
        ProcessOptions options,
        Action<ProcessingProgress>? onProgress,
        CancellationToken cancellationToken = default)
    {
        var video = await _repository.GetNextPendingTranscriptVideoAsync(channelId, options.IncludeShorts, cancellationToken);
        if (video is null)
        {
            return new TranscribeOneResult(false, false, false, false, TimeSpan.Zero);
        }

        await _repository.UpdateVideoStatusAsync(video.VideoId, ChannelTranscriptStatuses.InProgress, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        onProgress?.Invoke(new ProcessingProgress
        {
            VideoId = video.VideoId,
            Title = video.Title,
            Position = 1,
            Total = 1,
            Kind = ProcessingEventKind.Started,
        });

        var channelOutputDirectory = ChannelManifestService.GetChannelOutputDirectory(options.OutputDirectory, video.ChannelId);
        var (outcome, _, _) = await ProcessVideoWithRetryAsync(
            video,
            options,
            channelOutputDirectory,
            position: 1,
            total: 1,
            onProgress,
            cancellationToken);
        stopwatch.Stop();

        if (outcome == VideoOutcome.Skipped)
        {
            await _repository.UpdateVideoStatusAsync(video.VideoId, ChannelTranscriptStatuses.Pending, cancellationToken);
        }

        return outcome switch
        {
            VideoOutcome.Succeeded => new TranscribeOneResult(true, true, false, false, stopwatch.Elapsed),
            VideoOutcome.Failed => new TranscribeOneResult(true, false, true, false, stopwatch.Elapsed),
            VideoOutcome.Skipped => new TranscribeOneResult(true, false, false, true, stopwatch.Elapsed),
            _ => new TranscribeOneResult(true, false, true, false, stopwatch.Elapsed),
        };
    }

    private async Task<(VideoOutcome outcome, string? origin, string? errorCategory)> ProcessVideoWithRetryAsync(
        VideoRecord video,
        ProcessOptions options,
        string channelOutputDirectory,
        int position,
        int total,
        Action<ProcessingProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        var videoUrl = $"https://www.youtube.com/watch?v={video.VideoId}";
        var attempt = 0;

        while (true)
        {
            // Apply inter-request delay (skip on very first video first attempt)
            if (position > 1 || attempt > 0)
            {
                var delay = _rateLimitTracker.GetCurrentDelay();
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }

            var videoStopwatch = Stopwatch.StartNew();

            try
            {
                var delayApplied = (long)_rateLimitTracker.GetCurrentDelay().TotalMilliseconds;
                var captionStopwatch = Stopwatch.StartNew();

                var exportRequest = new YouTubeExportRequest(
                    Url: videoUrl,
                    OutputDirectory: channelOutputDirectory,
                    Settings: options.Settings ?? new AppSettings(),
                    CaptionLanguage: options.CaptionLanguage,
                    ForceAudio: options.ForceAudio,
                    Overwrite: false,
                    KeepTemp: false,
                    WritePlainText: true,
                    ProviderId: options.ProviderId,
                    SettingOverrides: new Dictionary<string, string>());

                var result = await _exportService.ExportAsync(exportRequest, cancellationToken);
                captionStopwatch.Stop();
                videoStopwatch.Stop();

                // Record successful caption metrics.
                await _rateLimitTracker.RecordSuccessAsync(
                    delayApplied,
                    result.TranscriptOrigin == "captions" ? captionStopwatch.ElapsedMilliseconds : null,
                    cancellationToken);

                // Persist transcription record.
                await _repository.InsertTranscriptionAsync(new TranscriptionRecord
                {
                    VideoId              = video.VideoId,
                    TranscriptOrigin     = result.TranscriptOrigin,
                    TranscriptFilePath   = result.TranscriptJsonPath,
                    SucceededAt          = DateTimeOffset.UtcNow,
                    TotalDurationMs      = videoStopwatch.ElapsedMilliseconds,
                    CaptionDownloadTimeMs = result.TranscriptOrigin == "captions" ? captionStopwatch.ElapsedMilliseconds : null,
                }, cancellationToken);

                await _repository.UpdateVideoStatusAsync(video.VideoId, "completed", cancellationToken);

                onProgress?.Invoke(new ProcessingProgress
                {
                    VideoId          = video.VideoId,
                    Title            = video.Title,
                    Position         = position,
                    Total            = total,
                    Kind             = ProcessingEventKind.Completed,
                    TranscriptOrigin = result.TranscriptOrigin,
                    ElapsedMs        = videoStopwatch.ElapsedMilliseconds,
                });

                return (VideoOutcome.Succeeded, result.TranscriptOrigin, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsRateLimitError(ex) && attempt < options.MaxRateLimitRetries)
            {
                attempt++;
                await _rateLimitTracker.RecordThrottledAsync(
                    (long)_rateLimitTracker.GetCurrentDelay().TotalMilliseconds, cancellationToken);

                var backoffDelay = _rateLimitTracker.GetCurrentDelay();

                onProgress?.Invoke(new ProcessingProgress
                {
                    VideoId      = video.VideoId,
                    Title        = video.Title,
                    Position     = position,
                    Total        = total,
                    Kind         = ProcessingEventKind.RateLimited,
                    RetryAttempt = attempt,
                    DelayMs      = (long)backoffDelay.TotalMilliseconds,
                    ErrorCategory = "rate-limited",
                });

                // Wait the updated (higher) backoff delay before retry.
                await Task.Delay(backoffDelay, cancellationToken);
                continue;
            }
            catch (IOException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                // Video output directory already exists and overwrite=false — treat as skipped.
                onProgress?.Invoke(new ProcessingProgress
                {
                    VideoId       = video.VideoId,
                    Title         = video.Title,
                    Position      = position,
                    Total         = total,
                    Kind          = ProcessingEventKind.Skipped,
                    ErrorCategory = "already-exists",
                });
                return (VideoOutcome.Skipped, null, "already-exists");
            }
            catch (Exception ex)
            {
                var errorCategory = CategorizeError(ex);
                await _repository.InsertTranscriptionAsync(new TranscriptionRecord
                {
                    VideoId       = video.VideoId,
                    ErrorCategory = errorCategory,
                    ErrorMessage  = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message,
                }, cancellationToken);

                await _repository.UpdateVideoStatusAsync(video.VideoId, "failed", cancellationToken);

                onProgress?.Invoke(new ProcessingProgress
                {
                    VideoId       = video.VideoId,
                    Title         = video.Title,
                    Position      = position,
                    Total         = total,
                    Kind          = ProcessingEventKind.Failed,
                    ElapsedMs     = videoStopwatch.ElapsedMilliseconds,
                    ErrorCategory = errorCategory,
                });

                return (VideoOutcome.Failed, null, errorCategory);
            }
        }
    }

    private static bool IsRateLimitError(Exception ex)
    {
        var msg = ex.Message;
        return msg.Contains("429", StringComparison.Ordinal)
            || msg.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("rate-limit", StringComparison.OrdinalIgnoreCase);
    }

    private static string CategorizeError(Exception ex) => ex switch
    {
        InvalidOperationException e when e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) => "video-not-found",
        InvalidOperationException e when e.Message.Contains("private", StringComparison.OrdinalIgnoreCase) => "private-video",
        InvalidOperationException e when e.Message.Contains("unavailable", StringComparison.OrdinalIgnoreCase) => "video-unavailable",
        TimeoutException => "timeout",
        IOException => "io-error",
        InvalidOperationException => "export-failed",
        _ => "unknown",
    };

    private enum VideoOutcome { Succeeded, Failed, Skipped }
}
