using Tts.App.Services;

namespace Tts.App.Services.Transcription;

public static class WhisperCppProviderSettings
{
    public static IReadOnlyList<ProviderSettingDescriptor> Descriptors { get; } = new[]
    {
        new ProviderSettingDescriptor(
            ProviderSettingKeys.WhisperCppModelId,
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
}
