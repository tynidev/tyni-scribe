namespace YtChannel.Core.Services;

/// <summary>Progress events emitted by <see cref="ChannelOrchestrator"/> during processing.</summary>
public sealed class ProcessingProgress
{
    public required string VideoId { get; init; }
    public required string? Title { get; init; }
    public required int Position { get; init; }
    public required int Total { get; init; }
    public ProcessingEventKind Kind { get; init; }
    public string? TranscriptOrigin { get; init; }
    public long? ElapsedMs { get; init; }
    public string? ErrorCategory { get; init; }
    public int RetryAttempt { get; init; }
    public long? DelayMs { get; init; }
}

public enum ProcessingEventKind
{
    Started,
    Completed,
    Failed,
    RateLimited,
    Skipped,
}
