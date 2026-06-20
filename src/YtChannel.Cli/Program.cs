using Microsoft.Extensions.DependencyInjection;
using Tts.Core;
using YtChannel.Cli;
using YtChannel.Cli.Commands;
using YtChannel.Core;
using YtChannel.Core.Configuration;
using YtChannel.Core.Data;
using YtScribe.Core;

DotEnvLoader.Load();

if (args.Length == 0 || IsHelp(args[0]))
{
    CommandLineHelp.Write(Console.Out);
    return 0;
}

var command = args[0];
var commandArgs = args.Skip(1).ToArray();

switch (command.ToLowerInvariant())
{
    case "discover":
        await using (var sp = BuildServiceProvider(requireDb: false))
            return await DiscoverCommand.RunAsync(commandArgs, sp);

    case "sync":
        await using (var sp = BuildServiceProvider(requireDb: true))
        {
            await InitializeDbAsync(sp);
            return await SyncCommand.RunAsync(commandArgs, sp);
        }

    case "status":
        await using (var sp = BuildServiceProvider(requireDb: true))
        {
            await InitializeDbAsync(sp);
            return await StatusCommand.RunAsync(commandArgs, sp);
        }

    case "process":
        await using (var sp = BuildServiceProvider(requireDb: true))
        {
            await InitializeDbAsync(sp);
            return await ProcessCommand.RunAsync(commandArgs, sp);
        }

    default:
        Console.Error.WriteLine($"Unknown command '{command}'.");
        CommandLineHelp.Write(Console.Error);
        return 2;
}

static ServiceProvider BuildServiceProvider(bool requireDb)
{
    var services = new ServiceCollection();

    // Core TTS and yt-scribe services (transcription, ffmpeg, yt-dlp)
    services.AddTtsCoreServices();
    services.AddYtScribeCoreServices();

    // YouTube API settings
    var apiSettings = new YouTubeApiSettings();
    // API key can also be set via YOUTUBE_API_KEY environment variable.
    // No validation here — commands that need the API will surface the error.

    // Database path
    var appDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "yt-channel");
    Directory.CreateDirectory(appDataDir);
    var dbPath = Path.Combine(appDataDir, "channel-data.db");

    // Register yt-channel core services
    services.AddYtChannelCoreServices(dbPath, apiSettings);

    return services.BuildServiceProvider(validateScopes: true);
}

static async Task InitializeDbAsync(ServiceProvider sp)
{
    var context = sp.GetRequiredService<ChannelDbContext>();
    await ChannelDbInitializer.InitializeAsync(context);
}

static bool IsHelp(string value) =>
    value.Equals("--help", StringComparison.OrdinalIgnoreCase)
    || value.Equals("-h", StringComparison.OrdinalIgnoreCase)
    || value.Equals("help", StringComparison.OrdinalIgnoreCase);
