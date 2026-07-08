using Microsoft.Extensions.DependencyInjection;
using Tts.Core.Configuration;
using Tts.Core.Services;
using Tts.Core.Services.Audio;
using Tts.Core.Services.AudioProcessing;
using Tts.Core.Services.Media;
using Tts.Core.Services.Timing;
using Tts.Core.Services.Transcription;

namespace Tts.Core;

public static class TtsCoreServiceCollectionExtensions
{
    public static IServiceCollection AddTtsCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<AppPaths>();
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<IAudioCaptureService, WasapiAudioCaptureService>();
        services.AddSingleton<IMediaPreparationService, FfmpegMediaPreparationService>();
        services.AddSingleton<IAudioProcessingProvider, NoOpAudioProcessingProvider>();
        services.AddSingleton<IWhisperNativeEngine, WhisperNativeEngine>();
        services.AddSingleton<IBatchTranscriptionProvider, WhisperCppBatchTranscriptionProvider>();
        services.AddSingleton<IBatchTranscriptionProvider, WhisperNativeBatchTranscriptionProvider>();
        services.AddSingleton<ITranscriptionExecutionService, TranscriptionExecutionService>();
        services.AddSingleton<ISessionTimingLogWriter, CsvSessionTimingLogWriter>();

        return services;
    }
}