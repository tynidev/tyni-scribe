using System.IO;
using Tts.App.Configuration;

namespace Tts.App.Services.Transcription;

public static class WhisperCppRuntimePaths
{
    public const string NativeInteropLibraryName = "tts-whisper-interop";

    public static string ResolveCliExecutablePath(TranscriptionSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.WhisperCppExecutablePathOverride))
        {
            return settings.WhisperCppExecutablePathOverride.Trim();
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "tts",
            "tools",
            "whisper.cpp",
            "v1.8.6",
            "Release",
            "whisper-cli.exe");
    }

    public static string ResolveServerExecutablePath(TranscriptionSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.WhisperCppExecutablePathOverride))
        {
            var overridePath = settings.WhisperCppExecutablePathOverride.Trim();
            var overrideDirectory = Path.GetDirectoryName(overridePath);

            if (!string.IsNullOrWhiteSpace(overrideDirectory))
            {
                return Path.Combine(overrideDirectory, "whisper-server.exe");
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "tts",
            "tools",
            "whisper.cpp",
            "v1.8.6",
            "Release",
            "whisper-server.exe");
    }

    public static string ResolveModelPath(TranscriptionSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.WhisperModelPathOverride))
        {
            return settings.WhisperModelPathOverride.Trim();
        }

        var modelFileName = WhisperCppModelCatalog.Resolve(settings.WhisperCppModelId).FileName;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "tts",
            "models",
            "whisper",
            modelFileName);
    }
}