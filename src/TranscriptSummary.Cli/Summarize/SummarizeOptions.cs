using System.Globalization;
using TranscriptSummary.Core.Services;

namespace TranscriptSummary.Cli.Summarize;

internal sealed class SummarizeOptions
{
    public const string DefaultPrompt = TranscriptSummaryDefaults.Prompt;
    public const string DefaultModel = TranscriptSummaryDefaults.Model;
    public const string DefaultEndpoint = TranscriptSummaryDefaults.Endpoint;
    public const int DefaultContextTokens = TranscriptSummaryDefaults.ContextTokens;
    public const int DefaultReservedOutputTokens = TranscriptSummaryDefaults.ReservedOutputTokens;
    public const int DefaultMaxOutputTokens = TranscriptSummaryDefaults.MaxOutputTokens;
    public const double DefaultCharsPerToken = TranscriptSummaryDefaults.CharsPerToken;
    public const int DefaultTimeoutSeconds = TranscriptSummaryDefaults.TimeoutSeconds;

    public string? InputPath { get; init; }

    public string? OutputPath { get; init; }

    public string Prompt { get; init; } = DefaultPrompt;

    public string Model { get; init; } = DefaultModel;

    public Uri Endpoint { get; init; } = new(DefaultEndpoint);

    public int ContextTokens { get; init; } = DefaultContextTokens;

    public int ReservedOutputTokens { get; init; } = DefaultReservedOutputTokens;

    public int MaxOutputTokens { get; init; } = DefaultMaxOutputTokens;

    public double CharsPerToken { get; init; } = DefaultCharsPerToken;

    public TranscriptSummaryMode Mode { get; init; } = TranscriptSummaryMode.Hierarchical;

    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;

    public string? MetricsOutputPath { get; init; }

    public bool EstimateOnly { get; init; }

    public static bool TryParse(string[] args, TextWriter errorWriter, out SummarizeOptions options)
    {
        string? inputPath = null;
        string? outputPath = null;
        var prompt = DefaultPrompt;
        var model = DefaultModel;
        var endpoint = new Uri(DefaultEndpoint);
        var contextTokens = DefaultContextTokens;
        var reservedOutputTokens = DefaultReservedOutputTokens;
        var maxOutputTokens = DefaultMaxOutputTokens;
        var charsPerToken = DefaultCharsPerToken;
        var mode = TranscriptSummaryMode.Hierarchical;
        var timeoutSeconds = DefaultTimeoutSeconds;
        string? metricsOutputPath = null;
        var estimateOnly = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.Equals("--help", StringComparison.OrdinalIgnoreCase) || argument.Equals("-h", StringComparison.OrdinalIgnoreCase))
            {
                CommandLineHelp.Write(errorWriter);
                options = new SummarizeOptions();
                return false;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                errorWriter.WriteLine($"Unexpected argument '{argument}'.");
                options = new SummarizeOptions();
                return false;
            }

            var option = argument;
            string? inlineValue = null;
            var equalsIndex = argument.IndexOf('=');
            if (equalsIndex > 0)
            {
                option = argument[..equalsIndex];
                inlineValue = argument[(equalsIndex + 1)..];
            }

