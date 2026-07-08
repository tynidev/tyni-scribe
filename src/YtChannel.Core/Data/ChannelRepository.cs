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
            INSERT INTO Channels (ChannelId, ChannelUrl, ChannelName, Description, ThumbnailUrl, IsEnabled, ScanIntervalMinutes, MaxVideoAgeDays, SyncedAt, LastScanStartedAt, LastScanCompletedAt, NextScanAfter, ScanStatus, CreatedAt)
            VALUES ($channelId, $channelUrl, $channelName, $description, $thumbnailUrl, $isEnabled, $scanIntervalMinutes, $maxVideoAgeDays, $syncedAt, $lastScanStartedAt, $lastScanCompletedAt, $nextScanAfter, $scanStatus, $createdAt)
            ON CONFLICT(ChannelId) DO UPDATE SET
                ChannelUrl  = excluded.ChannelUrl,
                ChannelName = excluded.ChannelName,
                Description = excluded.Description,
                ThumbnailUrl = excluded.ThumbnailUrl,
                ScanIntervalMinutes = excluded.ScanIntervalMinutes,
                MaxVideoAgeDays = COALESCE(excluded.MaxVideoAgeDays, Channels.MaxVideoAgeDays),
                SyncedAt    = excluded.SyncedAt,
                LastScanStartedAt = excluded.LastScanStartedAt,
                LastScanCompletedAt = excluded.LastScanCompletedAt,
                NextScanAfter = excluded.NextScanAfter,
                ScanStatus = excluded.ScanStatus;
            """;
        cmd.Parameters.AddWithValue("$channelId", channel.ChannelId);
        cmd.Parameters.AddWithValue("$channelUrl", channel.ChannelUrl);
        cmd.Parameters.AddWithValue("$channelName", (object?)channel.ChannelName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$description", (object?)channel.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$thumbnailUrl", (object?)channel.ThumbnailUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$isEnabled", channel.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$scanIntervalMinutes", channel.ScanIntervalMinutes);
        cmd.Parameters.AddWithValue("$maxVideoAgeDays", (object?)channel.MaxVideoAgeDays ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$syncedAt", (object?)channel.SyncedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lastScanStartedAt", (object?)channel.LastScanStartedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lastScanCompletedAt", (object?)channel.LastScanCompletedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$nextScanAfter", (object?)channel.NextScanAfter?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$scanStatus", channel.ScanStatus);
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

    public async Task<IReadOnlyList<ChannelRecord>> GetEnabledChannelsAsync(CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Channels WHERE IsEnabled = 1 ORDER BY COALESCE(NextScanAfter, '') ASC, ChannelName COLLATE NOCASE, ChannelId";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<ChannelRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadChannel(reader));
        }

        return results;
    }

    public async Task UpdateChannelScanStartedAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Channels SET LastScanStartedAt = $now, ScanStatus = 'in-progress' WHERE ChannelId = $channelId";
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$channelId", channelId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateChannelScanCompletedAsync(string channelId, int scanIntervalMinutes, string status, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Channels SET LastScanCompletedAt = $now, NextScanAfter = $nextScanAfter, ScanStatus = $status WHERE ChannelId = $channelId";
        cmd.Parameters.AddWithValue("$now", now.ToString("O"));
        cmd.Parameters.AddWithValue("$nextScanAfter", now.AddMinutes(scanIntervalMinutes).ToString("O"));
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$channelId", channelId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateChannelMaxVideoAgeDaysAsync(string channelId, int maxVideoAgeDays, CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Channels SET MaxVideoAgeDays = $maxVideoAgeDays WHERE ChannelId = $channelId";
        cmd.Parameters.AddWithValue("$maxVideoAgeDays", maxVideoAgeDays);
        cmd.Parameters.AddWithValue("$channelId", channelId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearChannelMaxVideoAgeDaysAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Channels SET MaxVideoAgeDays = NULL WHERE ChannelId = $channelId";
        cmd.Parameters.AddWithValue("$channelId", channelId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
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
        var retentionPredicate = " AND (PublishedAt IS NULL OR PublishedAt >= $publishedAfter)";
        var publishedAfter = await GetChannelPublishedAfterAsync(channelId, cancellationToken);
        var shortsPredicate = includeShorts
            ? string.Empty
            : " AND NOT (DurationSeconds IS NOT NULL AND DurationSeconds <= 180 AND IsShortsPlaylistVideo = 1)";
        var durationPredicate = " AND (DurationSeconds IS NULL OR DurationSeconds > 0)";
        cmd.CommandText = limit.HasValue
            ? $"SELECT * FROM Videos WHERE ChannelId = $channelId AND TranscriptStatus = 'pending'{durationPredicate}{shortsPredicate}{(publishedAfter.HasValue ? retentionPredicate : string.Empty)} ORDER BY PublishedAt DESC LIMIT $limit"
            : $"SELECT * FROM Videos WHERE ChannelId = $channelId AND TranscriptStatus = 'pending'{durationPredicate}{shortsPredicate}{(publishedAfter.HasValue ? retentionPredicate : string.Empty)} ORDER BY PublishedAt DESC";
        cmd.Parameters.AddWithValue("$channelId", channelId);
        if (publishedAfter.HasValue) cmd.Parameters.AddWithValue("$publishedAfter", publishedAfter.Value.ToString("O"));
        if (limit.HasValue) cmd.Parameters.AddWithValue("$limit", limit.Value);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<VideoRecord>();
        while (await reader.ReadAsync(cancellationToken))
            results.Add(ReadVideo(reader));
        return results;
    }

    public async Task<VideoRecord?> GetNextPendingTranscriptVideoAsync(
        string? channelId,
        bool includeShorts,
        CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        var channelPredicate = string.IsNullOrWhiteSpace(channelId) ? string.Empty : " AND Videos.ChannelId = $channelId";
        var retentionPredicate = " AND (c.MaxVideoAgeDays IS NULL OR Videos.PublishedAt IS NULL OR julianday(Videos.PublishedAt) >= julianday('now') - c.MaxVideoAgeDays)";
        var shortsPredicate = includeShorts
            ? string.Empty
            : " AND NOT (DurationSeconds IS NOT NULL AND DurationSeconds <= 180 AND IsShortsPlaylistVideo = 1)";
        var durationPredicate = " AND (Videos.DurationSeconds IS NULL OR Videos.DurationSeconds > 0)";
        cmd.CommandText = $"SELECT Videos.* FROM Videos INNER JOIN Channels c ON c.ChannelId = Videos.ChannelId WHERE Videos.TranscriptStatus = 'pending'{channelPredicate}{durationPredicate}{shortsPredicate}{retentionPredicate} ORDER BY Videos.PublishedAt DESC, Videos.VideoId ASC LIMIT 1";
        if (!string.IsNullOrWhiteSpace(channelId))
        {
            cmd.Parameters.AddWithValue("$channelId", channelId);
        }

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadVideo(reader) : null;
    }

    public async Task<IReadOnlyList<VideoRetentionPruneCandidate>> GetVideosOlderThanAsync(
        string channelId,
        DateTimeOffset publishedBefore,
        CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT VideoId FROM Videos WHERE ChannelId = $channelId AND PublishedAt IS NOT NULL AND PublishedAt < $publishedBefore";
        cmd.Parameters.AddWithValue("$channelId", channelId);
        cmd.Parameters.AddWithValue("$publishedBefore", publishedBefore.ToString("O"));
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<VideoRetentionPruneCandidate>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new VideoRetentionPruneCandidate(reader.GetString(reader.GetOrdinal("VideoId"))));
        }

        return results;
    }

    public async Task<int> PruneVideosOlderThanAsync(
        string channelId,
        DateTimeOffset publishedBefore,
        CancellationToken cancellationToken = default)
    {
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var tx = conn.BeginTransaction();

        using var deleteSummaries = conn.CreateCommand();
        deleteSummaries.Transaction = tx;
        deleteSummaries.CommandText = """
            DELETE FROM Summaries
            WHERE VideoId IN (
                SELECT VideoId FROM Videos
                WHERE ChannelId = $channelId
                  AND PublishedAt IS NOT NULL
                  AND PublishedAt < $publishedBefore
            );
            """;
        deleteSummaries.Parameters.AddWithValue("$channelId", channelId);
        deleteSummaries.Parameters.AddWithValue("$publishedBefore", publishedBefore.ToString("O"));
        await deleteSummaries.ExecuteNonQueryAsync(cancellationToken);

        using var deleteTranscriptions = conn.CreateCommand();
        deleteTranscriptions.Transaction = tx;
        deleteTranscriptions.CommandText = """
            DELETE FROM Transcriptions
            WHERE VideoId IN (
                SELECT VideoId FROM Videos
                WHERE ChannelId = $channelId
                  AND PublishedAt IS NOT NULL
                  AND PublishedAt < $publishedBefore
            );
            """;
        deleteTranscriptions.Parameters.AddWithValue("$channelId", channelId);
        deleteTranscriptions.Parameters.AddWithValue("$publishedBefore", publishedBefore.ToString("O"));
        await deleteTranscriptions.ExecuteNonQueryAsync(cancellationToken);

        using var deleteVideos = conn.CreateCommand();
        deleteVideos.Transaction = tx;
        deleteVideos.CommandText = """
            DELETE FROM Videos
            WHERE ChannelId = $channelId
              AND PublishedAt IS NOT NULL
              AND PublishedAt < $publishedBefore;
            """;
        deleteVideos.Parameters.AddWithValue("$channelId", channelId);
        deleteVideos.Parameters.AddWithValue("$publishedBefore", publishedBefore.ToString("O"));
        var pruned = await deleteVideos.ExecuteNonQueryAsync(cancellationToken);

        tx.Commit();
        return pruned;
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

    private async Task<DateTimeOffset?> GetChannelPublishedAfterAsync(string channelId, CancellationToken cancellationToken)
    {
        var channel = await GetChannelAsync(channelId, cancellationToken);
        return channel?.MaxVideoAgeDays is > 0
            ? DateTimeOffset.UtcNow.AddDays(-channel.MaxVideoAgeDays.Value)
            : null;
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
        var retentionPredicate = " AND (c.MaxVideoAgeDays IS NULL OR v.PublishedAt IS NULL OR julianday(v.PublishedAt) >= julianday('now') - c.MaxVideoAgeDays)";
        var summaryPredicate = includeAlreadySummarized ? string.Empty : " AND v.SummaryStatus = 'pending'";
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
                        INNER JOIN Channels c ON c.ChannelId = v.ChannelId
            INNER JOIN LatestSuccessfulTranscription t ON t.VideoId = v.VideoId
            WHERE v.TranscriptStatus = 'completed'
              {channelPredicate}
              {summaryPredicate}
              {shortsPredicate}
                            {retentionPredicate}
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

    public async Task ResetStaleInProgressAsync(TimeSpan staleAfter, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - staleAfter;
        var conn = await _context.GetConnectionAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Videos
            SET TranscriptStatus = 'pending', UpdatedAt = $now
            WHERE TranscriptStatus = 'in-progress' AND UpdatedAt < $cutoff;

            UPDATE Videos
            SET SummaryStatus = 'pending', UpdatedAt = $now
            WHERE SummaryStatus = 'in-progress' AND UpdatedAt < $cutoff;
            """;
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
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
                  TotalDurationMs, TotalLlmDurationMs, TotalPromptTokens, TotalCompletionTokens,
                  TotalTokens, EstimatedOutputTokens, PromptTokensPerSecond, CompletionTokensPerSecond,
                  TotalTokensPerSecond, SummarizedAt, ErrorCategory, ErrorMessage)
            VALUES
                ($videoId, $summaryFilePath, $modelId, $endpointHost, $contextTokens, $maxOutputTokens,
                 $estimatedTranscriptTokens, $chunkCount, $mergePassCount, $llmRequestCount,
                  $totalDurationMs, $totalLlmDurationMs, $totalPromptTokens, $totalCompletionTokens,
                  $totalTokens, $estimatedOutputTokens, $promptTokensPerSecond, $completionTokensPerSecond,
                  $totalTokensPerSecond, $summarizedAt, $errorCategory, $errorMessage);
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
        cmd.Parameters.AddWithValue("$totalPromptTokens", (object?)record.TotalPromptTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$totalCompletionTokens", (object?)record.TotalCompletionTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$totalTokens", (object?)record.TotalTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$estimatedOutputTokens", (object?)record.EstimatedOutputTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$promptTokensPerSecond", (object?)record.PromptTokensPerSecond ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$completionTokensPerSecond", (object?)record.CompletionTokensPerSecond ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$totalTokensPerSecond", (object?)record.TotalTokensPerSecond ?? DBNull.Value);
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
        IsEnabled    = r.IsDBNull(r.GetOrdinal("IsEnabled")) || r.GetInt32(r.GetOrdinal("IsEnabled")) != 0,
        ScanIntervalMinutes = r.IsDBNull(r.GetOrdinal("ScanIntervalMinutes")) ? 30 : r.GetInt32(r.GetOrdinal("ScanIntervalMinutes")),
        MaxVideoAgeDays = r.IsDBNull(r.GetOrdinal("MaxVideoAgeDays")) ? null : r.GetInt32(r.GetOrdinal("MaxVideoAgeDays")),
        SyncedAt     = r.IsDBNull(r.GetOrdinal("SyncedAt"))     ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("SyncedAt"))),
        LastScanStartedAt = r.IsDBNull(r.GetOrdinal("LastScanStartedAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("LastScanStartedAt"))),
        LastScanCompletedAt = r.IsDBNull(r.GetOrdinal("LastScanCompletedAt")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("LastScanCompletedAt"))),
        NextScanAfter = r.IsDBNull(r.GetOrdinal("NextScanAfter")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("NextScanAfter"))),
        ScanStatus = r.IsDBNull(r.GetOrdinal("ScanStatus")) ? "pending" : r.GetString(r.GetOrdinal("ScanStatus")),
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
