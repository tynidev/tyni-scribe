using System.Runtime.InteropServices;

namespace Tts.Core.Services.Transcription;

public sealed class WhisperNativeEngine : IWhisperNativeEngine
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private IntPtr _engine;
    private string? _loadedModelPath;
    private bool _disposed;

    public async Task<string> TranscribeAsync(WhisperNativeTranscriptionRequest request, CancellationToken cancellationToken = default)
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
            WhisperNativeMethods.DisposeEngine(_engine);
            _engine = IntPtr.Zero;
            _loadedModelPath = null;
        }

        _sync.Dispose();
    }

    private string TranscribeOnWorkerThread(WhisperNativeTranscriptionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureEngineCreated();
        EnsureModelLoaded(request.ModelPath);

        using var cancellationRegistration = cancellationToken.Register(RequestCancel);

        var status = WhisperNativeInteropStatus.NativeFailure;
        var transcriptPointer = IntPtr.Zero;

        try
        {
            status = WhisperNativeMethods.TranscribeWav(
                _engine,
                request.AudioFilePath,
                request.Language,
                request.TimeoutSeconds,
                out transcriptPointer);

            if (status == WhisperNativeInteropStatus.Ok)
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
                WhisperNativeMethods.FreeString(transcriptPointer);
            }
        }

        if ((status is WhisperNativeInteropStatus.Canceled or WhisperNativeInteropStatus.Timeout) && cancellationToken.IsCancellationRequested)
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
            var status = WhisperNativeMethods.CreateEngine(out var engine);
            if (status != WhisperNativeInteropStatus.Ok || engine == IntPtr.Zero)
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

    private void EnsureModelLoaded(string modelPath)
    {
        if (_loadedModelPath is not null && _loadedModelPath.Equals(modelPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_loadedModelPath is not null)
        {
            var unloadStatus = WhisperNativeMethods.UnloadModel(_engine);
            if (unloadStatus != WhisperNativeInteropStatus.Ok)
            {
                throw CreateNativeFailureException(unloadStatus);
            }

            _loadedModelPath = null;
        }

        var loadStatus = WhisperNativeMethods.LoadModel(_engine, modelPath);
        if (loadStatus != WhisperNativeInteropStatus.Ok)
        {
            throw CreateNativeFailureException(loadStatus);
        }

        _loadedModelPath = modelPath;
    }

    private void RequestCancel()
    {
        if (_engine == IntPtr.Zero)
        {
            return;
        }

        try
        {
            WhisperNativeMethods.RequestCancel(_engine);
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

    private InvalidOperationException CreateNativeFailureException(WhisperNativeInteropStatus status)
    {
        var message = TryGetLastError();
        if (IsSanitizedNativeMessage(message))
        {
            return new InvalidOperationException(message);
        }

        return status switch
        {
            WhisperNativeInteropStatus.ModelNotFound => new InvalidOperationException("The selected local Whisper model is not installed or was not found."),
            WhisperNativeInteropStatus.ModelLoadFailed => new InvalidOperationException("The selected local Whisper model could not be loaded."),
            WhisperNativeInteropStatus.InvalidAudio => new InvalidOperationException("The completed recording could not be read by the native Whisper engine."),
            WhisperNativeInteropStatus.InvalidArgument => new InvalidOperationException("The native Whisper engine received invalid transcription options."),
            WhisperNativeInteropStatus.NotInitialized => new InvalidOperationException("The native Whisper engine is not initialized."),
            WhisperNativeInteropStatus.Timeout => new InvalidOperationException("Native whisper.cpp transcription timed out."),
            WhisperNativeInteropStatus.Canceled => new InvalidOperationException("Native whisper.cpp transcription was canceled."),
            _ => new InvalidOperationException("Native whisper.cpp transcription failed.")
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
            var errorPointer = WhisperNativeMethods.LastError(_engine);
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
        return new InvalidOperationException("The native whisper.cpp engine is not installed or could not be loaded.", exception);
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