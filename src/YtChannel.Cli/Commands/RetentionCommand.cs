using Microsoft.Extensions.DependencyInjection;
using YtChannel.Core.Services;

namespace YtChannel.Cli.Commands;

internal static class RetentionCommand
{
    internal static async Task<int> RunAsync(string[] args, IServiceProvider serviceProvider)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteUsage();
            return 2;
        }

        if (!TryParseOptions(args, out var channelInput, out var maxAgeDays, out var clear, out var prune, out var error))
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
            var channelService = serviceProvider.GetRequiredService<IYouTubeChannelService>();
            var retentionService = serviceProvider.GetRequiredService<ChannelRetentionService>();
            var channelInfo = await channelService.GetChannelInfoAsync(channelInput, cts.Token);

            var result = await retentionService.SetMaxVideoAgeDaysAsync(
                channelInfo.ChannelId,
                clear ? null : maxAgeDays,
                GetDefaultOutputDirectory(),
                prune,
                cts.Token);

            Console.WriteLine($"Channel   : {channelInfo.Title} ({result.ChannelId})");
            Console.WriteLine($"Retention : {(result.MaxVideoAgeDays.HasValue ? $"{result.MaxVideoAgeDays.Value} days" : "none")}");
            Console.WriteLine($"Pruned DB videos          : {result.PrunedVideos}");
            Console.WriteLine($"Deleted artifact folders  : {result.DeletedArtifactDirectories}");
            return 0;
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

    private static bool TryParseOptions(
        string[] args,
        out string channelInput,
        out int? maxAgeDays,
        out bool clear,
        out bool prune,
        out string? error)
    {
        channelInput = string.Empty;
        maxAgeDays = null;
        clear = false;
        prune = false;
        error = null;

        var positional = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            switch (token)
            {
                case "--max-age-days":
                    if (!TryReadPositiveInt(args, ref i, token, out var days, out error)) return false;
                    maxAgeDays = days;
                    break;
                case "--max-age-months":
                    if (!TryReadPositiveInt(args, ref i, token, out var months, out error)) return false;
                    maxAgeDays = months * 30;
                    break;
                case "--clear":
                    clear = true;
                    break;
                case "--prune":
                    prune = true;
                    break;
                default:
                    if (token.StartsWith("-", StringComparison.Ordinal))
                    {
                        error = $"Unknown option '{token}'.";
                        return false;
                    }

                    positional.Add(token);
                    break;
            }
        }

        if (positional.Count != 1)
        {
            error = "Exactly one channel URL or ID is required.";
            return false;
        }

        if (clear == maxAgeDays.HasValue)
        {
            error = "Specify exactly one of --clear, --max-age-days, or --max-age-months.";
            return false;
        }

        channelInput = positional[0];
        return true;
    }

    private static bool TryReadPositiveInt(
        string[] args,
        ref int index,
        string optionName,
        out int value,
        out string? error)
    {
        value = 0;
        index++;
        if (index >= args.Length || !int.TryParse(args[index], out value) || value <= 0)
        {
            error = $"{optionName} must be a positive integer.";
            return false;
        }

        error = null;
        return true;
    }

    private static string GetDefaultOutputDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "yt-channel",
        "transcripts");

    private static void WriteUsage()
    {
        Console.Error.WriteLine("Usage: yt-channel retention <channel-url-or-id> --max-age-days <n>|--max-age-months <n> [--prune]");
        Console.Error.WriteLine("   or: yt-channel retention <channel-url-or-id> --clear");
    }

    private static bool IsHelp(string arg) => arg is "--help" or "-h" or "help";
}