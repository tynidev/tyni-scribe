# Speech-to-Text Daemon Build Steps

This checklist is derived from [plan.md](plan.md) and informed by [stack.md](stack.md). Completion status reflects the current repository state as of 2026-06-12.

## Status Legend

- `Complete`: Done in the repo.
- `In Progress`: Started but not finished.
- `Not Started`: Planned, but no implementation exists yet.
- `Blocked`: Cannot continue until a dependency or decision is resolved.
- `Deferred`: Intentionally out of first-build scope.

## Current Summary

| Area | Status | Complete? |
| --- | --- | --- |
| Product plan | `Complete` | Yes |
| Tech stack recommendation | `Complete` | Yes |
| Prerequisites and environment checks | `Complete` | Yes |
| Application source code | `In Progress` | No |
| First build implementation | `In Progress` | No |

## Prerequisites And Environment Checks

Complete these checks before starting application implementation.

| Check | Required For | Status | Complete? | How To Verify |
| --- | --- | --- | --- | --- |
| Windows development machine | WPF, WASAPI, global hotkeys, tray integration | `Complete` | Yes | Confirm the app is being built and run on Windows. |
| .NET SDK 8 LTS or .NET 9 | Building and running the C# app | `Complete` | Yes | Installed and verified .NET SDK 8.0.422 with `dotnet --version`. |
| WPF-capable build tooling | Windows desktop app build | `Complete` | Yes | Verified `dotnet new wpf --help` and built a temporary .NET 8 WPF project. |
| NuGet package restore | CommunityToolkit.Mvvm, NAudio, host packages, logging packages | `Complete` | Yes | Restored planned packages in a temporary WPF project. |
| Windows SDK and Win32 interop support | RegisterHotKey, clipboard, tray integration | `Complete` | Yes | Built a temporary WPF project and verified Win32 clipboard open/close through P/Invoke. |
| Microphone hardware and Windows input permission | Audio recording and level meter | `Complete` | Yes | Windows microphone consent is `Allow`; an input endpoint is present. |
| WASAPI shared capture availability | First audio backend | `Complete` | Yes | NAudio enumerated active capture endpoints through MMDevice/WASAPI. |
| System temp/AppData write access | Config, logs, and temporary WAV files | `Complete` | Yes | Created and deleted test files under `%AppData%` and the OS temp directory. |
| Clipboard access | Clipboard output provider | `Complete` | Yes | Verified non-destructive Win32 `OpenClipboard`/`CloseClipboard` access. |
| whisper.cpp executable | First local batch transcription provider | `Complete` | Yes | Installed v1.8.6 at `%LOCALAPPDATA%/tts/tools/whisper.cpp/v1.8.6/Release/whisper-cli.exe` and verified `--help`. |
| Whisper model files | First local batch transcription provider | `Complete` | Yes | Installed `ggml-tiny.en.bin`, `ggml-base.en.bin`, `ggml-small.en.bin`, and `ggml-large-v3-turbo.bin` under `%LOCALAPPDATA%/tts/models/whisper`; tiny completed a CLI smoke test. |
| Native whisper.cpp provider | Warm local batch transcription provider | `In Progress` | No | Built `tts-whisper-interop.dll` from pinned `ggml-org/whisper.cpp` submodule source for `whisper-cpp-native-local`; full microphone-session validation and packaging polish remain. |
| CUDA Toolkit 12.9 | GPU-backed native whisper.cpp provider | `Complete` | Yes | Installed `Nvidia.CUDA` 12.9 with winget; verified `nvcc` 12.9.86 and rebuilt `tts-whisper-interop.dll` with `GGML_CUDA=ON`. |
| CLI transcription and benchmark harness | Provider timing comparisons | `In Progress` | No | Split shared provider logic into `Tts.Core`, added focused single-file `Tts.Cli transcribe`, and added PowerShell LibriSpeech prep/benchmark scripts. Full 1000-file dataset benchmark runs remain to be executed. |
| yt-scribe YouTube transcript exporter | YouTube metadata/transcript artifacts | `In Progress` | No | Added separate `YtScribe.Core` and `YtScribe.Cli` projects so YouTube ingestion stays out of `Tts.Cli`; first slice exports metadata JSON plus transcript JSON/VTT/TXT, preferring captions and falling back to local audio transcription through `Tts.Core`. |
| Remote streaming provider credentials or mock provider decision | First streaming transcription provider | `Complete` | Yes | Use a local mock streaming provider for v1 so no remote credentials are required yet. |
| Serilog file sink package | Sanitized rolling file logs | `Complete` | Yes | Restored `Serilog` and `Serilog.Sinks.File` in the temporary WPF project. |
| Packaging tool decision | Single-file publish | `Complete` | Yes | Use a self-contained single-file Windows publish instead of an installer/MSIX for the first distribution path. |
| Native C++ build toolchain | Future ASIO/native model paths | `Complete` | Yes | Verified VS 2022 MSVC 19.35, CMake 4.3.3, and Ninja 1.13.2 by building and running a C++20 CMake/Ninja probe. |

