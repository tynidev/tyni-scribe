using System.IO;
using Tts.App.Configuration;
using Tts.App.Services.Audio;

namespace Tts.App.Services;

public sealed class SessionOrchestrator : ISessionOrchestrator
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly SemaphoreSlim _transitionLock = new(1, 1);
    private CancellationTokenSource? _activeSessionCancellation;
    private AudioRecordingResult? _completedRecording;

    public SessionOrchestrator(IAppSettingsStore settingsStore, IAudioCaptureService audioCaptureService)
    {
        _settingsStore = settingsStore;
        _audioCaptureService = audioCaptureService;
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
                    if (!await TryStopRecordingAndBeginProcessingAsync(cancellationToken))
                    {
                        return;
                    }

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
                    await CancelActiveCaptureAsync();
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

        try
        {
            await _audioCaptureService.StartRecordingAsync(ActiveSessionSnapshot.MicrophoneDeviceId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            EndActiveSession();
            TransitionTo(AppSessionState.Error, $"Could not start recording: {exception.Message}");
            return;
        }

        TransitionTo(AppSessionState.Recording, "Recording. Press Start/Stop to finish, or Cancel to discard.");
    }

    private async Task<bool> TryStopRecordingAndBeginProcessingAsync(CancellationToken cancellationToken)
    {
        try
        {
            _completedRecording = await _audioCaptureService.StopRecordingAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await CancelActiveCaptureAsync();
            EndActiveSession();
            TransitionTo(AppSessionState.Error, $"Could not stop recording cleanly: {exception.Message}");
            return false;
        }

        TransitionTo(AppSessionState.Processing, "Processing recording.");
        return true;
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

            var duration = _completedRecording?.Duration.TotalSeconds ?? 0;
            EndActiveSession();
            TransitionTo(AppSessionState.Idle, $"Recording stopped. Captured {duration:0.0} seconds of audio; transcription attaches in the next steps.");
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    private void EndActiveSession()
    {
        DeleteCompletedRecording();
        _activeSessionCancellation?.Dispose();
        _activeSessionCancellation = null;
        ActiveSessionSnapshot = null;
    }

    private async Task CancelActiveCaptureAsync()
    {
        try
        {
            await _audioCaptureService.CancelRecordingAsync(CancellationToken.None);
        }
        catch (Exception)
        {
        }
    }

    private void DeleteCompletedRecording()
    {
        if (_completedRecording is null)
        {
            return;
        }

        try
        {
            if (File.Exists(_completedRecording.FilePath))
            {
                File.Delete(_completedRecording.FilePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            _completedRecording = null;
        }
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