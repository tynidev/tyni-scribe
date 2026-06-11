namespace Tts.App.Services.Transcription;

public static class WhisperCppModelCatalog
{
    public const string TinyEnglishModelId = "tiny-en";
    public const string BaseEnglishModelId = "base-en";
    public const string SmallEnglishModelId = "small-en";
    public const string LargeV3TurboModelId = "large-v3-turbo";

    public static IReadOnlyList<WhisperCppModelDefinition> Models { get; } = new[]
    {
        new WhisperCppModelDefinition(TinyEnglishModelId, "Tiny English (fastest)", "ggml-tiny.en.bin"),
        new WhisperCppModelDefinition(BaseEnglishModelId, "Base English (balanced)", "ggml-base.en.bin"),
        new WhisperCppModelDefinition(SmallEnglishModelId, "Small English (better accuracy)", "ggml-small.en.bin"),
        new WhisperCppModelDefinition(LargeV3TurboModelId, "Large v3 Turbo (best local quality)", "ggml-large-v3-turbo.bin")
    };

    public static WhisperCppModelDefinition Resolve(string modelId)
    {
        return Models.FirstOrDefault(model => model.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The selected local Whisper model '{modelId}' is not supported by this build.");
    }
}