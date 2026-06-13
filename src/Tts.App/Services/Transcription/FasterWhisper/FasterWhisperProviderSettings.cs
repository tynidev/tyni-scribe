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
            ProviderSettingKeys.ModelId,
            "Whisper model",
            ProviderSettingControlKind.Select,
            FasterWhisperModelCatalog.Models
                .Select(model => new ProviderSettingOption(model.Id, model.DisplayName))
                .ToArray(),
            Layout: ProviderSettingLayout.Compact),
        new ProviderSettingDescriptor(
            ProviderSettingKeys.ComputeType,
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
            ProviderSettingKeys.Language,
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
            ProviderSettingKeys.TimeoutSeconds,
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
            [ProviderSettingKeys.ModelId] = ResolveModelId(GetValue(settings, ProviderSettingKeys.ModelId)),
            [ProviderSettingKeys.ComputeType] = ResolveComputeType(GetValue(settings, ProviderSettingKeys.ComputeType)),
            [ProviderSettingKeys.Language] = NormalizeLanguage(GetValue(settings, ProviderSettingKeys.Language)),
            [ProviderSettingKeys.TimeoutSeconds] = ResolveTimeoutSeconds(GetValue(settings, ProviderSettingKeys.TimeoutSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        AddOptionalValue(normalized, ProviderSettingKeys.ModelPathOverride, GetValue(settings, ProviderSettingKeys.ModelPathOverride));

        return normalized;
    }

    public static FasterWhisperTranscriptionSettings Parse(IReadOnlyDictionary<string, string> settings)
    {
        var normalized = Normalize(settings);

        return new FasterWhisperTranscriptionSettings(
            normalized[ProviderSettingKeys.ModelId],
            GetValue(normalized, ProviderSettingKeys.ModelPathOverride),
            normalized[ProviderSettingKeys.Language],
            normalized[ProviderSettingKeys.ComputeType],
            ResolveTimeoutSeconds(normalized[ProviderSettingKeys.TimeoutSeconds]));
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
            .First(descriptor => descriptor.Key.Equals(ProviderSettingKeys.ComputeType, StringComparison.OrdinalIgnoreCase))
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