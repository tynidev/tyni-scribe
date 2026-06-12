using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Tts.App.Configuration;
using Tts.App.Services.Audio;
using Tts.App.Services.AudioProcessing;
using Tts.App.Services.Output;
using Tts.App.Services.Timing;
using Tts.App.Services.Transcription;

namespace Tts.App.Services;

public sealed class SessionOrchestrator : ISessionOrchestrator
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly IReadOnlyDictionary<string, IAudioProcessingProvider> _audioProcessingProviders;
    private readonly IReadOnlyDictionary<string, IBatchTranscriptionProvider> _batchTranscriptionProviders;
    private readonly IReadOnlyDictionary<string, IOutputProvider> _outputProviders;
    private readonly ISessionTimingLogWriter _timingLogWriter;
    private readonly SemaphoreSlim _transitionLock = new(1, 1);
    private CancellationTokenSource? _activeSessionCancellation;
    private AudioRecordingResult? _completedRecording;
    private string? _processedAudioFilePath;
    private ActiveSessionTiming? _activeSessionTiming;
    private PendingOutput? _pendingOutput;

    public SessionOrchestrator(
        IAppSettingsStore settingsStore,
        IAudioCaptureService audioCaptureService,
        IEnumerable<IAudioProcessingProvider> audioProcessingProviders,
        IEnumerable<IBatchTranscriptionProvider> batchTranscriptionProviders,
        IEnumerable<IOutputProvider> outputProviders,
        ISessionTimingLogWriter timingLogWriter)
    {
        _settingsStore = settingsStore;
        _audioCaptureService = audioCaptureService;
        _audioProcessingProviders = audioProcessingProviders.ToDictionary(provider => provider.Metadata.Id, StringComparer.OrdinalIgnoreCase);
        _batchTranscriptionProviders = batchTranscriptionProviders.ToDictionary(provider => provider.Metadata.Id, StringComparer.OrdinalIgnoreCase);
        _outputProviders = outputProviders.ToDictionary(provider => provider.Id, StringComparer.OrdinalIgnoreCase);
        _timingLogWriter = timingLogWriter;
    }

    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    public AppSessionState State { get; private set; } = AppSessionState.Idle;

    public string StatusMessage { get; private set; } = "Ready";

    public SessionSnapshot? ActiveSessionSnapshot { get; private set; }

    public bool HasPendingOutput => _pendingOutput is not null;

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
                    if (_pendingOutput is not null)
                    {
                        await DismissPendingOutputOnLockAsync("Output dismissed. Ready.");
                        return;
                    }

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

        await CompleteProcessingAsync(cancellationToken);
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
                    if (_pendingOutput is not null)
                    {
                        await DismissPendingOutputOnLockAsync("Output dismissed.");
                        return;
                    }

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

    public async Task RetryOutputAsync(CancellationToken cancellationToken = default)
    {
        PendingOutput pendingOutput;

        await _transitionLock.WaitAsync(cancellationToken);

        try
        {
            if (_pendingOutput is null)
            {
                PublishStatus("No pending output to retry.");
                return;
            }

            if (State != AppSessionState.Error)
            {
                PublishStatus($"Busy: current session is {State}.");
                return;
            }

            pendingOutput = _pendingOutput;
            TransitionTo(AppSessionState.Outputting, "Retrying output.");
        }
        finally
        {
            _transitionLock.Release();
        }

        var result = await WriteEnabledOutputProvidersAsync(pendingOutput.Text, pendingOutput.Snapshot, pendingOutput.SessionId, cancellationToken);

        await _transitionLock.WaitAsync(CancellationToken.None);

        try
        {
            if (!result.Succeeded)
            {
                _pendingOutput = pendingOutput with { ErrorCategory = result.ErrorCategory ?? "output-provider" };
                TransitionTo(AppSessionState.Error, result.StatusMessage);
                return;
            }

            _pendingOutput = null;
            await EndActiveSessionAsync("success");
            TransitionTo(AppSessionState.Idle, result.StatusMessage);
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public async Task DismissPendingOutputAsync(CancellationToken cancellationToken = default)
    {
        await _transitionLock.WaitAsync(cancellationToken);

        try
        {
            if (_pendingOutput is null)
            {
                PublishStatus("No pending output to dismiss.");
                return;
            }

            await DismissPendingOutputOnLockAsync("Output dismissed.");
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

    private async Task CompleteProcessingAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();

        AudioRecordingResult recording;
        SessionSnapshot snapshot;
        CancellationToken sessionCancellationToken;

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

            if (_completedRecording is null || ActiveSessionSnapshot is null || _activeSessionCancellation is null)
            {
                await EndActiveSessionAsync("failure", "transcription");
                TransitionTo(AppSessionState.Error, "Recording was not available for transcription.");
                return;
            }

            recording = _completedRecording;
            snapshot = ActiveSessionSnapshot;
            sessionCancellationToken = _activeSessionCancellation.Token;
        }
        finally
        {
            _transitionLock.Release();
        }

        await RunBatchTranscriptionAsync(recording, snapshot, sessionCancellationToken);
    }

    private async Task RunBatchTranscriptionAsync(
        AudioRecordingResult recording,
        SessionSnapshot snapshot,
        CancellationToken sessionCancellationToken)
    {
        var processedAudio = await ProcessCompletedAudioAsync(recording, snapshot, sessionCancellationToken);

        if (processedAudio is null)
        {
            return;
        }

        if (!_batchTranscriptionProviders.TryGetValue(snapshot.TranscriptionProviderId, out var provider))
        {
            await FailProcessingAsync($"Transcription provider '{snapshot.TranscriptionProviderId}' is not available.", "transcription-provider");
            return;
        }

        var transcriptionStopwatch = Stopwatch.StartNew();
        BatchTranscriptionResult transcriptionResult;

        try
        {
            transcriptionResult = await provider.TranscribeAsync(
                new BatchTranscriptionRequest(
                    processedAudio.FilePath,
                    recording.Format,
                    recording.Duration,
                    snapshot.TranscriptionProviderSettings),
                sessionCancellationToken);
        }
        catch (OperationCanceledException)
        {
            transcriptionStopwatch.Stop();
            await CompleteCanceledProcessingAsync();
            return;
        }
        catch (TimeoutException exception)
        {
            transcriptionStopwatch.Stop();
            await FailProcessingAsync(exception.Message, "transcription-timeout", transcriptionStopwatch.Elapsed);
            return;
        }
        catch (Exception exception)
        {
            transcriptionStopwatch.Stop();
            await FailProcessingAsync(exception.Message, "transcription", transcriptionStopwatch.Elapsed);
            return;
        }

        transcriptionStopwatch.Stop();

        await _transitionLock.WaitAsync(CancellationToken.None);

        try
        {
            if (State != AppSessionState.Processing)
            {
                return;
            }

            _activeSessionTiming?.CompleteTranscription(transcriptionStopwatch.Elapsed);

            if (string.IsNullOrWhiteSpace(transcriptionResult.Text))
            {
                await EndActiveSessionAsync("success");
                TransitionTo(AppSessionState.Idle, "Transcription produced no text.");
                return;
            }

            await TryWriteFinalOutputAsync(transcriptionResult.Text, CancellationToken.None);
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    private async Task<AudioProcessingResult?> ProcessCompletedAudioAsync(
        AudioRecordingResult recording,
        SessionSnapshot snapshot,
        CancellationToken sessionCancellationToken)
    {
        if (!_audioProcessingProviders.TryGetValue(snapshot.AudioProcessorProviderId, out var provider))
        {
            await FailProcessingAsync($"Audio processor '{snapshot.AudioProcessorProviderId}' is not available.", "audio-processing-provider");
            return null;
        }

        var processingStopwatch = Stopwatch.StartNew();

        try
        {
            var result = await provider.ProcessAsync(
                new AudioProcessingRequest(recording.FilePath, recording.Format, recording.Duration, snapshot.AudioProcessingProviderSettings),
                sessionCancellationToken);

            processingStopwatch.Stop();

            await _transitionLock.WaitAsync(CancellationToken.None);

            try
            {
                if (State != AppSessionState.Processing)
                {
                    return null;
                }

                _activeSessionTiming?.CompleteAudioProcessing(processingStopwatch.Elapsed);

                if (!result.IsOriginalFile)
                {
                    _processedAudioFilePath = result.FilePath;
                }
            }
            finally
            {
                _transitionLock.Release();
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            processingStopwatch.Stop();
            await CompleteCanceledProcessingAsync();
            return null;
        }
        catch (Exception exception)
        {
            processingStopwatch.Stop();
            await FailProcessingAsync(exception.Message, "audio-processing", audioProcessingDuration: processingStopwatch.Elapsed);
            return null;
        }
    }

    private async Task CompleteCanceledProcessingAsync()
    {
        await _transitionLock.WaitAsync(CancellationToken.None);

        try
        {
            if (State != AppSessionState.Processing)
            {
                return;
            }

            await EndActiveSessionAsync("canceled");
            TransitionTo(AppSessionState.Idle, "Session canceled.");
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    private async Task FailProcessingAsync(
        string statusMessage,
        string errorCategory,
        TimeSpan? transcriptionDuration = null,
        TimeSpan? audioProcessingDuration = null)
    {
        await _transitionLock.WaitAsync(CancellationToken.None);

        try
        {
            if (State != AppSessionState.Processing)
            {
                return;
            }

            if (transcriptionDuration is not null)
            {
                _activeSessionTiming?.CompleteTranscription(transcriptionDuration.Value);
            }

            if (audioProcessingDuration is not null)
            {
                _activeSessionTiming?.CompleteAudioProcessing(audioProcessingDuration.Value);
            }

            await EndActiveSessionAsync("failure", errorCategory);
            TransitionTo(AppSessionState.Error, statusMessage);
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    private async Task<bool> TryWriteFinalOutputAsync(string finalText, CancellationToken cancellationToken)
    {
        if (ActiveSessionSnapshot is null || _activeSessionTiming is null)
        {
            return false;
        }

        var snapshot = ActiveSessionSnapshot;
        var sessionId = _activeSessionTiming.SessionId;

        TransitionTo(AppSessionState.Outputting, "Writing output.");

        var result = await WriteEnabledOutputProvidersAsync(finalText, snapshot, sessionId, cancellationToken);

        if (!result.Succeeded)
        {
            DeleteCompletedRecording();
            _pendingOutput = new PendingOutput(finalText, snapshot, sessionId, result.ErrorCategory ?? "output-provider");
            TransitionTo(AppSessionState.Error, result.StatusMessage);
            return false;
        }

        await EndActiveSessionAsync("success");
        TransitionTo(AppSessionState.Idle, result.StatusMessage);
        return true;
    }

    private async Task<OutputWriteResult> WriteEnabledOutputProvidersAsync(
        string finalText,
        SessionSnapshot snapshot,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var enabledProviderIds = snapshot.EnabledOutputProviderIds
            .Where(providerId => !string.IsNullOrWhiteSpace(providerId))
            .Select(providerId => providerId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (enabledProviderIds.Length == 0)
        {
            return OutputWriteResult.Success("No output providers are enabled.");
        }

        foreach (var providerId in enabledProviderIds)
        {
            if (!_outputProviders.TryGetValue(providerId, out var provider))
            {
                return OutputWriteResult.Failure($"Output provider '{providerId}' is not available.", "output-provider");
            }

            var outputStopwatch = Stopwatch.StartNew();

            try
            {
                await provider.WriteAsync(finalText, new OutputProviderContext(sessionId, GetOutputProviderSettings(snapshot, provider.Id)), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                outputStopwatch.Stop();
                RecordOutputDuration(provider.Id, outputStopwatch.Elapsed);
                return OutputWriteResult.Failure("Output was canceled.", "output-canceled");
            }
            catch (Exception exception)
            {
                outputStopwatch.Stop();
                RecordOutputDuration(provider.Id, outputStopwatch.Elapsed);

                var errorCategory = provider.Id switch
                {
                    var id when id.Equals(ClipboardOutputProvider.ProviderId, StringComparison.OrdinalIgnoreCase) => "clipboard-output",
                    var id when id.Equals(PasteOutputProvider.ProviderId, StringComparison.OrdinalIgnoreCase) => "paste-output",
                    _ => "output-provider"
                };

                return OutputWriteResult.Failure($"Could not write output through {provider.DisplayName}: {exception.Message}", errorCategory);
            }

            outputStopwatch.Stop();
            RecordOutputDuration(provider.Id, outputStopwatch.Elapsed);
        }

        return OutputWriteResult.Success(GetOutputSuccessMessage(enabledProviderIds));
    }

    private static string GetOutputSuccessMessage(IReadOnlyList<string> enabledProviderIds)
    {
        if (enabledProviderIds.Count == 1)
        {
            var providerId = enabledProviderIds[0];

            if (providerId.Equals(ClipboardOutputProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return "Output copied to clipboard.";
            }

            if (providerId.Equals(PasteOutputProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return "Output pasted.";
            }
        }

        return "Output written.";
    }

    private void RecordOutputDuration(string providerId, TimeSpan duration)
    {
        if (providerId.Equals(ClipboardOutputProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            _activeSessionTiming?.AddClipboardOutputDuration(duration);
        }
    }

    private async Task DismissPendingOutputOnLockAsync(string statusMessage)
    {
        var errorCategory = _pendingOutput?.ErrorCategory ?? "clipboard-output";
        _pendingOutput = null;
        await EndActiveSessionAsync("failure", errorCategory);
        TransitionTo(AppSessionState.Idle, statusMessage);
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
        DeleteProcessedRecording();

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

    private void DeleteProcessedRecording()
    {
        if (string.IsNullOrWhiteSpace(_processedAudioFilePath))
        {
            return;
        }

        try
        {
            if (File.Exists(_processedAudioFilePath))
            {
                File.Delete(_processedAudioFilePath);
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
            _processedAudioFilePath = null;
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
            settings.SelectedAudioProcessorProviderId,
            CloneSelectedTranscriptionProviderSettings(settings),
            CloneSelectedProviderSettings(settings.AudioProcessingProviderSettings, settings.SelectedAudioProcessorProviderId),
            settings.EnabledOutputProviderIds.ToArray(),
            CloneEnabledOutputProviderSettings(settings),
            settings.Cleanup.IsEnabled,
            settings.Cleanup.ProviderId,
            settings.Cleanup.Prompt);
    }

    private static IReadOnlyDictionary<string, string> CloneSelectedTranscriptionProviderSettings(AppSettings settings)
    {
        return CloneSelectedProviderSettings(settings.TranscriptionProviderSettings, settings.SelectedTranscriptionProviderId);
    }

    private static IReadOnlyDictionary<string, string> CloneSelectedProviderSettings(
        IReadOnlyDictionary<string, Dictionary<string, string>> providerSettingsById,
        string providerId)
    {
        foreach (var providerSettings in providerSettingsById)
        {
            if (providerSettings.Key.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, string>(providerSettings.Value, StringComparer.OrdinalIgnoreCase);
            }
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> CloneEnabledOutputProviderSettings(AppSettings settings)
    {
        var outputProviderSettings = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var providerId in settings.EnabledOutputProviderIds
                     .Where(providerId => !string.IsNullOrWhiteSpace(providerId))
                     .Select(providerId => providerId.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            outputProviderSettings[providerId] = CloneSelectedProviderSettings(settings.OutputProviderSettings, providerId);
        }

        return outputProviderSettings;
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

        public TimeSpan? AudioProcessingDuration { get; private set; }

        public TimeSpan? TranscriptionDuration { get; private set; }

        public TimeSpan? ClipboardOutputDuration { get; private set; }

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

        public void CompleteAudioProcessing(TimeSpan audioProcessingDuration)
        {
            AudioProcessingDuration = audioProcessingDuration;
        }

        public void AddClipboardOutputDuration(TimeSpan clipboardOutputDuration)
        {
            ClipboardOutputDuration = ClipboardOutputDuration is null
                ? clipboardOutputDuration
                : ClipboardOutputDuration.Value + clipboardOutputDuration;
        }

        public void CompleteTranscription(TimeSpan transcriptionDuration)
        {
            TranscriptionDuration = transcriptionDuration;
        }

        public SessionTimingLogEntry CreateLogEntry(string status, string? errorCategory)
        {
            _totalSessionStopwatch.Stop();
            var completedUtc = DateTimeOffset.UtcNow;

            return new SessionTimingLogEntry(
                SchemaVersion: 2,
                SessionId,
                StartedUtc,
                completedUtc,
                status,
                errorCategory,
                _snapshot.MicrophoneDeviceId,
                _snapshot.TranscriptionProviderId,
                _snapshot.AudioProcessorProviderId,
                _snapshot.IsCleanupEnabled ? _snapshot.CleanupProviderId : null,
                _snapshot.EnabledOutputProviderIds,
                RecordingDuration,
                _totalSessionStopwatch.Elapsed,
                CaptureFinalizationDuration,
                AudioProcessingDuration,
                TranscriptionDuration,
                TextCleanupDuration: null,
                ClipboardOutputDuration,
                TempFileCleanupDuration: null,
                CreateProviderSettingsJson(_snapshot));
        }

        private static string CreateProviderSettingsJson(SessionSnapshot snapshot)
        {
            var transcriptionSettings = snapshot.TranscriptionProviderSettings;
            var computeType = GetProviderSettingValue(transcriptionSettings, ProviderSettingKeys.TranscriptionComputeType);

            var providerSettings = new
            {
                transcription = new
                {
                    providerId = snapshot.TranscriptionProviderId,
                    modelId = GetProviderSettingValue(transcriptionSettings, ProviderSettingKeys.TranscriptionModelId),
                    language = GetProviderSettingValue(transcriptionSettings, ProviderSettingKeys.TranscriptionLanguage),
                    computeType,
                    timeoutSeconds = GetProviderSettingValue(transcriptionSettings, ProviderSettingKeys.TranscriptionTimeoutSeconds),
                    executablePathOverrideSet = HasProviderSettingValue(transcriptionSettings, ProviderSettingKeys.TranscriptionExecutablePathOverride),
                    modelPathOverrideSet = HasProviderSettingValue(transcriptionSettings, ProviderSettingKeys.TranscriptionModelPathOverride)
                },
                audioProcessing = new
                {
                    providerId = snapshot.AudioProcessorProviderId,
                    settingsConfigured = snapshot.AudioProcessingProviderSettings.Count > 0
                },
                cleanup = new
                {
                    enabled = snapshot.IsCleanupEnabled,
                    providerId = snapshot.IsCleanupEnabled ? snapshot.CleanupProviderId : null,
                    promptSet = !string.IsNullOrWhiteSpace(snapshot.CleanupPrompt)
                },
                output = new
                {
                    providerIds = snapshot.EnabledOutputProviderIds,
                    settingsConfiguredProviderIds = snapshot.OutputProviderSettings
                        .Where(providerSettings => providerSettings.Value.Count > 0)
                        .Select(providerSettings => providerSettings.Key)
                        .ToArray()
                }
            };

            return JsonSerializer.Serialize(providerSettings);
        }

        private static string? GetProviderSettingValue(IReadOnlyDictionary<string, string> settings, string key)
        {
            return settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;
        }

        private static bool HasProviderSettingValue(IReadOnlyDictionary<string, string> settings, string key)
        {
            return !string.IsNullOrWhiteSpace(GetProviderSettingValue(settings, key));
        }
    }

    private sealed record PendingOutput(string Text, SessionSnapshot Snapshot, Guid SessionId, string ErrorCategory);

    private static IReadOnlyDictionary<string, string> GetOutputProviderSettings(SessionSnapshot snapshot, string providerId)
    {
        foreach (var providerSettings in snapshot.OutputProviderSettings)
        {
            if (providerSettings.Key.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            {
                return providerSettings.Value;
            }
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private readonly record struct OutputWriteResult(bool Succeeded, string StatusMessage, string? ErrorCategory)
    {
        public static OutputWriteResult Success(string statusMessage)
        {
            return new OutputWriteResult(true, statusMessage, null);
        }

        public static OutputWriteResult Failure(string statusMessage, string errorCategory)
        {
            return new OutputWriteResult(false, statusMessage, errorCategory);
        }
    }
}