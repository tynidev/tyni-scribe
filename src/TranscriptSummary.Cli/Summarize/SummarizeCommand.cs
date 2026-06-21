using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TranscriptSummary.Core.Services;

namespace TranscriptSummary.Cli.Summarize;

internal static class SummarizeCommand
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

        if (!SummarizeOptions.TryParse(args, Console.Error, out var options))
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
        TranscriptSummaryResult? result = null;
        string? errorCategory = null;

        try
        {
            var summaryService = serviceProvider.GetRequiredService<ITranscriptSummaryService>();
            var progress = new Progress<SummaryProgress>(ReportProgress);
            result = await summaryService.SummarizeAsync(
                new TranscriptSummaryRequest(
                    options.InputPath!,
                    options.Prompt,
                    options.Model,
                    options.Endpoint,
                    options.ContextTokens,
                    options.ReservedOutputTokens,
                    options.MaxOutputTokens,
                    options.CharsPerToken,
                    options.Mode,
                    options.TimeoutSeconds,
                    options.EstimateOnly,
                    progress),
                cancellation.Token);

            if (!options.EstimateOnly)
            {
                await WriteSummaryAsync(options.OutputPath!, result.SummaryText ?? string.Empty, cancellation.Token);
            }

            totalStopwatch.Stop();
            await WriteMetricsAsync(
                options.MetricsOutputPath,
                CreateMetrics(options, result, "success", null, totalStopwatch.Elapsed),
                cancellation.Token);

            Console.Out.WriteLine(options.OutputPath);
            return 0;
        }
        catch (OperationCanceledException)
        {
            totalStopwatch.Stop();
            errorCategory = "canceled";
            Console.Error.WriteLine("summarize-canceled");
            await WriteMetricsAsync(options.MetricsOutputPath, CreateMetrics(options, result, "failure", errorCategory, totalStopwatch.Elapsed), CancellationToken.None);
            return 130;
        }
        catch (Exception exception)
        {
            totalStopwatch.Stop();
            errorCategory = CategorizeError(exception);
            Console.Error.WriteLine(errorCategory);
            await WriteMetricsAsync(options.MetricsOutputPath, CreateMetrics(options, result, "failure", errorCategory, totalStopwatch.Elapsed), CancellationToken.None);
            return errorCategory is "usage" or "file-not-found" ? 2 : 1;
        }
    }

    private static async Task WriteSummaryAsync(string outputPath, string summary, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(fullPath, summary, cancellationToken);
    }

    private static void ReportProgress(SummaryProgress progress)
    {
        Console.Error.WriteLine($"{progress.Stage} pass={progress.PassIndex} item={progress.ItemIndex}/{progress.ItemCount} {progress.Status}");
    }

    private static async Task WriteMetricsAsync(string? metricsOutputPath, SummarizeMetrics metrics, CancellationToken cancellationToken)
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

    private static SummarizeMetrics CreateMetrics(
        SummarizeOptions options,
        TranscriptSummaryResult? result,
        string status,
        string? errorCategory,
        TimeSpan totalDuration)
    {
        return new SummarizeMetrics(
            status,
            errorCategory,
            Path.GetFileName(options.InputPath),
            GetParentDirectoryName(options.InputPath),
            options.OutputPath,
            options.Model,
            options.Endpoint.Host,
            options.ContextTokens,
            options.ReservedOutputTokens,
            options.MaxOutputTokens,
            options.CharsPerToken,
            result?.EstimatedTranscriptTokens,
            options.Mode == TranscriptSummaryMode.SinglePass ? "single-pass" : "hierarchical",
            options.EstimateOnly,
            result?.ChunkCount,
            result?.MergePassCount,
            result?.LlmRequestCount,
            result?.TotalLlmMilliseconds,
            result?.Passes,
            result?.Requests,
            ToMilliseconds(totalDuration));
    }

    private static string? GetParentDirectoryName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        return string.IsNullOrWhiteSpace(directory) ? null : Path.GetFileName(directory);
    }

    private static string CategorizeError(Exception exception)
    {
        return exception switch
        {
            FileNotFoundException => "file-not-found",
            DirectoryNotFoundException => "directory-not-found",
            TaskCanceledException => "timeout",
            TimeoutException => "timeout",
            UnauthorizedAccessException => "access-denied",
            IOException => "io-error",
            JsonException => "json-error",
            InvalidOperationException => "invalid-operation",
            _ => "summarize-failed"
        };
    }

    private static long ToMilliseconds(TimeSpan duration)
    {
        return (long)Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero);
    }
}