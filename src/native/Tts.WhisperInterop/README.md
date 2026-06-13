# Tts.WhisperInterop

This directory holds the native whisper.cpp interop boundary for the in-process local transcription provider.

The WPF app uses `whisper-cpp-native-local` as the in-process warm local whisper.cpp path. It calls `tts-whisper-interop.dll`, built from the pinned `ggml-org/whisper.cpp` submodule under `third_party/whisper.cpp`.

The first implementation target is Windows x64. The WPF app calls a small native DLL named `tts-whisper-interop.dll` through P/Invoke. The DLL owns the whisper.cpp context, keeps one selected model loaded across transcription sessions, unloads that model when the selected model changes, and releases all native memory on dispose.

The existing `whisper-cpp-local` CLI provider remains available as the compatibility path when native interop is unavailable.

The app expects model files from the `ggerganov/whisper.cpp` Hugging Face repository under `%LOCALAPPDATA%\tts\models\whisper`; see the root `build.md` for the exact model download commands.

The fallback executable `whisper-cli.exe` comes from official whisper.cpp GitHub releases rather than winget; see the root `build.md` for fallback-provider setup.

## Layout

```text
src/native/Tts.WhisperInterop/
  include/tts_whisper_interop.h
  src/tts_whisper_interop.cpp
  CMakeLists.txt
  third_party/whisper.cpp/   # pinned upstream Git submodule
```

After cloning this repository, initialize the native dependency with:

```powershell
git submodule update --init --recursive
```

## ABI Rules

- Export only a C ABI with `extern "C"` and primitive types.
- Do not expose C++ classes, STL containers, exceptions, or ownership rules across the ABI.
- Use UTF-8 paths and strings.
- Return sanitized status codes and sanitized short error messages only.
- Do not return or log transcript text except through the explicit transcription result pointer.
- Do not include temp audio paths, raw endpoint URLs, raw stderr, secrets, or audio content in error messages.
- `tts_whisper_engine_load_model` unloads any previously loaded model before making the new model active.
- `tts_whisper_engine_request_cancel` must be safe to call from another thread while transcription is running.
- `tts_whisper_engine_dispose` releases the loaded model and engine memory.

## Build Notes

The current local build targets the NVIDIA CUDA backend with CUDA Toolkit 12.9. Install it with:

```powershell
winget install --id Nvidia.CUDA --exact --version 12.9 --accept-package-agreements --accept-source-agreements --silent
```

Build from the repository root after loading the x64 MSVC environment:

```powershell
$cudaRoot = 'C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.9'
$cmake = "$env:ProgramFiles\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
$ninja = "$env:LOCALAPPDATA\Microsoft\WinGet\Links\ninja.exe"
$vcvars = "$env:ProgramFiles\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat"

cmd /c "`"$vcvars`" && set CUDA_PATH=$cudaRoot && set PATH=$cudaRoot\bin;%PATH% && `"$cmake`" -S src/native/Tts.WhisperInterop -B build/native/Tts.WhisperInterop -G Ninja -DCMAKE_MAKE_PROGRAM=`"$ninja`" -DCMAKE_BUILD_TYPE=Release -DGGML_CUDA=ON -DCUDAToolkit_ROOT=`"$cudaRoot`" && `"$cmake`" --build build/native/Tts.WhisperInterop --config Release"
```

The current CMake build statically links whisper.cpp and GGML from the submodule into `build/native/Tts.WhisperInterop/tts-whisper-interop.dll`. The WPF project copies that DLL next to `Tts.App.dll` when the native output exists. For CUDA local runs it also copies `cudart64_12.dll`, `cublas64_12.dll`, and `cublasLt64_12.dll` from CUDA Toolkit 12.9. `nvcuda.dll` is provided by the NVIDIA display driver.

The CUDA smoke test should show lines like:

```text
ggml_cuda_init: found 1 CUDA devices
Device 0: NVIDIA GeForce RTX 4090, compute capability 8.9
whisper_backend_init_gpu: using CUDA0 backend
```