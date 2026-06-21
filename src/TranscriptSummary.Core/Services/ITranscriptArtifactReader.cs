using YtScribe.Core.Model;

namespace TranscriptSummary.Core.Services;

public interface ITranscriptArtifactReader
{
    Task<TranscriptDocument> ReadAsync(string path, CancellationToken cancellationToken = default);
}