namespace Tts.App.Services.Transcription;

public static class FasterWhisperModelCatalog
{
    public const string TinyEnglishModelId = "tiny-en";
    public const string BaseEnglishModelId = "base-en";
    public const string SmallEnglishModelId = "small-en";
    public const string LargeV3TurboModelId = "large-v3-turbo";

    public static IReadOnlyList<FasterWhisperModelDefinition> Models { get; } = new[]
    {
        new FasterWhisperModelDefinition(TinyEnglishModelId, "Tiny English (fastest)", "tiny-en", "Systran/faster-whisper-tiny.en"),
        new FasterWhisperModelDefinition(BaseEnglishModelId, "Base English (balanced)", "base-en", "Systran/faster-whisper-base.en"),
        new FasterWhisperModelDefinition(SmallEnglishModelId, "Small English (better accuracy)", "small-en", "Systran/faster-whisper-small.en"),
        new FasterWhisperModelDefinition(LargeV3TurboModelId, "Large v3 Turbo (best local quality)", "large-v3-turbo", "mobiuslabsgmbh/faster-whisper-large-v3-turbo")
    };

    public static FasterWhisperModelDefinition Resolve(string modelId)
    {
        return Models.FirstOrDefault(model => model.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The selected faster-whisper model '{modelId}' is not supported by this build.");
    }
}