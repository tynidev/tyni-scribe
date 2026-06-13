using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NAudio.Wave;
using Tts.Core;
using Tts.Core.Configuration;
using Tts.Core.Services.Audio;
using Tts.Core.Services.AudioProcessing;
using Tts.Core.Services.Transcription;

namespace Tts.Cli.Transcription;

internal static class TranscribeBatchCommand
{
    private const string CsvHeader = "runLabel,fileId,providerId,modelId,language,timeoutSeconds,audioDurationSeconds,audioProcessingMs,transcriptionMs,transcriptionRealTimeFactor,transcriptionAudioSecondsPerSecond,cliTotalMs,referenceWordCount,hypothesisWordCount,wordErrors,wordErrorRate,wordAccuracy,exactNormalizedMatch,status,exitCode,errorCategory";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(argument => argument.Equals("--help", StringComparison.OrdinalIgnoreCase) || argument.Equals("-h", StringComparison.OrdinalIgnoreCase)))
        {
            CommandLineHelp.Write(Console.Out);
            return 0;
        }

        if (!TranscribeBatchOptions.TryParse(args, Console.Error, out var options))
        {
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            await using var serviceProvider = BuildServiceProvider();
            var settings = await LoadSettingsAsync(options.ConfigPath, cancellation.Token);
            var manifest = await LoadManifestAsync(options.ManifestPath!, cancellation.Token);
            var files = manifest.AudioFiles.Take(options.Count).ToArray();
            if (files.Length == 0)
            {
                throw new InvalidOperationException("The manifest does not contain any audio files to transcribe.");
            }

            var providerId = string.IsNullOrWhiteSpace(options.ProviderId)
                ? settings.SelectedTranscriptionProviderId
                : options.ProviderId.Trim();
            var audioProcessorProviderId = string.IsNullOrWhiteSpace(settings.SelectedAudioProcessorProviderId)
                ? NoOpAudioProcessingProvider.ProviderId
                : settings.SelectedAudioProcessorProviderId;
            var effectiveSettings = BuildEffectiveSettings(settings, providerId, options.SettingOverrides);
            var runLabel = BuildRunLabel(providerId, effectiveSettings);
            var audioProcessor = ResolveAudioProcessor(serviceProvider, audioProcessorProviderId);
            var transcriptionProvider = ResolveTranscriptionProvider(serviceProvider, providerId);
            var audioProcessorSettings = GetProviderSettings(settings.AudioProcessingProviderSettings, audioProcessorProviderId);

            if (options.WarmupFirstFile)
            {
                await TranscribeFileAsync(
                    manifest,
                    files[0],
                    runLabel,
                    providerId,
                    audioProcessorProviderId,
                    effectiveSettings,
                    audioProcessorSettings,
                    audioProcessor,
                    transcriptionProvider,
                    isMeasured: false,
                    cancellation.Token);
            }

            var rows = new List<BatchBenchmarkRow>(files.Length);
            foreach (var file in files)
            {
                var row = await TranscribeFileAsync(
                    manifest,
                    file,
                    runLabel,
                    providerId,
                    audioProcessorProviderId,
                    effectiveSettings,
                    audioProcessorSettings,
                    audioProcessor,
                    transcriptionProvider,
                    isMeasured: true,
                    cancellation.Token);
                rows.Add(row);

                if (rows.Count % 25 == 0)
                {
                    Console.Error.WriteLine($"Processed {rows.Count} / {files.Length} for {runLabel}");
                }
            }

            await WriteCsvAsync(options.OutputCsvPath!, rows, cancellation.Token);
            if (!string.IsNullOrWhiteSpace(options.OutputJsonPath))
            {
                await WriteJsonAsync(options.OutputJsonPath, rows, cancellation.Token);
            }

            return rows.Any(row => row.Status != "success") ? 1 : 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("batch-transcription-canceled");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(CategorizeError(exception));
            return 1;
        }
    }

    private static async Task<BatchBenchmarkRow> TranscribeFileAsync(
        BatchManifest manifest,
        BatchManifestAudioFile file,
        string runLabel,
        string providerId,
        string audioProcessorProviderId,
        IReadOnlyDictionary<string, string> effectiveSettings,
        IReadOnlyDictionary<string, string> audioProcessorSettings,
        IAudioProcessingProvider audioProcessor,
        IBatchTranscriptionProvider transcriptionProvider,
        bool isMeasured,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var audioPath = Path.GetFullPath(Path.Combine(manifest.DatasetRoot, file.WavPath));
        TimeSpan? audioDuration = null;
        TimeSpan? audioProcessingDuration = null;
        TimeSpan? transcriptionDuration = null;
        string transcript = string.Empty;
        string? errorCategory = null;

        try
        {
            if (!File.Exists(audioPath))
            {
                throw new FileNotFoundException("The audio file was not found.");
            }

            var audioInfo = ReadSupportedWavInfo(audioPath);
            audioDuration = audioInfo.Duration;
            var audioProcessingStopwatch = Stopwatch.StartNew();
            var processedAudio = await audioProcessor.ProcessAsync(
                new AudioProcessingRequest(audioPath, audioInfo.Format, audioInfo.Duration, audioProcessorSettings),
                cancellationToken);
            audioProcessingStopwatch.Stop();
            audioProcessingDuration = audioProcessingStopwatch.Elapsed;

            try
            {
                var transcriptionStopwatch = Stopwatch.StartNew();
                var result = await transcriptionProvider.TranscribeAsync(
                    new BatchTranscriptionRequest(
                        processedAudio.FilePath,
                        audioInfo.Format,
                        audioInfo.Duration,
                        effectiveSettings),
                    cancellationToken);
                transcriptionStopwatch.Stop();
                transcriptionDuration = transcriptionStopwatch.Elapsed;
                transcript = result.Text;
                totalStopwatch.Stop();
                return CreateRow(
                    runLabel,
                    file,
                    providerId,
                    effectiveSettings,
                    audioDuration,
                    audioProcessingDuration,
                    transcriptionDuration,
                    totalStopwatch.Elapsed,
                    transcript,
                    "success",
                    0,
                    null);
            }
            finally
            {
                if (!processedAudio.IsOriginalFile)
                {
                    DeleteIfExists(processedAudio.FilePath);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errorCategory = CategorizeError(exception);
            totalStopwatch.Stop();
            return CreateRow(
                runLabel,
                file,
                providerId,
                effectiveSettings,
                audioDuration,
                audioProcessingDuration,
                transcriptionDuration,
                totalStopwatch.Elapsed,
                transcript,
                isMeasured ? "failure" : "warmup-failure",
                1,
                errorCategory);
        }
    }

    private static BatchBenchmarkRow CreateRow(
        string runLabel,
        BatchManifestAudioFile file,
        string providerId,
        IReadOnlyDictionary<string, string> effectiveSettings,
        TimeSpan? audioDuration,
        TimeSpan? audioProcessingDuration,
        TimeSpan? transcriptionDuration,
        TimeSpan totalDuration,
        string transcript,
        string status,
        int exitCode,
        string? errorCategory)
    {
        var score = GetWordErrorStats(file.Transcript, transcript);
        return new BatchBenchmarkRow(
            runLabel,
            file.Id,
            providerId,
            GetSetting(effectiveSettings, "modelId"),
            GetSetting(effectiveSettings, "language"),
            GetSetting(effectiveSettings, "timeoutSeconds"),
            audioDuration?.TotalSeconds,
            ToMilliseconds(audioProcessingDuration),
            ToMilliseconds(transcriptionDuration),
            CalculateRealTimeFactor(audioDuration, transcriptionDuration),
            CalculateAudioSecondsPerSecond(audioDuration, transcriptionDuration),
            ToMilliseconds(totalDuration),
            score.ReferenceWordCount,
            score.HypothesisWordCount,
            score.WordErrors,
            score.WordErrorRate,
            score.WordAccuracy,
            score.ExactNormalizedMatch,
            status,
            exitCode,
            errorCategory);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddTtsCoreServices();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<AppSettings> LoadSettingsAsync(string? configPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return AppSettingsNormalizer.Normalize(new AppSettings());
        }

        var fullConfigPath = Path.GetFullPath(configPath);
        if (!File.Exists(fullConfigPath))
        {
            throw new FileNotFoundException("The config file was not found.");
        }

        await using var stream = new FileStream(
            fullConfigPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
        return AppSettingsNormalizer.Normalize(settings ?? new AppSettings());
    }

    private static async Task<BatchManifest> LoadManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullManifestPath))
        {
            throw new FileNotFoundException("The manifest file was not found.");
        }

        await using var stream = new FileStream(
            fullManifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        var manifest = await JsonSerializer.DeserializeAsync<BatchManifest>(stream, JsonOptions, cancellationToken);
        return manifest ?? throw new InvalidOperationException("The manifest file could not be parsed.");
    }

    private static WavInfo ReadSupportedWavInfo(string audioPath)
    {
        using var reader = new WaveFileReader(audioPath);
        var format = reader.WaveFormat;
        if (format.SampleRate != 16000 || format.Channels != 1 || format.BitsPerSample != 16 || !IsPcmWav(format))
        {
            throw new InvalidOperationException("The CLI currently supports 16 kHz mono PCM 16-bit WAV input. Convert the audio before transcription.");
        }

        return new WavInfo(AudioCaptureFormat.FromWaveFormat(format), reader.TotalTime);
    }

    private static bool IsPcmWav(WaveFormat format)
    {
        return format.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.Extensible;
    }

    private static Dictionary<string, string> BuildEffectiveSettings(
        AppSettings settings,
        string providerId,
        IReadOnlyDictionary<string, string> overrides)
    {
        var effectiveSettings = new Dictionary<string, string>(GetProviderSettings(settings.TranscriptionProviderSettings, providerId), StringComparer.OrdinalIgnoreCase);
        foreach (var pair in overrides)
        {
            effectiveSettings[pair.Key] = pair.Value;
        }

        return effectiveSettings;
    }

    private static IReadOnlyDictionary<string, string> GetProviderSettings(
        Dictionary<string, Dictionary<string, string>> providerSettings,
        string providerId)
    {
        foreach (var pair in providerSettings)
        {
            if (pair.Key.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static IAudioProcessingProvider ResolveAudioProcessor(ServiceProvider serviceProvider, string audioProcessorProviderId)
    {
        var audioProcessor = serviceProvider
            .GetServices<IAudioProcessingProvider>()
            .FirstOrDefault(provider => provider.Metadata.Id.Equals(audioProcessorProviderId, StringComparison.OrdinalIgnoreCase));

        return audioProcessor ?? throw new InvalidOperationException($"Audio processor '{audioProcessorProviderId}' is not available.");
    }

    private static IBatchTranscriptionProvider ResolveTranscriptionProvider(ServiceProvider serviceProvider, string providerId)
    {
        var provider = serviceProvider
            .GetServices<IBatchTranscriptionProvider>()
            .FirstOrDefault(candidate => candidate.Metadata.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));

        return provider ?? throw new InvalidOperationException($"Transcription provider '{providerId}' is not available.");
    }

    private static async Task WriteCsvAsync(string outputCsvPath, IReadOnlyList<BatchBenchmarkRow> rows, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(outputCsvPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
        await using var writer = new StreamWriter(stream);
        await writer.WriteLineAsync(CsvHeader.AsMemory(), cancellationToken);
        foreach (var row in rows)
        {
            await writer.WriteLineAsync(FormatCsvRow(row).AsMemory(), cancellationToken);
        }
    }

    private static async Task WriteJsonAsync(string outputJsonPath, IReadOnlyList<BatchBenchmarkRow> rows, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(outputJsonPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, rows, JsonOptions, cancellationToken);
    }

    private static string FormatCsvRow(BatchBenchmarkRow row)
    {
        var fields = new[]
        {
            row.RunLabel,
            row.FileId,
            row.ProviderId,
            row.ModelId,
            row.Language,
            row.TimeoutSeconds,
            FormatDouble(row.AudioDurationSeconds),
            FormatLong(row.AudioProcessingMilliseconds),
            FormatLong(row.TranscriptionMilliseconds),
            FormatDouble(row.TranscriptionRealTimeFactor),
            FormatDouble(row.TranscriptionAudioSecondsPerSecond),
            FormatLong(row.CliTotalMilliseconds),
            row.ReferenceWordCount.ToString(CultureInfo.InvariantCulture),
            row.HypothesisWordCount.ToString(CultureInfo.InvariantCulture),
            row.WordErrors.ToString(CultureInfo.InvariantCulture),
            FormatDouble(row.WordErrorRate),
            FormatDouble(row.WordAccuracy),
            row.ExactNormalizedMatch.ToString(CultureInfo.InvariantCulture),
            row.Status,
            row.ExitCode.ToString(CultureInfo.InvariantCulture),
            row.ErrorCategory ?? string.Empty
        };

        return string.Join(',', fields.Select(EscapeCsv));
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string FormatDouble(double? value)
    {
        return value is null ? string.Empty : value.Value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string FormatLong(long? value)
    {
        return value is null ? string.Empty : value.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string GetSetting(IReadOnlyDictionary<string, string> settings, string key)
    {
        return settings.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static long? ToMilliseconds(TimeSpan? duration)
    {
        return duration is null ? null : ToMilliseconds(duration.Value);
    }

    private static long ToMilliseconds(TimeSpan duration)
    {
        return (long)Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero);
    }

    private static double? CalculateRealTimeFactor(TimeSpan? audioDuration, TimeSpan? elapsed)
    {
        if (audioDuration is null || elapsed is null || audioDuration.Value.TotalSeconds <= 0)
        {
            return null;
        }

        return Math.Round(elapsed.Value.TotalSeconds / audioDuration.Value.TotalSeconds, 6, MidpointRounding.AwayFromZero);
    }

    private static double? CalculateAudioSecondsPerSecond(TimeSpan? audioDuration, TimeSpan? elapsed)
    {
        if (audioDuration is null || elapsed is null || elapsed.Value.TotalSeconds <= 0)
        {
            return null;
        }

        return Math.Round(audioDuration.Value.TotalSeconds / elapsed.Value.TotalSeconds, 6, MidpointRounding.AwayFromZero);
    }

    private static WordErrorStats GetWordErrorStats(string expected, string actual)
    {
        var reference = GetNormalizedWords(expected);
        var hypothesis = GetNormalizedWords(actual);
        var distance = CalculateEditDistance(reference, hypothesis);
        double? wordErrorRate = reference.Length > 0
            ? Math.Round(distance / (double)reference.Length, 6, MidpointRounding.AwayFromZero)
            : null;
        double? wordAccuracy = wordErrorRate is null
            ? null
            : Math.Round(Math.Max(0.0, 1.0 - wordErrorRate.Value), 6, MidpointRounding.AwayFromZero);

        return new WordErrorStats(
            reference.Length,
            hypothesis.Length,
            distance,
            wordErrorRate,
            wordAccuracy,
            string.Join(' ', reference).Equals(string.Join(' ', hypothesis), StringComparison.Ordinal));
    }

    private static string[] GetNormalizedWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var normalizedCharacters = text
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character == '\'' ? character : ' ')
            .ToArray();

        return new string(normalizedCharacters)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static int CalculateEditDistance(IReadOnlyList<string> reference, IReadOnlyList<string> hypothesis)
    {
        var previous = new int[hypothesis.Count + 1];
        var current = new int[hypothesis.Count + 1];
        for (var column = 0; column <= hypothesis.Count; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= reference.Count; row++)
        {
            current[0] = row;
            for (var column = 1; column <= hypothesis.Count; column++)
            {
                var cost = reference[row - 1].Equals(hypothesis[column - 1], StringComparison.Ordinal) ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(previous[column] + 1, current[column - 1] + 1),
                    previous[column - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[hypothesis.Count];
    }

    private static string CategorizeError(Exception exception)
    {
        return exception switch
        {
            FileNotFoundException => "file-not-found",
            DirectoryNotFoundException => "directory-not-found",
            TimeoutException => "transcription-timeout",
            InvalidOperationException => "invalid-operation",
            UnauthorizedAccessException => "access-denied",
            JsonException => "config-json",
            _ => "transcription-failed"
        };
    }

    private static string BuildRunLabel(string providerId, IReadOnlyDictionary<string, string> settings)
    {
        var model = GetSetting(settings, "modelId");
        return $"{providerId}-{model}";
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

    private sealed record BatchManifest(string DatasetRoot, IReadOnlyList<BatchManifestAudioFile> AudioFiles);

    private sealed record BatchManifestAudioFile(string Id, string WavPath, string Transcript);

    private sealed record WavInfo(AudioCaptureFormat Format, TimeSpan Duration);

    private sealed record WordErrorStats(int ReferenceWordCount, int HypothesisWordCount, int WordErrors, double? WordErrorRate, double? WordAccuracy, bool ExactNormalizedMatch);

    private sealed record BatchBenchmarkRow(
        string RunLabel,
        string FileId,
        string ProviderId,
        string ModelId,
        string Language,
        string TimeoutSeconds,
        double? AudioDurationSeconds,
        long? AudioProcessingMilliseconds,
        long? TranscriptionMilliseconds,
        double? TranscriptionRealTimeFactor,
        double? TranscriptionAudioSecondsPerSecond,
        long CliTotalMilliseconds,
        int ReferenceWordCount,
        int HypothesisWordCount,
        int WordErrors,
        double? WordErrorRate,
        double? WordAccuracy,
        bool ExactNormalizedMatch,
        string Status,
        int ExitCode,
        string? ErrorCategory);
}