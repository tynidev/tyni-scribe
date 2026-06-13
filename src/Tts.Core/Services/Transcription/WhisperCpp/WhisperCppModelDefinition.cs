namespace Tts.Core.Services.Transcription;

public sealed record WhisperCppModelDefinition(
    string Id,
    string DisplayName,
    string FileName);