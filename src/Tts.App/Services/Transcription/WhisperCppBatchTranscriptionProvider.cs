using System.Diagnostics;
using System.IO;
using Tts.App.Configuration;

namespace Tts.App.Services.Transcription;

public sealed class WhisperCppBatchTranscriptionProvider : IBatchTranscriptionProvider
{
    public const string ProviderId = "whisper-cpp-local";

    public TranscriptionProviderMetadata Metadata { get; } = new(
        ProviderId,
        "whisper.cpp local",
        TranscriptionMode.Batch,
        RequiresEndpoint: false);

    public async Task<BatchTranscriptionResult> TranscribeAsync(BatchTranscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.AudioFilePath))
        {
            throw new FileNotFoundException("The completed recording is not available for transcription.");
        }

        var executablePath = ResolveExecutablePath(request.Settings);
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new FileNotFoundException("The local Whisper engine is not installed or was not found.");
        }

        var modelPath = ResolveModelPath(request.Settings);
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            throw new FileNotFoundException($"The selected local Whisper model '{request.Settings.WhisperCppModelId}' is not installed or was not found.");
        }

        using var process = new Process
        {
            StartInfo = BuildStartInfo(executablePath, modelPath, request.AudioFilePath, request.Settings.Language)
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Could not start whisper.cpp transcription.");
            }
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            throw new InvalidOperationException("Could not start whisper.cpp transcription.", exception);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(request.Settings.TimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            KillProcess(process);
            await DrainProcessOutputAsync(standardOutputTask, standardErrorTask);
            throw new TimeoutException("whisper.cpp transcription timed out.");
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            await DrainProcessOutputAsync(standardOutputTask, standardErrorTask);
            throw;
        }

        var standardOutput = await standardOutputTask;
        await standardErrorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"whisper.cpp transcription failed with exit code {process.ExitCode}.");
        }

        return new BatchTranscriptionResult(standardOutput.Trim());
    }

    private static string ResolveExecutablePath(TranscriptionSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.WhisperCppExecutablePathOverride))
        {
            return settings.WhisperCppExecutablePathOverride.Trim();
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "tts",
            "tools",
            "whisper.cpp",
            "v1.8.6",
            "Release",
            "whisper-cli.exe");
    }

    private static string ResolveModelPath(TranscriptionSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.WhisperModelPathOverride))
        {
            return settings.WhisperModelPathOverride.Trim();
        }

        var modelFileName = WhisperCppModelCatalog.Resolve(settings.WhisperCppModelId).FileName;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "tts",
            "models",
            "whisper",
            modelFileName);
    }

    private static ProcessStartInfo BuildStartInfo(string executablePath, string modelPath, string audioFilePath, string language)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add(modelPath);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(audioFilePath);
        startInfo.ArgumentList.Add("-nt");
        startInfo.ArgumentList.Add("-np");

        if (!string.IsNullOrWhiteSpace(language))
        {
            startInfo.ArgumentList.Add("-l");
            startInfo.ArgumentList.Add(language.Trim());
        }

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
        catch (InvalidOperationException)
        {
        }
    }
}