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

### Optional Fallback Provider Tools

The native provider does not use `whisper-cli.exe` or `whisper-server.exe`. It uses the repo-built `tts-whisper-interop.dll` and still requires the model files above.

The CLI fallback provider needs the official whisper.cpp release tools:

- `whisper.cpp local` uses `whisper-cli.exe`.

These upstream tools are not currently available as a reliable winget package. Download a Windows release from:

```text
https://github.com/ggml-org/whisper.cpp/releases
```

Extract the release under:

```text
%LOCALAPPDATA%\tts\tools\whisper.cpp\v1.8.6\Release
```

Expected executable for that provider:

```text
whisper-cli.exe
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

## Solution Projects

The managed solution has five projects:

```text
src/Tts.Core           Shared configuration, paths, audio/transcription providers, timing primitives, native engine wrappers, and generic media preparation.
src/Tts.App            WPF tray app, settings UI, hotkeys, session orchestration, clipboard/paste output.
src/Tts.Cli            Focused transcription CLI for local audio files and benchmarks.
src/YtScribe.Core      YouTube metadata/caption/audio ingestion and transcript artifact writing.
src/YtScribe.Cli       `yt-scribe` transcript-export command-line app.
```

Native/runtime assets such as `tts-whisper-interop.dll` and CUDA runtime DLLs are copied by `src/Tts.Core/Tts.Core.csproj` into consuming outputs when present.

## Build Managed Projects

Build the app from the repository root:

```powershell
dotnet build Tts.sln
```

If the tray app is already running and locking `Tts.App.exe`, use:

```powershell
dotnet build Tts.sln /p:UseAppHost=false
```

When the native DLL exists, `src/Tts.Core/Tts.Core.csproj` copies it into the app and CLI output. For CUDA local runs it also copies these CUDA runtime DLLs from CUDA Toolkit 12.9 when present:

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

## CLI Transcription

Build the CLI through the solution or directly:

```powershell
dotnet build src/Tts.Cli/Tts.Cli.csproj -c Release
```

Transcribe one supported WAV file and write transcript text to standard output:

```powershell
src\Tts.Cli\bin\Release\net8.0-windows\Tts.Cli.exe transcribe `
    --audio src\native\Tts.WhisperInterop\third_party\whisper.cpp\samples\jfk.wav `
    --provider whisper-cpp-native-local `
    --model tiny-en `
    --language en `
    --metrics-output "$env:TEMP\tts-cli-metrics.json"
```

The CLI currently supports 16 kHz mono PCM 16-bit WAV input. LibriSpeech benchmark prep converts source FLAC files to this format. Use `--config <path>` to read an app-compatible settings JSON file, or omit it to use `%AppData%/SpeechToTextDaemon/config.json`. Common overrides are `--provider`, `--model`, `--language`, `--timeout-seconds`, and repeated `--setting key=value`.

`--metrics-output` writes machine-readable timing/status JSON for scripts. Transcript text remains on stdout; sanitized errors go to stderr. The metrics include `transcriptionRealTimeFactor` and `transcriptionAudioSecondsPerSecond`; lower real-time factor is better, while higher audio-seconds-per-second is faster. For example, `0.25` RTF is equivalent to `4x` realtime transcription speed.

`Tts.Cli` is intentionally transcription-only. YouTube ingestion lives in `yt-scribe`.

## yt-scribe YouTube Transcript Export

`yt-scribe` exports YouTube video metadata and transcript artifacts to a directory. It prefers downloadable captions/subtitles and falls back to downloading audio with `yt-dlp`, converting it with `ffmpeg`, and transcribing through the same local providers used by `Tts.Core`.

Prerequisites:

```powershell
yt-dlp --version
ffmpeg -version
```

Build through the solution or directly:

```powershell
dotnet build src/YtScribe.Cli/YtScribe.Cli.csproj -c Release
```

Export a single video:

```powershell
src\YtScribe.Cli\bin\Release\net8.0-windows\yt-scribe.exe export `
    --url "https://www.youtube.com/watch?v=<video-id>" `
    --output-dir "$env:TEMP\yt-scribe" `
    --caption-language "en.*" `
    --metrics-output "$env:TEMP\yt-scribe-metrics.json"
```

Force local audio transcription instead of captions:

