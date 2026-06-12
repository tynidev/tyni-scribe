namespace Tts.App.Configuration;

public sealed class AppSettings
{
    public const int CurrentConfigVersion = 1;

    public int ConfigVersion { get; set; } = CurrentConfigVersion;

    public string? SelectedMicrophoneDeviceId { get; set; }

    public HotkeySettings StartStopHotkey { get; set; } = HotkeySettings.FromGesture("Ctrl+Alt+Space");

    public HotkeySettings CancelHotkey { get; set; } = HotkeySettings.FromGesture("Ctrl+Shift+Space");

    public string SelectedTranscriptionProviderId { get; set; } = "whisper-cpp-native-local";

    public string SelectedAudioProcessorProviderId { get; set; } = "noop";

    public TranscriptionSettings Transcription { get; set; } = new();

    public CleanupSettings Cleanup { get; set; } = new();

    public List<string> EnabledOutputProviderIds { get; set; } = new() { "paste" };

    public SettingsWindowPlacement SettingsWindow { get; set; } = new();
}

public sealed class TranscriptionSettings
{
    public string WhisperCppModelId { get; set; } = "tiny-en";

    public string FasterWhisperModelId { get; set; } = "tiny-en";

    public string? WhisperCppExecutablePathOverride { get; set; }

    public string? WhisperModelPathOverride { get; set; }

    public string? FasterWhisperModelPathOverride { get; set; }

    public string FasterWhisperComputeType { get; set; } = "float16";

    public string Language { get; set; } = "en";

    public int TimeoutSeconds { get; set; } = 600;

    public TranscriptionSettings Copy()
    {
        return new TranscriptionSettings
        {
            WhisperCppModelId = WhisperCppModelId,
            FasterWhisperModelId = FasterWhisperModelId,
            WhisperCppExecutablePathOverride = WhisperCppExecutablePathOverride,
            WhisperModelPathOverride = WhisperModelPathOverride,
            FasterWhisperModelPathOverride = FasterWhisperModelPathOverride,
            FasterWhisperComputeType = FasterWhisperComputeType,
            Language = Language,
            TimeoutSeconds = TimeoutSeconds
        };
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
    public bool IsEnabled { get; set; }

    public string ProviderId { get; set; } = "noop";

    public string Prompt { get; set; } = "Clean up punctuation and casing without changing meaning.";
}

public sealed class SettingsWindowPlacement
{
    public double Width { get; set; } = 720;

    public double Height { get; set; } = 560;
}