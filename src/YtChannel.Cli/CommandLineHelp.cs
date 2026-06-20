namespace YtChannel.Cli;

internal static class CommandLineHelp
{
    internal static void Write(TextWriter writer)
    {
        writer.WriteLine("yt-channel - YouTube channel video discovery and batch transcript orchestrator");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  yt-channel <command> [options]");
        writer.WriteLine();
        writer.WriteLine("Commands:");
        writer.WriteLine("  discover <channel-url-or-id>... [--channels-file <path>]");
        writer.WriteLine("      List all videos for one or more channels (no DB write)");
        writer.WriteLine("  sync <channel-url-or-id>... [--channels-file <path>]");
        writer.WriteLine("      Sync one or more channels to local database");
        writer.WriteLine("  status <channel-url-or-id>... [--channels-file <path>]");
        writer.WriteLine("      Show transcript status for one or more channels");
        writer.WriteLine("  process <channel-url-or-id>... [--output-dir <dir>] [options]");
        writer.WriteLine("      Discover/sync and process pending videos for one or more channels");
        writer.WriteLine();
        writer.WriteLine("Shared channel input:");
        writer.WriteLine("  Positional channels can be provided to all commands.");
        writer.WriteLine("  --channels-file <path> can be used with all commands.");
        writer.WriteLine("  Inputs are deduplicated case-insensitively in first-seen order.");
        writer.WriteLine();
        writer.WriteLine("Process options:");
        writer.WriteLine("  --output-dir, -o <dir>      Root directory for transcript output");
        writer.WriteLine("                              (default: %AppData%\\yt-channel\\transcripts)");
        writer.WriteLine("                              Channel output: {channel-id}\\{video-id}");
        writer.WriteLine("  --channels-file <path>      Text file with channels (blank lines and # comments ignored)");
        writer.WriteLine("  --max-videos <n>            Limit to first N pending videos per channel");
        writer.WriteLine("  --language <lang>           Caption language preference (default: en)");
        writer.WriteLine("  --caption-language <lang>   Alias for --language");
        writer.WriteLine("  --force-audio               Skip captions, use audio transcription");
        writer.WriteLine("  --include-shorts            Include Shorts (default excludes UUSH videos <= 180s)");
        writer.WriteLine("  --provider <id>             Transcription provider ID override");
        writer.WriteLine("  --watch                     Repeat process cycle every 30 minutes until Ctrl+C");
        writer.WriteLine();
        writer.WriteLine("Exit codes:");
        writer.WriteLine("  0   Success");
        writer.WriteLine("  1   One or more channels failed");
        writer.WriteLine("  2   Invalid CLI usage");
        writer.WriteLine("  130 Canceled (Ctrl+C)");
        writer.WriteLine();
        writer.WriteLine("Configuration:");
        writer.WriteLine("  YouTube API key: set YOUTUBE_API_KEY environment variable");
        writer.WriteLine("  Database:        %AppData%\\yt-channel\\channel-data.db");
        writer.WriteLine();
        writer.WriteLine("Examples:");
        writer.WriteLine("  yt-channel discover https://www.youtube.com/@ChannelOne https://www.youtube.com/@ChannelTwo");
        writer.WriteLine("  yt-channel sync --channels-file channels.txt");
        writer.WriteLine("  yt-channel status https://www.youtube.com/@ChannelOne --channels-file channels.txt");
        writer.WriteLine("  yt-channel process https://www.youtube.com/@ChannelOne --output-dir C:\\transcripts");
        writer.WriteLine("  yt-channel process --channels-file channels.txt --output-dir C:\\transcripts --max-videos 10 --watch");
    }
}
