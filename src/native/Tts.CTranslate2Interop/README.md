# Tts.CTranslate2Interop

This directory holds the native ABI scaffold for a future in-process `faster-whisper-local` provider based on CTranslate2.

The current implementation deliberately exports the stable C ABI but does not run CTranslate2 inference yet. It returns sanitized provider-unavailable status values so the WPF app can keep the provider boundary, settings shape, cancellation path, and native loading behavior compile-safe while the larger Whisper/CTranslate2 pipeline is implemented.

## Intended Native Shape

```text
src/native/Tts.CTranslate2Interop/
  include/tts_ctranslate2_interop.h
  src/tts_ctranslate2_interop.cpp
  CMakeLists.txt
```

The final DLL name is `tts-ctranslate2-interop.dll`. The C# provider calls it through P/Invoke only when the user selects `faster-whisper-local`.

## ABI Rules

- Export only a C ABI with `extern "C"` and primitive types.
- Do not expose C++ classes, STL containers, exceptions, or ownership rules across the ABI.
- Use UTF-8 paths and strings.
- Return sanitized status codes and short sanitized error messages only.
- Do not return or log transcript text except through the explicit transcription result pointer.
- Do not include temp audio paths, raw endpoint URLs, raw stderr, secrets, or audio content in error messages.
- `tts_ctranslate2_engine_load_model` should unload any previously loaded model before making the new model active.
- `tts_ctranslate2_engine_request_cancel` must be safe to call from another thread while transcription is running.
- `tts_ctranslate2_engine_dispose` must release loaded model state and all native memory.

## Open Native Work

- Pin CTranslate2 as a source submodule or package version.
- Build CTranslate2 on Windows x64 with MSVC/CMake and CUDA enabled.
- Decide the CUDA/cuDNN pairing and copy required runtime DLLs next to the WPF app.
- Load converted CTranslate2 Whisper model directories from `%LOCALAPPDATA%\tts\models\faster-whisper`.
- Implement WAV decode/resampling to mono 16 kHz without FFmpeg/Python in the normal app path.
- Implement Whisper log-mel feature extraction and 30-second windowing.
- Load tokenizer assets such as `tokenizer.json` and `preprocessor_config.json` without Python.
- Build Whisper prompts for language/task/no-timestamps and decode generated token IDs to final text.
- Preserve serialized access until CTranslate2 model/thread-safety is proven for this usage.