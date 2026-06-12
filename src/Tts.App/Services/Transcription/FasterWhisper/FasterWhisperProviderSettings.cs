using Tts.App.Services;

namespace Tts.App.Services.Transcription;

public static class FasterWhisperProviderSettings
{
    private const string DefaultLanguage = "en";
    private const int DefaultTimeoutSeconds = 600;

    public const string DefaultComputeType = "float16";

    public static IReadOnlyList<ProviderSettingDescriptor> Descriptors { get; } = new[]
    {
        new ProviderSettingDescriptor(
            ProviderSettingKeys.TranscriptionModelId,
            "Whisper model",
            ProviderSettingControlKind.Select,
            FasterWhisperModelCatalog.Models
                .Select(model => new ProviderSettingOption(model.Id, model.DisplayName))
                .ToArray(),
            Layout: ProviderSettingLayout.Compact),
        new ProviderSettingDescriptor(
            ProviderSettingKeys.TranscriptionComputeType,
            "Compute",
            ProviderSettingControlKind.Select,
            new[]
            {
                new ProviderSettingOption("float16", "CUDA float16"),
                new ProviderSettingOption("int8_float16", "CUDA int8/float16"),
                new ProviderSettingOption("auto", "Auto"),
                new ProviderSettingOption("int8", "CPU/GPU int8"),
                new ProviderSettingOption("float32", "CPU float32")
            },
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
            [ProviderSettingKeys.TranscriptionComputeType] = ResolveComputeType(GetValue(settings, ProviderSettingKeys.TranscriptionComputeType)),
            [ProviderSettingKeys.TranscriptionLanguage] = NormalizeLanguage(GetValue(settings, ProviderSettingKeys.TranscriptionLanguage)),
            [ProviderSettingKeys.TranscriptionTimeoutSeconds] = ResolveTimeoutSeconds(GetValue(settings, ProviderSettingKeys.TranscriptionTimeoutSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        AddOptionalValue(normalized, ProviderSettingKeys.TranscriptionModelPathOverride, GetValue(settings, ProviderSettingKeys.TranscriptionModelPathOverride));

        return normalized;
    }

    public static FasterWhisperTranscriptionSettings Parse(IReadOnlyDictionary<string, string> settings)
    {
        var normalized = Normalize(settings);

        return new FasterWhisperTranscriptionSettings(
            normalized[ProviderSettingKeys.TranscriptionModelId],
            GetValue(normalized, ProviderSettingKeys.TranscriptionModelPathOverride),
            normalized[ProviderSettingKeys.TranscriptionLanguage],
            normalized[ProviderSettingKeys.TranscriptionComputeType],
            ResolveTimeoutSeconds(normalized[ProviderSettingKeys.TranscriptionTimeoutSeconds]));
    }

    private static string ResolveModelId(string? modelId)
    {
        return FasterWhisperModelCatalog.Models.Any(model => model.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase))
            ? modelId!
            : FasterWhisperModelCatalog.TinyEnglishModelId;
    }

    private static string ResolveComputeType(string? computeType)
    {
        return Descriptors
            .First(descriptor => descriptor.Key.Equals(ProviderSettingKeys.TranscriptionComputeType, StringComparison.OrdinalIgnoreCase))
            .Options!
            .Any(option => option.Value.Equals(computeType, StringComparison.OrdinalIgnoreCase))
            ? computeType!.Trim()
            : DefaultComputeType;
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

public sealed record FasterWhisperTranscriptionSettings(
    string ModelId,
    string? ModelPathOverride,
    string Language,
    string ComputeType,
    int TimeoutSeconds);