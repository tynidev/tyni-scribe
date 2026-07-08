namespace YtScribe.Cli;

internal static class CommandLineHelp
{
    public static void Write(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  yt-scribe export --url <youtube-url> --output-dir <path> [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --url <url>                YouTube video URL.");
        writer.WriteLine("  --output-dir <path>        Directory where video artifacts are written.");
        writer.WriteLine("  --caption-language <code>  Preferred caption language. Defaults to en.");
        writer.WriteLine("  --force-audio              Download audio and transcribe instead of using captions.");
        writer.WriteLine("  --provider <id>            Fallback transcription provider id. Defaults to app config.");
        writer.WriteLine("  --config <path>            Read app-compatible settings JSON from this path.");
        writer.WriteLine("  --model <id>               Override provider modelId setting. Defaults to tiny-en.");
        writer.WriteLine("  --language <code>          Override provider language setting.");
        writer.WriteLine("  --timeout-seconds <value>  Override provider timeoutSeconds setting.");
        writer.WriteLine("  --setting <key=value>      Override any provider setting. Can repeat.");
        writer.WriteLine("  --metrics-output <path>    Write machine-readable export timing/status JSON.");
        writer.WriteLine("  --overwrite                Replace an existing video output directory.");
        writer.WriteLine("  --keep-temp                Keep temporary downloaded/converted audio files.");
        writer.WriteLine("  --no-transcript-text       Do not write transcript.txt convenience output.");
        writer.WriteLine("  --help                     Show help.");
    }
}