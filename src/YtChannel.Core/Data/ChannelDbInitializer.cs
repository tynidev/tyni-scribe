using Microsoft.Data.Sqlite;

namespace YtChannel.Core.Data;

/// <summary>
/// Creates the yt-channel SQLite schema on first run.
/// Safe to call on every startup — uses CREATE TABLE IF NOT EXISTS.
/// </summary>
public static class ChannelDbInitializer
{
    public static async Task InitializeAsync(ChannelDbContext context, CancellationToken cancellationToken = default)
    {
        var connection = await context.GetConnectionAsync(cancellationToken);
        await CreateTablesAsync(connection, cancellationToken);
        await CreateIndexesAsync(connection, cancellationToken);
    }

    private static async Task CreateTablesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Channels (
                ChannelId   TEXT PRIMARY KEY NOT NULL,
                ChannelUrl  TEXT NOT NULL,
                ChannelName TEXT,
                Description TEXT,
                ThumbnailUrl TEXT,
                SyncedAt    TEXT,
                CreatedAt   TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Videos (
                VideoId          TEXT PRIMARY KEY NOT NULL,
                ChannelId        TEXT NOT NULL REFERENCES Channels(ChannelId),
                Title            TEXT,
                DurationSeconds  REAL,
                IsShortsPlaylistVideo INTEGER,
                PublishedAt      TEXT,
                TranscriptStatus TEXT NOT NULL DEFAULT 'pending',
                SummaryStatus    TEXT NOT NULL DEFAULT 'pending',
                CreatedAt        TEXT NOT NULL,
                UpdatedAt        TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Transcriptions (
                Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                VideoId               TEXT NOT NULL REFERENCES Videos(VideoId),
                TranscriptOrigin      TEXT,
                TranscriptFilePath    TEXT,
                ProviderId            TEXT,
                ModelId               TEXT,
                TotalDurationMs       INTEGER,
                CaptionDownloadTimeMs INTEGER,
                SucceededAt           TEXT,
                ErrorCategory         TEXT,
                ErrorMessage          TEXT
            );

            CREATE TABLE IF NOT EXISTS Summaries (
                Id                        INTEGER PRIMARY KEY AUTOINCREMENT,
                VideoId                   TEXT NOT NULL REFERENCES Videos(VideoId),
                SummaryFilePath           TEXT,
                ModelId                   TEXT,
                EndpointHost              TEXT,
                ContextTokens             INTEGER,
                MaxOutputTokens           INTEGER,
                EstimatedTranscriptTokens INTEGER,
                ChunkCount                INTEGER,
                MergePassCount            INTEGER,
                LlmRequestCount           INTEGER,
                TotalDurationMs           INTEGER,
                TotalLlmDurationMs        INTEGER,
                SummarizedAt              TEXT,
                ErrorCategory             TEXT,
                ErrorMessage              TEXT
            );

            CREATE TABLE IF NOT EXISTS RateLimitMetrics (
                Id                       INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp                TEXT NOT NULL,
                CaptionDownloadDelayMs   INTEGER NOT NULL,
                CaptionDownloadDurationMs INTEGER,
                HttpStatus               INTEGER,
                BackoffAppliedMs         INTEGER NOT NULL DEFAULT 0
            );
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        await AddMissingColumnAsync(
            connection,
            tableName: "Videos",
            columnName: "IsShortsPlaylistVideo",
            columnDefinition: "INTEGER",
            cancellationToken);

        await AddMissingColumnAsync(
            connection,
            tableName: "Videos",
            columnName: "SummaryStatus",
            columnDefinition: "TEXT NOT NULL DEFAULT 'pending'",
            cancellationToken);
    }

    private static async Task AddMissingColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        using (var inspect = connection.CreateCommand())
        {
            inspect.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = await inspect.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(reader.GetOrdinal("name")), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CreateIndexesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE INDEX IF NOT EXISTS IX_Videos_ChannelId_Status
                ON Videos(ChannelId, TranscriptStatus);

            CREATE INDEX IF NOT EXISTS IX_Videos_ChannelId_SummaryStatus
                ON Videos(ChannelId, SummaryStatus);

            CREATE INDEX IF NOT EXISTS IX_Videos_SummaryStatus
                ON Videos(SummaryStatus);

            CREATE INDEX IF NOT EXISTS IX_Videos_CreatedAt
                ON Videos(CreatedAt);

            CREATE INDEX IF NOT EXISTS IX_Transcriptions_VideoId
                ON Transcriptions(VideoId);

            CREATE INDEX IF NOT EXISTS IX_Summaries_VideoId
                ON Summaries(VideoId);

            CREATE INDEX IF NOT EXISTS IX_RateLimitMetrics_Timestamp
                ON RateLimitMetrics(Timestamp);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
