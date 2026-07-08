using System.Diagnostics;
using NAudio.Wave;
using Tts.Core.Services.Audio;

namespace Tts.Core.Services.Media;

public sealed class FfmpegMediaPreparationService : IMediaPreparationService
{
    private readonly AppPaths _paths;

    public FfmpegMediaPreparationService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<MediaPreparationResult> PrepareAsync(MediaPreparationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var inputPath = Path.GetFullPath(request.InputFilePath);
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("The input media file was not found.");
        }

        if (TryReadSupportedWavInfo(inputPath, out var existingInfo))
        {
            return new MediaPreparationResult(inputPath, IsOriginalFile: true, existingInfo.Format, existingInfo.Duration);
        }

        var outputDirectory = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? Path.Combine(_paths.TempAudioDirectory, "prepared")
            : Path.GetFullPath(request.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, $"prepared-{Guid.NewGuid():N}.wav");
        using var process = new Process
        {
            StartInfo = BuildStartInfo(inputPath, outputPath)
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Could not start ffmpeg audio conversion.");
            }
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            throw new InvalidOperationException("ffmpeg was not found or could not be started.", exception);
        }

        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, request.TimeoutSeconds)));

        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            KillProcess(process);
            await DrainOutputAsync(standardErrorTask);
            DeleteIfExists(outputPath);
            throw new TimeoutException("ffmpeg audio conversion timed out.");
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            await DrainOutputAsync(standardErrorTask);
            DeleteIfExists(outputPath);
            throw;
        }

        await standardErrorTask;
        if (process.ExitCode != 0)
        {
            DeleteIfExists(outputPath);
            throw new InvalidOperationException($"ffmpeg audio conversion failed with exit code {process.ExitCode}.");
        }

        if (!TryReadSupportedWavInfo(outputPath, out var convertedInfo))
        {
            DeleteIfExists(outputPath);
            throw new InvalidOperationException("ffmpeg did not produce a supported WAV file.");
        }

        return new MediaPreparationResult(outputPath, IsOriginalFile: false, convertedInfo.Format, convertedInfo.Duration);
    }

    private static ProcessStartInfo BuildStartInfo(string inputPath, string outputPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("-vn");
        startInfo.ArgumentList.Add("-acodec");
        startInfo.ArgumentList.Add("pcm_s16le");
        startInfo.ArgumentList.Add("-ar");
        startInfo.ArgumentList.Add("16000");
        startInfo.ArgumentList.Add("-ac");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add(outputPath);

        return startInfo;
    }

    private static bool TryReadSupportedWavInfo(string audioPath, out WavInfo wavInfo)
    {
        wavInfo = default;

        try
        {
            using var reader = new WaveFileReader(audioPath);
            var format = reader.WaveFormat;
            if (format.SampleRate != 16000 || format.Channels != 1 || format.BitsPerSample != 16 || !IsPcmWav(format))
            {
                return false;
            }

            wavInfo = new WavInfo(AudioCaptureFormat.FromWaveFormat(format), reader.TotalTime);
            return true;
        }
        catch (Exception exception) when (exception is IOException or FormatException or NotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsPcmWav(WaveFormat format)
    {
        return format.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.Extensible;
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

    private static async Task DrainOutputAsync(Task<string> outputTask)
    {
        try
        {
            await outputTask;
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
        {
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct WavInfo(AudioCaptureFormat Format, TimeSpan Duration);
}