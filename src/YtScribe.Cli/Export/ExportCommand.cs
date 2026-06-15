using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tts.Core.Configuration;
using YtScribe.Core.Model;
using YtScribe.Core.Services;

namespace YtScribe.Cli.Export;

internal static class ExportCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync(string[] args, IServiceProvider serviceProvider)
    {
        if (args.Any(argument => argument.Equals("--help", StringComparison.OrdinalIgnoreCase) || argument.Equals("-h", StringComparison.OrdinalIgnoreCase)))
        {
            CommandLineHelp.Write(Console.Out);
            return 0;
        }

        if (!ExportOptions.TryParse(args, Console.Error, out var options))
        {
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        var totalStopwatch = Stopwatch.StartNew();
        string? errorCategory = null;
        YouTubeExportResult? result = null;

        try
        {
            var settings = await LoadSettingsAsync(serviceProvider, options, cancellation.Token);
            var exportService = serviceProvider.GetRequiredService<IYouTubeExportService>();
            result = await exportService.ExportAsync(
                new YouTubeExportRequest(
                    options.Url!,
                    options.OutputDirectory!,
                    settings,
                    options.CaptionLanguage,
                    options.ForceAudio,
                    options.Overwrite,
                    options.KeepTemp,
                    options.WritePlainText,
                    options.ProviderId,
                    options.SettingOverrides),
                cancellation.Token);

            totalStopwatch.Stop();
            await WriteMetricsAsync(
                options.MetricsOutputPath,
                CreateMetrics(options, result, "success", null, totalStopwatch.Elapsed),
                cancellation.Token);

            Console.Out.WriteLine(result.OutputDirectory);
            return 0;
        }
        catch (OperationCanceledException)
        {
            totalStopwatch.Stop();
            errorCategory = "canceled";
            Console.Error.WriteLine("export-canceled");
            await WriteMetricsAsync(options.MetricsOutputPath, CreateMetrics(options, result, "failure", errorCategory, totalStopwatch.Elapsed), CancellationToken.None);
            return 130;
        }
        catch (Exception exception)
        {
            totalStopwatch.Stop();
            errorCategory = CategorizeError(exception);
            Console.Error.WriteLine(errorCategory);
            await WriteMetricsAsync(options.MetricsOutputPath, CreateMetrics(options, result, "failure", errorCategory, totalStopwatch.Elapsed), CancellationToken.None);
            return errorCategory == "usage" ? 2 : 1;
        }
    }

    private static async Task<AppSettings> LoadSettingsAsync(IServiceProvider serviceProvider, ExportOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ConfigPath))
        {
            return await serviceProvider.GetRequiredService<IAppSettingsStore>().LoadAsync(cancellationToken);
        }

        var configPath = Path.GetFullPath(options.ConfigPath);
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("The config file was not found.");
        }

        await using var stream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
        return AppSettingsNormalizer.Normalize(settings ?? new AppSettings());
    }

    private static YouTubeExportMetrics CreateMetrics(
        ExportOptions options,
        YouTubeExportResult? result,
        string status,
        string? errorCategory,
        TimeSpan totalDuration)
    {
        return new YouTubeExportMetrics(
            status,
            errorCategory,
            options.Url ?? string.Empty,
            null,
            result?.OutputDirectory,
            result?.TranscriptOrigin,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            ToMilliseconds(totalDuration));
    }

    private static async Task WriteMetricsAsync(string? metricsOutputPath, YouTubeExportMetrics metrics, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(metricsOutputPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(metricsOutputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, metrics, JsonOptions, cancellationToken);
    }

    private static string CategorizeError(Exception exception)
    {
        return exception switch
        {
            FileNotFoundException => "file-not-found",
            DirectoryNotFoundException => "directory-not-found",
            TimeoutException => "timeout",
            InvalidOperationException => "invalid-operation",
            UnauthorizedAccessException => "access-denied",
            IOException => "io-error",
            JsonException => "json-error",
            _ => "export-failed"
        };
    }

    private static long ToMilliseconds(TimeSpan duration)
    {
        return (long)Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero);
    }
}