using System.IO;

namespace Tts.App.Services.Transcription;

public static class FasterWhisperRuntimePaths
{
    public const string NativeInteropLibraryName = "tts-ctranslate2-interop";

    public static string ResolvePythonExecutablePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "tts",
            "tools",
            "faster-whisper-python",
            "Scripts",
            "python.exe");
    }

    public static string ResolveRunnerScriptPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "faster_whisper_transcribe.py");
    }

    public static string ResolveNativeInteropPath()
    {
        return Path.Combine(AppContext.BaseDirectory, NativeInteropLibraryName + ".dll");
    }

    public static string ResolveModelDirectory(FasterWhisperTranscriptionSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ModelPathOverride))
        {
            return settings.ModelPathOverride.Trim();
        }

        var modelDirectoryName = FasterWhisperModelCatalog.Resolve(settings.ModelId).DirectoryName;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "tts",
            "models",
            "faster-whisper",
            modelDirectoryName);
    }
}