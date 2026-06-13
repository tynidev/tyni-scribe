namespace Tts.Cli;

internal static class CommandLineHelp
{
    public static void Write(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  Tts.Cli transcribe --audio <path> [options]");
        writer.WriteLine("  Tts.Cli transcribe-batch --manifest <path> --output-csv <path> [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --provider <id>            Transcription provider id. Defaults to app config.");
        writer.WriteLine("  --config <path>            Read app-compatible settings JSON from this path.");
        writer.WriteLine("  --model <id>               Override provider modelId setting.");
        writer.WriteLine("  --language <code>          Override provider language setting.");
        writer.WriteLine("  --timeout-seconds <value>  Override provider timeoutSeconds setting.");
        writer.WriteLine("  --setting <key=value>      Override any provider setting. Can repeat.");
        writer.WriteLine("  --metrics-output <path>    Write machine-readable timing/status JSON.");
        writer.WriteLine("  --output-csv <path>        Batch command CSV output path.");
        writer.WriteLine("  --output-json <path>       Batch command JSON output path.");
        writer.WriteLine("  --count <value>            Batch command maximum measured files.");
        writer.WriteLine("  --warmup-first-file        Batch command transcribes first file once before measuring.");
        writer.WriteLine("  --help                     Show help.");
    }
}