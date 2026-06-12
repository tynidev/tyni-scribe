using System.Runtime.InteropServices;

namespace Tts.App.Services.Transcription;

public sealed class FasterWhisperNativeEngine : IFasterWhisperNativeEngine
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private IntPtr _engine;
    private string? _loadedModelDirectory;
    private string? _loadedComputeType;
    private bool _disposed;

    public async Task<string> TranscribeAsync(FasterWhisperNativeTranscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _sync.WaitAsync(cancellationToken);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return await Task.Run(() => TranscribeOnWorkerThread(request, cancellationToken), CancellationToken.None);
        }
        finally
        {
            _sync.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_engine != IntPtr.Zero)
        {
            FasterWhisperNativeMethods.DisposeEngine(_engine);
            _engine = IntPtr.Zero;
            _loadedModelDirectory = null;
            _loadedComputeType = null;
        }

        _sync.Dispose();
    }

    private string TranscribeOnWorkerThread(FasterWhisperNativeTranscriptionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureEngineCreated();
        EnsureModelLoaded(request.ModelDirectory, request.ComputeType);

        using var cancellationRegistration = cancellationToken.Register(RequestCancel);

        var status = FasterWhisperNativeInteropStatus.NativeFailure;
        var transcriptPointer = IntPtr.Zero;

        try
        {
            status = FasterWhisperNativeMethods.TranscribeWav(
                _engine,
                request.AudioFilePath,
                request.Language,
                request.TimeoutSeconds,
                out transcriptPointer);

            if (status == FasterWhisperNativeInteropStatus.Ok)
            {
                return Marshal.PtrToStringUTF8(transcriptPointer) ?? string.Empty;
            }
        }
        catch (DllNotFoundException exception)
        {
            throw CreateUnavailableException(exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw CreateUnavailableException(exception);
        }
        catch (BadImageFormatException exception)
        {
            throw CreateUnavailableException(exception);
        }
        finally
        {
            if (transcriptPointer != IntPtr.Zero)
            {
                FasterWhisperNativeMethods.FreeString(transcriptPointer);
            }
        }

        if ((status is FasterWhisperNativeInteropStatus.Canceled or FasterWhisperNativeInteropStatus.Timeout) && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        throw CreateNativeFailureException(status);
    }

    private void EnsureEngineCreated()
    {
        if (_engine != IntPtr.Zero)
        {
            return;
        }

        try
        {
            var status = FasterWhisperNativeMethods.CreateEngine(out var engine);
            if (status != FasterWhisperNativeInteropStatus.Ok || engine == IntPtr.Zero)
            {
                throw CreateNativeFailureException(status);
            }

            _engine = engine;
        }
        catch (DllNotFoundException exception)
        {
            throw CreateUnavailableException(exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw CreateUnavailableException(exception);
        }
        catch (BadImageFormatException exception)
        {
            throw CreateUnavailableException(exception);
        }
    }

    private void EnsureModelLoaded(string modelDirectory, string computeType)
    {
        if (_loadedModelDirectory is not null
            && _loadedModelDirectory.Equals(modelDirectory, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_loadedComputeType, computeType, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_loadedModelDirectory is not null)
        {
            var unloadStatus = FasterWhisperNativeMethods.UnloadModel(_engine);
            if (unloadStatus != FasterWhisperNativeInteropStatus.Ok)
            {
                throw CreateNativeFailureException(unloadStatus);
            }

            _loadedModelDirectory = null;
            _loadedComputeType = null;
        }

        var loadStatus = FasterWhisperNativeMethods.LoadModel(_engine, modelDirectory, computeType);
        if (loadStatus != FasterWhisperNativeInteropStatus.Ok)
        {
            throw CreateNativeFailureException(loadStatus);
        }

        _loadedModelDirectory = modelDirectory;
        _loadedComputeType = computeType;
    }

    private void RequestCancel()
    {
        if (_engine == IntPtr.Zero)
        {
            return;
        }

        try
        {
            FasterWhisperNativeMethods.RequestCancel(_engine);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        catch (BadImageFormatException)
        {
        }
    }

    private InvalidOperationException CreateNativeFailureException(FasterWhisperNativeInteropStatus status)
    {
        var message = TryGetLastError();
        if (IsSanitizedNativeMessage(message))
        {
            return new InvalidOperationException(message);
        }

        return status switch
        {
            FasterWhisperNativeInteropStatus.ModelNotFound => new InvalidOperationException("The selected faster-whisper model is not installed or was not found."),
            FasterWhisperNativeInteropStatus.ModelLoadFailed => new InvalidOperationException("The selected faster-whisper model could not be loaded."),
            FasterWhisperNativeInteropStatus.InvalidAudio => new InvalidOperationException("The completed recording could not be read by the faster-whisper engine."),
            FasterWhisperNativeInteropStatus.InvalidArgument => new InvalidOperationException("The faster-whisper engine received invalid transcription options."),
            FasterWhisperNativeInteropStatus.NotInitialized => new InvalidOperationException("The faster-whisper engine is not initialized."),
            FasterWhisperNativeInteropStatus.Timeout => new InvalidOperationException("faster-whisper local GPU transcription timed out."),
            FasterWhisperNativeInteropStatus.Canceled => new InvalidOperationException("faster-whisper local GPU transcription was canceled."),
            FasterWhisperNativeInteropStatus.DependencyUnavailable => new InvalidOperationException("The faster-whisper local GPU engine is not installed or could not be loaded."),
            FasterWhisperNativeInteropStatus.NotImplemented => new InvalidOperationException("The faster-whisper local GPU native provider is scaffolded but not implemented yet."),
            _ => new InvalidOperationException("faster-whisper local GPU transcription failed.")
        };
    }

    private string? TryGetLastError()
    {
        if (_engine == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var errorPointer = FasterWhisperNativeMethods.LastError(_engine);
            return errorPointer == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUTF8(errorPointer);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static InvalidOperationException CreateUnavailableException(Exception exception)
    {
        return new InvalidOperationException("The faster-whisper local GPU engine is not installed or could not be loaded.", exception);
    }

    private static bool IsSanitizedNativeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 160)
        {
            return false;
        }

        return !message.Any(character => character is '\\' or '/' or ':' or '"' or '\'' or '\r' or '\n');
    }
}