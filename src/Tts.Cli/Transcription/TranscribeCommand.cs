using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NAudio.Wave;
using Tts.Core;
using Tts.Core.Configuration;
using Tts.Core.Services.Audio;
using Tts.Core.Services.AudioProcessing;
using Tts.Core.Services.Transcription;

namespace Tts.Cli.Transcription;

internal static class TranscribeCommand
{
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

        if (!TranscribeOptions.TryParse(args, Console.Error, out var options))
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
        string? providerId = null;
        string? audioProcessorProviderId = null;
        var effectiveSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? normalizedAudioPath = null;
        TimeSpan? audioDuration = null;
        TimeSpan? audioProcessingDuration = null;
        TimeSpan? transcriptionDuration = null;
        string? errorCategory = null;

        try
        {
            await using var serviceProvider = BuildServiceProvider();
            var settings = await LoadSettingsAsync(serviceProvider, options, cancellation.Token);

            normalizedAudioPath = Path.GetFullPath(options.AudioPath!);
            if (!File.Exists(normalizedAudioPath))
            {
                throw new FileNotFoundException("The audio file was not found.");
            }

            var audioInfo = ReadSupportedWavInfo(normalizedAudioPath);
            audioDuration = audioInfo.Duration;

            providerId = string.IsNullOrWhiteSpace(options.ProviderId)
                ? settings.SelectedTranscriptionProviderId
                : options.ProviderId.Trim();
            audioProcessorProviderId = string.IsNullOrWhiteSpace(settings.SelectedAudioProcessorProviderId)
                ? AppSettings.DefaultAudioProcessorProviderId
                : settings.SelectedAudioProcessorProviderId;

            effectiveSettings = BuildEffectiveSettings(settings, providerId, options.SettingOverrides);

            var audioProcessor = ResolveAudioProcessor(serviceProvider, audioProcessorProviderId);
            var transcriptionProvider = ResolveTranscriptionProvider(serviceProvider, providerId);

            var audioProcessingStopwatch = Stopwatch.StartNew();
            var processedAudio = await audioProcessor.ProcessAsync(
                new AudioProcessingRequest(
                    normalizedAudioPath,
                    audioInfo.Format,
                    audioInfo.Duration,
                    GetProviderSettings(settings.AudioProcessingProviderSettings, audioProcessorProviderId)),
                cancellation.Token);
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
                    cancellation.Token);
                transcriptionStopwatch.Stop();
                transcriptionDuration = transcriptionStopwatch.Elapsed;

                totalStopwatch.Stop();
                await WriteMetricsAsync(
                    options.MetricsOutputPath,
                    new TranscriptionMetrics(
                        "success",
                        null,
                        providerId,
                        audioProcessorProviderId,
                        effectiveSettings,
                        normalizedAudioPath,
                        audioDuration.Value.TotalSeconds,
                        ToMilliseconds(audioProcessingDuration),
                        ToMilliseconds(transcriptionDuration),
                        CalculateRealTimeFactor(audioDuration, transcriptionDuration),
                        CalculateAudioSecondsPerSecond(audioDuration, transcriptionDuration),
                        ToMilliseconds(totalStopwatch.Elapsed)),
                    cancellation.Token);

                Console.Out.Write(result.Text);
                return 0;
            }
            finally
            {
                if (!processedAudio.IsOriginalFile)
                {
                    DeleteIfExists(processedAudio.FilePath);
                }
            }
        }
        catch (OperationCanceledException)
        {
            totalStopwatch.Stop();
            errorCategory = "canceled";
            Console.Error.WriteLine("transcription-canceled");
            return await WriteFailureAndReturnAsync();
        }
        catch (Exception exception)
        {
            totalStopwatch.Stop();
            errorCategory = CategorizeError(exception);
            Console.Error.WriteLine(errorCategory);
            return await WriteFailureAndReturnAsync();
        }

        async Task<int> WriteFailureAndReturnAsync()
        {
            await WriteMetricsAsync(
                options.MetricsOutputPath,
                new TranscriptionMetrics(
                    "failure",
                    errorCategory,
                    providerId,
                    audioProcessorProviderId,
                    effectiveSettings,
                    normalizedAudioPath,
                    audioDuration?.TotalSeconds,
                    ToMilliseconds(audioProcessingDuration),
                    ToMilliseconds(transcriptionDuration),
                    CalculateRealTimeFactor(audioDuration, transcriptionDuration),
                    CalculateAudioSecondsPerSecond(audioDuration, transcriptionDuration),
                    ToMilliseconds(totalStopwatch.Elapsed)),
                CancellationToken.None);

            return errorCategory == "usage" ? 2 : 1;
        }
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddTtsCoreServices();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<AppSettings> LoadSettingsAsync(
        ServiceProvider serviceProvider,
        TranscribeOptions options,
        CancellationToken cancellationToken)
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

        await using var stream = new FileStream(
            configPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
        return AppSettingsNormalizer.Normalize(settings ?? new AppSettings());
    }

    private static WavInfo ReadSupportedWavInfo(string audioPath)
    {
        try
        {
            using var reader = new WaveFileReader(audioPath);
            var format = reader.WaveFormat;
            if (format.SampleRate != 16000 || format.Channels != 1 || format.BitsPerSample != 16 || !IsPcmWav(format))
            {
                throw new InvalidOperationException("The CLI currently supports 16 kHz mono PCM 16-bit WAV input. Convert the audio before transcription.");
            }

            return new WavInfo(AudioCaptureFormat.FromWaveFormat(format), reader.TotalTime);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or FormatException or NotSupportedException)
        {
            throw new InvalidOperationException("The audio file could not be read as a supported WAV file.", exception);
        }
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

    private static async Task WriteMetricsAsync(
        string? metricsOutputPath,
        TranscriptionMetrics metrics,
        CancellationToken cancellationToken)
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

        await using var stream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        await JsonSerializer.SerializeAsync(stream, metrics, JsonOptions, cancellationToken);
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

    private static long? ToMilliseconds(TimeSpan? duration)
    {
        return duration is null
            ? null
            : ToMilliseconds(duration.Value);
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

    private static long ToMilliseconds(TimeSpan duration)
    {
        return (long)Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero);
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

    private sealed record WavInfo(AudioCaptureFormat Format, TimeSpan Duration);
}