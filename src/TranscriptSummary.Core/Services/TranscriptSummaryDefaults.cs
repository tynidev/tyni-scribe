namespace TranscriptSummary.Core.Services;

public static class TranscriptSummaryDefaults
{
    public const string Prompt = "Read this transcript and return a high level overview in smart brevity format";
    public const string Model = "gemma-4-26b-a4b-it";
    public const string Endpoint = "http://localhost:1234/v1/chat/completions";
    public const int ContextTokens = 98304;
    public const int ReservedOutputTokens = 1024;
    public const int MaxOutputTokens = 2048;
    public const double CharsPerToken = 3.0;
    public const int TimeoutSeconds = 600;
}