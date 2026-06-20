using YtChannel.Core.Configuration;
using YtChannel.Core.Data;

namespace YtChannel.Core.Services;

/// <summary>
/// Tracks caption download timing and adaptively learns a safe inter-request delay.
/// Persists metrics to the database so learning survives restarts.
/// Thread-safety: designed for single-threaded serial caption downloads.
/// </summary>
public sealed class RateLimitTracker
{
    private readonly RateLimitSettings _settings;
    private readonly ChannelRepository _repository;
    private readonly Random _random = new();

    private double _currentDelayMs;

    public RateLimitTracker(RateLimitSettings settings, ChannelRepository repository)
    {
        _settings = settings;
        _repository = repository;
        _currentDelayMs = settings.BaseDelayMs;
    }

    /// <summary>
    /// Warms up the delay estimate from recent persisted metrics.
    /// Call once after the DB is initialized.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        var recent = await _repository.GetRecentRateLimitMetricsAsync(
            _settings.RecentMetricsToLoad, cancellationToken);

        if (recent.Count == 0) return;

        // If any recent requests were throttled, restore a conservative delay.
        bool anyThrottled = recent.Any(m => m.HttpStatus == 429);
        if (anyThrottled)
        {
            // Last recorded backoff or default max/2
            var lastBackoff = recent.First(m => m.HttpStatus == 429).BackoffAppliedMs;
            _currentDelayMs = Math.Max(lastBackoff, _settings.BaseDelayMs);
            return;
        }

        // No recent throttling — use the average delay from successful requests
        var successfulDelays = recent
            .Where(m => m.HttpStatus is null)
            .Select(m => (double)m.CaptionDownloadDelayMs)
            .ToList();

        if (successfulDelays.Count > 0)
            _currentDelayMs = successfulDelays.Average();
    }

    /// <summary>
    /// Current recommended delay before the next caption download request.
    /// Includes jitter.
    /// </summary>
    public TimeSpan GetCurrentDelay()
    {
        var jitter = _currentDelayMs * _settings.JitterFraction;
        var actual = _currentDelayMs + (_random.NextDouble() * 2 - 1) * jitter;
        actual = Math.Clamp(actual, 0, _settings.MaxDelayMs);
        return TimeSpan.FromMilliseconds(actual);
    }

    /// <summary>
    /// Records a successful caption download and decays the delay toward the base.
    /// </summary>
    public async Task RecordSuccessAsync(
        long delayAppliedMs,
        long? captionDownloadDurationMs,
        CancellationToken cancellationToken = default)
    {
        // Decay toward base
        _currentDelayMs = Math.Max(_settings.BaseDelayMs, _currentDelayMs / _settings.RecoveryDivisor);

        await _repository.InsertRateLimitMetricAsync(new RateLimitMetricRecord
        {
            Timestamp                 = DateTimeOffset.UtcNow,
            CaptionDownloadDelayMs    = delayAppliedMs,
            CaptionDownloadDurationMs = captionDownloadDurationMs,
            HttpStatus                = null,
            BackoffAppliedMs          = 0,
        }, cancellationToken);
    }

    /// <summary>
    /// Records a rate-limit (HTTP 429) response and increases the backoff delay.
    /// </summary>
    public async Task RecordThrottledAsync(long delayAppliedMs, CancellationToken cancellationToken = default)
    {
        var newDelay = Math.Min(_currentDelayMs * _settings.BackoffMultiplier, _settings.MaxDelayMs);
        _currentDelayMs = newDelay;

        await _repository.InsertRateLimitMetricAsync(new RateLimitMetricRecord
        {
            Timestamp                 = DateTimeOffset.UtcNow,
            CaptionDownloadDelayMs    = delayAppliedMs,
            CaptionDownloadDurationMs = null,
            HttpStatus                = 429,
            BackoffAppliedMs          = (long)newDelay,
        }, cancellationToken);
    }
}
