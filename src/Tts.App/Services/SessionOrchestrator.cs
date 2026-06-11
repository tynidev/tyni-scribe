using Tts.App.Configuration;

namespace Tts.App.Services;

public sealed class SessionOrchestrator : ISessionOrchestrator
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly SemaphoreSlim _transitionLock = new(1, 1);
    private CancellationTokenSource? _activeSessionCancellation;

    public SessionOrchestrator(IAppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    public AppSessionState State { get; private set; } = AppSessionState.Idle;

    public string StatusMessage { get; private set; } = "Ready";

    public SessionSnapshot? ActiveSessionSnapshot { get; private set; }

    public async Task HandleStartStopAsync(CancellationToken cancellationToken = default)
    {
        await _transitionLock.WaitAsync(cancellationToken);

        try
        {
            switch (State)
            {
                case AppSessionState.Idle:
                    await StartRecordingAsync(cancellationToken);
                    return;
                case AppSessionState.Recording:
                    BeginProcessing();
                    break;
                case AppSessionState.Processing:
                case AppSessionState.Outputting:
                    PublishStatus($"Busy: current session is {State}.");
                    return;
                case AppSessionState.Error:
                    EndActiveSession();
                    TransitionTo(AppSessionState.Idle, "Recovered from the last error. Press Start/Stop again to record.");
                    return;
                default:
                    return;
            }
        }
        finally
        {
            _transitionLock.Release();
        }

        await CompleteProcessingPlaceholderAsync(cancellationToken);
    }

    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        await _transitionLock.WaitAsync(cancellationToken);

        try
        {
            switch (State)
            {
                case AppSessionState.Recording:
                case AppSessionState.Processing:
                    _activeSessionCancellation?.Cancel();
                    EndActiveSession();
                    TransitionTo(AppSessionState.Idle, "Session canceled.");
                    return;
                case AppSessionState.Idle:
                    PublishStatus("Ready");
                    return;
                case AppSessionState.Outputting:
                    PublishStatus("Busy: output is already in progress.");
                    return;
                case AppSessionState.Error:
                    EndActiveSession();
                    TransitionTo(AppSessionState.Idle, "Error dismissed.");
                    return;
            }
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    private async Task StartRecordingAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        _activeSessionCancellation = new CancellationTokenSource();
        ActiveSessionSnapshot = CreateSnapshot(settings);

        TransitionTo(AppSessionState.Recording, "Recording. Press Start/Stop to finish, or Cancel to discard.");
    }

    private void BeginProcessing()
    {
        TransitionTo(AppSessionState.Processing, "Processing recording.");
    }

    private async Task CompleteProcessingPlaceholderAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();

        await _transitionLock.WaitAsync(cancellationToken);

        try
        {
            if (State != AppSessionState.Processing)
            {
                return;
            }

            if (_activeSessionCancellation?.IsCancellationRequested == true)
            {
                EndActiveSession();
                TransitionTo(AppSessionState.Idle, "Session canceled.");
                return;
            }

            EndActiveSession();
            TransitionTo(AppSessionState.Idle, "Recording stopped. Audio capture and transcription attach in the next steps.");
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    private void EndActiveSession()
    {
        _activeSessionCancellation?.Dispose();
        _activeSessionCancellation = null;
        ActiveSessionSnapshot = null;
    }

    private void TransitionTo(AppSessionState nextState, string statusMessage)
    {
        var previousState = State;
        State = nextState;
        StatusMessage = statusMessage;

        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(previousState, State, StatusMessage));
    }

    private void PublishStatus(string statusMessage)
    {
        StatusMessage = statusMessage;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(State, State, StatusMessage));
    }

    private static SessionSnapshot CreateSnapshot(AppSettings settings)
    {
        return new SessionSnapshot(
            settings.SelectedMicrophoneDeviceId,
            settings.SelectedTranscriptionProviderId,
            settings.EnabledOutputProviderIds.ToArray(),
            settings.Cleanup.IsEnabled,
            settings.Cleanup.ProviderId,
            settings.Cleanup.Prompt);
    }
}