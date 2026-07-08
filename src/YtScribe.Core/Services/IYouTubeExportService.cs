using Tts.Core.Configuration;
using YtScribe.Core.Model;

namespace YtScribe.Core.Services;

public interface IYouTubeExportService
{
    Task<YouTubeExportResult> ExportAsync(YouTubeExportRequest request, CancellationToken cancellationToken = default);
}

public sealed record YouTubeExportRequest(
    string Url,
    string OutputDirectory,
    AppSettings Settings,
    string CaptionLanguage,
    bool ForceAudio,
    bool Overwrite,
    bool KeepTemp,
    bool WritePlainText,
    string? ProviderId,
    IReadOnlyDictionary<string, string> SettingOverrides);