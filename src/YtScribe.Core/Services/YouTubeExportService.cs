using System.Diagnostics;
using System.Text.RegularExpressions;
using Tts.Core.Services;
using Tts.Core.Services.Media;
using Tts.Core.Services.Transcription;
using YtScribe.Core.Model;

namespace YtScribe.Core.Services;

public sealed partial class YouTubeExportService : IYouTubeExportService
{
    private readonly AppPaths _paths;
    private readonly IYtDlpService _ytDlp;
    private readonly IVttTranscriptParser _vttParser;
    private readonly ITranscriptArtifactWriter _artifactWriter;
    private readonly IMediaPreparationService _mediaPreparation;
    private readonly ITranscriptionExecutionService _transcriptionExecution;

    public YouTubeExportService(
        AppPaths paths,
        IYtDlpService ytDlp,
        IVttTranscriptParser vttParser,
        ITranscriptArtifactWriter artifactWriter,
        IMediaPreparationService mediaPreparation,
        ITranscriptionExecutionService transcriptionExecution)
    {
        _paths = paths;
        _ytDlp = ytDlp;
        _vttParser = vttParser;
        _artifactWriter = artifactWriter;
        _mediaPreparation = mediaPreparation;
        _transcriptionExecution = transcriptionExecution;
    }

    public async Task<YouTubeExportResult> ExportAsync(YouTubeExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateUrl(request.Url);

        var metadataStopwatch = Stopwatch.StartNew();
        var videoMetadata = await _ytDlp.GetMetadataAsync(request.Url, cancellationToken);
        metadataStopwatch.Stop();

        var videoOutputDirectory = Path.Combine(Path.GetFullPath(request.OutputDirectory), SanitizePathSegment(videoMetadata.Id));
        PrepareOutputDirectory(videoOutputDirectory, request.Overwrite);

        var tempDirectory = Path.Combine(_paths.TempAudioDirectory, "yt-scribe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            if (!request.ForceAudio)
            {
                var captionStopwatch = Stopwatch.StartNew();
                var captionPath = await _ytDlp.DownloadCaptionsAsync(request.Url, tempDirectory, request.CaptionLanguage, cancellationToken);
                captionStopwatch.Stop();

                if (!string.IsNullOrWhiteSpace(captionPath))
                {
                    var segments = await _vttParser.ParseAsync(captionPath, cancellationToken);
                    if (segments.Count > 0)
                    {
                        var transcript = CreateTranscript(request.Url, videoMetadata, "captions", "timestamped", segments);
                        var metadata = CreateMetadata(
                            request.Url,
                            videoMetadata,
                            request.CaptionLanguage,
                            "captions",
                            "success",
                            null,
                            null,
                            metadataStopwatch.Elapsed,
                            captionStopwatch.Elapsed,
                            null,
                            null,
                            null,
                            null);

                        var artifacts = await _artifactWriter.WriteAsync(videoOutputDirectory, metadata, transcript, captionPath, request.WritePlainText, cancellationToken);
                        return new YouTubeExportResult("success", "captions", artifacts.OutputDirectory, artifacts.MetadataPath, artifacts.TranscriptJsonPath, artifacts.TranscriptVttPath, artifacts.TranscriptTextPath, null);
                    }
                }
            }

            return await ExportFromAudioAsync(request, videoMetadata, videoOutputDirectory, tempDirectory, metadataStopwatch.Elapsed, cancellationToken);
        }
        finally
        {
            if (!request.KeepTemp)
            {
                DeleteDirectoryIfExists(tempDirectory);
            }
        }
    }

