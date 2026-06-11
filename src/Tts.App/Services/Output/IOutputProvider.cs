namespace Tts.App.Services.Output;

public interface IOutputProvider
{
    string Id { get; }

    string DisplayName { get; }

    Task WriteAsync(string text, OutputProviderContext context, CancellationToken cancellationToken = default);
}