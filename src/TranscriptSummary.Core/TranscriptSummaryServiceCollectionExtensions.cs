using Microsoft.Extensions.DependencyInjection;
using TranscriptSummary.Core.Services;

namespace TranscriptSummary.Core;

public static class TranscriptSummaryServiceCollectionExtensions
{
    public static IServiceCollection AddTranscriptSummaryCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<HttpClient>();
        services.AddSingleton<ITranscriptArtifactReader, TranscriptArtifactReader>();
        services.AddSingleton<IOpenAiSummaryClient, OpenAiSummaryClient>();
        services.AddSingleton<ITranscriptSummaryService, TranscriptSummaryService>();

        return services;
    }
}