```powershell
src\YtScribe.Cli\bin\Release\net8.0-windows\yt-scribe.exe export `
    --url "https://www.youtube.com/watch?v=<video-id>" `
    --output-dir "$env:TEMP\yt-scribe" `
    --force-audio `
    --provider whisper-cpp-native-local `
    --model tiny-en `
    --language en `
    --overwrite
```

For each video, `yt-scribe` writes one subdirectory named after the YouTube video ID. The current artifact contract is:

```text
metadata.json      Video metadata, transcript origin, provider/settings summary when transcribed, and sanitized timings.
transcript.json    Normalized full transcript. Caption exports include timestamped segments; audio fallback is untimed until providers expose segments.
transcript.vtt     Original caption VTT when captions were available.
transcript.txt     Plain text convenience output unless `--no-transcript-text` is used.
```

Transcript content is written only to explicit transcript artifacts. Metrics and errors are sanitized and should not contain raw transcript text, raw captions, raw stderr, secrets, or endpoint URLs.

## LibriSpeech Benchmark Scripts

The benchmark flow is PowerShell-based and drives the focused CLI one audio file at a time.

Prerequisite for dataset prep:

```powershell
ffmpeg -version
ffprobe -version
```

Prepare a small smoke subset first:

```powershell
.\scripts\benchmarks\Prepare-LibriSpeechTestClean.ps1 -Count 5
```

Prepare the full 1000-file benchmark set:

```powershell
.\scripts\benchmarks\Prepare-LibriSpeechTestClean.ps1 -Count 1000
```

By default, the script stores generated dataset files under:

```text
%LOCALAPPDATA%\tts\datasets\librispeech-test-clean
```

Run a single-provider benchmark:

```powershell
.\scripts\benchmarks\Run-TranscriptionBenchmark.ps1 `
    -BuildCli `
    -Provider whisper-cpp-native-local `
    -Model tiny-en `
    -Language en `
    -Count 1000
```

The benchmark runner uses `Tts.Cli transcribe-batch` by default. That keeps one CLI process and provider instance alive for each provider/settings run, transcribes the first file once as an unmeasured warmup, then transcribes the same first file again as measured row 1. This avoids charging repeated model-load time to providers that can stay warm, such as `whisper-cpp-native-local`.

You can run batch transcription directly:

```powershell
src\Tts.Cli\bin\Release\net8.0-windows\Tts.Cli.exe transcribe-batch `
    --manifest "$env:LOCALAPPDATA\tts\datasets\librispeech-test-clean\manifest.json" `
    --provider whisper-cpp-native-local `
    --model small-en `
    --language en `
    --count 1000 `
    --warmup-first-file `
    --output-csv "$env:TEMP\tts-batch.csv" `
    --output-json "$env:TEMP\tts-batch.json"
```

Run a provider matrix:

```powershell
.\scripts\benchmarks\Run-TranscriptionBenchmark.ps1 `
    -BuildCli `
    -MatrixPath .\scripts\benchmarks\provider-matrix.example.json `
    -Count 1000
```

Benchmark output CSV/JSON files are written by default under:

```text
%AppData%\SpeechToTextDaemon\logs\benchmarks
```

Use `-ColdPerFile` with `Run-TranscriptionBenchmark.ps1` to force the older cold-start mode that invokes `Tts.Cli.exe` once per file. Cold mode includes process startup, service-provider creation, and model/provider initialization in every row.

Benchmark rows also include scoring columns derived from the LibriSpeech expected transcript: `wordErrorRate`, `wordAccuracy`, `wordErrors`, and `exactNormalizedMatch`. Lower `wordErrorRate` is better; higher `wordAccuracy` is better. These scores are punctuation/case-insensitive word-level comparisons and are meant for benchmark triage, not a full ASR evaluation suite.

In the settings window, choose a transcription provider:

```text
whisper.cpp local         # whisper-cli.exe per session, compatibility path
whisper.cpp native local  # tts-whisper-interop.dll, keeps model warm in-process
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

## Clean

Remove generated build outputs:

```powershell
Remove-Item build -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item src/Tts.App/bin, src/Tts.App/obj -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item src/Tts.Core/bin, src/Tts.Core/obj -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item src/Tts.Cli/bin, src/Tts.Cli/obj -Recurse -Force -ErrorAction SilentlyContinue
```

Then rebuild native and app outputs as needed.
