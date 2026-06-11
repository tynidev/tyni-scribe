using System.Diagnostics;
using System.IO;
using Tts.App.Configuration;
using Tts.App.Services.Audio;
using Tts.App.Services.Timing;

namespace Tts.App.Services;

public sealed class SessionOrchestrator : ISessionOrchestrator
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly ISessionTimingLogWriter _timingLogWriter;
    private readonly SemaphoreSlim _transitionLock = new(1, 1);
    private CancellationTokenSource? _activeSessionCancellation;
    private AudioRecordingResult? _completedRecording;
    private ActiveSessionTiming? _activeSessionTiming;

    public SessionOrchestrator(
        IAppSettingsStore settingsStore,
        IAudioCaptureService audioCaptureService,
        ISessionTimingLogWriter timingLogWriter)
    {
        _settingsStore = settingsStore;
        _audioCaptureService = audioCaptureService;
        _timingLogWriter = timingLogWriter;
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
                    await EndActiveSessionAsync();
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
                    if (_activeSessionTiming?.RecordingDuration is null)
                    {
                        _activeSessionTiming?.CompleteRecordingDurationFromElapsedTime();
                    }

                    await EndActiveSessionAsync("canceled");
                    TransitionTo(AppSessionState.Idle, "Session canceled.");
                    return;
                case AppSessionState.Idle:
                    PublishStatus("Ready");
                    return;
                case AppSessionState.Outputting:
                    PublishStatus("Busy: output is already in progress.");
                    return;
                case AppSessionState.Error:
                    await EndActiveSessionAsync();
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
        _activeSessionTiming = new ActiveSessionTiming(ActiveSessionSnapshot);

        try
        {
            await _audioCaptureService.StartRecordingAsync(ActiveSessionSnapshot.MicrophoneDeviceId, cancellationToken);
            _activeSessionTiming.StartRecordingDuration();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await EndActiveSessionAsync("failure", "capture-start");
            TransitionTo(AppSessionState.Error, $"Could not start recording: {exception.Message}");
            return;
        }

        TransitionTo(AppSessionState.Recording, "Recording. Press Start/Stop to finish, or Cancel to discard.");
    }

    private async Task<bool> TryStopRecordingAndBeginProcessingAsync(CancellationToken cancellationToken)
    {
        var captureFinalizationStopwatch = Stopwatch.StartNew();

        try
        {
            _completedRecording = await _audioCaptureService.StopRecordingAsync(cancellationToken);
            captureFinalizationStopwatch.Stop();
            _activeSessionTiming?.CompleteRecordingDuration(_completedRecording.Duration);
            _activeSessionTiming?.CompleteCaptureFinalization(captureFinalizationStopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            captureFinalizationStopwatch.Stop();
            _activeSessionTiming?.CompleteRecordingDurationFromElapsedTime();
            _activeSessionTiming?.CompleteCaptureFinalization(captureFinalizationStopwatch.Elapsed);
            await CancelActiveCaptureAsync();
            await EndActiveSessionAsync("failure", "capture-finalization");
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
                await EndActiveSessionAsync("canceled");
                TransitionTo(AppSessionState.Idle, "Session canceled.");
                return;
            }

            var duration = _completedRecording?.Duration.TotalSeconds ?? 0;
            await EndActiveSessionAsync("success");
            TransitionTo(AppSessionState.Idle, $"Recording stopped. Captured {duration:0.0} seconds of audio; transcription attaches in the next steps.");
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    private async Task EndActiveSessionAsync(string? status = null, string? errorCategory = null)
    {
        DeleteCompletedRecording();
        await WriteTimingLogAsync(status, errorCategory);
        _activeSessionCancellation?.Dispose();
        _activeSessionCancellation = null;
        ActiveSessionSnapshot = null;
        _activeSessionTiming = null;
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

    private async Task WriteTimingLogAsync(string? status, string? errorCategory)
    {
        if (_activeSessionTiming is null || status is null)
        {
            return;
        }

        try
        {
            await _timingLogWriter.AppendAsync(_activeSessionTiming.CreateLogEntry(status, errorCategory), CancellationToken.None);
        }
        catch (Exception)
        {
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

    private sealed class ActiveSessionTiming
    {
        private readonly Stopwatch _totalSessionStopwatch = Stopwatch.StartNew();
        private readonly SessionSnapshot _snapshot;
        private Stopwatch? _recordingStopwatch;

        public ActiveSessionTiming(SessionSnapshot snapshot)
        {
            _snapshot = snapshot;
            SessionId = Guid.NewGuid();
            StartedUtc = DateTimeOffset.UtcNow;
        }

        public Guid SessionId { get; }

        public DateTimeOffset StartedUtc { get; }

        public TimeSpan? RecordingDuration { get; private set; }

        public TimeSpan? CaptureFinalizationDuration { get; private set; }

        public void StartRecordingDuration()
        {
            _recordingStopwatch = Stopwatch.StartNew();
        }

        public void CompleteRecordingDuration(TimeSpan recordingDuration)
        {
            RecordingDuration = recordingDuration;
            _recordingStopwatch?.Stop();
        }

        public void CompleteRecordingDurationFromElapsedTime()
        {
            if (RecordingDuration is not null || _recordingStopwatch is null)
            {
                return;
            }

            _recordingStopwatch.Stop();
            RecordingDuration = _recordingStopwatch.Elapsed;
        }

        public void CompleteCaptureFinalization(TimeSpan captureFinalizationDuration)
        {
            CaptureFinalizationDuration = captureFinalizationDuration;
        }

        public SessionTimingLogEntry CreateLogEntry(string status, string? errorCategory)
        {
            _totalSessionStopwatch.Stop();
            var completedUtc = DateTimeOffset.UtcNow;

            return new SessionTimingLogEntry(
                SchemaVersion: 1,
                SessionId,
                StartedUtc,
                completedUtc,
                status,
                errorCategory,
                _snapshot.MicrophoneDeviceId,
                _snapshot.TranscriptionProviderId,
                AudioProcessorProviderId: null,
                _snapshot.IsCleanupEnabled ? _snapshot.CleanupProviderId : null,
                _snapshot.EnabledOutputProviderIds,
                RecordingDuration,
                _totalSessionStopwatch.Elapsed,
                CaptureFinalizationDuration,
                AudioProcessingDuration: null,
                TranscriptionDuration: null,
                TextCleanupDuration: null,
                ClipboardOutputDuration: null,
                TempFileCleanupDuration: null);
        }
    }
}