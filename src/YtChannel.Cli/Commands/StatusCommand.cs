using Microsoft.Extensions.DependencyInjection;
using YtChannel.Core.Data;
using YtChannel.Core.Services;

namespace YtChannel.Cli.Commands;

internal static class StatusCommand
{
    internal static async Task<int> RunAsync(string[] args, IServiceProvider serviceProvider)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteUsage();
            return 2;
        }

        if (!TryParseInputs(args, out var channels, out var parseError))
        {
            Console.Error.WriteLine($"Error: {parseError}");
            WriteUsage();
            return 2;
        }

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var repository = serviceProvider.GetRequiredService<ChannelRepository>();
        var channelService = serviceProvider.GetRequiredService<IYouTubeChannelService>();
        var hadFailures = false;

        try
        {
            for (var i = 0; i < channels.Length; i++)
            {
                var channelInput = channels[i];

                if (i > 0)
                {
                    Console.WriteLine();
                }

                try
                {
                    // Resolve channel ID
                    var channelInfo = await channelService.GetChannelInfoAsync(channelInput, cts.Token);
                    var channelId = channelInfo.ChannelId;

                    var total = await repository.CountAllVideosAsync(channelId, cts.Token);
                    var pending = await repository.CountVideosByStatusAsync(channelId, "pending", cts.Token);
                    var completed = await repository.CountVideosByStatusAsync(channelId, "completed", cts.Token);
                    var failed = await repository.CountVideosByStatusAsync(channelId, "failed", cts.Token);
                    var summaryPending = await repository.CountVideosBySummaryStatusAsync(channelId, ChannelSummaryStatuses.Pending, cts.Token);
                    var summarized = await repository.CountVideosBySummaryStatusAsync(channelId, ChannelSummaryStatuses.Summarized, cts.Token);
                    var summaryFailed = await repository.CountVideosBySummaryStatusAsync(channelId, ChannelSummaryStatuses.Failed, cts.Token);

                    var pct = total > 0 ? (completed * 100.0 / total).ToString("F1") : "0.0";

                    var channel = await repository.GetChannelAsync(channelId, cts.Token);

                    Console.WriteLine($"Channel   : {channelInfo.Title}");
                    Console.WriteLine($"ID        : {channelId}");
                    Console.WriteLine($"Last sync : {channel?.SyncedAt?.ToString("yyyy-MM-dd HH:mm") ?? "never"}");
                    Console.WriteLine($"Retention : {(channel?.MaxVideoAgeDays is > 0 ? $"{channel.MaxVideoAgeDays.Value} days" : "none")}");
                    Console.WriteLine($"Total     : {total}");
                    Console.WriteLine($"Completed : {completed} ({pct}%)");
                    Console.WriteLine($"Pending   : {pending}");
                    Console.WriteLine($"Failed    : {failed}");
                    Console.WriteLine($"Summarized: {summarized}");
                    Console.WriteLine($"Summary pending: {summaryPending}");
                    Console.WriteLine($"Summary failed : {summaryFailed}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    hadFailures = true;
                    Console.Error.WriteLine($"Error processing channel '{channelInput}': {ex.Message}");
                }
            }

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
    }

    private static bool TryParseInputs(string[] args, out string[] channels, out string? error)
    {
        channels = Array.Empty<string>();
        error = null;

        var positionalChannels = new List<string>();
        string? channelsFile = null;

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];

            switch (token)
            {
                case "--channels-file":
                    i++;
                    if (i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                    {
                        error = "Missing value for --channels-file.";
                        return false;
                    }

                    channelsFile = args[i];
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

        return ChannelInputParser.TryParseChannels(positionalChannels, channelsFile, out channels, out error);
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine("Usage: yt-channel status <channel-url-or-id>... [--channels-file <path>]");
    }

    private static bool IsHelp(string arg) =>
        arg is "--help" or "-h" or "help";
}
