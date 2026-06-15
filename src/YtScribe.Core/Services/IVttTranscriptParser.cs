using YtScribe.Core.Model;

namespace YtScribe.Core.Services;

public interface IVttTranscriptParser
{
    Task<IReadOnlyList<TranscriptSegment>> ParseAsync(string vttPath, CancellationToken cancellationToken = default);
}