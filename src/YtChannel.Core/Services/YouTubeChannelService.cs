using Google;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using System.Net;
using System.Xml;
using YtChannel.Core.Configuration;

namespace YtChannel.Core.Services;

public interface IYouTubeChannelService
{
    /// <summary>Resolves the channel handle/URL to a channel ID and basic info.</summary>
    Task<YouTubeChannelInfo> GetChannelInfoAsync(string channelUrl, CancellationToken cancellationToken = default);

    /// <summary>Returns all videos in the channel (handles pagination).</summary>
    Task<IReadOnlyList<ChannelVideoInfo>> GetChannelVideosAsync(string channelId, CancellationToken cancellationToken = default);
}

public sealed class YouTubeChannelService : IYouTubeChannelService, IDisposable
{
    private readonly YouTubeService _youTubeService;
    private readonly int _maxResultsPerPage;

    public YouTubeChannelService(YouTubeApiSettings settings)
    {
        var apiKey = settings.ResolvedApiKey
            ?? throw new InvalidOperationException(
                "YouTube API key is required. Set it in configuration (YouTubeApi:ApiKey) or via the YOUTUBE_API_KEY environment variable.");

        _youTubeService = new YouTubeService(new BaseClientService.Initializer
        {
            ApiKey = apiKey,
            ApplicationName = "yt-channel",
        });

        _maxResultsPerPage = Math.Clamp(settings.MaxResultsPerPage, 1, 50);
    }

    public async Task<YouTubeChannelInfo> GetChannelInfoAsync(string channelUrl, CancellationToken cancellationToken = default)
    {
        // Resolve the channel URL to a channel ID.
        // The YouTube API search can resolve @handles and channel names.
        var channelId = ExtractChannelId(channelUrl);

        if (channelId is not null)
        {
            // Direct channel ID — look up by ID.
            var req = _youTubeService.Channels.List("id,snippet");
            req.Id = channelId;
            req.MaxResults = 1;
            var resp = await req.ExecuteAsync(cancellationToken);
            var ch = resp.Items?.FirstOrDefault();
            if (ch is null)
                throw new InvalidOperationException($"Channel '{channelUrl}' not found.");
            return MapChannel(ch);
        }

        // Handle @username or channel name — use search API.
        var handle = ExtractHandle(channelUrl) ?? channelUrl;
        var searchReq = _youTubeService.Search.List("id,snippet");
        searchReq.Q = handle;
        searchReq.Type = "channel";
        searchReq.MaxResults = 1;
        var searchResp = await searchReq.ExecuteAsync(cancellationToken);
        var hit = searchResp.Items?.FirstOrDefault();
        if (hit is null)
            throw new InvalidOperationException($"Channel '{channelUrl}' not found via search.");

        return new YouTubeChannelInfo(
            ChannelId: hit.Snippet.ChannelId,
            Title: hit.Snippet.ChannelTitle,
            Description: hit.Snippet.Description,
            ThumbnailUrl: hit.Snippet.Thumbnails?.Default__?.Url);
    }

    public async Task<IReadOnlyList<ChannelVideoInfo>> GetChannelVideosAsync(string channelId, CancellationToken cancellationToken = default)
    {
        // Get the channel's uploads playlist ID.
        var channelReq = _youTubeService.Channels.List("contentDetails");
        channelReq.Id = channelId;
        channelReq.MaxResults = 1;
        var channelResp = await channelReq.ExecuteAsync(cancellationToken);
        var uploadsPlaylistId = channelResp.Items?.FirstOrDefault()?.ContentDetails?.RelatedPlaylists?.Uploads;
        if (uploadsPlaylistId is null)
            throw new InvalidOperationException($"Could not retrieve uploads playlist for channel '{channelId}'.");

        // Page through the uploads playlist.
        var videos = new List<ChannelVideoInfo>();
        string? pageToken = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var playlistReq = _youTubeService.PlaylistItems.List("snippet,contentDetails");
            playlistReq.PlaylistId = uploadsPlaylistId;
            playlistReq.MaxResults = _maxResultsPerPage;
            if (pageToken is not null) playlistReq.PageToken = pageToken;

            var playlistResp = await playlistReq.ExecuteAsync(cancellationToken);

            if (playlistResp.Items is not null)
            {
                foreach (var item in playlistResp.Items)
                {
                    var videoId = item.ContentDetails?.VideoId ?? item.Snippet?.ResourceId?.VideoId;
                    if (string.IsNullOrWhiteSpace(videoId)) continue;

                    videos.Add(new ChannelVideoInfo(
                        VideoId: videoId,
                        VideoUrl: $"https://www.youtube.com/watch?v={videoId}",
                        Title: item.Snippet?.Title,
                        DurationSeconds: null,
                        IsShortsPlaylistVideo: null,
                        PublishedAt: item.ContentDetails?.VideoPublishedAtDateTimeOffset?.ToString("O"),
                        ThumbnailUrl: item.Snippet?.Thumbnails?.Default__?.Url));
                }
            }

            pageToken = playlistResp.NextPageToken;
        } while (pageToken is not null);

