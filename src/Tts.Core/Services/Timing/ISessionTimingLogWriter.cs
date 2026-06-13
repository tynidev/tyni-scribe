namespace Tts.Core.Services.Timing;

public interface ISessionTimingLogWriter
{
    Task AppendAsync(SessionTimingLogEntry entry, CancellationToken cancellationToken = default);
}