## First Build Checklist

| Step | Work | Status | Complete? | Depends On |
| --- | --- | --- | --- | --- |
| 1 | Build app shell, tray lifecycle, and settings storage | `Complete` | Yes | Product and stack plan |
| 2 | Implement explicit state machine, global hotkeys, and cancellation | `Complete` | Yes | Step 1 |
| 3 | Implement microphone selection, recording, and level meter | `Complete` | Yes | Steps 1-2 |
| 4 | Add completed-file audio processing provider pipeline | `Complete` | Yes | Step 3 |
| 5 | Implement clipboard output provider | `Complete` | Yes | Step 2 |
| 6 | Implement first batch transcription provider | `Complete` | Yes | Steps 3-4 |
| 7 | Implement first streaming transcription provider with final-text output only | `Not Started` | No | Steps 2-3, provider interfaces |
| 8 | Add multiple built-in transcription provider selection | `In Progress` | No | Steps 6-7 |
| 9 | Add optional text cleanup provider with raw-transcript fallback | `Not Started` | No | Final transcript flow |
| 10 | Add temp-file cleanup and sanitized logging | `In Progress` | No | Steps 2-9 |
| 11 | Polish errors, tray status, and settings validation | `Not Started` | No | Steps 1-10 |

## Step Details

### 1. App Shell, Tray Lifecycle, And Settings Storage

Status: `Complete`

Complete? Yes

- Created a C#/.NET 8 WPF Windows utility under `src/Tts.App` with a solution file at the repository root.
- Split shared non-UI configuration, paths, audio/provider abstractions, provider implementations, timing primitives, and native engine wrappers into `src/Tts.Core` so the WPF app and CLI can consume the same provider layer.
- Added `src/Tts.Cli` as a focused single-file transcription CLI that writes transcript text to stdout and can write a metrics JSON sidecar for benchmark scripts.
- Added separate `src/YtScribe.Core` and `src/YtScribe.Cli` projects for YouTube transcript export. `Tts.Cli` remains transcription-only; `yt-scribe` owns YouTube metadata/caption/audio ingestion and writes explicit transcript artifacts to an output directory.
- Added hosted startup services and dependency injection through `Microsoft.Extensions.Hosting`.
- Added tray lifecycle behavior: open settings, minimize or close settings to tray, and quit from the tray menu.
- Added typed JSON configuration stored under `%AppData%/SpeechToTextDaemon/config.json`.
- Included a config version field, without migration machinery until version 2 exists.
- Kept startup lightweight by loading settings through a hosted warmup service before showing the settings window.
- Service-layer infrastructure is organized by responsibility: orchestration/session state under `Services/Orchestration`, hotkeys under `Services/Hotkeys`, and provider-setting descriptor primitives under `Services/ProviderSettings`.
- Shared provider registrations live behind `AddTtsCoreServices()` in `Tts.Core`; WPF-only registrations remain in `App.xaml.cs`.

### 2. State Machine, Hotkeys, And Cancellation

Status: `Complete`

Complete? Yes

- Added the explicit states: `Idle`, `Recording`, `Processing`, `Outputting`, and `Error`.
- Added a session orchestrator as the only owner of app state transitions.
- Future workflow work should record sanitized stage timings through the orchestrator, using the names from `plan.md`.
- Registered global Start/Stop and Cancel hotkeys through Win32 `RegisterHotKey` on the WPF UI thread.
- Start/Stop moves from `Idle` to `Recording`, and from `Recording` to `Processing`.
- Start/Stop during `Processing` or `Outputting` reports the app as busy.
- Cancel stops active `Recording` or `Processing` work and returns to `Idle`.
- The orchestrator snapshots provider, microphone, output, and cleanup settings at session start so later changes apply to the next session.
- Stopping a recording transitions through `Processing`, runs completed-file audio processing, runs batch transcription, and sends non-empty final text to enabled output providers.
- Future cleanup and streaming work should attach to `SessionOrchestrator` rather than changing app state directly.

