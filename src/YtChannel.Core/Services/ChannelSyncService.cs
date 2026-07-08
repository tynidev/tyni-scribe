using YtChannel.Core.Data;

namespace YtChannel.Core.Services;

/// <summary>
/// Syncs a YouTube channel's video list into the local SQLite database.
/// Uses YouTube API for discovery and stores new videos with status=pending.
/// Existing completed/failed videos are not overwritten.
/// </summary>
public sealed class ChannelSyncService
{
    private readonly IYouTubeChannelService _youTubeChannelService;
    private readonly ChannelRepository _repository;

    public ChannelSyncService(IYouTubeChannelService youTubeChannelService, ChannelRepository repository)
    {
        _youTubeChannelService = youTubeChannelService;
        _repository = repository;
    }

    /// <summary>
    /// Fetches channel info and all videos from YouTube API, then upserts into the DB.
    /// Returns the number of newly inserted video records.
    /// </summary>
    public async Task<ChannelSyncResult> SyncChannelAsync(string channelUrl, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        // 1. Resolve channel info
        var channelInfo = await _youTubeChannelService.GetChannelInfoAsync(channelUrl, cancellationToken);

        var existingChannel = await _repository.GetChannelAsync(channelInfo.ChannelId, cancellationToken);
        var publishedAfter = existingChannel?.MaxVideoAgeDays is > 0
            ? now.AddDays(-existingChannel.MaxVideoAgeDays.Value)
            : (DateTimeOffset?)null;

        // 2. Count videos before sync (to compute delta)
        var existingCount = await _repository.CountAllVideosAsync(channelInfo.ChannelId, cancellationToken);

        // 3. Fetch all videos from YouTube API
        var apiVideos = await _youTubeChannelService.GetChannelVideosAsync(channelInfo.ChannelId, publishedAfter, cancellationToken);

        // 4. Upsert channel record
        var channelRecord = new ChannelRecord
        {
            ChannelId    = channelInfo.ChannelId,
            ChannelUrl   = channelUrl,
            ChannelName  = channelInfo.Title,
            Description  = channelInfo.Description,
            ThumbnailUrl = channelInfo.ThumbnailUrl,
            MaxVideoAgeDays = existingChannel?.MaxVideoAgeDays,
            SyncedAt     = now,
            CreatedAt    = now,
        };
        await _repository.UpsertChannelAsync(channelRecord, cancellationToken);

        // 5. Upsert videos (skips videos already in DB to preserve existing status)
        var videoRecords = apiVideos.Select(v => new VideoRecord
        {
            VideoId         = v.VideoId,
            ChannelId       = channelInfo.ChannelId,
            Title           = v.Title,
            DurationSeconds = v.DurationSeconds,
            IsShortsPlaylistVideo = v.IsShortsPlaylistVideo,
            PublishedAt     = v.PublishedAt,
            CreatedAt       = now,
            UpdatedAt       = now,
        }).ToList();

        await _repository.UpsertVideosAsync(videoRecords, cancellationToken);

        var newCount = await _repository.CountAllVideosAsync(channelInfo.ChannelId, cancellationToken);
        var newlyInserted = newCount - existingCount;

        return new ChannelSyncResult(
            ChannelId: channelInfo.ChannelId,
            ChannelUrl: channelUrl,
            ChannelName: channelInfo.Title,
            Description: channelInfo.Description,
            ThumbnailUrl: channelInfo.ThumbnailUrl,
            SyncedAt: now,
            TotalVideosInChannel: apiVideos.Count,
            NewlyInserted: newlyInserted,
            AlreadyInDatabase: apiVideos.Count - newlyInserted);
    }
}

public sealed record ChannelSyncResult(
    string ChannelId,
    string ChannelUrl,
    string? ChannelName,
    string? Description,
    string? ThumbnailUrl,
    DateTimeOffset SyncedAt,
    int TotalVideosInChannel,
    int NewlyInserted,
    int AlreadyInDatabase);
