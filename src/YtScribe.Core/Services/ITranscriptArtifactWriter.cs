using YtScribe.Core.Model;

namespace YtScribe.Core.Services;

public interface ITranscriptArtifactWriter
{
    Task<ExportedTranscriptArtifacts> WriteAsync(
        string outputDirectory,
        VideoMetadataDocument metadata,
        TranscriptDocument transcript,
        string? sourceVttPath,
        bool writePlainText,
        CancellationToken cancellationToken = default);
}