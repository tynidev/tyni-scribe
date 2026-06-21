using Microsoft.Extensions.DependencyInjection;
using YtChannel.Core.Configuration;
using YtChannel.Core.Data;
using YtChannel.Core.Services;

namespace YtChannel.Core;

public static class YtChannelServiceCollectionExtensions
{
    /// <summary>
    /// Registers all yt-channel core services.
    /// Call <see cref="ChannelDbInitializer.InitializeAsync"/> after building the provider to ensure the schema exists.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="databasePath">Full path to the SQLite database file. Created if absent.</param>
    /// <param name="youTubeApiSettings">YouTube Data API settings (API key etc.).</param>
    /// <param name="rateLimitSettings">Adaptive rate-limit settings.</param>
    public static IServiceCollection AddYtChannelCoreServices(
        this IServiceCollection services,
        string databasePath,
        YouTubeApiSettings youTubeApiSettings,
        RateLimitSettings? rateLimitSettings = null)
    {
        var effectiveRateLimitSettings = rateLimitSettings ?? new RateLimitSettings();

        // Configuration singletons
        services.AddSingleton(youTubeApiSettings);
        services.AddSingleton(effectiveRateLimitSettings);

        // Data layer
        services.AddSingleton(new ChannelDbContext(databasePath));
        services.AddSingleton<ChannelRepository>();

        // Services
        services.AddSingleton<IYouTubeChannelService, YouTubeChannelService>();
        services.AddSingleton<ChannelSyncService>();
        services.AddSingleton<ChannelManifestService>();
        services.AddSingleton<ChannelSummaryPromptStore>();
        services.AddSingleton<RateLimitTracker>();
        services.AddSingleton<ChannelOrchestrator>();
        services.AddSingleton<ChannelSummaryOrchestrator>();

        return services;
    }
}
