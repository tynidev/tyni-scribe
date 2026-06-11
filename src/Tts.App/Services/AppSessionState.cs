namespace Tts.App.Services;

public enum AppSessionState
{
    Idle,
    Recording,
    Processing,
    Outputting,
    Error
}