namespace YtChannel.Core.Configuration;

/// <summary>
/// Settings for adaptive rate limiting of caption download requests.
/// </summary>
public sealed class RateLimitSettings
{
    /// <summary>Starting delay between caption requests (milliseconds).</summary>
    public int BaseDelayMs { get; set; } = 1000;

    /// <summary>Maximum delay ever applied (milliseconds).</summary>
    public int MaxDelayMs { get; set; } = 30_000;

    /// <summary>Multiply current delay by this on a 429 response.</summary>
    public double BackoffMultiplier { get; set; } = 1.5;

    /// <summary>Divide current delay by this after each successful request.</summary>
    public double RecoveryDivisor { get; set; } = 1.1;

    /// <summary>Fraction of current delay to add/subtract as random jitter (±).</summary>
    public double JitterFraction { get; set; } = 0.20;

    /// <summary>How many recent rate-limit metric rows to load on startup for warm delay estimation.</summary>
    public int RecentMetricsToLoad { get; set; } = 20;
}
