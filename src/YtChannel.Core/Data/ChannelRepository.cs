using Microsoft.Data.Sqlite;
using YtChannel.Core.Data;

namespace YtChannel.Core.Data;

/// <summary>
/// Data access layer for yt-channel's SQLite database.
/// All methods are async and accept CancellationToken.
/// </summary>
public sealed class ChannelRepository
{
    private readonly ChannelDbContext _context;

    public ChannelRepository(ChannelDbContext context)
    {
        _context = context;
    }

    // ── Channels ─────────────────────────────────────────────────────────────

    public async Task UpsertChannelAsync(ChannelRecord channel, CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Channels (ChannelId, ChannelUrl, ChannelName, Description, ThumbnailUrl, SyncedAt, CreatedAt)
            VALUES ($channelId, $channelUrl, $channelName, $description, $thumbnailUrl, $syncedAt, $createdAt)
            ON CONFLICT(ChannelId) DO UPDATE SET
                ChannelUrl  = excluded.ChannelUrl,
                ChannelName = excluded.ChannelName,
                Description = excluded.Description,
                ThumbnailUrl = excluded.ThumbnailUrl,
                SyncedAt    = excluded.SyncedAt;
            """;
        cmd.Parameters.AddWithValue("$channelId", channel.ChannelId);
        cmd.Parameters.AddWithValue("$channelUrl", channel.ChannelUrl);
        cmd.Parameters.AddWithValue("$channelName", (object?)channel.ChannelName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$description", (object?)channel.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$thumbnailUrl", (object?)channel.ThumbnailUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$syncedAt", (object?)channel.SyncedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdAt", channel.CreatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ChannelRecord?> GetChannelAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Channels WHERE ChannelId = $id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", channelId);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadChannel(reader) : null;
    }

    // ── Videos ───────────────────────────────────────────────────────────────

    public async Task UpsertVideosAsync(IReadOnlyList<VideoRecord> videos, CancellationToken cancellationToken = default)
    {
        if (videos.Count == 0) return;
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Videos (VideoId, ChannelId, Title, DurationSeconds, IsShortsPlaylistVideo, PublishedAt, TranscriptStatus, CreatedAt, UpdatedAt)
            VALUES ($videoId, $channelId, $title, $duration, $isShortsPlaylistVideo, $publishedAt, 'pending', $createdAt, $updatedAt)
            ON CONFLICT(VideoId) DO UPDATE SET
                ChannelId = excluded.ChannelId,
                Title = excluded.Title,
                DurationSeconds = excluded.DurationSeconds,
                IsShortsPlaylistVideo = excluded.IsShortsPlaylistVideo,
                PublishedAt = excluded.PublishedAt,
                UpdatedAt = excluded.UpdatedAt;
            """;
        var pVideoId = cmd.Parameters.Add("$videoId", SqliteType.Text);
        var pChannelId = cmd.Parameters.Add("$channelId", SqliteType.Text);
        var pTitle = cmd.Parameters.Add("$title", SqliteType.Text);
        var pDuration = cmd.Parameters.Add("$duration", SqliteType.Real);
        var pIsShortsPlaylistVideo = cmd.Parameters.Add("$isShortsPlaylistVideo", SqliteType.Integer);
        var pPublishedAt = cmd.Parameters.Add("$publishedAt", SqliteType.Text);
        var pCreatedAt = cmd.Parameters.Add("$createdAt", SqliteType.Text);
        var pUpdatedAt = cmd.Parameters.Add("$updatedAt", SqliteType.Text);

        foreach (var v in videos)
        {
            pVideoId.Value = v.VideoId;
            pChannelId.Value = v.ChannelId;
            pTitle.Value = (object?)v.Title ?? DBNull.Value;
            pDuration.Value = (object?)v.DurationSeconds ?? DBNull.Value;
            pIsShortsPlaylistVideo.Value = v.IsShortsPlaylistVideo.HasValue
                ? (v.IsShortsPlaylistVideo.Value ? 1 : 0)
                : DBNull.Value;
            pPublishedAt.Value = (object?)v.PublishedAt ?? DBNull.Value;
            pCreatedAt.Value = v.CreatedAt.ToString("O");
            pUpdatedAt.Value = v.UpdatedAt.ToString("O");
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        tx.Commit();
    }

    public async Task<int> CountVideosByStatusAsync(string channelId, string status, CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Videos WHERE ChannelId = $channelId AND TranscriptStatus = $status";
        cmd.Parameters.AddWithValue("$channelId", channelId);
        cmd.Parameters.AddWithValue("$status", status);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<int> CountVideosBySummaryStatusAsync(string channelId, string status, CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Videos WHERE ChannelId = $channelId AND SummaryStatus = $status";
        cmd.Parameters.AddWithValue("$channelId", channelId);
        cmd.Parameters.AddWithValue("$status", status);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<int> CountAllVideosAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Videos WHERE ChannelId = $channelId";
        cmd.Parameters.AddWithValue("$channelId", channelId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<VideoRecord>> GetPendingVideosAsync(
        string channelId,
        int? limit,
        bool includeShorts,
        CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        var shortsPredicate = includeShorts
            ? string.Empty
            : " AND NOT (DurationSeconds IS NOT NULL AND DurationSeconds <= 180 AND IsShortsPlaylistVideo = 1)";
        cmd.CommandText = limit.HasValue
            ? $"SELECT * FROM Videos WHERE ChannelId = $channelId AND TranscriptStatus = 'pending'{shortsPredicate} ORDER BY PublishedAt DESC LIMIT $limit"
            : $"SELECT * FROM Videos WHERE ChannelId = $channelId AND TranscriptStatus = 'pending'{shortsPredicate} ORDER BY PublishedAt DESC";
        cmd.Parameters.AddWithValue("$channelId", channelId);
        if (limit.HasValue) cmd.Parameters.AddWithValue("$limit", limit.Value);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<VideoRecord>();
        while (await reader.ReadAsync(cancellationToken))
            results.Add(ReadVideo(reader));
        return results;
    }

    public async Task<IReadOnlyList<ChannelVideoIndexRecord>> GetChannelVideoIndexAsync(
        string channelId,
        CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            WITH LatestSuccessfulTranscription AS (
                SELECT t.*
                FROM Transcriptions t
                INNER JOIN (
                    SELECT VideoId, MAX(Id) AS Id
                    FROM Transcriptions
                    WHERE ErrorCategory IS NULL
                      AND TranscriptFilePath IS NOT NULL
                    GROUP BY VideoId
                ) latest ON latest.Id = t.Id
            )
            SELECT
                v.VideoId,
                v.Title,
                v.DurationSeconds,
                v.IsShortsPlaylistVideo,
                v.PublishedAt,
                v.TranscriptStatus,
                v.SummaryStatus,
                t.TranscriptOrigin,
                t.TranscriptFilePath,
                t.SucceededAt,
                s.SummaryFilePath,
                s.SummarizedAt
            FROM Videos v
            LEFT JOIN LatestSuccessfulTranscription t ON t.VideoId = v.VideoId
            LEFT JOIN (
                SELECT sm.*
                FROM Summaries sm
                INNER JOIN (
                    SELECT VideoId, MAX(Id) AS Id
                    FROM Summaries
                    WHERE ErrorCategory IS NULL
                      AND SummaryFilePath IS NOT NULL
                    GROUP BY VideoId
                ) latestSummary ON latestSummary.Id = sm.Id
            ) s ON s.VideoId = v.VideoId
            WHERE v.ChannelId = $channelId
            ORDER BY v.PublishedAt DESC, v.VideoId ASC;
            """;
        cmd.Parameters.AddWithValue("$channelId", channelId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<ChannelVideoIndexRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var duration = reader.IsDBNull(reader.GetOrdinal("DurationSeconds"))
                ? null
                : (double?)reader.GetDouble(reader.GetOrdinal("DurationSeconds"));
            var isShortsPlaylistVideo = !reader.IsDBNull(reader.GetOrdinal("IsShortsPlaylistVideo"))
                && reader.GetInt32(reader.GetOrdinal("IsShortsPlaylistVideo")) != 0;

            results.Add(new ChannelVideoIndexRecord
            {
                VideoId = reader.GetString(reader.GetOrdinal("VideoId")),
                Title = reader.IsDBNull(reader.GetOrdinal("Title")) ? null : reader.GetString(reader.GetOrdinal("Title")),
                DurationSeconds = duration,
                IsShort = duration.HasValue && duration.Value <= 180 && isShortsPlaylistVideo,
                PublishedAt = reader.IsDBNull(reader.GetOrdinal("PublishedAt")) ? null : reader.GetString(reader.GetOrdinal("PublishedAt")),
                TranscriptStatus = reader.GetString(reader.GetOrdinal("TranscriptStatus")),
                SummaryStatus = reader.GetString(reader.GetOrdinal("SummaryStatus")),
                TranscriptOrigin = reader.IsDBNull(reader.GetOrdinal("TranscriptOrigin")) ? null : reader.GetString(reader.GetOrdinal("TranscriptOrigin")),
                TranscriptFilePath = reader.IsDBNull(reader.GetOrdinal("TranscriptFilePath")) ? null : reader.GetString(reader.GetOrdinal("TranscriptFilePath")),
                TranscribedAt = reader.IsDBNull(reader.GetOrdinal("SucceededAt")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("SucceededAt"))),
                SummaryFilePath = reader.IsDBNull(reader.GetOrdinal("SummaryFilePath")) ? null : reader.GetString(reader.GetOrdinal("SummaryFilePath")),
                SummarizedAt = reader.IsDBNull(reader.GetOrdinal("SummarizedAt")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("SummarizedAt"))),
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<SummaryCandidateRecord>> GetSummarizationCandidatesAsync(
        string? channelId,
        int? limit,
        bool includeShorts,
        bool includeAlreadySummarized,
        CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        var channelPredicate = string.IsNullOrWhiteSpace(channelId) ? string.Empty : " AND v.ChannelId = $channelId";
        var summaryPredicate = includeAlreadySummarized ? string.Empty : " AND v.SummaryStatus <> 'summarized'";
        var shortsPredicate = includeShorts
            ? string.Empty
            : " AND NOT (v.DurationSeconds IS NOT NULL AND v.DurationSeconds <= 180 AND v.IsShortsPlaylistVideo = 1)";
        var limitClause = limit.HasValue ? " LIMIT $limit" : string.Empty;

        cmd.CommandText = $"""
            WITH LatestSuccessfulTranscription AS (
                SELECT t.*
                FROM Transcriptions t
                INNER JOIN (
                    SELECT VideoId, MAX(Id) AS Id
                    FROM Transcriptions
                    WHERE ErrorCategory IS NULL
                      AND TranscriptFilePath IS NOT NULL
                    GROUP BY VideoId
                ) latest ON latest.Id = t.Id
            )
            SELECT
                v.VideoId,
                v.ChannelId,
                v.Title,
                v.DurationSeconds,
                v.IsShortsPlaylistVideo,
                v.SummaryStatus,
                t.TranscriptFilePath
            FROM Videos v
            INNER JOIN LatestSuccessfulTranscription t ON t.VideoId = v.VideoId
            WHERE v.TranscriptStatus = 'completed'
              {channelPredicate}
              {summaryPredicate}
              {shortsPredicate}
            ORDER BY v.PublishedAt DESC, v.VideoId ASC
            {limitClause};
            """;
        if (!string.IsNullOrWhiteSpace(channelId))
        {
            cmd.Parameters.AddWithValue("$channelId", channelId);
        }

        if (limit.HasValue)
        {
            cmd.Parameters.AddWithValue("$limit", limit.Value);
        }

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<SummaryCandidateRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadSummaryCandidate(reader));
        }

        return results;
    }

    public async Task UpdateVideoStatusAsync(string videoId, string status, CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Videos SET TranscriptStatus = $status, UpdatedAt = $updatedAt WHERE VideoId = $videoId";
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$videoId", videoId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateVideoSummaryStatusAsync(string videoId, string status, CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Videos SET SummaryStatus = $status, UpdatedAt = $updatedAt WHERE VideoId = $videoId";
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$videoId", videoId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Transcriptions ────────────────────────────────────────────────────────

    public async Task InsertTranscriptionAsync(TranscriptionRecord record, CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Transcriptions
                (VideoId, TranscriptOrigin, TranscriptFilePath, ProviderId, ModelId,
                 TotalDurationMs, CaptionDownloadTimeMs, SucceededAt, ErrorCategory, ErrorMessage)
            VALUES
                ($videoId, $origin, $filePath, $provider, $model,
                 $totalMs, $captionMs, $succeededAt, $errorCat, $errorMsg);
            """;
        cmd.Parameters.AddWithValue("$videoId", record.VideoId);
        cmd.Parameters.AddWithValue("$origin", (object?)record.TranscriptOrigin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$filePath", (object?)record.TranscriptFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$provider", (object?)record.ProviderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$model", (object?)record.ModelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$totalMs", (object?)record.TotalDurationMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$captionMs", (object?)record.CaptionDownloadTimeMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$succeededAt", (object?)record.SucceededAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$errorCat", (object?)record.ErrorCategory ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$errorMsg", (object?)record.ErrorMessage ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertSummaryAsync(SummaryRecord record, CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Summaries
                (VideoId, SummaryFilePath, ModelId, EndpointHost, ContextTokens, MaxOutputTokens,
                 EstimatedTranscriptTokens, ChunkCount, MergePassCount, LlmRequestCount,
                 TotalDurationMs, TotalLlmDurationMs, SummarizedAt, ErrorCategory, ErrorMessage)
            VALUES
                ($videoId, $summaryFilePath, $modelId, $endpointHost, $contextTokens, $maxOutputTokens,
                 $estimatedTranscriptTokens, $chunkCount, $mergePassCount, $llmRequestCount,
                 $totalDurationMs, $totalLlmDurationMs, $summarizedAt, $errorCategory, $errorMessage);
            """;
        cmd.Parameters.AddWithValue("$videoId", record.VideoId);
        cmd.Parameters.AddWithValue("$summaryFilePath", (object?)record.SummaryFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$modelId", (object?)record.ModelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$endpointHost", (object?)record.EndpointHost ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$contextTokens", (object?)record.ContextTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$maxOutputTokens", (object?)record.MaxOutputTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$estimatedTranscriptTokens", (object?)record.EstimatedTranscriptTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$chunkCount", (object?)record.ChunkCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mergePassCount", (object?)record.MergePassCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$llmRequestCount", (object?)record.LlmRequestCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$totalDurationMs", (object?)record.TotalDurationMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$totalLlmDurationMs", (object?)record.TotalLlmDurationMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$summarizedAt", (object?)record.SummarizedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$errorCategory", (object?)record.ErrorCategory ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$errorMessage", (object?)record.ErrorMessage ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Rate limit metrics ────────────────────────────────────────────────────

    public async Task InsertRateLimitMetricAsync(RateLimitMetricRecord record, CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO RateLimitMetrics
                (Timestamp, CaptionDownloadDelayMs, CaptionDownloadDurationMs, HttpStatus, BackoffAppliedMs)
            VALUES
                ($ts, $delay, $duration, $status, $backoff);
            """;
        cmd.Parameters.AddWithValue("$ts", record.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("$delay", record.CaptionDownloadDelayMs);
        cmd.Parameters.AddWithValue("$duration", (object?)record.CaptionDownloadDurationMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", (object?)record.HttpStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$backoff", record.BackoffAppliedMs);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Returns the most recent N rate-limit metrics rows, newest first.</summary>
    public async Task<IReadOnlyList<RateLimitMetricRecord>> GetRecentRateLimitMetricsAsync(
        int count, CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM RateLimitMetrics ORDER BY Id DESC LIMIT $count";
        cmd.Parameters.AddWithValue("$count", count);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<RateLimitMetricRecord>();
        while (await reader.ReadAsync(cancellationToken))
            results.Add(ReadRateLimitMetric(reader));
        return results;
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static ChannelRecord ReadChannel(SqliteDataReader r) => new()
    {
        ChannelId    = r.GetString(r.GetOrdinal("ChannelId")),
        ChannelUrl   = r.GetString(r.GetOrdinal("ChannelUrl")),
        ChannelName  = r.IsDBNull(r.GetOrdinal("ChannelName"))  ? null : r.GetString(r.GetOrdinal("ChannelName")),
        Description  = r.IsDBNull(r.GetOrdinal("Description"))  ? null : r.GetString(r.GetOrdinal("Description")),
        ThumbnailUrl = r.IsDBNull(r.GetOrdinal("ThumbnailUrl")) ? null : r.GetString(r.GetOrdinal("ThumbnailUrl")),
        SyncedAt     = r.IsDBNull(r.GetOrdinal("SyncedAt"))     ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("SyncedAt"))),
        CreatedAt    = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
    };

    private static VideoRecord ReadVideo(SqliteDataReader r) => new()
    {
        VideoId          = r.GetString(r.GetOrdinal("VideoId")),
        ChannelId        = r.GetString(r.GetOrdinal("ChannelId")),
        Title            = r.IsDBNull(r.GetOrdinal("Title"))           ? null : r.GetString(r.GetOrdinal("Title")),
        DurationSeconds  = r.IsDBNull(r.GetOrdinal("DurationSeconds")) ? null : r.GetDouble(r.GetOrdinal("DurationSeconds")),
        IsShortsPlaylistVideo = r.IsDBNull(r.GetOrdinal("IsShortsPlaylistVideo")) ? null : r.GetInt32(r.GetOrdinal("IsShortsPlaylistVideo")) != 0,
        PublishedAt      = r.IsDBNull(r.GetOrdinal("PublishedAt"))     ? null : r.GetString(r.GetOrdinal("PublishedAt")),
        TranscriptStatus = r.GetString(r.GetOrdinal("TranscriptStatus")),
        SummaryStatus    = r.GetString(r.GetOrdinal("SummaryStatus")),
        CreatedAt        = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
        UpdatedAt        = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("UpdatedAt"))),
    };

    private static SummaryCandidateRecord ReadSummaryCandidate(SqliteDataReader r) => new()
    {
        VideoId = r.GetString(r.GetOrdinal("VideoId")),
        ChannelId = r.GetString(r.GetOrdinal("ChannelId")),
        Title = r.IsDBNull(r.GetOrdinal("Title")) ? null : r.GetString(r.GetOrdinal("Title")),
        DurationSeconds = r.IsDBNull(r.GetOrdinal("DurationSeconds")) ? null : r.GetDouble(r.GetOrdinal("DurationSeconds")),
        IsShortsPlaylistVideo = r.IsDBNull(r.GetOrdinal("IsShortsPlaylistVideo")) ? null : r.GetInt32(r.GetOrdinal("IsShortsPlaylistVideo")) != 0,
        SummaryStatus = r.GetString(r.GetOrdinal("SummaryStatus")),
        TranscriptFilePath = r.GetString(r.GetOrdinal("TranscriptFilePath")),
    };

    private static RateLimitMetricRecord ReadRateLimitMetric(SqliteDataReader r) => new()
    {
        Id                        = r.GetInt64(r.GetOrdinal("Id")),
        Timestamp                 = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("Timestamp"))),
        CaptionDownloadDelayMs    = r.GetInt64(r.GetOrdinal("CaptionDownloadDelayMs")),
        CaptionDownloadDurationMs = r.IsDBNull(r.GetOrdinal("CaptionDownloadDurationMs")) ? null : r.GetInt64(r.GetOrdinal("CaptionDownloadDurationMs")),
        HttpStatus                = r.IsDBNull(r.GetOrdinal("HttpStatus")) ? null : r.GetInt32(r.GetOrdinal("HttpStatus")),
        BackoffAppliedMs          = r.GetInt64(r.GetOrdinal("BackoffAppliedMs")),
    };
}
