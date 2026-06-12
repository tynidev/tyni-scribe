using Tts.App.Services;

namespace Tts.App.Services.Transcription;

public static class FasterWhisperProviderSettings
{
    public const string DefaultComputeType = "float16";

    public static IReadOnlyList<ProviderSettingDescriptor> Descriptors { get; } = new[]
    {
        new ProviderSettingDescriptor(
            ProviderSettingKeys.FasterWhisperModelId,
            "Whisper model",
            ProviderSettingControlKind.Select,
            FasterWhisperModelCatalog.Models
                .Select(model => new ProviderSettingOption(model.Id, model.DisplayName))
                .ToArray(),
            Layout: ProviderSettingLayout.Compact),
        new ProviderSettingDescriptor(
            ProviderSettingKeys.FasterWhisperComputeType,
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
}