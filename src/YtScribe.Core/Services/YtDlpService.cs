using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using YtScribe.Core.Model;

namespace YtScribe.Core.Services;

public sealed class YtDlpService : IYtDlpService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<YouTubeVideoMetadata> GetMetadataAsync(string url, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(new[] { "--no-playlist", "-J", url }, null, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"yt-dlp metadata fetch failed with exit code {result.ExitCode}.");
        }

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        var id = GetString(root, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("yt-dlp metadata did not include a video id.");
        }

        return new YouTubeVideoMetadata(
            id,
            url,
            GetString(root, "title"),
            GetString(root, "channel"),
            GetString(root, "uploader"),
            GetString(root, "upload_date"),
            GetDouble(root, "duration"),
            GetString(root, "webpage_url") ?? url,
            GetString(root, "thumbnail"));
    }

    public async Task<string?> DownloadCaptionsAsync(string url, string outputDirectory, string captionLanguage, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var outputTemplate = Path.Combine(outputDirectory, "caption.%(ext)s");
        var subLanguage = string.IsNullOrWhiteSpace(captionLanguage)
            ? "en.*"
            : captionLanguage.Trim();

        var arguments = new[]
        {
            "--no-playlist",
            "--skip-download",
            "--write-subs",
            "--write-auto-subs",
            "--sub-langs",
            subLanguage,
            "--sub-format",
            "vtt",
            "-o",
            outputTemplate,
            url
        };

        var result = await RunAsync(arguments, outputDirectory, cancellationToken);
        var captionPath = Directory
            .EnumerateFiles(outputDirectory, "caption*.vtt", SearchOption.TopDirectoryOnly)
            .OrderBy(path => GetCaptionRank(path, captionLanguage))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (captionPath is not null)
        {
            return captionPath;
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"yt-dlp caption download failed with exit code {result.ExitCode}.");
        }

        return null;
    }

    public async Task<string> DownloadAudioAsync(string url, string outputDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var outputTemplate = Path.Combine(outputDirectory, "audio.%(ext)s");
        var before = Directory.EnumerateFiles(outputDirectory, "audio.*", SearchOption.TopDirectoryOnly).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var arguments = new[]
        {
            "--no-playlist",
            "-f",
            "bestaudio/best",
            "-o",
            outputTemplate,
            url
        };

        var result = await RunAsync(arguments, outputDirectory, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"yt-dlp audio download failed with exit code {result.ExitCode}.");
        }

        var audioPath = Directory
            .EnumerateFiles(outputDirectory, "audio.*", SearchOption.TopDirectoryOnly)
            .Where(path => !before.Contains(path) && !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault()
            ?? Directory
                .EnumerateFiles(outputDirectory, "audio.*", SearchOption.TopDirectoryOnly)
                .Where(path => !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

        return audioPath ?? throw new InvalidOperationException("yt-dlp did not produce an audio file.");
    }

    private static async Task<YtDlpProcessResult> RunAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        try
        {
            return await RunProcessAsync("yt-dlp", arguments, workingDirectory, cancellationToken);
        }
        catch (InvalidOperationException exception) when (exception.InnerException is Win32Exception)
        {
            return await RunProcessAsync("python", ["-m", "yt_dlp", .. arguments], workingDirectory, cancellationToken);
        }
    }

    private static async Task<YtDlpProcessResult> RunProcessAsync(IReadOnlyList<string> command, string? workingDirectory, CancellationToken cancellationToken)
    {
        return await RunProcessAsync(command[0], command.Skip(1).ToArray(), workingDirectory, cancellationToken);
    }

    private static async Task<YtDlpProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Could not start {fileName}.");
            }
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            throw new InvalidOperationException($"{fileName} was not found or could not be started.", exception);
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

        return new YtDlpProcessResult(process.ExitCode, await standardOutputTask, await standardErrorTask);
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static double? GetDouble(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.TryGetDouble(out var parsed)
            ? parsed
            : null;
    }

    private static int GetCaptionRank(string path, string captionLanguage)
    {
        var fileName = Path.GetFileName(path);
        var language = captionLanguage.Trim().TrimEnd('*').TrimEnd('.');
        if (language.Length == 0)
        {
            return 10;
        }

        if (fileName.Equals($"caption.{language}.vtt", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (fileName.StartsWith($"caption.{language}-", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 10;
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

    private sealed record YtDlpProcessResult(int ExitCode, string StandardOutput, string StandardError);
}