            switch (option.ToLowerInvariant())
            {
                case "--estimate-only":
                    estimateOnly = true;
                    break;
                case "--input":
                    inputPath = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
                    if (inputPath is null)
                    {
                        options = new SummarizeOptions();
                        return false;
                    }

                    break;
                case "--output":
                    outputPath = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
                    if (outputPath is null)
                    {
                        options = new SummarizeOptions();
                        return false;
                    }

                    break;
                case "--prompt":
                    var parsedPrompt = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
                    if (parsedPrompt is null)
                    {
                        options = new SummarizeOptions();
                        return false;
                    }

                    prompt = parsedPrompt;
                    break;
                case "--model":
                    var parsedModel = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
                    if (parsedModel is null)
                    {
                        options = new SummarizeOptions();
                        return false;
                    }

                    model = parsedModel;
                    break;
                case "--endpoint":
                    var parsedEndpoint = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
                    if (parsedEndpoint is null || !TryParseUri(parsedEndpoint, errorWriter, out endpoint))
                    {
                        options = new SummarizeOptions();
                        return false;
                    }

                    break;
                case "--context-tokens":
                    if (!TryReadInt(args, ref index, option, inlineValue, errorWriter, out contextTokens))
                    {
                        options = new SummarizeOptions();
                        return false;
                    }

                    break;
                case "--reserved-output-tokens":
                    if (!TryReadInt(args, ref index, option, inlineValue, errorWriter, out reservedOutputTokens))
                    {
                        options = new SummarizeOptions();
                        return false;
                    }

                    break;
                case "--max-output-tokens":
                    if (!TryReadInt(args, ref index, option, inlineValue, errorWriter, out maxOutputTokens))
                    {
                        options = new SummarizeOptions();
                        return false;
                    }

                    break;
                case "--chars-per-token":
                    if (!TryReadDouble(args, ref index, option, inlineValue, errorWriter, out charsPerToken))
                    {
                        options = new SummarizeOptions();
                        return false;
                    }

                    break;
                case "--mode":
                    var parsedMode = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
                    if (parsedMode is null || !TryParseMode(parsedMode, errorWriter, out mode))
                    {
                        options = new SummarizeOptions();
                        return false;
                    }

                    break;
                case "--timeout-seconds":
                    if (!TryReadInt(args, ref index, option, inlineValue, errorWriter, out timeoutSeconds))
                    {
                        options = new SummarizeOptions();
                        return false;
                    }

                    break;
                case "--metrics-output":
                    metricsOutputPath = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
                    if (metricsOutputPath is null)
                    {
                        options = new SummarizeOptions();
                        return false;
                    }

                    break;
                default:
                    errorWriter.WriteLine($"Unknown option '{option}'.");
                    options = new SummarizeOptions();
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            errorWriter.WriteLine("Missing required --input option.");
            options = new SummarizeOptions();
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            errorWriter.WriteLine("Missing required --output option.");
            options = new SummarizeOptions();
            return false;
        }

        if (!File.Exists(inputPath))
        {
            errorWriter.WriteLine("The input transcript file was not found.");
            options = new SummarizeOptions();
            return false;
        }

        if (contextTokens <= 0 || reservedOutputTokens <= 0 || maxOutputTokens <= 0 || timeoutSeconds <= 0)
        {
            errorWriter.WriteLine("Token and timeout values must be greater than zero.");
            options = new SummarizeOptions();
            return false;
        }

        if (reservedOutputTokens >= contextTokens)
        {
            errorWriter.WriteLine("--reserved-output-tokens must be lower than --context-tokens.");
            options = new SummarizeOptions();
            return false;
        }

        if (charsPerToken <= 0)
        {
            errorWriter.WriteLine("--chars-per-token must be greater than zero.");
            options = new SummarizeOptions();
            return false;
        }

        options = new SummarizeOptions
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            Prompt = prompt,
            Model = model,
            Endpoint = endpoint,
            ContextTokens = contextTokens,
            ReservedOutputTokens = reservedOutputTokens,
            MaxOutputTokens = maxOutputTokens,
            CharsPerToken = charsPerToken,
            Mode = mode,
            TimeoutSeconds = timeoutSeconds,
            MetricsOutputPath = metricsOutputPath,
            EstimateOnly = estimateOnly
        };
        return true;
    }

    private static string? ReadRequiredValue(string[] args, ref int index, string option, string? inlineValue, TextWriter errorWriter)
    {
        if (inlineValue is not null)
        {
            return inlineValue;
        }

        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            errorWriter.WriteLine($"Missing value for {option}.");
            return null;
        }

        index++;
        return args[index];
    }

    private static bool TryReadInt(string[] args, ref int index, string option, string? inlineValue, TextWriter errorWriter, out int value)
    {
        var text = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
        if (text is null)
        {
            value = 0;
            return false;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            errorWriter.WriteLine($"{option} must be an integer.");
            return false;
        }

        return true;
    }

    private static bool TryReadDouble(string[] args, ref int index, string option, string? inlineValue, TextWriter errorWriter, out double value)
    {
        var text = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
        if (text is null)
        {
            value = 0;
            return false;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            errorWriter.WriteLine($"{option} must be a number.");
            return false;
        }

        return true;
    }

    private static bool TryParseUri(string value, TextWriter errorWriter, out Uri endpoint)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out endpoint!))
        {
            errorWriter.WriteLine("--endpoint must be an absolute URL.");
            return false;
        }

        return true;
    }

    private static bool TryParseMode(string value, TextWriter errorWriter, out TranscriptSummaryMode mode)
    {
        if (value.Equals("hierarchical", StringComparison.OrdinalIgnoreCase))
        {
            mode = TranscriptSummaryMode.Hierarchical;
            return true;
        }

        if (value.Equals("single-pass", StringComparison.OrdinalIgnoreCase))
        {
            mode = TranscriptSummaryMode.SinglePass;
            return true;
        }

        errorWriter.WriteLine("--mode must be hierarchical or single-pass.");
        mode = TranscriptSummaryMode.Hierarchical;
        return false;
    }
}