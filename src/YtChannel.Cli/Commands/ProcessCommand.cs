using Microsoft.Extensions.DependencyInjection;
using Tts.Core.Configuration;
using YtChannel.Core.Services;

namespace YtChannel.Cli.Commands;

internal static class ProcessCommand
{
    private static readonly TimeSpan WatchInterval = TimeSpan.FromMinutes(30);

    internal static async Task<int> RunAsync(string[] args, IServiceProvider serviceProvider)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteUsage();
            return 2;
        }

        if (!TryParseOptions(args, out var channels, out var options, out var watch, out var parseError))
        {
            Console.Error.WriteLine($"Error: {parseError}");
            WriteUsage();
            return 2;
        }

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        var orchestrator = serviceProvider.GetRequiredService<ChannelOrchestrator>();

        try
        {
            if (!watch)
            {
                var cycle = await RunCycleAsync(1, channels, options, orchestrator, cts.Token);
                return cycle.HadFailures ? 1 : 0;
            }

            var cycleNumber = 1;
            while (true)
            {
                await RunCycleAsync(cycleNumber, channels, options, orchestrator, cts.Token);

                Console.Error.WriteLine();
                Console.Error.WriteLine($"Watch mode: waiting {WatchInterval.TotalMinutes:F0} minutes before next cycle.");
                Console.Error.WriteLine("Press Ctrl+C to stop.");

                cycleNumber++;
                await Task.Delay(WatchInterval, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("\nCanceled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\nFatal error: {ex.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static async Task<CycleSummary> RunCycleAsync(
        int cycleNumber,
        string[] channels,
        ProcessOptions options,
        ChannelOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        var cycleStart = DateTimeOffset.UtcNow;
        var hadFailures = false;
        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;

        Console.Error.WriteLine();
        Console.Error.WriteLine($"Cycle {cycleNumber} started at {cycleStart:yyyy-MM-dd HH:mm:ss} UTC");
        Console.Error.WriteLine($"Channels: {channels.Length}");
        Console.Error.WriteLine($"Output directory: {options.OutputDirectory}");
        if (options.MaxVideos.HasValue)
        {
            Console.Error.WriteLine($"Max videos per channel: {options.MaxVideos.Value}");
        }

        foreach (var channel in channels)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"Processing channel: {channel}");

                var result = await orchestrator.ProcessChannelAsync(
                    channel,
                    options,
                    onProgress: progress => WriteProgress(channel, progress),
                    cancellationToken);

                processed += result.Processed;
                succeeded += result.Succeeded;
                failed += result.Failed;
                skipped += result.Skipped;

                if (result.Failed > 0)
                {
                    hadFailures = true;
                }

                Console.WriteLine();
                Console.WriteLine($"Channel complete: {channel}");
                Console.WriteLine($"  Processed: {result.Processed}");
                Console.WriteLine($"  Succeeded: {result.Succeeded}");
                Console.WriteLine($"  Failed   : {result.Failed}");
                Console.WriteLine($"  Skipped  : {result.Skipped}");
                if (result.FinalManifestWrite is not null)
                {
                    WriteManifestTiming(result.FinalManifestWrite);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                hadFailures = true;
                Console.Error.WriteLine($"Channel failed '{channel}': {ex.Message}");
            }
        }

        var elapsed = DateTimeOffset.UtcNow - cycleStart;

        Console.WriteLine();
        Console.WriteLine($"Cycle {cycleNumber} summary");
        Console.WriteLine($"  Processed: {processed}");
        Console.WriteLine($"  Succeeded: {succeeded}");
        Console.WriteLine($"  Failed   : {failed}");
        Console.WriteLine($"  Skipped  : {skipped}");
        Console.WriteLine($"  Elapsed  : {elapsed.TotalSeconds:F1}s");

        return new CycleSummary(
            HadFailures: hadFailures,
            Processed: processed,
            Succeeded: succeeded,
            Failed: failed,
            Skipped: skipped);
    }

    private static void WriteProgress(string channel, ProcessingProgress progress)
    {
        var prefix = $"[{channel}] [{progress.Position}/{progress.Total}]";
        var title = progress.Title?.Length > 50
            ? progress.Title[..47] + "..."
            : progress.Title ?? progress.VideoId;

        switch (progress.Kind)
        {
            case ProcessingEventKind.Started:
                Console.Write($"{prefix} {title} ... ");
                break;
            case ProcessingEventKind.Completed:
                Console.WriteLine($"{progress.TranscriptOrigin} ({progress.ElapsedMs}ms)");
                break;
            case ProcessingEventKind.Failed:
                Console.WriteLine($"FAILED ({progress.ErrorCategory})");
                break;
            case ProcessingEventKind.RateLimited:
                Console.WriteLine($"rate-limited, retry {progress.RetryAttempt} after {progress.DelayMs}ms");
                break;
            case ProcessingEventKind.Skipped:
                Console.WriteLine($"skipped ({progress.ErrorCategory})");
                break;
        }
    }

    private static void WriteManifestTiming(ChannelManifestWriteResult result)
    {
        Console.WriteLine($"  Manifest : {result.VideoCount} videos, {FormatBytes(result.FileSizeBytes)}, {result.TotalDurationMs}ms total " +
            $"(db {result.QueryDurationMs}ms, write {result.WriteDurationMs}ms)");
    }

    private static string FormatBytes(long bytes)
    {
        const double oneMegabyte = 1024d * 1024d;
        return bytes >= oneMegabyte
            ? $"{bytes / oneMegabyte:F2} MB"
            : $"{bytes / 1024d:F1} KB";
    }

    private static bool TryParseOptions(
        string[] args,
        out string[] channels,
        out ProcessOptions options,
        out bool watch,
        out string? error)
    {
        channels = Array.Empty<string>();
        options = null!;
        watch = false;
        error = null;

        var positionalChannels = new List<string>();
        string? channelsFile = null;
        string? outputDir = null;
        int? maxVideos = null;
        var captionLanguage = "en";
        var forceAudio = false;
        var includeShorts = false;
        string? providerId = null;

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];

            switch (token)
            {
                case "--output-dir":
                case "-o":
                    outputDir = NextArg(args, ref i, token, out error);
                    if (error is not null)
                    {
                        return false;
                    }
                    break;

                case "--max-videos":
                    var maxVideosText = NextArg(args, ref i, "--max-videos", out error);
                    if (error is not null)
                    {
                        return false;
                    }

                    if (!int.TryParse(maxVideosText, out var parsedMaxVideos) || parsedMaxVideos <= 0)
                    {
                        error = "--max-videos must be a positive integer.";
                        return false;
                    }

                    maxVideos = parsedMaxVideos;
                    break;

                case "--language":
                case "--caption-language":
                    captionLanguage = NextArg(args, ref i, token, out error) ?? "en";
                    if (error is not null)
                    {
                        return false;
                    }
                    break;

                case "--force-audio":
                    forceAudio = true;
                    break;

                case "--include-shorts":
                    includeShorts = true;
                    break;

                case "--provider":
                    providerId = NextArg(args, ref i, "--provider", out error);
                    if (error is not null)
                    {
                        return false;
                    }
                    break;

                case "--channels-file":
                    channelsFile = NextArg(args, ref i, "--channels-file", out error);
                    if (error is not null)
                    {
                        return false;
                    }
                    break;

                case "--watch":
                    watch = true;
                    break;

                default:
                    if (token.StartsWith("-", StringComparison.Ordinal))
                    {
                        error = $"Unknown option '{token}'.";
                        return false;
                    }

                    positionalChannels.Add(token);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(outputDir))
        {
              // Default: alongside the database at %AppData%\yt-channel\transcripts
              outputDir = Path.Combine(
                 Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                 "yt-channel",
                 "transcripts");
        }

        if (!ChannelInputParser.TryParseChannels(positionalChannels, channelsFile, out channels, out error))
        {
            return false;
        }

        options = new ProcessOptions
        {
            OutputDirectory = outputDir,
            MaxVideos = maxVideos,
            CaptionLanguage = captionLanguage,
            ForceAudio = forceAudio,
            IncludeShorts = includeShorts,
            ProviderId = providerId,
            Settings = new AppSettings(),
        };

        return true;
    }

    private static string? NextArg(string[] args, ref int i, string optionName, out string? error)
    {
        i++;
        if (i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
        {
            error = $"Missing value for {optionName}.";
            return null;
        }

        error = null;
        return args[i];
    }

    private static void WriteUsage()
    {
           Console.Error.WriteLine("Usage: yt-channel process <channel-url-or-id>... [--output-dir <dir>] [options]");
           Console.Error.WriteLine("   or: yt-channel process --channels-file <path> [--output-dir <dir>] [options]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
           Console.Error.WriteLine("  --output-dir, -o <dir>      Root directory for transcript output");
           Console.Error.WriteLine("                              (default: %AppData%\\yt-channel\\transcripts)");
              Console.Error.WriteLine("                              Channel output: {channel-id}\\{video-id}");
        Console.Error.WriteLine("  --channels-file <path>      Text file containing channel URLs/IDs (one per line)");
        Console.Error.WriteLine("  --max-videos <n>            Limit to first N pending videos per channel");
        Console.Error.WriteLine("  --language <lang>           Caption language (default: en)");
        Console.Error.WriteLine("  --caption-language <lang>   Alias for --language");
        Console.Error.WriteLine("  --force-audio               Skip captions, always use audio transcription");
        Console.Error.WriteLine("  --include-shorts            Include Shorts (default excludes UUSH videos <= 180s)");
        Console.Error.WriteLine("  --provider <id>             Transcription provider ID override");
        Console.Error.WriteLine("  --watch                     Run continuously every 30 minutes until Ctrl+C");
    }

    private static bool IsHelp(string arg) =>
        arg is "--help" or "-h" or "help";

    private sealed record CycleSummary(
        bool HadFailures,
        int Processed,
        int Succeeded,
        int Failed,
        int Skipped);
}
