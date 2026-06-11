namespace Tts.App.Configuration;

public interface IAppSettingsStore
{
    string SettingsFilePath { get; }

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}