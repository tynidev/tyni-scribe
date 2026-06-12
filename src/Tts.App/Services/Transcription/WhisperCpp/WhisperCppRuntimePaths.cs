using System.IO;

namespace Tts.App.Services.Transcription;

public static class WhisperCppRuntimePaths
{
    public const string NativeInteropLibraryName = "tts-whisper-interop";

    public static string ResolveCliExecutablePath(WhisperCppTranscriptionSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ExecutablePathOverride))
        {
            return settings.ExecutablePathOverride.Trim();
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

    public static string ResolveServerExecutablePath(WhisperWarmProviderTranscriptionSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ServerExecutablePathOverride))
        {
            return settings.ServerExecutablePathOverride.Trim();
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

    public static string ResolveModelPath(WhisperCppTranscriptionSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ModelPathOverride))
        {
            return settings.ModelPathOverride.Trim();
        }

        return ResolveModelPath(settings.ModelId);
    }

    public static string ResolveModelPath(WhisperNativeProviderTranscriptionSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ModelPathOverride))
        {
            return settings.ModelPathOverride.Trim();
        }

        return ResolveModelPath(settings.ModelId);
    }

    public static string ResolveModelPath(WhisperWarmProviderTranscriptionSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ModelPathOverride))
        {
            return settings.ModelPathOverride.Trim();
        }

        return ResolveModelPath(settings.ModelId);
    }

    private static string ResolveModelPath(string modelId)
    {
        var modelFileName = WhisperCppModelCatalog.Resolve(modelId).FileName;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "tts",
            "models",
            "whisper",
            modelFileName);
    }
}