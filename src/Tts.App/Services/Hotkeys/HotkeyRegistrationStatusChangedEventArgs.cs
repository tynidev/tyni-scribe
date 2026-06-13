namespace Tts.App.Services;

public sealed class HotkeyRegistrationStatusChangedEventArgs : EventArgs
{
    public HotkeyRegistrationStatusChangedEventArgs(string message, bool isRegistered)
    {
        Message = message;
        IsRegistered = isRegistered;
    }

    public string Message { get; }

    public bool IsRegistered { get; }
}