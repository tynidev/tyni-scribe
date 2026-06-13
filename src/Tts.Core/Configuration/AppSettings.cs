namespace Tts.Core.Configuration;

public sealed class AppSettings
{
    public const int CurrentConfigVersion = 2;

    public int ConfigVersion { get; set; } = CurrentConfigVersion;

    public string? SelectedMicrophoneDeviceId { get; set; }

    public HotkeySettings StartStopHotkey { get; set; } = HotkeySettings.FromGesture("Ctrl+Alt+Space");

    public HotkeySettings CancelHotkey { get; set; } = HotkeySettings.FromGesture("Ctrl+Shift+Space");

    public string SelectedTranscriptionProviderId { get; set; } = "whisper-cpp-native-local";

    public string SelectedAudioProcessorProviderId { get; set; } = "noop";

    public Dictionary<string, Dictionary<string, string>> TranscriptionProviderSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Dictionary<string, string>> AudioProcessingProviderSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public CleanupSettings Cleanup { get; set; } = new();

    public List<string> EnabledOutputProviderIds { get; set; } = new() { "paste" };

    public Dictionary<string, Dictionary<string, string>> OutputProviderSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public SettingsWindowPlacement SettingsWindow { get; set; } = new();
}

public sealed class HotkeySettings
{
    public string Gesture { get; set; } = string.Empty;

    public static HotkeySettings FromGesture(string gesture)
    {
        return new HotkeySettings { Gesture = gesture };
    }
}

public sealed class CleanupSettings
{
    public bool IsEnabled { get; set; }

    public string ProviderId { get; set; } = "noop";

    public string Prompt { get; set; } = "Clean up punctuation and casing without changing meaning.";
}

public sealed class SettingsWindowPlacement
{
    public double Width { get; set; } = 720;

    public double Height { get; set; } = 560;
}