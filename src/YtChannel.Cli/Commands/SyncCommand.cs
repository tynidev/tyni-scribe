using Microsoft.Extensions.DependencyInjection;
using YtChannel.Core.Services;

namespace YtChannel.Cli.Commands;

internal static class SyncCommand
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

        var syncService = serviceProvider.GetRequiredService<ChannelSyncService>();
        var manifestService = serviceProvider.GetRequiredService<ChannelManifestService>();
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
                    Console.Error.WriteLine($"Syncing channel: {channelInput}");
                    var result = await syncService.SyncChannelAsync(channelInput, cts.Token);

                    Console.WriteLine($"Channel: {result.ChannelName} ({result.ChannelId})");
                    Console.WriteLine($"Total videos in channel : {result.TotalVideosInChannel}");
                    Console.WriteLine($"Newly added to database : {result.NewlyInserted}");
                    Console.WriteLine($"Already in database     : {result.AlreadyInDatabase}");

                    var manifest = await manifestService.WriteAsync(GetDefaultOutputDirectory(), result, cts.Token);
                    Console.WriteLine($"Manifest updated        : {manifest.OutputPath}");
                    Console.WriteLine($"Manifest timing         : {manifest.VideoCount} videos, {FormatBytes(manifest.FileSizeBytes)}, {manifest.TotalDurationMs}ms total (db {manifest.QueryDurationMs}ms, write {manifest.WriteDurationMs}ms)");
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
        Console.Error.WriteLine("Usage: yt-channel sync <channel-url-or-id>... [--channels-file <path>]");
    }

    private static string GetDefaultOutputDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "yt-channel",
        "transcripts");

    private static string FormatBytes(long bytes)
    {
        const double oneMegabyte = 1024d * 1024d;
        return bytes >= oneMegabyte
            ? $"{bytes / oneMegabyte:F2} MB"
            : $"{bytes / 1024d:F1} KB";
    }

    private static bool IsHelp(string arg) =>
        arg is "--help" or "-h" or "help";
}
