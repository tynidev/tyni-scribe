using Tts.Core.Services.AudioProcessing;
using Tts.Core.Services.Output;
using Tts.Core.Services.Transcription;

namespace Tts.Core.Configuration;

public sealed class AppSettings
{
    public const int CurrentConfigVersion = 2;
    public const string DefaultStartStopHotkeyGesture = "Ctrl+Space";
    public const string PreviousDefaultStartStopHotkeyGesture = "Ctrl+Alt+Space";
    public const string DefaultCancelHotkeyGesture = "Ctrl+Shift+Space";
    public const string PreviousDefaultCancelHotkeyGesture = "Ctrl+Alt+Escape";
    public const string DefaultTranscriptionProviderId = WhisperNativeBatchTranscriptionProvider.ProviderId;
    public const string DefaultAudioProcessorProviderId = NoOpAudioProcessingProvider.ProviderId;
    public const string DefaultCleanupProviderId = "noop";
    public const string DefaultOutputProviderId = BuiltInOutputProviderIds.Paste;

    public int ConfigVersion { get; set; } = CurrentConfigVersion;

    public string? SelectedMicrophoneDeviceId { get; set; }

    public HotkeySettings StartStopHotkey { get; set; } = CreateDefaultStartStopHotkey();

    public HotkeySettings CancelHotkey { get; set; } = CreateDefaultCancelHotkey();

    public string SelectedTranscriptionProviderId { get; set; } = DefaultTranscriptionProviderId;

    public string SelectedAudioProcessorProviderId { get; set; } = DefaultAudioProcessorProviderId;

    public Dictionary<string, Dictionary<string, string>> TranscriptionProviderSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Dictionary<string, string>> AudioProcessingProviderSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public CleanupSettings Cleanup { get; set; } = new();

    public List<string> EnabledOutputProviderIds { get; set; } = CreateDefaultEnabledOutputProviderIds();

    public Dictionary<string, Dictionary<string, string>> OutputProviderSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public SettingsWindowPlacement SettingsWindow { get; set; } = new();

    public static HotkeySettings CreateDefaultStartStopHotkey()
    {
        return HotkeySettings.FromGesture(DefaultStartStopHotkeyGesture);
    }

    public static HotkeySettings CreateDefaultCancelHotkey()
    {
        return HotkeySettings.FromGesture(DefaultCancelHotkeyGesture);
    }

    public static List<string> CreateDefaultEnabledOutputProviderIds()
    {
        return new List<string> { DefaultOutputProviderId };
    }
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
    public const string DefaultProviderId = AppSettings.DefaultCleanupProviderId;
    public const string DefaultPrompt = "Clean up punctuation and casing without changing meaning.";

    public bool IsEnabled { get; set; }

    public string ProviderId { get; set; } = DefaultProviderId;

    public string Prompt { get; set; } = DefaultPrompt;
}

public sealed class SettingsWindowPlacement
{
    public const double DefaultWidth = 720;
    public const double DefaultHeight = 560;

    public double Width { get; set; } = DefaultWidth;

    public double Height { get; set; } = DefaultHeight;
}