        // Enrich with precise durations from videos.list(contentDetails).
        var durations = await GetVideoDurationsAsync(videos.Select(v => v.VideoId).Distinct(StringComparer.Ordinal), cancellationToken);
        var shortsVideoIds = await GetShortsPlaylistVideoIdsAsync(channelId, cancellationToken);
        var enriched = videos
            .Select(v =>
            {
                var duration = durations.TryGetValue(v.VideoId, out var d) ? d : v.DurationSeconds;
                var isShortsPlaylistVideo = shortsVideoIds.Contains(v.VideoId);
                return v with
                {
                    DurationSeconds = duration,
                    IsShortsPlaylistVideo = isShortsPlaylistVideo,
                };
            })
            .ToList();

        return enriched;
    }

    private async Task<HashSet<string>> GetShortsPlaylistVideoIdsAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var shortsPlaylistId = GetShortsPlaylistId(channelId);
        var result = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var playlistReq = _youTubeService.PlaylistItems.List("contentDetails");
            playlistReq.PlaylistId = shortsPlaylistId;
            playlistReq.MaxResults = _maxResultsPerPage;
            if (pageToken is not null) playlistReq.PageToken = pageToken;

            PlaylistItemListResponse playlistResp;
            try
            {
                playlistResp = await playlistReq.ExecuteAsync(cancellationToken);
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                return result;
            }

            if (playlistResp.Items is not null)
            {
                foreach (var item in playlistResp.Items)
                {
                    var videoId = item.ContentDetails?.VideoId;
                    if (!string.IsNullOrWhiteSpace(videoId))
                    {
                        result.Add(videoId);
                    }
                }
            }

            pageToken = playlistResp.NextPageToken;
        } while (pageToken is not null);

        return result;
    }

    private static string GetShortsPlaylistId(string channelId)
    {
        if (!channelId.StartsWith("UC", StringComparison.Ordinal) || channelId.Length <= 2)
        {
            throw new InvalidOperationException($"Channel ID '{channelId}' cannot be converted to a Shorts playlist ID.");
        }

        return "UUSH" + channelId[2..];
    }

    private async Task<Dictionary<string, double?>> GetVideoDurationsAsync(
        IEnumerable<string> videoIds,
        CancellationToken cancellationToken)
    {
        var ids = videoIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        var result = new Dictionary<string, double?>(StringComparer.Ordinal);

        const int batchSize = 50;
        for (var i = 0; i < ids.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = ids.Skip(i).Take(batchSize).ToArray();
            var req = _youTubeService.Videos.List("contentDetails");
            req.Id = string.Join(',', batch);
            req.MaxResults = batch.Length;

            var resp = await req.ExecuteAsync(cancellationToken);
            if (resp.Items is null)
            {
                continue;
            }

            foreach (var item in resp.Items)
            {
                var id = item.Id;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var iso = item.ContentDetails?.Duration;
                result[id] = ParseDurationSeconds(iso);
            }
        }

        return result;
    }

    private static double? ParseDurationSeconds(string? isoDuration)
    {
        if (string.IsNullOrWhiteSpace(isoDuration))
        {
            return null;
        }

        try
        {
            return XmlConvert.ToTimeSpan(isoDuration).TotalSeconds;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static YouTubeChannelInfo MapChannel(Channel ch) => new(
        ChannelId: ch.Id,
        Title: ch.Snippet?.Title,
        Description: ch.Snippet?.Description,
        ThumbnailUrl: ch.Snippet?.Thumbnails?.Default__?.Url);

    /// <summary>Extracts a raw channel ID (UCxxx...) from a URL if present.</summary>
    private static string? ExtractChannelId(string url)
    {
        // https://www.youtube.com/channel/UCxxxxxx
        var match = System.Text.RegularExpressions.Regex.Match(url, @"youtube\.com/channel/(UC[\w-]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Extracts the @handle portion from a URL like https://www.youtube.com/@Handle.</summary>
    private static string? ExtractHandle(string url)
    {
        var match = System.Text.RegularExpressions.Regex.Match(url, @"youtube\.com/@([\w.-]+)");
        return match.Success ? "@" + match.Groups[1].Value : null;
    }

    public void Dispose() => _youTubeService.Dispose();
}
