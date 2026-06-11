using System.IO;

namespace Tts.App.Services;

public sealed class AppPaths
{
    public string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SpeechToTextDaemon");

    public string TempAudioDirectory { get; } = Path.Combine(
        Path.GetTempPath(),
        "SpeechToTextDaemon",
        "audio");

    public string LogDirectory => Path.Combine(AppDataDirectory, "logs");

    public string TimingLogFilePath => Path.Combine(LogDirectory, "timings.csv");

    public string SettingsFilePath => Path.Combine(AppDataDirectory, "config.json");
}