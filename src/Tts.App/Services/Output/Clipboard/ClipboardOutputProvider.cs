using System.Windows.Threading;
using Tts.App.Services;
using WpfClipboard = System.Windows.Clipboard;
using WpfApplication = System.Windows.Application;

namespace Tts.App.Services.Output;

public sealed class ClipboardOutputProvider : IOutputProvider
{
    public const string ProviderId = "clipboard";

    public string Id => ProviderId;

    public string DisplayName => "Clipboard";

    public string Description => "Copies final transcript text to the Windows clipboard.";

    public IReadOnlyList<ProviderSettingDescriptor> SettingDescriptors { get; } = Array.Empty<ProviderSettingDescriptor>();

    public async Task WriteAsync(string text, OutputProviderContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        var application = WpfApplication.Current
            ?? throw new InvalidOperationException("The WPF application is not available.");

        if (application.Dispatcher.CheckAccess())
        {
            WpfClipboard.SetText(text);
            return;
        }

        await application.Dispatcher
            .InvokeAsync(() => WpfClipboard.SetText(text), DispatcherPriority.Normal, cancellationToken)
            .Task;
    }
}
