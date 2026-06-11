using System.Globalization;
using System.IO;

namespace Tts.App.Services.Timing;

public sealed class CsvSessionTimingLogWriter : ISessionTimingLogWriter
{
    private const string Header = "schemaVersion,sessionId,startedUtc,completedUtc,status,errorCategory,microphoneDeviceId,transcriptionProviderId,audioProcessorProviderId,cleanupProviderId,outputProviderIds,recordingDurationMs,totalSessionMs,captureFinalizationMs,audioProcessingMs,transcriptionMs,textCleanupMs,clipboardOutputMs,tempFileCleanupMs";

    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _sync = new(1, 1);

    public CsvSessionTimingLogWriter(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task AppendAsync(SessionTimingLogEntry entry, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(_paths.LogDirectory);

            var shouldWriteHeader = !File.Exists(_paths.TimingLogFilePath)
                || new FileInfo(_paths.TimingLogFilePath).Length == 0;

            await using var stream = new FileStream(
                _paths.TimingLogFilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            await using var writer = new StreamWriter(stream);

            if (shouldWriteHeader)
            {
                await writer.WriteLineAsync(Header.AsMemory(), cancellationToken);
            }

            await writer.WriteLineAsync(FormatRow(entry).AsMemory(), cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    private static string FormatRow(SessionTimingLogEntry entry)
    {
        var fields = new[]
        {
            entry.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            entry.SessionId.ToString(),
            entry.StartedUtc.ToString("O", CultureInfo.InvariantCulture),
            entry.CompletedUtc.ToString("O", CultureInfo.InvariantCulture),
            entry.Status,
            entry.ErrorCategory ?? string.Empty,
            entry.MicrophoneDeviceId ?? string.Empty,
            entry.TranscriptionProviderId,
            entry.AudioProcessorProviderId ?? string.Empty,
            entry.CleanupProviderId ?? string.Empty,
            string.Join(';', entry.OutputProviderIds),
            FormatDuration(entry.RecordingDuration),
            FormatDuration(entry.TotalSessionDuration),
            FormatDuration(entry.CaptureFinalizationDuration),
            FormatDuration(entry.AudioProcessingDuration),
            FormatDuration(entry.TranscriptionDuration),
            FormatDuration(entry.TextCleanupDuration),
            FormatDuration(entry.ClipboardOutputDuration),
            FormatDuration(entry.TempFileCleanupDuration)
        };

        return string.Join(',', fields.Select(Escape));
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        return duration is null
            ? string.Empty
            : Math.Round(duration.Value.TotalMilliseconds, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
    }

    private static string Escape(string value)
    {
        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}