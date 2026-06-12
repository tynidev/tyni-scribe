using System.Diagnostics;
using System.IO;

namespace Tts.App.Services.Transcription;

public sealed class FasterWhisperProcessEngine : IFasterWhisperNativeEngine
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private bool _disposed;

    public async Task<string> TranscribeAsync(FasterWhisperNativeTranscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _sync.WaitAsync(cancellationToken);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await TranscribeWithProcessAsync(request, cancellationToken);
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
        _sync.Dispose();
    }

    private static async Task<string> TranscribeWithProcessAsync(FasterWhisperNativeTranscriptionRequest request, CancellationToken cancellationToken)
    {
        var pythonPath = FasterWhisperRuntimePaths.ResolvePythonExecutablePath();
        if (!File.Exists(pythonPath))
        {
            throw new InvalidOperationException("The faster-whisper Python runtime is not installed or could not be loaded.");
        }

        var runnerScriptPath = FasterWhisperRuntimePaths.ResolveRunnerScriptPath();
        if (!File.Exists(runnerScriptPath))
        {
            throw new InvalidOperationException("The faster-whisper runner is not installed or could not be loaded.");
        }

        using var process = new Process
        {
            StartInfo = BuildStartInfo(pythonPath, runnerScriptPath, request)
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Could not start faster-whisper transcription.");
            }
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            throw new InvalidOperationException("Could not start faster-whisper transcription.", exception);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            await DrainProcessOutputAsync(standardOutputTask, standardErrorTask);
            throw;
        }

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(GetSanitizedFailureMessage(standardError));
        }

        return standardOutput.Trim();
    }

    private static ProcessStartInfo BuildStartInfo(string pythonPath, string runnerScriptPath, FasterWhisperNativeTranscriptionRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(runnerScriptPath);
        startInfo.ArgumentList.Add("--model-dir");
        startInfo.ArgumentList.Add(request.ModelDirectory);
        startInfo.ArgumentList.Add("--audio-file");
        startInfo.ArgumentList.Add(request.AudioFilePath);
        startInfo.ArgumentList.Add("--language");
        startInfo.ArgumentList.Add(request.Language);
        startInfo.ArgumentList.Add("--compute-type");
        startInfo.ArgumentList.Add(request.ComputeType);

        var pythonDirectory = Path.GetDirectoryName(pythonPath);
        var existingPath = startInfo.EnvironmentVariables["PATH"] ?? string.Empty;
        var pathParts = new List<string> { AppContext.BaseDirectory };

        if (!string.IsNullOrWhiteSpace(pythonDirectory))
        {
            pathParts.Add(pythonDirectory);
        }

        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        if (!string.IsNullOrWhiteSpace(cudaPath))
        {
            pathParts.Add(Path.Combine(cudaPath, "bin"));
        }

        pathParts.Add(existingPath);
        startInfo.EnvironmentVariables["PATH"] = string.Join(Path.PathSeparator, pathParts.Where(part => !string.IsNullOrWhiteSpace(part)));
        startInfo.EnvironmentVariables["PYTHONUTF8"] = "1";
        startInfo.EnvironmentVariables["HF_HUB_OFFLINE"] = "1";

        return startInfo;
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task DrainProcessOutputAsync(Task<string> standardOutputTask, Task<string> standardErrorTask)
    {
        try
        {
            await Task.WhenAll(standardOutputTask, standardErrorTask);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
        {
        }
    }

    private static string GetSanitizedFailureMessage(string standardError)
    {
        var message = standardError
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault()
            ?.Trim();

        return IsSanitizedMessage(message)
            ? message!
            : "faster-whisper local GPU transcription failed.";
    }

    private static bool IsSanitizedMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 160)
        {
            return false;
        }

        return !message.Any(character => character is '\\' or '/' or ':' or '"' or '\'' or '\r' or '\n');
    }
}