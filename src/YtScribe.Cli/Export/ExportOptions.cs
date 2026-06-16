using Tts.Core.Services;
using Tts.Core.Services.Transcription;

namespace YtScribe.Cli.Export;

internal sealed class ExportOptions
{
    public string? Url { get; init; }

    public string? OutputDirectory { get; init; }

    public string CaptionLanguage { get; init; } = "en.*";

    public bool ForceAudio { get; init; }

    public bool Overwrite { get; init; }

    public bool KeepTemp { get; init; }

    public bool WritePlainText { get; init; } = true;

    public string? ProviderId { get; init; }

    public string? ConfigPath { get; init; }

    public string? MetricsOutputPath { get; init; }

    public IReadOnlyDictionary<string, string> SettingOverrides { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static bool TryParse(string[] args, TextWriter errorWriter, out ExportOptions options)
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? url = null;
        string? outputDirectory = null;
        string captionLanguage = "en.*";
        string? providerId = null;
        string? configPath = null;
        string? metricsOutputPath = null;
        var forceAudio = false;
        var overwrite = false;
        var keepTemp = false;
        var writePlainText = true;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.Equals("--help", StringComparison.OrdinalIgnoreCase) || argument.Equals("-h", StringComparison.OrdinalIgnoreCase))
            {
                CommandLineHelp.Write(errorWriter);
                options = new ExportOptions();
                return false;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                errorWriter.WriteLine($"Unexpected argument '{argument}'.");
                options = new ExportOptions();
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
                case "--force-audio":
                    forceAudio = true;
                    break;
                case "--overwrite":
                    overwrite = true;
                    break;
                case "--keep-temp":
                    keepTemp = true;
                    break;
                case "--no-transcript-text":
                    writePlainText = false;
                    break;
                case "--url":
                    url = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
                    if (url is null)
                    {
                        options = new ExportOptions();
                        return false;
                    }

                    break;
                case "--output-dir":
                    outputDirectory = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
                    if (outputDirectory is null)
                    {
                        options = new ExportOptions();
                        return false;
                    }

                    break;
                case "--caption-language":
                    var parsedCaptionLanguage = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
                    if (parsedCaptionLanguage is null)
                    {
                        options = new ExportOptions();
                        return false;
                    }

                    captionLanguage = parsedCaptionLanguage;
                    break;
                case "--provider":
                    providerId = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
                    if (providerId is null)
                    {
                        options = new ExportOptions();
                        return false;
                    }

                    break;
                case "--config":
                    configPath = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
                    if (configPath is null)
                    {
                        options = new ExportOptions();
                        return false;
                    }

                    break;
                case "--metrics-output":
                    metricsOutputPath = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
                    if (metricsOutputPath is null)
                    {
                        options = new ExportOptions();
                        return false;
                    }

                    break;
                case "--model":
                    if (!TryReadSettingValue(args, ref index, option, inlineValue, ProviderSettingKeys.ModelId, overrides, errorWriter))
                    {
                        options = new ExportOptions();
                        return false;
                    }

                    break;
                case "--language":
                    if (!TryReadSettingValue(args, ref index, option, inlineValue, ProviderSettingKeys.Language, overrides, errorWriter))
                    {
                        options = new ExportOptions();
                        return false;
                    }

                    break;
                case "--timeout-seconds":
                    if (!TryReadSettingValue(args, ref index, option, inlineValue, ProviderSettingKeys.TimeoutSeconds, overrides, errorWriter))
                    {
                        options = new ExportOptions();
                        return false;
                    }

                    break;
                case "--setting":
                    var setting = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
                    if (setting is null || !TryAddSettingOverride(setting, overrides, errorWriter))
                    {
                        options = new ExportOptions();
                        return false;
                    }

                    break;
                default:
                    errorWriter.WriteLine($"Unknown option '{option}'.");
                    options = new ExportOptions();
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            errorWriter.WriteLine("Missing required --url option.");
            options = new ExportOptions();
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            errorWriter.WriteLine("Missing required --output-dir option.");
            options = new ExportOptions();
            return false;
        }

        if (!overrides.ContainsKey(ProviderSettingKeys.ModelId))
        {
            overrides[ProviderSettingKeys.ModelId] = WhisperCppModelCatalog.TinyEnglishModelId;
        }

        options = new ExportOptions
        {
            Url = url,
            OutputDirectory = outputDirectory,
            CaptionLanguage = captionLanguage,
            ForceAudio = forceAudio,
            Overwrite = overwrite,
            KeepTemp = keepTemp,
            WritePlainText = writePlainText,
            ProviderId = providerId,
            ConfigPath = configPath,
            MetricsOutputPath = metricsOutputPath,
            SettingOverrides = overrides
        };
        return true;
    }

    private static bool TryReadSettingValue(
        string[] args,
        ref int index,
        string option,
        string? inlineValue,
        string settingKey,
        Dictionary<string, string> overrides,
        TextWriter errorWriter)
    {
        var value = ReadRequiredValue(args, ref index, option, inlineValue, errorWriter);
        if (value is null)
        {
            return false;
        }

        overrides[settingKey] = value;
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