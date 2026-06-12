# Build Guide

This repository builds a Windows speech-to-text tray utility and an optional native whisper.cpp interop DLL. The main app is a .NET 8 WPF project. The native provider is Windows x64 and currently targets NVIDIA CUDA through GGML.

## Clone

Clone with submodules:

```powershell
git clone --recurse-submodules https://github.com/<owner>/<repo>.git
cd <repo>
```

If the repository was cloned without submodules:

```powershell
git submodule update --init --recursive
```

The whisper.cpp source is pinned as a Git submodule at:

```text
src/native/Tts.WhisperInterop/third_party/whisper.cpp
```

The faster-whisper/CTranslate2 provider scaffold is under:

```text
src/native/Tts.CTranslate2Interop
```

It currently exports the planned C ABI and returns a sanitized provider-unavailable result. CTranslate2 is not pinned in the repo yet, so this scaffold does not perform transcription.

The app also includes an experimental process bridge for `faster-whisper-local`. It uses an app-managed Python virtual environment under `%LOCALAPPDATA%\tts\tools\faster-whisper-python` to run `faster-whisper`, which itself uses CTranslate2. This is the current runnable path for exercising faster-whisper while the direct Python-free C++ integration remains future work.

## Prerequisites

Required for the WPF app:

```powershell
winget install Microsoft.DotNet.SDK.8
```

Required for the native interop DLL:

```powershell
winget install Microsoft.VisualStudio.2022.BuildTools
winget install Kitware.CMake
winget install Ninja-build.Ninja
```

Use Visual Studio Installer to add the **Desktop development with C++** workload to Build Tools. The native build needs the MSVC x64 compiler and Windows SDK.

Required for the CUDA native provider on NVIDIA GPUs:

```powershell
winget install --id Nvidia.CUDA --exact --version 12.9 --accept-package-agreements --accept-source-agreements --silent
```

Verify the CUDA compiler:

```powershell
& 'C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.9\bin\nvcc.exe' --version
nvidia-smi
```

### CTranslate2 / faster-whisper Research Status

CTranslate2 has official C++ APIs, supports CMake builds, and supports Windows x64 GPU execution. CUDA execution requires CUDA 12.x. Speech recognition models with convolutional layers also require cuDNN; current faster-whisper documentation calls for cuDNN 9 with recent CTranslate2 releases, while older CTranslate2 versions used cuDNN 8 combinations.

The direct native provider is not fully implemented in this repository yet because CTranslate2 only covers the optimized model runtime. A complete Python-free faster-whisper provider still needs native implementations for:

- WAV decode/resampling to mono 16 kHz.
- Whisper log-mel feature extraction.
- 30-second chunking and result aggregation.
- tokenizer loading from `tokenizer.json` and related assets.
- prompt token construction for language, transcribe mode, and no timestamps.
- token decoding back to final transcript text.

These behaviors are supplied today by `faster-whisper`, `transformers`, PyAV/librosa, and tokenizers in Python examples. The current experimental app bridge uses `faster-whisper` in an isolated Python process to exercise the CTranslate2 pipeline now. Porting the same behavior into the native DLL remains the next product-sized step for a Python-free normal runtime path.

## External Runtime Assets

Model files are not stored in this repository. Place supported whisper models under:

```text
%LOCALAPPDATA%\tts\models\whisper
```

Expected model filenames:

```text
ggml-tiny.en.bin
ggml-base.en.bin
ggml-small.en.bin
ggml-large-v3-turbo.bin
```

These files are published by the whisper.cpp project under the `ggerganov/whisper.cpp` Hugging Face repository:

```text
https://huggingface.co/ggerganov/whisper.cpp/tree/main
```

Download the required models with:

```powershell
$modelDir = Join-Path $env:LOCALAPPDATA 'tts\models\whisper'
New-Item -ItemType Directory -Force -Path $modelDir | Out-Null

$models = @(
    'ggml-tiny.en.bin',
    'ggml-base.en.bin',
    'ggml-small.en.bin',
    'ggml-large-v3-turbo.bin'
)

foreach ($model in $models) {
    Invoke-WebRequest `
        -Uri "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$model" `
        -OutFile (Join-Path $modelDir $model)
}
```

The direct source URLs are:

```text
https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.en.bin
https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin
https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin
https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin
```

### faster-whisper / CTranslate2 Model Assets

The `faster-whisper-local` provider does not use ggml `.bin` files. It expects converted CTranslate2 Whisper model directories under:

```text
%LOCALAPPDATA%\tts\models\faster-whisper
```

Friendly app model IDs map to separate directories:

```text
tiny-en          -> %LOCALAPPDATA%\tts\models\faster-whisper\tiny-en
base-en          -> %LOCALAPPDATA%\tts\models\faster-whisper\base-en
small-en         -> %LOCALAPPDATA%\tts\models\faster-whisper\small-en
large-v3-turbo   -> %LOCALAPPDATA%\tts\models\faster-whisper\large-v3-turbo
```

