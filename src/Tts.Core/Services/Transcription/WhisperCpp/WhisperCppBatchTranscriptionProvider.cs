using System.Diagnostics;
using System.IO;
using Tts.Core.Services;

namespace Tts.Core.Services.Transcription;

public sealed class WhisperCppBatchTranscriptionProvider : IBatchTranscriptionProvider
{
    public const string ProviderId = "whisper-cpp-local";

    public TranscriptionProviderMetadata Metadata { get; } = new(
        ProviderId,
        "whisper-cli",
        TranscriptionMode.Batch,
        RequiresEndpoint: false,
        "Runs whisper-cli.exe for each completed recording.");

    public IReadOnlyList<ProviderSettingDescriptor> SettingDescriptors => WhisperCppProviderSettings.Descriptors;

    public async Task<BatchTranscriptionResult> TranscribeAsync(BatchTranscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.AudioFilePath))
        {
            throw new FileNotFoundException("The completed recording is not available for transcription.");
        }

        var settings = WhisperCppProviderSettings.Parse(request.Settings);
        var executablePath = WhisperCppRuntimePaths.ResolveCliExecutablePath(settings);
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new FileNotFoundException("The local Whisper engine is not installed or was not found.");
        }

        var modelPath = WhisperCppRuntimePaths.ResolveModelPath(settings);
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            throw new FileNotFoundException($"The selected local Whisper model '{settings.ModelId}' is not installed or was not found.");
        }

        using var process = new Process
        {
            StartInfo = BuildStartInfo(executablePath, modelPath, request.AudioFilePath, settings.Language)
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
        timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

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
