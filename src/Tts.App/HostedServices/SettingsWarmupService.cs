using Microsoft.Extensions.Hosting;
using Tts.Core.Configuration;

namespace Tts.App.HostedServices;

public sealed class SettingsWarmupService : IHostedService
{
    private readonly IAppSettingsStore _settingsStore;

    public SettingsWarmupService(IAppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _settingsStore.LoadAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}