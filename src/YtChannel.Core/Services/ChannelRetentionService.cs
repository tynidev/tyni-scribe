using YtChannel.Core.Data;

namespace YtChannel.Core.Services;

public sealed record ChannelRetentionResult(
    string ChannelId,
    int? MaxVideoAgeDays,
    int PrunedVideos,
    int DeletedArtifactDirectories);

public sealed class ChannelRetentionService(ChannelRepository repository)
{
    public async Task<ChannelRetentionResult> SetMaxVideoAgeDaysAsync(
        string channelId,
        int? maxVideoAgeDays,
        string outputRootDirectory,
        bool pruneNow,
        CancellationToken cancellationToken = default)
    {
        if (maxVideoAgeDays is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxVideoAgeDays), "Max video age days must be greater than zero.");
        }

        var channel = await repository.GetChannelAsync(channelId, cancellationToken)
            ?? throw new InvalidOperationException($"Channel '{channelId}' is not in the database. Sync it first.");

        if (maxVideoAgeDays.HasValue)
        {
            await repository.UpdateChannelMaxVideoAgeDaysAsync(channel.ChannelId, maxVideoAgeDays.Value, cancellationToken);
        }
        else
        {
            await repository.ClearChannelMaxVideoAgeDaysAsync(channel.ChannelId, cancellationToken);
        }

        if (!pruneNow || !maxVideoAgeDays.HasValue)
        {
            return new ChannelRetentionResult(channel.ChannelId, maxVideoAgeDays, 0, 0);
        }

        return await PruneAsync(channel.ChannelId, outputRootDirectory, cancellationToken);
    }

    public async Task<ChannelRetentionResult> PruneAsync(
        string channelId,
        string outputRootDirectory,
        CancellationToken cancellationToken = default)
    {
        var channel = await repository.GetChannelAsync(channelId, cancellationToken)
            ?? throw new InvalidOperationException($"Channel '{channelId}' is not in the database.");

        if (channel.MaxVideoAgeDays is not > 0)
        {
            return new ChannelRetentionResult(channel.ChannelId, channel.MaxVideoAgeDays, 0, 0);
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-channel.MaxVideoAgeDays.Value);
        var oldVideos = await repository.GetVideosOlderThanAsync(channel.ChannelId, cutoff, cancellationToken);
        var deletedDirectories = DeleteArtifactDirectories(outputRootDirectory, channel.ChannelId, oldVideos);
        var prunedVideos = await repository.PruneVideosOlderThanAsync(channel.ChannelId, cutoff, cancellationToken);

        return new ChannelRetentionResult(channel.ChannelId, channel.MaxVideoAgeDays, prunedVideos, deletedDirectories);
    }

    private static int DeleteArtifactDirectories(
        string outputRootDirectory,
        string channelId,
        IReadOnlyList<VideoRetentionPruneCandidate> oldVideos)
    {
        if (oldVideos.Count == 0)
        {
            return 0;
        }

        var deleted = 0;
        var channelOutputDirectory = ChannelManifestService.GetChannelOutputDirectory(outputRootDirectory, channelId);
        foreach (var oldVideo in oldVideos)
        {
            var path = Path.Combine(channelOutputDirectory, oldVideo.VideoId);
            if (!Directory.Exists(path))
            {
                continue;
            }

            Directory.Delete(path, recursive: true);
            deleted++;
        }

        return deleted;
    }
}