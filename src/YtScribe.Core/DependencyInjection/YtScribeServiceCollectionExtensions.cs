using Microsoft.Extensions.DependencyInjection;
using YtScribe.Core.Services;

namespace YtScribe.Core;

public static class YtScribeServiceCollectionExtensions
{
    public static IServiceCollection AddYtScribeCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<IYtDlpService, YtDlpService>();
        services.AddSingleton<IVttTranscriptParser, VttTranscriptParser>();
        services.AddSingleton<ITranscriptArtifactWriter, TranscriptArtifactWriter>();
        services.AddSingleton<IYouTubeExportService, YouTubeExportService>();

        return services;
    }
}