### 3. Microphone Selection, Recording, And Level Meter

Status: `Complete`

Complete? Yes

- Added NAudio as the first audio dependency and implemented WASAPI shared-mode capture through `WasapiAudioCaptureService`.
- Added microphone endpoint enumeration with a settings-window microphone selector and refresh action.
- Preserved the nullable selected microphone setting: an empty value means use the current Windows default input endpoint, and a device ID pins recording to that endpoint.
- Connected `SessionOrchestrator` to real audio capture for `Idle` -> `Recording`, WAV finalization for `Recording` -> `Processing`, and capture cancellation for discard paths.
- Wrote active-session recordings to app-specific temp WAV files under `%TEMP%/SpeechToTextDaemon/audio`.
- Deletes captured temp WAV files after success, cancellation, and recoverable failure paths.
- Added an explicit settings-window input level meter using the selected microphone; preview capture starts only when the user starts the meter, while active recordings also publish level data.
- Added `AudioChunkCaptured` events carrying copied PCM chunks plus capture format metadata so Step 7 streaming providers can attach without changing the WASAPI callback path.
- Future capture finalization should be timed as `capture-finalization`: Stop requested while recording through completed WAV flush/close and readiness for audio processing.

Future-step notes:

- Step 7 streaming transcription can subscribe to `IAudioCaptureService.AudioChunkCaptured` during recording; the event is raised only for recording sessions, not idle level-meter preview sessions.
- Step 10 stale temp-file cleanup should target `%TEMP%/SpeechToTextDaemon/audio` for orphaned `recording-*.wav` files left by crashes or forced shutdowns.
- Idle level metering is intentionally explicit so the app does not keep the microphone open continuously while idle.

### 4. Completed-File Audio Processing Provider Pipeline

Status: `Complete`

Complete? Yes

- Defined a small completed-file audio processing provider interface.
- Added a no-op audio processor with provider ID `noop` that returns the original completed audio file unchanged.
- The no-op audio processor implementation lives under its own provider folder.
- Added selected audio processor settings with `noop` as the default.
- Snapshotted the selected audio processor at recording start so changes apply to the next session.
- Timed this stage as `audio-processing`, including the no-op processor.
- The orchestrator now passes the processed audio result to downstream batch transcription.
- The processing result tracks whether the returned file is the original capture or a processor-created file.
- Processor-created temp files are tracked for end-of-session cleanup without deleting files owned elsewhere.
- Streaming chunk handling remains separate; first-build audio processing is completed-file only.
- Processing failures are treated as recoverable session failures and return through the existing `Error` state.
- Privacy rules are preserved: the provider boundary does not log raw audio, file contents, or user speech.

Future-step notes:

- Future non-no-op processors should write outputs to app-owned temp locations and return `IsOriginalFile: false` so cleanup can delete them safely.
- Later provider selection should expose audio processors separately from transcription providers so users can combine a processor with any compatible batch transcription provider.

### 5. Clipboard And Paste Output Providers

Status: `Complete`

Complete? Yes

- Defined a small `IOutputProvider` interface with provider ID, display name, and final-text write operation.
- Added `ClipboardOutputProvider` as the first output provider with ID `clipboard`.
- Added `PasteOutputProvider` with ID `paste`; it sets the clipboard to the final transcript and sends Ctrl+V to the active window.
- Clipboard and paste output implementations live under their own provider folders.
- Registered clipboard output through dependency injection as a safe output provider.
- Registered paste output through dependency injection as an opt-in output provider.
- Clipboard writes marshal to the WPF dispatcher so hotkey or tray-triggered sessions can safely write to the Windows clipboard.
- Added `Outputting` state handling for final-text output and timed clipboard writes as `clipboard-output` when the provider runs.
- Kept output providers final-text only; no partial streaming text is sent to output providers.
- Clipboard failures move the session to `Error`, keep the final transcript in memory for retry or dismissal, and avoid logging transcript text.
- Added tray actions to retry or dismiss pending output after an output failure.
- Left room for additional output destinations through the output provider collection without changing clipboard-specific orchestration.

Future-step notes:

