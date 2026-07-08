namespace TranscriptSummary.Cli;

internal static class CommandLineHelp
{
    public static void Write(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  transcript-summary summarize --input <transcript.json> --output <summary.md> [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --input <path>                    Transcript JSON file to summarize.");
        writer.WriteLine("  --output <path>                   File path where the summary text is written.");
        writer.WriteLine("  --prompt <text>                   Summary prompt.");
        writer.WriteLine("  --model <id>                      Model id. Defaults to gemma-4-26b-a4b-it.");
        writer.WriteLine("  --endpoint <url>                  OpenAI-compatible chat completions endpoint.");
        writer.WriteLine("  --context-tokens <value>          Loaded model context length. Defaults to 98304.");
        writer.WriteLine("  --reserved-output-tokens <value>  Tokens reserved for instructions and output. Defaults to 1024.");
        writer.WriteLine("  --max-output-tokens <value>       max_tokens sent to the endpoint. Defaults to 2048.");
        writer.WriteLine("  --chars-per-token <value>         Token estimate ratio. Defaults to 3.0.");
        writer.WriteLine("  --mode <hierarchical|single-pass> Summarization mode. Defaults to hierarchical.");
        writer.WriteLine("  --timeout-seconds <value>         Timeout per LLM request. Defaults to 600.");
        writer.WriteLine("  --metrics-output <path>           Write machine-readable timing/status JSON.");
        writer.WriteLine("  --estimate-only                   Plan chunks and merges without calling the model.");
        writer.WriteLine("  --help                            Show help.");
    }
}