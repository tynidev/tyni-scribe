using Microsoft.Extensions.DependencyInjection;
using YtChannel.Core.Data;
using YtChannel.Core.Services;

namespace YtChannel.Cli.Commands;

internal static class ScanCommand
{
    private const int DefaultIntervalMinutes = 30;

    internal static async Task<int> RunAsync(string[] args, IServiceProvider serviceProvider)
    {
        if (args.Length > 0 && IsHelp(args[0]))
        {
            WriteUsage();
            return 2;
        }

        if (!TryParseOptions(args, out var watch, out var intervalMinutes, out var error))
        {
            Console.Error.WriteLine($"Error: {error}");
            WriteUsage();
            return 2;
        }

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var cycle = 1;
            do
            {
                await RunCycleAsync(cycle, serviceProvider, cts.Token);
                if (!watch)
                {
                    break;
                }

                Console.Error.WriteLine($"Scan watch: sleeping {intervalMinutes} minutes.");
                cycle++;
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), cts.Token);
            } while (true);

            return 0;
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

    private static async Task RunCycleAsync(int cycle, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var repository = serviceProvider.GetRequiredService<ChannelRepository>();
        var syncService = serviceProvider.GetRequiredService<ChannelSyncService>();
        var manifestService = serviceProvider.GetRequiredService<ChannelManifestService>();
        var retentionService = serviceProvider.GetRequiredService<ChannelRetentionService>();
        var outputDirectory = GetDefaultOutputDirectory();
        var channels = await repository.GetEnabledChannelsAsync(cancellationToken);

        Console.Error.WriteLine($"Scan cycle {cycle}: {channels.Count} enabled channel(s).");
        foreach (var channel in channels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Console.Error.WriteLine($"Scanning channel: {channel.ChannelName ?? channel.ChannelId}");
                await repository.UpdateChannelScanStartedAsync(channel.ChannelId, cancellationToken);
                var result = await syncService.SyncChannelAsync(channel.ChannelUrl, cancellationToken);
                var retention = await retentionService.PruneAsync(result.ChannelId, outputDirectory, cancellationToken);
                await repository.UpdateChannelScanCompletedAsync(channel.ChannelId, channel.ScanIntervalMinutes, "completed", cancellationToken);
                await manifestService.WriteAsync(outputDirectory, result, cancellationToken);
                var retentionText = retention.MaxVideoAgeDays.HasValue
                    ? $", {retention.PrunedVideos} pruned older than {retention.MaxVideoAgeDays.Value} days"
                    : string.Empty;
                Console.WriteLine($"{result.ChannelName ?? result.ChannelId}: {result.NewlyInserted} new, {result.AlreadyInDatabase} existing{retentionText}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await repository.UpdateChannelScanCompletedAsync(channel.ChannelId, channel.ScanIntervalMinutes, "failed", cancellationToken);
                Console.Error.WriteLine($"Scan failed for {channel.ChannelName ?? channel.ChannelId}: {ex.Message}");
            }
        }
    }

    private static bool TryParseOptions(string[] args, out bool watch, out int intervalMinutes, out string? error)
    {
        watch = false;
        intervalMinutes = DefaultIntervalMinutes;
        error = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--watch":
                    watch = true;
                    break;
                case "--interval-minutes":
                    i++;
                    if (i >= args.Length || !int.TryParse(args[i], out intervalMinutes) || intervalMinutes <= 0)
                    {
                        error = "--interval-minutes must be a positive integer.";
                        return false;
                    }
                    break;
                default:
                    error = $"Unknown option '{args[i]}'.";
                    return false;
            }
        }

        return true;
    }

    private static string GetDefaultOutputDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "yt-channel",
        "transcripts");

    private static void WriteUsage()
    {
        Console.Error.WriteLine("Usage: yt-channel scan [--watch] [--interval-minutes <n>]");
    }

    private static bool IsHelp(string arg) => arg is "--help" or "-h" or "help";
}