- Batch transcription now calls the existing final output path with a real final transcript.
- Output failure handling retains only the final transcript text for retry; captured temp audio is deleted once it is no longer needed for transcription/output retry.

### 6. First Batch Transcription Provider

Status: `Complete`

Complete? Yes

- Defined a batch transcription provider interface.
- Added provider metadata: `id`, `displayName`, `transcriptionMode`, and `requiresEndpoint`.
- Implemented `WhisperCppBatchTranscriptionProvider` with provider ID `whisper-cpp-local`.
- Added user-facing whisper.cpp provider settings for model selection, language, and timeout seconds.
- Added local model options for `Tiny English (fastest)`, `Base English (balanced)`, `Small English (better accuracy)`, and `Large v3 Turbo (best local quality)`.
- The normal settings UI does not expose the whisper.cpp executable path or raw model file path; those are internal provider/deployment details.
- The provider resolves the local engine from `%LOCALAPPDATA%/tts/tools/whisper.cpp/v1.8.6/Release/whisper-cli.exe` unless an advanced config override is set.
- The provider maps user-facing model IDs to app-managed files under `%LOCALAPPDATA%/tts/models/whisper` unless an advanced config override is set: `tiny-en` -> `ggml-tiny.en.bin`, `base-en` -> `ggml-base.en.bin`, `small-en` -> `ggml-small.en.bin`, and `large-v3-turbo` -> `ggml-large-v3-turbo.bin`.
- Unsupported or stale saved model IDs normalize back to `tiny-en`.
- The adapter invokes `whisper-cli.exe` with the resolved model, processed WAV file, language, no timestamps, and no extra prints.
- The adapter captures stdout as the final transcript and does not log transcript text or raw audio.
- The orchestrator passes the processed audio file path to the selected batch provider.
- Provider work is timed as `transcription`.
- Final transcript text is returned to the orchestrator and sent through the existing output provider pipeline.
- Empty or silent transcription returns to `Idle` without writing empty text to output providers.
- Cancellation and timeout kill the whisper.cpp process tree and return through the session cancellation or recoverable failure path.
- Captured and processor-created temp files are deleted after success, cancellation, and recoverable failure.
- Extracted shared whisper.cpp runtime path resolution so the CLI provider and future native provider use the same model ID mapping and advanced model path override behavior.
- Added native local whisper.cpp provider support with provider ID `whisper-cpp-native-local`.
- Added the native C ABI contract and implementation under `src/native/Tts.WhisperInterop`, pinned upstream `ggml-org/whisper.cpp` as a submodule under `src/native/Tts.WhisperInterop/third_party/whisper.cpp`, and built `build/native/Tts.WhisperInterop/tts-whisper-interop.dll`.
- Rebuilt `tts-whisper-interop.dll` with `GGML_CUDA=ON` against CUDA Toolkit 12.9 for the RTX 4090.
- The app project copies `tts-whisper-interop.dll` into the WPF output directory when the native DLL has been built, and copies CUDA runtime dependencies `cudart64_12.dll`, `cublas64_12.dll`, and `cublasLt64_12.dll` when CUDA Toolkit 12.9 is installed.
- Direct native smoke test passed against the tiny model using `tts_whisper_engine_create`, `tts_whisper_engine_load_model`, `tts_whisper_engine_transcribe_wav`, and `tts_whisper_engine_dispose`; whisper.cpp reported `ggml_cuda_init`, `NVIDIA GeForce RTX 4090`, and `using CUDA0 backend`.
- The existing CLI provider remains usable as the compatibility path.

Future-step notes:

- Step 8 should replace the free-text provider ID fields with provider selection controls backed by registered provider metadata.
- Step 8 should keep provider executable paths and raw model file paths out of the normal UI; expose friendly provider settings such as model/profile and language instead.
- The native provider should become the default candidate only after full microphone-session validation, model switch behavior, cancellation, timeout, and packaging paths are proven. Until then, `whisper-cpp-local` remains the compatibility path.
- The native wrapper should return only sanitized errors and must not include temp audio paths, transcript text, raw stderr, endpoint secrets, raw endpoint URLs, or audio content in diagnostics.
- Keep `whisper-cpp-native-local` as the default provider, with `whisper-cpp-local` remaining available as the compatibility fallback.
- Step 9 text cleanup should run after this batch transcription result and before the output provider pipeline.
- Step 10 sanitized file logging should log provider IDs and sanitized stage outcomes only; do not add whisper stdout, transcript text, raw stderr, or temp file paths to logs.

