namespace Tts.App.Services;

public sealed class SessionStateChangedEventArgs : EventArgs
{
    public SessionStateChangedEventArgs(
        AppSessionState previousState,
        AppSessionState currentState,
        string statusMessage)
    {
        PreviousState = previousState;
        CurrentState = currentState;
        StatusMessage = statusMessage;
    }

    public AppSessionState PreviousState { get; }

    public AppSessionState CurrentState { get; }

    public string StatusMessage { get; }
}