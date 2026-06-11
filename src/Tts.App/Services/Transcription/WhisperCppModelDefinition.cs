namespace Tts.App.Services.Transcription;

public sealed record WhisperCppModelDefinition(
    string Id,
    string DisplayName,
    string FileName);