### 7. First Streaming Transcription Provider

Status: `Not Started`

Complete? No

- Define the streaming transcription provider interface.
- Define a streaming session abstraction with `acceptAudio`, `stop`, and `cancel` operations.
- Add one initial streaming provider or mock provider.
- Feed audio chunks during recording.
- Return final transcript text only after recording stops.
- Do not output partial streaming text in the first build.

### 8. Built-In Provider Selection

Status: `In Progress`

Complete? No

- Registered local batch transcription providers: `whisper-cpp-local` for the current CLI adapter and `whisper-cpp-native-local` for the in-process native DLL wrapper.
- `whisper-cpp-native-local` is the default transcription provider for new configs, using the Large v3 Turbo model by default.
- `paste` is the default output provider for new configs, with `clipboard` still available as the safer manual-paste option.
- Replaced the settings-window free-text transcription provider field with a dropdown populated from registered provider metadata.
- The dropdown stores the selected provider ID in the existing config field and falls back to `whisper-cpp-local` when saved settings reference an unavailable provider.
- Transcriber implementation files now live under provider folders: `WhisperCpp` and `WhisperNative`.
- Shared provider-setting descriptor primitives now live under `Services/ProviderSettings`, while provider-specific parser/default classes stay inside provider folders.
- Transcription provider settings are stored per provider ID, so `whisper-cpp-local` and `whisper-cpp-native-local` each keep independent model, language, timeout, and provider-specific options.
- Audio processing and output provider settings also use provider-ID keyed storage; the current built-in audio/output providers have empty setting dictionaries, but future providers can add descriptors without changing the config shape.
- Provider-specific settings now react to the selected provider; whisper.cpp-style model, language, and timeout controls are backed by independent provider-owned settings for the CLI and native local providers.

Remaining work:

- Add streaming provider registration and selection once Step 7 exists.
- Show only metadata needed for first routing and settings decisions.
- Clearly label providers that send audio or text to a remote endpoint.
- Apply provider changes only when the app is idle or to the next session.

### 9. Optional Text Cleanup Provider

Status: `Not Started`

Complete? No

- Add a text cleanup provider interface.
- Start with a no-op cleanup provider.
- Add cleanup enable/disable setting.
- Add cleanup prompt setting.
- Run cleanup only after recording stops and a final transcript exists.
- Time this LLM/no-op text transformation stage as `text-cleanup`.
- Use the raw transcript if cleanup fails or times out.
- Add tray action for enabling or disabling cleanup.

### 10. Temp-File Cleanup And Sanitized Logging

Status: `In Progress`

Complete? No

- Added app log path support for `%AppData%/SpeechToTextDaemon/logs`.
- Added `CsvSessionTimingLogWriter` to create and append `%AppData%/SpeechToTextDaemon/logs/timings.csv`.
- Added one timing CSV row per recording session when the session ends in success, cancellation, or recoverable failure.
- Captured current available timing data: `totalSessionMs`, `recordingDurationMs`, `captureFinalizationMs`, `audioProcessingMs`, `transcriptionMs`, and `clipboardOutputMs` when those stages run.
- Captured sanitized session metadata currently available: session ID, UTC start/completion timestamps, status, sanitized error category, microphone device ID, transcription provider ID, cleanup provider ID, and output provider IDs.
- Added timing schema version 2 with a `providerSettingsJson` column containing a compact sanitized provider settings snapshot for performance analysis.
- `providerSettingsJson` records safe settings such as transcription model ID, language, compute type, timeout seconds, audio/output provider IDs, non-sensitive settings-present indicators, and path-override booleans; it does not record raw paths, transcript text, cleanup prompt text, secrets, endpoint URLs, or audio content.
- Left not-yet-implemented stage duration columns empty: `textCleanupMs` and `tempFileCleanupMs`.
- Run stale temp-file cleanup on startup.
- Delete captured and processed temp audio after success, cancellation, and failure.
- Time end-of-session deletion as `temp-file-cleanup`.
- Retry cleanup on next app start when deletion fails.
- Add Serilog rolling file logs.
- Log app version, OS version, state transitions, provider IDs, endpoint type, total session duration, stage timings, and sanitized errors.
- Use the stage names from `plan.md`: `capture-finalization`, `audio-processing`, `transcription`, `text-cleanup`, `clipboard-output`, and `temp-file-cleanup`.
- Write stage timing rows to `%AppData%/SpeechToTextDaemon/logs/timings.csv`.
- Append exactly one CSV row per recording session when the session returns to `Idle`, including successful, canceled, and recoverable failure sessions.
- Create the timing log directory and CSV header when the file does not exist.
- Leave skipped stage duration fields empty instead of writing `0`.
- Leave provider ID fields empty when that provider category was disabled or not used, such as `cleanupProviderId` when text cleanup is disabled.
- Do not write temp file paths, transcript text, endpoint secrets, raw endpoint URLs, or audio content to the timing CSV.
- Do not log raw audio or transcript text by default.
- Avoid logging secrets.

