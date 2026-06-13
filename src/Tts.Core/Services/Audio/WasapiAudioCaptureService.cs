using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Tts.Core.Services.Audio;

public sealed class WasapiAudioCaptureService : IAudioCaptureService, IDisposable
{
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _sync = new();
    private ActiveCaptureSession? _activeCapture;

    public WasapiAudioCaptureService(AppPaths paths)
    {
        _paths = paths;
    }

    public event EventHandler<MicrophoneLevelChangedEventArgs>? LevelChanged;

    public event EventHandler<AudioChunkCapturedEventArgs>? AudioChunkCaptured;

    public bool IsRecording => GetActiveCapture()?.Mode == AudioCaptureMode.Recording;

    public bool IsLevelMonitoring => GetActiveCapture()?.Mode == AudioCaptureMode.LevelMonitoring;

    public double CurrentLevel { get; private set; }

    public Task<IReadOnlyList<MicrophoneDevice>> EnumerateMicrophonesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<MicrophoneDevice>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var enumerator = new MMDeviceEnumerator();
            string? defaultDeviceId = null;

            try
            {
                using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                defaultDeviceId = defaultDevice.ID;
            }
            catch (Exception)
            {
                // No default input endpoint is available.
            }

            var devices = new List<MicrophoneDevice>();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

            foreach (var endpoint in endpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();
                devices.Add(new MicrophoneDevice(endpoint.ID, endpoint.FriendlyName, endpoint.ID == defaultDeviceId));
            }

            return devices
                .OrderByDescending(device => device.IsDefault)
                .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    public async Task StartLevelMonitoringAsync(string? microphoneDeviceId, CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);

        try
        {
            var activeCapture = GetActiveCapture();

            if (activeCapture?.Mode == AudioCaptureMode.Recording)
            {
                throw new InvalidOperationException("Cannot start microphone level monitoring while recording.");
            }

            if (activeCapture is not null)
            {
                await StopActiveCaptureAsync(activeCapture, deleteFile: false, cancellationToken);
            }

            var session = CreateCaptureSession(AudioCaptureMode.LevelMonitoring, microphoneDeviceId);
            StartCaptureSession(session);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task StopLevelMonitoringAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);

        try
        {
            var activeCapture = GetActiveCapture();

            if (activeCapture?.Mode != AudioCaptureMode.LevelMonitoring)
            {
                PublishLevel(0);
                return;
            }

            await StopActiveCaptureAsync(activeCapture, deleteFile: false, cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task StartRecordingAsync(string? microphoneDeviceId, CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);

        try
        {
            var activeCapture = GetActiveCapture();

            if (activeCapture?.Mode == AudioCaptureMode.Recording)
            {
                throw new InvalidOperationException("A recording is already active.");
            }

            if (activeCapture is not null)
            {
                await StopActiveCaptureAsync(activeCapture, deleteFile: false, cancellationToken);
            }

            var session = CreateCaptureSession(AudioCaptureMode.Recording, microphoneDeviceId);
            StartCaptureSession(session);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<AudioRecordingResult> StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);

        try
        {
            var activeCapture = GetActiveCapture();

            if (activeCapture?.Mode != AudioCaptureMode.Recording)
            {
                throw new InvalidOperationException("No recording is active.");
            }

            var result = await StopActiveCaptureAsync(activeCapture, deleteFile: false, cancellationToken);
            return result ?? throw new InvalidOperationException("Recording stopped without an audio file.");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task CancelRecordingAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);

        try
        {
            var activeCapture = GetActiveCapture();

            if (activeCapture?.Mode != AudioCaptureMode.Recording)
            {
                PublishLevel(0);
                return;
            }

            await StopActiveCaptureAsync(activeCapture, deleteFile: true, cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public void Dispose()
    {
        ActiveCaptureSession? activeCapture;

        lock (_sync)
        {
            activeCapture = _activeCapture;
            _activeCapture = null;
        }

        activeCapture?.Dispose();
        _operationLock.Dispose();
    }

    private ActiveCaptureSession CreateCaptureSession(AudioCaptureMode mode, string? microphoneDeviceId)
    {
        var device = GetCaptureDevice(microphoneDeviceId);
        var capture = new WasapiCapture(device);
        var format = AudioCaptureFormat.FromWaveFormat(capture.WaveFormat);
        var filePath = mode == AudioCaptureMode.Recording ? CreateTempAudioFilePath() : null;
        var writer = filePath is null ? null : new WaveFileWriter(filePath, capture.WaveFormat);

        var session = new ActiveCaptureSession(mode, device, capture, writer, filePath, format);

        capture.DataAvailable += (_, eventArgs) => OnDataAvailable(session, eventArgs);
        capture.RecordingStopped += (_, eventArgs) => OnRecordingStopped(session, eventArgs);

        return session;
    }

    private void StartCaptureSession(ActiveCaptureSession session)
    {
        try
        {
            lock (_sync)
            {
                _activeCapture = session;
            }

            session.Capture.StartRecording();
            PublishLevel(0);
        }
        catch
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeCapture, session))
                {
                    _activeCapture = null;
                }
            }

            session.Dispose();
            TryDeleteFile(session.FilePath);
            PublishLevel(0);
            throw;
        }
    }

