using Tts.App.Services;

namespace Tts.App.Services.Transcription;

public static class WhisperCppProviderSettings
{
    private const string DefaultLanguage = "en";
    private const int DefaultTimeoutSeconds = 600;

    public static IReadOnlyList<ProviderSettingDescriptor> Descriptors { get; } = new[]
    {
        new ProviderSettingDescriptor(
            ProviderSettingKeys.TranscriptionModelId,
            "Whisper model",
            ProviderSettingControlKind.Select,
            WhisperCppModelCatalog.Models
                .Select(model => new ProviderSettingOption(model.Id, model.DisplayName))
                .ToArray(),
            Layout: ProviderSettingLayout.Compact),
        new ProviderSettingDescriptor(
            ProviderSettingKeys.TranscriptionLanguage,
            "Language",
            ProviderSettingControlKind.Select,
            new[]
            {
                new ProviderSettingOption("en", "English"),
                new ProviderSettingOption("auto", "Auto detect"),
                new ProviderSettingOption("es", "Spanish"),
                new ProviderSettingOption("fr", "French"),
                new ProviderSettingOption("de", "German"),
                new ProviderSettingOption("it", "Italian"),
                new ProviderSettingOption("pt", "Portuguese"),
                new ProviderSettingOption("nl", "Dutch"),
                new ProviderSettingOption("ja", "Japanese"),
                new ProviderSettingOption("ko", "Korean"),
                new ProviderSettingOption("zh", "Chinese")
            },
            Layout: ProviderSettingLayout.Compact),
        new ProviderSettingDescriptor(
            ProviderSettingKeys.TranscriptionTimeoutSeconds,
            "Timeout seconds",
            ProviderSettingControlKind.Integer,
            Layout: ProviderSettingLayout.Compact)
    };

    public static Dictionary<string, string> CreateDefaultValues()
    {
        return Normalize(null);
    }

    public static Dictionary<string, string> Normalize(IReadOnlyDictionary<string, string>? settings)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ProviderSettingKeys.TranscriptionModelId] = ResolveModelId(GetValue(settings, ProviderSettingKeys.TranscriptionModelId)),
            [ProviderSettingKeys.TranscriptionLanguage] = NormalizeLanguage(GetValue(settings, ProviderSettingKeys.TranscriptionLanguage)),
            [ProviderSettingKeys.TranscriptionTimeoutSeconds] = ResolveTimeoutSeconds(GetValue(settings, ProviderSettingKeys.TranscriptionTimeoutSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        AddOptionalValue(normalized, ProviderSettingKeys.TranscriptionExecutablePathOverride, GetValue(settings, ProviderSettingKeys.TranscriptionExecutablePathOverride));
        AddOptionalValue(normalized, ProviderSettingKeys.TranscriptionModelPathOverride, GetValue(settings, ProviderSettingKeys.TranscriptionModelPathOverride));

        return normalized;
    }

    public static WhisperCppTranscriptionSettings Parse(IReadOnlyDictionary<string, string> settings)
    {
        var normalized = Normalize(settings);

        return new WhisperCppTranscriptionSettings(
            normalized[ProviderSettingKeys.TranscriptionModelId],
            GetValue(normalized, ProviderSettingKeys.TranscriptionExecutablePathOverride),
            GetValue(normalized, ProviderSettingKeys.TranscriptionModelPathOverride),
            normalized[ProviderSettingKeys.TranscriptionLanguage],
            ResolveTimeoutSeconds(normalized[ProviderSettingKeys.TranscriptionTimeoutSeconds]));
    }

    private static string ResolveModelId(string? modelId)
    {
        return WhisperCppModelCatalog.Models.Any(model => model.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase))
            ? modelId!
            : WhisperCppModelCatalog.TinyEnglishModelId;
    }

    private static string NormalizeLanguage(string? language)
    {
        return string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language.Trim();
    }

    private static int ResolveTimeoutSeconds(string? timeoutSeconds)
    {
        return int.TryParse(timeoutSeconds, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : DefaultTimeoutSeconds;
    }

    private static string? GetValue(IReadOnlyDictionary<string, string>? settings, string key)
    {
        return settings is not null && settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static void AddOptionalValue(Dictionary<string, string> settings, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            settings[key] = value.Trim();
        }
    }
}

public sealed record WhisperCppTranscriptionSettings(
    string ModelId,
    string? ExecutablePathOverride,
    string? ModelPathOverride,
    string Language,
    int TimeoutSeconds);
