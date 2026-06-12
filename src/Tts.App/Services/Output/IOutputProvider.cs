namespace Tts.App.Services.Output;

public interface IOutputProvider
{
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    IReadOnlyList<global::Tts.App.Services.ProviderSettingDescriptor> SettingDescriptors { get; }

    Task WriteAsync(string text, OutputProviderContext context, CancellationToken cancellationToken = default);
}
