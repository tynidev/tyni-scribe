namespace Tts.App.Services;

public interface ISessionOrchestrator
{
    event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    AppSessionState State { get; }

    string StatusMessage { get; }

    SessionSnapshot? ActiveSessionSnapshot { get; }

    Task HandleStartStopAsync(CancellationToken cancellationToken = default);

    Task CancelAsync(CancellationToken cancellationToken = default);
}