using System.Text.Json;
using YtScribe.Core.Model;

namespace YtScribe.Core.Services;

public sealed class TranscriptArtifactWriter : ITranscriptArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<ExportedTranscriptArtifacts> WriteAsync(
        string outputDirectory,
        VideoMetadataDocument metadata,
        TranscriptDocument transcript,
        string? sourceVttPath,
        bool writePlainText,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var metadataPath = Path.Combine(outputDirectory, "metadata.json");
        var transcriptJsonPath = Path.Combine(outputDirectory, "transcript.json");
        await WriteJsonAsync(metadataPath, metadata, cancellationToken);
        await WriteJsonAsync(transcriptJsonPath, transcript, cancellationToken);

        string? transcriptVttPath = null;
        if (!string.IsNullOrWhiteSpace(sourceVttPath) && File.Exists(sourceVttPath))
        {
            transcriptVttPath = Path.Combine(outputDirectory, "transcript.vtt");
            File.Copy(sourceVttPath, transcriptVttPath, overwrite: true);
        }

        string? transcriptTextPath = null;
        if (writePlainText)
        {
            transcriptTextPath = Path.Combine(outputDirectory, "transcript.txt");
            await File.WriteAllTextAsync(transcriptTextPath, string.Join(Environment.NewLine, transcript.Segments.Select(segment => segment.Text)), cancellationToken);
        }

        return new ExportedTranscriptArtifacts(outputDirectory, metadataPath, transcriptJsonPath, transcriptVttPath, transcriptTextPath);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }
}