    private async Task<YouTubeExportResult> ExportFromAudioAsync(
        YouTubeExportRequest request,
        YouTubeVideoMetadata videoMetadata,
        string videoOutputDirectory,
        string tempDirectory,
        TimeSpan metadataDuration,
        CancellationToken cancellationToken)
    {
        var audioDownloadStopwatch = Stopwatch.StartNew();
        var audioPath = await _ytDlp.DownloadAudioAsync(request.Url, tempDirectory, cancellationToken);
        audioDownloadStopwatch.Stop();

        var mediaPreparationStopwatch = Stopwatch.StartNew();
        var preparedAudio = await _mediaPreparation.PrepareAsync(
            new MediaPreparationRequest(audioPath, Path.Combine(tempDirectory, "prepared")),
            cancellationToken);
        mediaPreparationStopwatch.Stop();

        try
        {
            var transcription = await _transcriptionExecution.TranscribeAsync(
                new TranscriptionExecutionRequest(
                    preparedAudio.AudioFilePath,
                    request.Settings,
                    request.ProviderId,
                    null,
                    request.SettingOverrides),
                cancellationToken);

            var segments = new[] { new TranscriptSegment(null, null, transcription.Text) };
            var transcript = CreateTranscript(request.Url, videoMetadata, "audio-transcription", "none", segments);
            var metadata = CreateMetadata(
                request.Url,
                videoMetadata,
                null,
                "audio-transcription",
                "success",
                transcription.ProviderId,
                transcription.EffectiveSettings,
                metadataDuration,
                null,
                audioDownloadStopwatch.Elapsed,
                mediaPreparationStopwatch.Elapsed,
                TimeSpan.FromMilliseconds(transcription.AudioProcessingMilliseconds),
                TimeSpan.FromMilliseconds(transcription.TranscriptionMilliseconds));

            var artifacts = await _artifactWriter.WriteAsync(videoOutputDirectory, metadata, transcript, null, request.WritePlainText, cancellationToken);
            return new YouTubeExportResult("success", "audio-transcription", artifacts.OutputDirectory, artifacts.MetadataPath, artifacts.TranscriptJsonPath, artifacts.TranscriptVttPath, artifacts.TranscriptTextPath, null);
        }
        finally
        {
            if (!request.KeepTemp && !preparedAudio.IsOriginalFile)
            {
                DeleteFileIfExists(preparedAudio.AudioFilePath);
            }
        }
    }

    private static TranscriptDocument CreateTranscript(
        string sourceUrl,
        YouTubeVideoMetadata metadata,
        string transcriptOrigin,
        string timingKind,
        IReadOnlyList<TranscriptSegment> segments)
    {
        return new TranscriptDocument(1, sourceUrl, metadata.Id, metadata.Title, transcriptOrigin, timingKind, DateTimeOffset.UtcNow, segments);
    }

    private static VideoMetadataDocument CreateMetadata(
        string sourceUrl,
        YouTubeVideoMetadata metadata,
        string? captionLanguage,
        string transcriptOrigin,
        string status,
        string? transcriptionProviderId,
        IReadOnlyDictionary<string, string>? transcriptionSettings,
        TimeSpan? metadataDuration,
        TimeSpan? captionDuration,
        TimeSpan? audioDownloadDuration,
        TimeSpan? mediaPreparationDuration,
        TimeSpan? audioProcessingDuration,
        TimeSpan? transcriptionDuration)
    {
        return new VideoMetadataDocument(
            1,
            sourceUrl,
            metadata.Id,
            metadata.Title,
            metadata.Channel,
            metadata.Uploader,
            metadata.UploadDate,
            metadata.DurationSeconds,
            metadata.WebpageUrl,
            metadata.ThumbnailUrl,
            captionLanguage,
            transcriptOrigin,
            DateTimeOffset.UtcNow,
            status,
            transcriptionProviderId,
            transcriptionSettings,
            ToMilliseconds(metadataDuration),
            ToMilliseconds(captionDuration),
            ToMilliseconds(audioDownloadDuration),
            ToMilliseconds(mediaPreparationDuration),
            ToMilliseconds(audioProcessingDuration),
            ToMilliseconds(transcriptionDuration));
    }

    private static void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("The URL must be an absolute HTTP or HTTPS URL.");
        }
    }

    private static void PrepareOutputDirectory(string outputDirectory, bool overwrite)
    {
        if (Directory.Exists(outputDirectory))
        {
            if (!overwrite)
            {
                throw new IOException("The video output directory already exists. Use --overwrite to replace it.");
            }

            Directory.Delete(outputDirectory, recursive: true);
        }

        Directory.CreateDirectory(outputDirectory);
    }

    private static string SanitizePathSegment(string value)
    {
        var sanitized = InvalidPathSegmentRegex().Replace(value, "_").Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? Guid.NewGuid().ToString("N") : sanitized;
    }

    private static long? ToMilliseconds(TimeSpan? duration)
    {
        return duration is null
            ? null
            : (long)Math.Round(duration.Value.TotalMilliseconds, MidpointRounding.AwayFromZero);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex("[^A-Za-z0-9._-]+")]
    private static partial Regex InvalidPathSegmentRegex();
}