The four built-in friendly IDs map to pre-converted Hugging Face repositories:

```text
tiny-en          -> Systran/faster-whisper-tiny.en
base-en          -> Systran/faster-whisper-base.en
small-en         -> Systran/faster-whisper-small.en
large-v3-turbo   -> mobiuslabsgmbh/faster-whisper-large-v3-turbo
```

Install the isolated faster-whisper runtime and download all four configured models with:

```powershell
$venv = Join-Path $env:LOCALAPPDATA 'tts\tools\faster-whisper-python'
if (-not (Test-Path $venv)) {
    python -m venv $venv
}

& (Join-Path $venv 'Scripts\python.exe') -m pip install --upgrade pip
& (Join-Path $venv 'Scripts\python.exe') -m pip install -U huggingface_hub faster-whisper

$modelRoot = Join-Path $env:LOCALAPPDATA 'tts\models\faster-whisper'
New-Item -ItemType Directory -Force -Path $modelRoot | Out-Null

$hf = Join-Path $venv 'Scripts\hf.exe'
$models = @(
    @{ Repo = 'Systran/faster-whisper-tiny.en'; Dir = 'tiny-en' },
    @{ Repo = 'Systran/faster-whisper-base.en'; Dir = 'base-en' },
    @{ Repo = 'Systran/faster-whisper-small.en'; Dir = 'small-en' },
    @{ Repo = 'mobiuslabsgmbh/faster-whisper-large-v3-turbo'; Dir = 'large-v3-turbo' }
)

foreach ($model in $models) {
    $target = Join-Path $modelRoot $model.Dir
    New-Item -ItemType Directory -Force -Path $target | Out-Null

    & $hf download $model.Repo `
        --local-dir $target `
        --include 'config.json' `
        --include 'preprocessor_config.json' `
        --include 'model.bin' `
        --include 'tokenizer.json' `
        --include 'vocabulary.*'
}
```

If a model is not available pre-converted, convert it with `ct2-transformers-converter` and place the converted directory under `%LOCALAPPDATA%\tts\models\faster-whisper`.

### Optional Fallback Provider Tools

The native provider does not use `whisper-cli.exe` or `whisper-server.exe`. It uses the repo-built `tts-whisper-interop.dll` and still requires the model files above.

The CLI and warm-worker fallback providers do need the official whisper.cpp release tools:

- `whisper.cpp local` uses `whisper-cli.exe`.
- `whisper.cpp warm local` uses `whisper-server.exe`.

These upstream tools are not currently available as a reliable winget package. Download a Windows release from:

```text
https://github.com/ggml-org/whisper.cpp/releases
```

Extract the release under:

```text
%LOCALAPPDATA%\tts\tools\whisper.cpp\v1.8.6\Release
```

Expected executables for those providers:

```text
whisper-cli.exe
whisper-server.exe
```

## Build Native Interop DLL

From the repository root:

```powershell
$cudaRoot = 'C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.9'
$vsRoot = "$env:ProgramFiles\Microsoft Visual Studio\2022\BuildTools"

if (-not (Test-Path "$vsRoot\VC\Auxiliary\Build\vcvars64.bat")) {
    $vsRoot = "$env:ProgramFiles\Microsoft Visual Studio\2022\Enterprise"
}

if (-not (Test-Path "$vsRoot\VC\Auxiliary\Build\vcvars64.bat")) {
    $vsRoot = "$env:ProgramFiles\Microsoft Visual Studio\2022\Community"
}

$vcvars = "$vsRoot\VC\Auxiliary\Build\vcvars64.bat"
$cmake = "$vsRoot\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
$ninja = "$env:LOCALAPPDATA\Microsoft\WinGet\Links\ninja.exe"

cmd /c "`"$vcvars`" && set CUDA_PATH=$cudaRoot && set PATH=$cudaRoot\bin;%PATH% && `"$cmake`" -S src/native/Tts.WhisperInterop -B build/native/Tts.WhisperInterop -G Ninja -DCMAKE_MAKE_PROGRAM=`"$ninja`" -DCMAKE_BUILD_TYPE=Release -DGGML_CUDA=ON -DCUDAToolkit_ROOT=`"$cudaRoot`" && `"$cmake`" --build build/native/Tts.WhisperInterop --config Release"
```

Expected output:

```text
build/native/Tts.WhisperInterop/tts-whisper-interop.dll
```

`build/` is generated output and is ignored by Git.

## Build CTranslate2 Interop Scaffold

The CTranslate2 scaffold can be built independently. It does not link CTranslate2 yet and will report the provider as unavailable at runtime.

```powershell
$vsRoot = "$env:ProgramFiles\Microsoft Visual Studio\2022\BuildTools"

