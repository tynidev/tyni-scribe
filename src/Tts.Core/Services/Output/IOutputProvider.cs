namespace Tts.Core.Services.Output;

public interface IOutputProvider
{
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    IReadOnlyList<global::Tts.Core.Services.ProviderSettingDescriptor> SettingDescriptors { get; }

    Task WriteAsync(string text, OutputProviderContext context, CancellationToken cancellationToken = default);
}

public static class BuiltInOutputProviderIds
{
    public const string Clipboard = "clipboard";

    public const string Paste = "paste";
}
