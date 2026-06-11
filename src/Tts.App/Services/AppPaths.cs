using System.IO;

namespace Tts.App.Services;

public sealed class AppPaths
{
    public string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SpeechToTextDaemon");

    public string SettingsFilePath => Path.Combine(AppDataDirectory, "config.json");
}