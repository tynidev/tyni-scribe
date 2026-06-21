using System.Text.Json;
using YtScribe.Core.Model;

namespace TranscriptSummary.Core.Services;

public sealed class TranscriptArtifactReader : ITranscriptArtifactReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TranscriptDocument> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        var transcript = await JsonSerializer.DeserializeAsync<TranscriptDocument>(stream, JsonOptions, cancellationToken);
        if (transcript is null)
        {
            throw new InvalidOperationException("The transcript file could not be read.");
        }

        if (transcript.Segments is null || !transcript.Segments.Any(segment => !string.IsNullOrWhiteSpace(segment.Text)))
        {
            throw new InvalidOperationException("The transcript file does not contain transcript text.");
        }

        return transcript;
    }
}