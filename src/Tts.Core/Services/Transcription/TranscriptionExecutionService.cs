using System.Diagnostics;
using NAudio.Wave;
using Tts.Core.Configuration;
using Tts.Core.Services.Audio;
using Tts.Core.Services.AudioProcessing;

namespace Tts.Core.Services.Transcription;

public sealed class TranscriptionExecutionService : ITranscriptionExecutionService
{
    private readonly IEnumerable<IAudioProcessingProvider> _audioProcessors;
    private readonly IEnumerable<IBatchTranscriptionProvider> _transcriptionProviders;

    public TranscriptionExecutionService(
        IEnumerable<IAudioProcessingProvider> audioProcessors,
        IEnumerable<IBatchTranscriptionProvider> transcriptionProviders)
    {
        _audioProcessors = audioProcessors;
        _transcriptionProviders = transcriptionProviders;
    }

    public async Task<TranscriptionExecutionResult> TranscribeAsync(TranscriptionExecutionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);

        var audioPath = Path.GetFullPath(request.AudioFilePath);
        if (!File.Exists(audioPath))
        {
            throw new FileNotFoundException("The audio file was not found.");
        }

        var audioInfo = ReadSupportedWavInfo(audioPath);
        var providerId = string.IsNullOrWhiteSpace(request.ProviderId)
            ? request.Settings.SelectedTranscriptionProviderId
            : request.ProviderId.Trim();
        var audioProcessorProviderId = string.IsNullOrWhiteSpace(request.AudioProcessorProviderId)
            ? request.Settings.SelectedAudioProcessorProviderId
            : request.AudioProcessorProviderId.Trim();

        if (string.IsNullOrWhiteSpace(audioProcessorProviderId))
        {
            audioProcessorProviderId = AppSettings.DefaultAudioProcessorProviderId;
        }

        var effectiveSettings = BuildEffectiveSettings(request.Settings, providerId, request.SettingOverrides ?? new Dictionary<string, string>());
        var audioProcessor = ResolveAudioProcessor(audioProcessorProviderId);
        var transcriptionProvider = ResolveTranscriptionProvider(providerId);
        var audioProcessorSettings = GetProviderSettings(request.Settings.AudioProcessingProviderSettings, audioProcessorProviderId);

        var audioProcessingStopwatch = Stopwatch.StartNew();
        var processedAudio = await audioProcessor.ProcessAsync(
            new AudioProcessingRequest(audioPath, audioInfo.Format, audioInfo.Duration, audioProcessorSettings),
            cancellationToken);
        audioProcessingStopwatch.Stop();

        try
        {
            var transcriptionStopwatch = Stopwatch.StartNew();
            var result = await transcriptionProvider.TranscribeAsync(
                new BatchTranscriptionRequest(processedAudio.FilePath, audioInfo.Format, audioInfo.Duration, effectiveSettings),
                cancellationToken);
            transcriptionStopwatch.Stop();

            return new TranscriptionExecutionResult(
                result.Text,
                providerId,
                audioProcessorProviderId,
                effectiveSettings,
                audioPath,
                audioInfo.Duration.TotalSeconds,
                ToMilliseconds(audioProcessingStopwatch.Elapsed),
                ToMilliseconds(transcriptionStopwatch.Elapsed),
                CalculateRealTimeFactor(audioInfo.Duration, transcriptionStopwatch.Elapsed),
                CalculateAudioSecondsPerSecond(audioInfo.Duration, transcriptionStopwatch.Elapsed));
        }
        finally
        {
            if (!processedAudio.IsOriginalFile)
            {
                DeleteIfExists(processedAudio.FilePath);
            }
        }
    }

    private IAudioProcessingProvider ResolveAudioProcessor(string audioProcessorProviderId)
    {
        var audioProcessor = _audioProcessors.FirstOrDefault(provider => provider.Metadata.Id.Equals(audioProcessorProviderId, StringComparison.OrdinalIgnoreCase));
        return audioProcessor ?? throw new InvalidOperationException($"Audio processor '{audioProcessorProviderId}' is not available.");
    }

    private IBatchTranscriptionProvider ResolveTranscriptionProvider(string providerId)
    {
        var provider = _transcriptionProviders.FirstOrDefault(candidate => candidate.Metadata.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        return provider ?? throw new InvalidOperationException($"Transcription provider '{providerId}' is not available.");
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

    private static WavInfo ReadSupportedWavInfo(string audioPath)
    {
        try
        {
            using var reader = new WaveFileReader(audioPath);
            var format = reader.WaveFormat;
            if (format.SampleRate != 16000 || format.Channels != 1 || format.BitsPerSample != 16 || !IsPcmWav(format))
            {
                throw new InvalidOperationException("Transcription currently supports 16 kHz mono PCM 16-bit WAV input. Convert the audio before transcription.");
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

    private static long ToMilliseconds(TimeSpan duration)
    {
        return (long)Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero);
    }

    private static double? CalculateRealTimeFactor(TimeSpan audioDuration, TimeSpan elapsed)
    {
        if (audioDuration.TotalSeconds <= 0)
        {
            return null;
        }

        return Math.Round(elapsed.TotalSeconds / audioDuration.TotalSeconds, 6, MidpointRounding.AwayFromZero);
    }

    private static double? CalculateAudioSecondsPerSecond(TimeSpan audioDuration, TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds <= 0)
        {
            return null;
        }

        return Math.Round(audioDuration.TotalSeconds / elapsed.TotalSeconds, 6, MidpointRounding.AwayFromZero);
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