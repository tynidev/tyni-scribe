using YtScribe.Core.Model;

namespace YtScribe.Core.Services;

public interface IYtDlpService
{
    Task<YouTubeVideoMetadata> GetMetadataAsync(string url, CancellationToken cancellationToken = default);

    Task<string?> DownloadCaptionsAsync(string url, string outputDirectory, string captionLanguage, CancellationToken cancellationToken = default);

    Task<string> DownloadAudioAsync(string url, string outputDirectory, CancellationToken cancellationToken = default);
}