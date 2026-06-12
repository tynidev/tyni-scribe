namespace Tts.App.Services;

public static class ProviderSettingKeys
{
    public const string WhisperCppModelId = "transcription.whisperCpp.modelId";
    public const string FasterWhisperModelId = "transcription.fasterWhisper.modelId";
    public const string FasterWhisperComputeType = "transcription.fasterWhisper.computeType";
    public const string TranscriptionLanguage = "transcription.language";
    public const string TranscriptionTimeoutSeconds = "transcription.timeoutSeconds";
}