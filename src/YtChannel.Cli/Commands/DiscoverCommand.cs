using Microsoft.Extensions.DependencyInjection;
using YtChannel.Core.Services;

namespace YtChannel.Cli.Commands;

internal static class DiscoverCommand
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
                    Console.Error.WriteLine($"Resolving channel: {channelInput}");
                    var channelInfo = await channelService.GetChannelInfoAsync(channelInput, cts.Token);
                    Console.Error.WriteLine($"Channel: {channelInfo.Title} ({channelInfo.ChannelId})");
                    Console.Error.WriteLine("Fetching video list...");

                    var videos = await channelService.GetChannelVideosAsync(channelInfo.ChannelId, cancellationToken: cts.Token);

                    Console.WriteLine($"{"VideoId",-15} {"PublishedAt",-12} Title");
                    Console.WriteLine(new string('-', 80));
                    foreach (var v in videos)
                    {
                        var published = v.PublishedAt?.Length >= 10 ? v.PublishedAt[..10] : "unknown";
                        var title = v.Title?.Length > 55 ? v.Title[..52] + "..." : v.Title ?? "(no title)";
                        Console.WriteLine($"{v.VideoId,-15} {published,-12} {title}");
                    }

                    Console.Error.WriteLine($"Total: {videos.Count} videos");
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
        Console.Error.WriteLine("Usage: yt-channel discover <channel-url-or-id>... [--channels-file <path>]");
    }

    private static bool IsHelp(string arg) =>
        arg is "--help" or "-h" or "help";
}
