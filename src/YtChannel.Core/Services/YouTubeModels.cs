namespace YtChannel.Core.Services;

/// <summary>Lightweight video metadata returned from YouTube API discovery.</summary>
public sealed record ChannelVideoInfo(
    string VideoId,
    string VideoUrl,
    string? Title,
    double? DurationSeconds,
    bool? IsShortsPlaylistVideo,
    string? PublishedAt,
    string? ThumbnailUrl);

/// <summary>Basic channel identity returned from YouTube API.</summary>
public sealed record YouTubeChannelInfo(
    string ChannelId,
    string? Title,
    string? Description,
    string? ThumbnailUrl);
