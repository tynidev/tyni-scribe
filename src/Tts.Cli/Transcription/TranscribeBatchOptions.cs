using Tts.Core.Services;

namespace Tts.Cli.Transcription;

internal sealed class TranscribeBatchOptions
{
    public string? ManifestPath { get; init; }

    public string? ProviderId { get; init; }

    public string? ConfigPath { get; init; }

    public string? OutputCsvPath { get; init; }

    public string? OutputJsonPath { get; init; }

    public int Count { get; init; } = 1000;

    public bool WarmupFirstFile { get; init; }

    public IReadOnlyDictionary<string, string> SettingOverrides { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static bool TryParse(string[] args, TextWriter errorWriter, out TranscribeBatchOptions options)
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? manifestPath = null;
        string? providerId = null;
        string? configPath = null;
        string? outputCsvPath = null;
        string? outputJsonPath = null;
        var count = 1000;
        var warmupFirstFile = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.Equals("--help", StringComparison.OrdinalIgnoreCase) || argument.Equals("-h", StringComparison.OrdinalIgnoreCase))
            {
                CommandLineHelp.Write(errorWriter);
                options = new TranscribeBatchOptions();
                return false;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                errorWriter.WriteLine($"Unexpected argument '{argument}'.");
                options = new TranscribeBatchOptions();
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
                case "--warmup-first-file":
                    warmupFirstFile = true;
                    continue;
            }

            var value = inlineValue ?? ReadValue(args, ref index, option, errorWriter);
            if (value is null)
            {
                options = new TranscribeBatchOptions();
                return false;
            }

            switch (option.ToLowerInvariant())
            {
                case "--manifest":
                    manifestPath = value;
                    break;
                case "--provider":
                    providerId = value;
                    break;
                case "--config":
                    configPath = value;
                    break;
                case "--output-csv":
                    outputCsvPath = value;
                    break;
                case "--output-json":
                    outputJsonPath = value;
                    break;
                case "--count":
                    if (!int.TryParse(value, out count) || count <= 0)
                    {
                        errorWriter.WriteLine("--count must be a positive integer.");
                        options = new TranscribeBatchOptions();
                        return false;
                    }

                    break;
                case "--model":
                    overrides[ProviderSettingKeys.ModelId] = value;
                    break;
                case "--language":
                    overrides[ProviderSettingKeys.Language] = value;
                    break;
                case "--timeout-seconds":
                    overrides[ProviderSettingKeys.TimeoutSeconds] = value;
                    break;
                case "--setting":
                    if (!TryAddSettingOverride(value, overrides, errorWriter))
                    {
                        options = new TranscribeBatchOptions();
                        return false;
                    }

                    break;
                default:
                    errorWriter.WriteLine($"Unknown option '{option}'.");
                    options = new TranscribeBatchOptions();
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            errorWriter.WriteLine("Missing required --manifest option.");
            options = new TranscribeBatchOptions();
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputCsvPath))
        {
            errorWriter.WriteLine("Missing required --output-csv option.");
            options = new TranscribeBatchOptions();
            return false;
        }

        options = new TranscribeBatchOptions
        {
            ManifestPath = manifestPath,
            ProviderId = providerId,
            ConfigPath = configPath,
            OutputCsvPath = outputCsvPath,
            OutputJsonPath = outputJsonPath,
            Count = count,
            WarmupFirstFile = warmupFirstFile,
            SettingOverrides = overrides
        };
        return true;
    }

    private static string? ReadValue(string[] args, ref int index, string option, TextWriter errorWriter)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            errorWriter.WriteLine($"Missing value for {option}.");
            return null;
        }

        index++;
        return args[index];
    }

    private static bool TryAddSettingOverride(string value, Dictionary<string, string> overrides, TextWriter errorWriter)
    {
        var separatorIndex = value.IndexOf('=');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            errorWriter.WriteLine("--setting values must use key=value syntax.");
            return false;
        }

        var key = value[..separatorIndex].Trim();
        var settingValue = value[(separatorIndex + 1)..].Trim();
        if (key.Length == 0 || settingValue.Length == 0)
        {
            errorWriter.WriteLine("--setting values must include a non-empty key and value.");
            return false;
        }

        overrides[key] = settingValue;
        return true;
    }
}