    private async Task<AudioRecordingResult?> StopActiveCaptureAsync(
        ActiveCaptureSession session,
        bool deleteFile,
        CancellationToken cancellationToken)
    {
        try
        {
            session.Capture.StopRecording();
            var result = await session.Completion.Task.WaitAsync(cancellationToken);

            if (deleteFile)
            {
                TryDeleteFile(session.FilePath);
            }

            return result;
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeCapture, session))
                {
                    _activeCapture = null;
                }
            }

            PublishLevel(0);
        }
    }

    private void OnDataAvailable(ActiveCaptureSession session, WaveInEventArgs eventArgs)
    {
        if (eventArgs.BytesRecorded <= 0)
        {
            PublishLevel(0);
            return;
        }

        session.Writer?.Write(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
        session.DataBytesWritten += eventArgs.BytesRecorded;

        PublishLevel(CalculatePeakLevel(eventArgs.Buffer, eventArgs.BytesRecorded, session.Capture.WaveFormat));

        if (session.Mode != AudioCaptureMode.Recording || AudioChunkCaptured is null)
        {
            return;
        }

        var audioData = new byte[eventArgs.BytesRecorded];
        Buffer.BlockCopy(eventArgs.Buffer, 0, audioData, 0, eventArgs.BytesRecorded);
        AudioChunkCaptured?.Invoke(this, new AudioChunkCapturedEventArgs(audioData, session.Format));
    }

    private void OnRecordingStopped(ActiveCaptureSession session, StoppedEventArgs eventArgs)
    {
        try
        {
            session.Writer?.Flush();

            if (eventArgs.Exception is not null)
            {
                session.Completion.TrySetException(eventArgs.Exception);
                return;
            }

            session.Completion.TrySetResult(session.CreateResult());
        }
        finally
        {
            session.Dispose();

            lock (_sync)
            {
                if (ReferenceEquals(_activeCapture, session))
                {
                    _activeCapture = null;
                }
            }

            PublishLevel(0);
        }
    }

    private ActiveCaptureSession? GetActiveCapture()
    {
        lock (_sync)
        {
            return _activeCapture;
        }
    }

    private MMDevice GetCaptureDevice(string? microphoneDeviceId)
    {
        using var enumerator = new MMDeviceEnumerator();

        try
        {
            if (string.IsNullOrWhiteSpace(microphoneDeviceId))
            {
                return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            }

            return enumerator.GetDevice(microphoneDeviceId);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("The selected microphone is unavailable.", exception);
        }
    }

    private string CreateTempAudioFilePath()
    {
        Directory.CreateDirectory(_paths.TempAudioDirectory);
        return Path.Combine(
            _paths.TempAudioDirectory,
            $"recording-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.wav");
    }

    private void PublishLevel(double level)
    {
        CurrentLevel = Math.Clamp(level, 0, 1);
        LevelChanged?.Invoke(this, new MicrophoneLevelChangedEventArgs(CurrentLevel));
    }

    private static double CalculatePeakLevel(byte[] buffer, int bytesRecorded, WaveFormat waveFormat)
    {
        return waveFormat.BitsPerSample switch
        {
            8 => CalculatePcm8Peak(buffer, bytesRecorded),
            16 => CalculatePcm16Peak(buffer, bytesRecorded),
            24 => CalculatePcm24Peak(buffer, bytesRecorded),
            32 when IsFloatFormat(waveFormat) => CalculateFloat32Peak(buffer, bytesRecorded),
            32 => CalculatePcm32Peak(buffer, bytesRecorded),
            _ => 0
        };
    }

    private static bool IsFloatFormat(WaveFormat waveFormat)
    {
        return waveFormat.Encoding is WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible
               && waveFormat.BitsPerSample == 32;
    }

    private static double CalculatePcm8Peak(byte[] buffer, int bytesRecorded)
    {
        double peak = 0;

        for (var index = 0; index < bytesRecorded; index++)
        {
            var sample = (buffer[index] - 128) / 128.0;
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak;
    }

    private static double CalculatePcm16Peak(byte[] buffer, int bytesRecorded)
    {
        double peak = 0;

        for (var index = 0; index + 1 < bytesRecorded; index += 2)
        {
            var sample = BitConverter.ToInt16(buffer, index) / 32768.0;
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak;
    }

    private static double CalculatePcm24Peak(byte[] buffer, int bytesRecorded)
    {
        double peak = 0;

        for (var index = 0; index + 2 < bytesRecorded; index += 3)
        {
            var sample = buffer[index] | buffer[index + 1] << 8 | buffer[index + 2] << 16;

            if ((sample & 0x800000) != 0)
            {
                sample |= unchecked((int)0xFF000000);
            }

            peak = Math.Max(peak, Math.Abs(sample / 8388608.0));
        }

        return peak;
    }

    private static double CalculatePcm32Peak(byte[] buffer, int bytesRecorded)
    {
        double peak = 0;

        for (var index = 0; index + 3 < bytesRecorded; index += 4)
        {
            var sample = BitConverter.ToInt32(buffer, index) / 2147483648.0;
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak;
    }

    private static double CalculateFloat32Peak(byte[] buffer, int bytesRecorded)
    {
        double peak = 0;

        for (var index = 0; index + 3 < bytesRecorded; index += 4)
        {
            var sample = BitConverter.ToSingle(buffer, index);

            if (float.IsNaN(sample) || float.IsInfinity(sample))
            {
                continue;
            }

            peak = Math.Max(peak, Math.Abs(sample));
        }

        return Math.Clamp(peak, 0, 1);
    }

    private static void TryDeleteFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class ActiveCaptureSession : IDisposable
    {
        public ActiveCaptureSession(
            AudioCaptureMode mode,
            MMDevice device,
            WasapiCapture capture,
            WaveFileWriter? writer,
            string? filePath,
            AudioCaptureFormat format)
        {
            Mode = mode;
            Device = device;
            Capture = capture;
            Writer = writer;
            FilePath = filePath;
            Format = format;
        }

        public AudioCaptureMode Mode { get; }

        public MMDevice Device { get; }

        public WasapiCapture Capture { get; }

        public WaveFileWriter? Writer { get; }

        public string? FilePath { get; }

        public AudioCaptureFormat Format { get; }

        public long DataBytesWritten { get; set; }

        public TaskCompletionSource<AudioRecordingResult?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AudioRecordingResult? CreateResult()
        {
            if (FilePath is null)
            {
                return null;
            }

            var duration = Capture.WaveFormat.AverageBytesPerSecond > 0
                ? TimeSpan.FromSeconds((double)DataBytesWritten / Capture.WaveFormat.AverageBytesPerSecond)
                : TimeSpan.Zero;

            return new AudioRecordingResult(FilePath, duration, DataBytesWritten, Format);
        }

        public void Dispose()
        {
            Writer?.Dispose();
            Capture.Dispose();
            Device.Dispose();
        }
    }
}