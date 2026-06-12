using System.Runtime.InteropServices;

namespace Tts.App.Services.Transcription;

internal static partial class WhisperNativeMethods
{
    [DllImport(WhisperCppRuntimePaths.NativeInteropLibraryName, EntryPoint = "tts_whisper_engine_create", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WhisperNativeInteropStatus CreateEngine(out IntPtr engine);

    [DllImport(WhisperCppRuntimePaths.NativeInteropLibraryName, EntryPoint = "tts_whisper_engine_load_model", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WhisperNativeInteropStatus LoadModel(IntPtr engine, [MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath);

    [DllImport(WhisperCppRuntimePaths.NativeInteropLibraryName, EntryPoint = "tts_whisper_engine_unload_model", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WhisperNativeInteropStatus UnloadModel(IntPtr engine);

    [DllImport(WhisperCppRuntimePaths.NativeInteropLibraryName, EntryPoint = "tts_whisper_engine_transcribe_wav", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WhisperNativeInteropStatus TranscribeWav(
        IntPtr engine,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string audioFilePath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string language,
        int timeoutSeconds,
        out IntPtr transcriptUtf8);

    [DllImport(WhisperCppRuntimePaths.NativeInteropLibraryName, EntryPoint = "tts_whisper_engine_request_cancel", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RequestCancel(IntPtr engine);

    [DllImport(WhisperCppRuntimePaths.NativeInteropLibraryName, EntryPoint = "tts_whisper_engine_last_error", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr LastError(IntPtr engine);

    [DllImport(WhisperCppRuntimePaths.NativeInteropLibraryName, EntryPoint = "tts_whisper_string_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FreeString(IntPtr value);

    [DllImport(WhisperCppRuntimePaths.NativeInteropLibraryName, EntryPoint = "tts_whisper_engine_dispose", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void DisposeEngine(IntPtr engine);
}