using Microsoft.Extensions.DependencyInjection;
using Tts.Core.Configuration;
using YtChannel.Core.Data;
using YtChannel.Core.Services;

namespace YtChannel.Cli.Commands;

internal static class TranscribeCommand
{
    private const int DefaultIdleSleepMinutes = 10;

    internal static async Task<int> RunAsync(string[] args, IServiceProvider serviceProvider)
    {
        if (args.Length > 0 && IsHelp(args[0]))
        {
            WriteUsage();
            return 2;
        }

        if (!TryParseOptions(args, out var options, out var watch, out var idleSleepMinutes, out var parseError))
        {
            Console.Error.WriteLine($"Error: {parseError}");
            WriteUsage();
            return 2;
        }

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += cancelHandler;

        var repository = serviceProvider.GetRequiredService<ChannelRepository>();
        var orchestrator = serviceProvider.GetRequiredService<ChannelOrchestrator>();
        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;

        try
        {
            await repository.ResetStaleInProgressAsync(TimeSpan.FromHours(6), cts.Token);

            while (!options.MaxVideos.HasValue || processed < options.MaxVideos.Value)
            {
                var result = await orchestrator.TranscribeNextAsync(
                    channelId: null,
                    options,
                    WriteProgress,
                    cts.Token);

                if (!result.HadWork)
                {
                    if (!watch)
                    {
                        break;
                    }

                    Console.Error.WriteLine($"No transcription work. Sleeping {idleSleepMinutes} minutes.");
                    await Task.Delay(TimeSpan.FromMinutes(idleSleepMinutes), cts.Token);
                    continue;
                }

                processed++;
                if (result.Succeeded) succeeded++;
                if (result.Failed) failed++;
                if (result.Skipped) skipped++;
            }

            Console.WriteLine("Transcribe summary");
            Console.WriteLine($"  Processed: {processed}");
            Console.WriteLine($"  Succeeded: {succeeded}");
            Console.WriteLine($"  Failed   : {failed}");
            Console.WriteLine($"  Skipped  : {skipped}");
            return failed > 0 ? 1 : 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Canceled.");
            return 130;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static void WriteProgress(ProcessingProgress progress)
    {
        var title = progress.Title?.Length > 60 ? progress.Title[..57] + "..." : progress.Title ?? progress.VideoId;
        switch (progress.Kind)
        {
            case ProcessingEventKind.Started:
                Console.Write($"{title} ... ");
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

    private static bool TryParseOptions(string[] args, out ProcessOptions options, out bool watch, out int idleSleepMinutes, out string? error)
    {
        options = null!;
        watch = false;
        idleSleepMinutes = DefaultIdleSleepMinutes;
        error = null;
        int? maxVideos = null;
        var includeShorts = false;
        var captionLanguage = "en";
        var forceAudio = false;
        string? providerId = null;
        var outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "yt-channel", "transcripts");

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--watch": watch = true; break;
                case "--include-shorts": includeShorts = true; break;
                case "--force-audio": forceAudio = true; break;
                case "--max-videos":
                    i++;
                    if (i >= args.Length || !int.TryParse(args[i], out var parsedMax) || parsedMax <= 0) { error = "--max-videos must be a positive integer."; return false; }
                    maxVideos = parsedMax;
                    break;
                case "--idle-sleep-minutes":
                    i++;
                    if (i >= args.Length || !int.TryParse(args[i], out idleSleepMinutes) || idleSleepMinutes <= 0) { error = "--idle-sleep-minutes must be a positive integer."; return false; }
                    break;
                case "--output-dir":
                case "-o":
                    i++;
                    if (i >= args.Length || string.IsNullOrWhiteSpace(args[i])) { error = $"Missing value for {args[i - 1]}."; return false; }
                    outputDir = args[i];
                    break;
                case "--language":
                case "--caption-language":
                    i++;
                    if (i >= args.Length || string.IsNullOrWhiteSpace(args[i])) { error = $"Missing value for {args[i - 1]}."; return false; }
                    captionLanguage = args[i];
                    break;
                case "--provider":
                    i++;
                    if (i >= args.Length || string.IsNullOrWhiteSpace(args[i])) { error = "Missing value for --provider."; return false; }
                    providerId = args[i];
                    break;
                default:
                    error = $"Unknown option '{args[i]}'.";
                    return false;
            }
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

    private static void WriteUsage()
    {
        Console.Error.WriteLine("Usage: yt-channel transcribe [--watch] [--idle-sleep-minutes <n>] [options]");
    }

    private static bool IsHelp(string arg) => arg is "--help" or "-h" or "help";
}