### 11. Error Handling, Tray Status, And Settings Polish

Status: `Not Started`

Complete? No

- Show recoverable microphone, audio processing, provider, hotkey, transcription, cleanup, clipboard, and temp-file errors.
- Return to `Idle` after recoverable failures when appropriate.
- Show tray status for `Idle`, `Recording`, `Processing`, `Outputting`, and `Error`.
- Validate hotkey conflicts and keep previous bindings when new bindings fail.
- Show a quiet notification for empty recordings.
- Keep diagnostics simple for the first build.
- Polish settings validation and app lifecycle behavior.

## First Build Scope Coverage

| Requirement | Covered By | Status | Complete? |
| --- | --- | --- | --- |
| Windows background app with tray lifecycle | Step 1 | `Complete` | Yes |
| Settings window | Steps 1, 8, 9, 11 | `In Progress` | No |
| Global Start/Stop hotkey | Step 2 | `Complete` | Yes |
| Global Cancel hotkey | Step 2 | `Complete` | Yes |
| Microphone selection | Step 3 | `Complete` | Yes |
| Microphone level meter | Step 3 | `Complete` | Yes |
| Completed-file audio processing provider pipeline | Step 4 | `Complete` | Yes |
| Explicit state machine | Step 2 | `Complete` | Yes |
| At least one batch transcription provider | Step 6 | `Complete` | Yes |
| At least one streaming transcription provider | Step 7 | `Not Started` | No |
| Multiple built-in transcription provider selection | Step 8 | `In Progress` | No |
| Optional text cleanup provider | Step 9 | `Not Started` | No |
| Clipboard output provider | Step 5 | `Complete` | Yes |
| Paste output by setting clipboard and sending Ctrl+V | Step 5 | `Complete` | Yes |
| Temp file cleanup | Step 10 | `In Progress` | No |
| Timing CSV logging | Step 10 | `Complete` | Yes |
| Sanitized file logging | Step 10 | `In Progress` | No |

## Deferred Items

These are intentionally out of first-build scope unless the plan changes.

| Future Work | Status | Complete? |
| --- | --- | --- |
| Live cursor insertion | `Deferred` | No |
| File output provider | `Deferred` | No |
| Webhook output provider | `Deferred` | No |
| Active-window typing output provider | `Deferred` | No |
| Streaming output while recording | `Deferred` | No |
| Dynamic provider plugin loading | `Deferred` | No |
| Full diagnostics UI | `Deferred` | No |
| Import/export settings | `Deferred` | No |
| Advanced provider capability metadata | `Deferred` | No |
| WASAPI exclusive backend | `Deferred` | No |
| Native C++ audio bridge | `Deferred` | No |
| ASIO backend through native C++ engine | `Deferred` | No |
| Native whisper.cpp or ONNX Runtime model wrapper | `Deferred` | No |

## Stack Notes For Implementation

- Build the first version as a C#/.NET WPF Windows utility.
- Use MVVM with CommunityToolkit.Mvvm.
- Use Microsoft.Extensions.Hosting, DependencyInjection, and Options for orchestration and configuration.
- Use NAudio with WASAPI shared capture first.
- Use Win32 RegisterHotKey through P/Invoke or a small helper package for global hotkeys.
- Use direct WPF/Win32 clipboard integration or TextCopy for clipboard output.
- Use System.Text.Json for typed configuration.
- Use Serilog with rolling sanitized file logs.
- Use whisper.cpp as the first local batch transcription path.
- Publish first builds as self-contained single-file Windows binaries, not installer/MSIX packages.
- Use VS 2022 MSVC with CMake and Ninja for future native ASIO or native model projects.
- Keep C++ reserved for later critical audio paths, ASIO support, or native model bindings.