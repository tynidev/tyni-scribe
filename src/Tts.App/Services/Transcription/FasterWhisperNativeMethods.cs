using System.Runtime.InteropServices;

namespace Tts.App.Services.Transcription;

internal static partial class FasterWhisperNativeMethods
{
    [DllImport(FasterWhisperRuntimePaths.NativeInteropLibraryName, EntryPoint = "tts_ctranslate2_engine_create", CallingConvention = CallingConvention.Cdecl)]
    internal static extern FasterWhisperNativeInteropStatus CreateEngine(out IntPtr engine);

    [DllImport(FasterWhisperRuntimePaths.NativeInteropLibraryName, EntryPoint = "tts_ctranslate2_engine_load_model", CallingConvention = CallingConvention.Cdecl)]
    internal static extern FasterWhisperNativeInteropStatus LoadModel(
        IntPtr engine,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string modelDirectory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string computeType);

    [DllImport(FasterWhisperRuntimePaths.NativeInteropLibraryName, EntryPoint = "tts_ctranslate2_engine_unload_model", CallingConvention = CallingConvention.Cdecl)]
    internal static extern FasterWhisperNativeInteropStatus UnloadModel(IntPtr engine);

    [DllImport(FasterWhisperRuntimePaths.NativeInteropLibraryName, EntryPoint = "tts_ctranslate2_engine_transcribe_wav", CallingConvention = CallingConvention.Cdecl)]
    internal static extern FasterWhisperNativeInteropStatus TranscribeWav(
        IntPtr engine,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string audioFilePath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string language,
        int timeoutSeconds,
        out IntPtr transcriptUtf8);

    [DllImport(FasterWhisperRuntimePaths.NativeInteropLibraryName, EntryPoint = "tts_ctranslate2_engine_request_cancel", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RequestCancel(IntPtr engine);

    [DllImport(FasterWhisperRuntimePaths.NativeInteropLibraryName, EntryPoint = "tts_ctranslate2_engine_last_error", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr LastError(IntPtr engine);

    [DllImport(FasterWhisperRuntimePaths.NativeInteropLibraryName, EntryPoint = "tts_ctranslate2_string_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FreeString(IntPtr value);

    [DllImport(FasterWhisperRuntimePaths.NativeInteropLibraryName, EntryPoint = "tts_ctranslate2_engine_dispose", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void DisposeEngine(IntPtr engine);
}