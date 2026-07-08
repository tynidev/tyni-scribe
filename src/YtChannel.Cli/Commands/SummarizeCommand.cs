using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using TranscriptSummary.Core.Services;
using YtChannel.Core.Configuration;
using YtChannel.Core.Services;

namespace YtChannel.Cli.Commands;

internal static class SummarizeCommand
{
    private const int DefaultIdleSleepMinutes = 10;

    internal static async Task<int> RunAsync(string[] args, IServiceProvider serviceProvider)
    {
        if (args.Length > 0 && IsHelp(args[0]))
        {
            WriteUsage();
            return 2;
        }

        if (!TryParseOptions(args, out var channels, out var options, out var watch, out var idleSleepMinutes, out var parseError))
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

        var orchestrator = serviceProvider.GetRequiredService<ChannelSummaryOrchestrator>();
        var channelService = serviceProvider.GetRequiredService<IYouTubeChannelService>();
        var hadFailures = false;
        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;

        try
        {
            var channelIds = new List<string?>();
            if (channels.Length == 0)
            {
                channelIds.Add(null);
            }
            else
            {
                foreach (var channel in channels)
                {
                    var channelInfo = await channelService.GetChannelInfoAsync(channel, cts.Token);
                    channelIds.Add(channelInfo.ChannelId);
                }
            }

            var cycle = 1;
            do
            {
                var cycleProcessed = 0;
                foreach (var channelId in channelIds)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(channelId is null
                        ? "Summarizing all transcribed videos without successful summaries."
                        : $"Summarizing channel: {channelId}");
                    Console.Error.WriteLine($"Context tokens: {options.ContextTokens}; max output tokens: {options.MaxOutputTokens}");

                    var result = await orchestrator.SummarizeAsync(
                        channelId,
                        options,
                        onProgress: WriteProgress,
                        cts.Token);

                    cycleProcessed += result.Processed;
                    processed += result.Processed;
                    succeeded += result.Succeeded;
                    failed += result.Failed;
                    skipped += result.Skipped;
                    if (result.Failed > 0)
                    {
                        hadFailures = true;
                    }

                    Console.WriteLine();
                    Console.WriteLine(channelId is null ? "Summary complete" : $"Channel summary complete: {channelId}");
                    Console.WriteLine($"  Processed: {result.Processed}");
                    Console.WriteLine($"  Succeeded: {result.Succeeded}");
                    Console.WriteLine($"  Failed   : {result.Failed}");
                    Console.WriteLine($"  Skipped  : {result.Skipped}");
                    Console.WriteLine($"  Elapsed  : {result.Elapsed.TotalSeconds:F1}s");
                }

                if (!watch || options.MaxVideos.HasValue)
                {
                    break;
                }

                if (cycleProcessed == 0)
                {
                    Console.Error.WriteLine($"No summary work. Sleeping {idleSleepMinutes} minutes.");
                    await Task.Delay(TimeSpan.FromMinutes(idleSleepMinutes), cts.Token);
                }

                cycle++;
            } while (true);

            Console.WriteLine();
            Console.WriteLine("Summary run totals");
            Console.WriteLine($"  Processed: {processed}");
            Console.WriteLine($"  Succeeded: {succeeded}");
            Console.WriteLine($"  Failed   : {failed}");
            Console.WriteLine($"  Skipped  : {skipped}");

            return hadFailures ? 1 : 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Canceled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static void WriteProgress(ChannelSummaryProgress progress)
    {
        var prefix = $"[{progress.Position}/{progress.Total}]";
        var title = progress.Title?.Length > 50
            ? progress.Title[..47] + "..."
            : progress.Title ?? progress.VideoId;

        switch (progress.Kind)
        {
            case ChannelSummaryEventKind.Started:
                Console.WriteLine($"{prefix} {title}");
                break;
            case ChannelSummaryEventKind.Progress:
                Console.WriteLine($"{prefix}   {progress.Stage} pass={progress.PassIndex} item={progress.ItemIndex}/{progress.ItemCount}");
                break;
            case ChannelSummaryEventKind.Completed:
                Console.WriteLine(progress.EstimateOnly
                    ? $"{prefix}   planned ({progress.ElapsedMs}ms)"
                    : $"{prefix}   summarized ({progress.ElapsedMs}ms)");
                if (!progress.EstimateOnly)
                {
                    WriteTokenStats(prefix, progress);
                }
                break;
            case ChannelSummaryEventKind.Failed:
                Console.WriteLine($"{prefix}   FAILED ({progress.ErrorCategory})");
                break;
            case ChannelSummaryEventKind.Skipped:
                Console.WriteLine($"{prefix}   skipped ({progress.ErrorCategory})");
                break;
        }
    }

    private static void WriteTokenStats(string prefix, ChannelSummaryProgress progress)
    {
        if (progress.TotalTokens.HasValue || progress.TotalPromptTokens.HasValue || progress.TotalCompletionTokens.HasValue)
        {
            Console.WriteLine($"{prefix}   tokens: prompt={FormatInt(progress.TotalPromptTokens)}, completion={FormatInt(progress.TotalCompletionTokens)}, total={FormatInt(progress.TotalTokens)}");
            Console.WriteLine($"{prefix}   speed : prompt={FormatRate(progress.PromptTokensPerSecond)}, completion={FormatRate(progress.CompletionTokensPerSecond)}, total={FormatRate(progress.TotalTokensPerSecond)} tok/s");
            return;
        }

        Console.WriteLine($"{prefix}   tokens: estimatedInput={FormatInt(progress.EstimatedTranscriptTokens)}, estimatedOutput={FormatInt(progress.EstimatedOutputTokens)}");
    }

    private static string FormatInt(int? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";

    private static string FormatRate(double? value) => value?.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";

    private static bool TryParseOptions(
        string[] args,
        out string[] channels,
        out ChannelSummaryOptions options,
        out bool watch,
        out int idleSleepMinutes,
        out string? error)
    {
        channels = Array.Empty<string>();
        options = null!;
        watch = false;
        idleSleepMinutes = DefaultIdleSleepMinutes;
        error = null;

        var positionalChannels = new List<string>();
        string? channelsFile = null;
        int? maxVideos = null;
        var includeShorts = false;
        var overwrite = false;
        var estimateOnly = false;
        var prompt = TranscriptSummaryDefaults.Prompt;
        var model = TranscriptSummaryDefaults.Model;
        var endpoint = new Uri(TranscriptSummaryDefaults.Endpoint);
        var contextTokens = TranscriptSummaryDefaults.ContextTokens;
        var reservedOutputTokens = TranscriptSummaryDefaults.ReservedOutputTokens;
        var maxOutputTokens = TranscriptSummaryDefaults.MaxOutputTokens;
        var charsPerToken = TranscriptSummaryDefaults.CharsPerToken;
        var timeoutSeconds = TranscriptSummaryDefaults.TimeoutSeconds;

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];

            switch (token)
            {
                case "--channels-file":
                    channelsFile = NextArg(args, ref i, token, out error);
                    if (error is not null) return false;
                    break;

                case "--max-videos":
                    if (!TryReadPositiveInt(args, ref i, token, out maxVideos, out error)) return false;
                    break;

                case "--include-shorts":
                    includeShorts = true;
                    break;

                case "--overwrite":
                    overwrite = true;
                    break;

                case "--estimate-only":
                    estimateOnly = true;
                    break;

                case "--watch":
                    watch = true;
                    break;

                case "--idle-sleep-minutes":
                    if (!TryReadInt(args, ref i, token, out idleSleepMinutes, out error)) return false;
                    break;

                case "--prompt":
                    prompt = NextArg(args, ref i, token, out error) ?? prompt;
                    if (error is not null) return false;
                    break;

                case "--model":
                    model = NextArg(args, ref i, token, out error) ?? model;
                    if (error is not null) return false;
                    break;

                case "--endpoint":
                    var endpointText = NextArg(args, ref i, token, out error);
                    if (error is not null) return false;
                    if (!Uri.TryCreate(endpointText, UriKind.Absolute, out endpoint!))
                    {
                        error = "--endpoint must be an absolute URL.";
                        return false;
                    }
                    break;

                case "--context-tokens":
                    if (!TryReadInt(args, ref i, token, out contextTokens, out error)) return false;
                    break;

                case "--reserved-output-tokens":
                    if (!TryReadInt(args, ref i, token, out reservedOutputTokens, out error)) return false;
                    break;

                case "--max-output-tokens":
                    if (!TryReadInt(args, ref i, token, out maxOutputTokens, out error)) return false;
                    break;

                case "--chars-per-token":
                    if (!TryReadDouble(args, ref i, token, out charsPerToken, out error)) return false;
                    break;

                case "--timeout-seconds":
                    if (!TryReadInt(args, ref i, token, out timeoutSeconds, out error)) return false;
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

        if (!ChannelInputParser.TryParseChannels(positionalChannels, channelsFile, out channels, out error, allowEmpty: true))
        {
            return false;
        }

        if (contextTokens <= 0 || reservedOutputTokens <= 0 || maxOutputTokens <= 0 || timeoutSeconds <= 0)
        {
            error = "Token and timeout values must be greater than zero.";
            return false;
        }

        if (idleSleepMinutes <= 0)
        {
            error = "--idle-sleep-minutes must be greater than zero.";
            return false;
        }

        if (reservedOutputTokens >= contextTokens)
        {
            error = "--reserved-output-tokens must be lower than --context-tokens.";
            return false;
        }

        if (charsPerToken <= 0)
        {
            error = "--chars-per-token must be greater than zero.";
            return false;
        }

        options = new ChannelSummaryOptions
        {
            MaxVideos = maxVideos,
            IncludeShorts = includeShorts,
            Overwrite = overwrite,
            EstimateOnly = estimateOnly,
            Prompt = prompt,
            Model = model,
            Endpoint = endpoint,
            ContextTokens = contextTokens,
            ReservedOutputTokens = reservedOutputTokens,
            MaxOutputTokens = maxOutputTokens,
            CharsPerToken = charsPerToken,
            TimeoutSeconds = timeoutSeconds,
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

    private static bool TryReadPositiveInt(string[] args, ref int i, string optionName, out int? value, out string? error)
    {
        if (!TryReadInt(args, ref i, optionName, out var parsed, out error))
        {
            value = null;
            return false;
        }

        if (parsed <= 0)
        {
            error = $"{optionName} must be a positive integer.";
            value = null;
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadInt(string[] args, ref int i, string optionName, out int value, out string? error)
    {
        var text = NextArg(args, ref i, optionName, out error);
        if (error is not null)
        {
            value = 0;
            return false;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = $"{optionName} must be an integer.";
            return false;
        }

        return true;
    }

    private static bool TryReadDouble(string[] args, ref int i, string optionName, out double value, out string? error)
    {
        var text = NextArg(args, ref i, optionName, out error);
        if (error is not null)
        {
            value = 0;
            return false;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            error = $"{optionName} must be a number.";
            return false;
        }

        return true;
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine("Usage: yt-channel summarize [channel-url-or-id]... [--channels-file <path>] [options]");
        Console.Error.WriteLine("   or: yt-channel summarize [options]  # summarize all DB videos with transcripts and no summary");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Options:");
        Console.Error.WriteLine("  --channels-file <path>          Text file containing channel URLs/IDs (one per line)");
        Console.Error.WriteLine("  --max-videos <n>                Limit videos per run/channel");
        Console.Error.WriteLine("  --include-shorts                Include Shorts (default excludes UUSH videos <= 180s)");
        Console.Error.WriteLine("  --overwrite                     Regenerate summaries even when already summarized");
        Console.Error.WriteLine("  --prompt <text>                 Summary prompt");
        Console.Error.WriteLine("  --model <id>                    Model id (default: gemma-4-26b-a4b-it)");
        Console.Error.WriteLine("  --endpoint <url>                OpenAI-compatible chat completions endpoint");
        Console.Error.WriteLine("  --context-tokens <n>            Loaded model context length (default: 98304)");
        Console.Error.WriteLine("  --reserved-output-tokens <n>    Tokens reserved for output/instructions (default: 1024)");
        Console.Error.WriteLine("  --max-output-tokens <n>         max_tokens sent to the endpoint (default: 2048)");
        Console.Error.WriteLine("  --chars-per-token <n>           Token estimate ratio (default: 3.0)");
        Console.Error.WriteLine("  --timeout-seconds <n>           Timeout per LLM request (default: 600)");
        Console.Error.WriteLine("  --estimate-only                 Plan summaries without calling the model or writing files");
        Console.Error.WriteLine("  --watch                         Keep polling when no summary work is available");
        Console.Error.WriteLine("  --idle-sleep-minutes <n>        Sleep duration when --watch has no work (default: 10)");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Per-channel config:");
        Console.Error.WriteLine("  channel.summary.json next to channel.json can override prompt/model/token defaults.");
        Console.Error.WriteLine("  Explicit CLI flags take precedence over channel.summary.json values.");
    }

    private static bool IsHelp(string arg) =>
        arg is "--help" or "-h" or "help";
}