if (-not (Test-Path "$vsRoot\VC\Auxiliary\Build\vcvars64.bat")) {
    $vsRoot = "$env:ProgramFiles\Microsoft Visual Studio\2022\Enterprise"
}

if (-not (Test-Path "$vsRoot\VC\Auxiliary\Build\vcvars64.bat")) {
    $vsRoot = "$env:ProgramFiles\Microsoft Visual Studio\2022\Community"
}

$vcvars = "$vsRoot\VC\Auxiliary\Build\vcvars64.bat"
$cmake = "$vsRoot\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
$ninja = "$env:LOCALAPPDATA\Microsoft\WinGet\Links\ninja.exe"

cmd /c "`"$vcvars`" && `"$cmake`" -S src/native/Tts.CTranslate2Interop -B build/native/Tts.CTranslate2Interop -G Ninja -DCMAKE_MAKE_PROGRAM=`"$ninja`" -DCMAKE_BUILD_TYPE=Release && `"$cmake`" --build build/native/Tts.CTranslate2Interop --config Release"
```

Expected scaffold output:

```text
build/native/Tts.CTranslate2Interop/tts-ctranslate2-interop.dll
```

Future full native integration should pin CTranslate2, for example as a Git submodule at a specific tag such as `v4.8.0`, build it with `-DWITH_CUDA=ON -DWITH_CUDNN=ON`, and copy the required CTranslate2/CUDA/cuDNN runtime DLLs into the WPF output when present.

## Build WPF App

Build the app from the repository root:

```powershell
dotnet build Tts.sln
```

If the tray app is already running and locking `Tts.App.exe`, use:

```powershell
dotnet build Tts.sln /p:UseAppHost=false
```

When the native DLL exists, `src/Tts.App/Tts.App.csproj` copies it into the app output. For CUDA local runs it also copies these CUDA runtime DLLs from CUDA Toolkit 12.9 when present:

```text
cudart64_12.dll
cublas64_12.dll
cublasLt64_12.dll
```

The NVIDIA driver provides `nvcuda.dll`.

## Run

```powershell
dotnet run --project src/Tts.App/Tts.App.csproj
```

In the settings window, choose a transcription provider:

```text
whisper.cpp local         # whisper-cli.exe per session, compatibility path
whisper.cpp warm local    # whisper-server.exe worker, keeps model warm out-of-process
whisper.cpp native local  # tts-whisper-interop.dll, keeps model warm in-process
faster-whisper local GPU  # experimental faster-whisper/CTranslate2 process bridge
```

For CUDA-backed native transcription, select:

```text
whisper.cpp native local
```

If the app was already running before rebuilding native DLLs, fully quit the tray app and restart it so Windows loads the new DLL.

## Verify CUDA Native Provider

A successful CUDA native smoke test or app run should include native output similar to:

```text
ggml_cuda_init: found 1 CUDA devices
Device 0: NVIDIA GeForce RTX 4090, compute capability 8.9
whisper_model_load: CUDA0 total size = ...
whisper_backend_init_gpu: using CUDA0 backend
```

The app timing CSV is written to:

```text
%APPDATA%\SpeechToTextDaemon\logs\timings.csv
```

## Verify faster-whisper Provider

`faster-whisper local GPU` should appear in the transcription provider dropdown. It is not the default provider. Selecting it runs the isolated faster-whisper process bridge when the Python runtime and selected model directory are installed.

Smoke test the runner from the repository root:

```powershell
$venv = Join-Path $env:LOCALAPPDATA 'tts\tools\faster-whisper-python'
$python = Join-Path $venv 'Scripts\python.exe'
$modelDir = Join-Path $env:LOCALAPPDATA 'tts\models\faster-whisper\tiny-en'
$audio = 'src\native\Tts.WhisperInterop\third_party\whisper.cpp\bindings\go\samples\jfk.wav'

& $python 'src\Tts.App\Services\Transcription\FasterWhisper\Scripts\faster_whisper_transcribe.py' `
    --model-dir $modelDir `
    --audio-file $audio `
    --language en `
    --compute-type auto
```

Expected text begins with:

```text
And so my fellow Americans ask not what your country can do for you...
```

The runner attempts CUDA first for CUDA-oriented compute types and falls back to CPU `int8` if the local CTranslate2 CUDA dependencies are unavailable. Once the native C++ implementation is completed, smoke test it with a small WAV and verify CTranslate2 reports CUDA execution before treating that native path as production-ready.

## Clean

Remove generated build outputs:

```powershell
Remove-Item build -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item src/Tts.App/bin, src/Tts.App/obj -Recurse -Force -ErrorAction SilentlyContinue
```

Then rebuild native and app outputs as needed.
