using System.IO;
using System.Text.Json;
using Tts.Core.Services;

namespace Tts.Core.Configuration;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim _sync = new(1, 1);

    public JsonAppSettingsStore(AppPaths paths)
    {
        _paths = paths;
    }

    public string SettingsFilePath => _paths.SettingsFilePath;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(_paths.AppDataDirectory);

            if (!File.Exists(SettingsFilePath))
            {
                var defaultSettings = AppSettingsNormalizer.Normalize(new AppSettings());
                await WriteSettingsAsync(defaultSettings, cancellationToken);
                return defaultSettings;
            }

            await using var stream = new FileStream(
                SettingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                _serializerOptions,
                cancellationToken);

            return AppSettingsNormalizer.Normalize(settings ?? new AppSettings());
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(_paths.AppDataDirectory);
            await WriteSettingsAsync(AppSettingsNormalizer.Normalize(settings), cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task WriteSettingsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var temporaryPath = SettingsFilePath + ".tmp";

        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 4096,
                         useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, settings, _serializerOptions, cancellationToken);
        }

        File.Move(temporaryPath, SettingsFilePath, overwrite: true);
    }

}