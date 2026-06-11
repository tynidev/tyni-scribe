# Speech-to-Text Daemon Build Steps

This checklist is derived from [plan.md](plan.md) and informed by [stack.md](stack.md). Completion status reflects the current repository state as of 2026-06-11.

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
| Whisper model file | First local batch transcription provider | `Complete` | Yes | Installed `ggml-tiny.en.bin` at `%LOCALAPPDATA%/tts/models/whisper/ggml-tiny.en.bin` and completed a CLI smoke test. |
| Remote streaming provider credentials or mock provider decision | First streaming transcription provider | `Complete` | Yes | Use a local mock streaming provider for v1 so no remote credentials are required yet. |
| Serilog file sink package | Sanitized rolling file logs | `Complete` | Yes | Restored `Serilog` and `Serilog.Sinks.File` in the temporary WPF project. |
| Packaging tool decision | Single-file publish | `Complete` | Yes | Use a self-contained single-file Windows publish instead of an installer/MSIX for the first distribution path. |
| Native C++ build toolchain | Future ASIO/native model paths | `Complete` | Yes | Verified VS 2022 MSVC 19.35, CMake 4.3.3, and Ninja 1.13.2 by building and running a C++20 CMake/Ninja probe. |

## First Build Checklist

| Step | Work | Status | Complete? | Depends On |
| --- | --- | --- | --- | --- |
| 1 | Build app shell, tray lifecycle, and settings storage | `Complete` | Yes | Product and stack plan |
| 2 | Implement explicit state machine, global hotkeys, and cancellation | `Not Started` | No | Step 1 |
| 3 | Implement microphone selection, recording, and level meter | `Not Started` | No | Steps 1-2 |
| 4 | Implement clipboard output provider | `Not Started` | No | Step 2 |
| 5 | Implement first batch transcription provider | `Not Started` | No | Steps 2-3 |
| 6 | Implement first streaming transcription provider with final-text output only | `Not Started` | No | Steps 2-3, provider interfaces |
| 7 | Add multiple built-in transcription provider selection | `Not Started` | No | Steps 5-6 |
| 8 | Add optional text cleanup provider with raw-transcript fallback | `Not Started` | No | Final transcript flow |
| 9 | Add temp-file cleanup and sanitized logging | `Not Started` | No | Steps 2-8 |
| 10 | Polish errors, tray status, and settings validation | `Not Started` | No | Steps 1-9 |

## Step Details

### 1. App Shell, Tray Lifecycle, And Settings Storage

Status: `Complete`

Complete? Yes

- Created a C#/.NET 8 WPF Windows utility under `src/Tts.App` with a solution file at the repository root.
- Added hosted startup services and dependency injection through `Microsoft.Extensions.Hosting`.
- Added tray lifecycle behavior: open settings, minimize or close settings to tray, and quit from the tray menu.
- Added typed JSON configuration stored under `%AppData%/SpeechToTextDaemon/config.json`.
- Included a config version field, without migration machinery until version 2 exists.
- Kept startup lightweight by loading settings through a hosted warmup service before showing the settings window.

### 2. State Machine, Hotkeys, And Cancellation

Status: `Not Started`

Complete? No

- Implement the explicit states: `Idle`, `Recording`, `Processing`, `Outputting`, and `Error`.
- Make the session orchestrator the only owner of app state transitions.
- Register global Start/Stop and Cancel hotkeys.
- Start recording from `Idle` when Start/Stop is pressed.
- Stop recording and begin processing from `Recording` when Start/Stop is pressed.
- Ignore or notify as busy when Start/Stop is pressed during `Processing` or `Outputting`.
- Cancel active recording or processing when Cancel is pressed.
- Keep provider, microphone, and output setting changes from affecting active sessions.

### 3. Microphone Selection, Recording, And Level Meter

Status: `Not Started`

Complete? No

- Use WASAPI shared mode through NAudio for the first capture backend.
- Enumerate microphone devices.
- Store and apply the selected microphone.
- Capture audio for the active session.
- Write audio to a temporary WAV file for batch providers.
- Feed audio chunks to streaming provider sessions.
- Provide microphone level data for the settings UI.
- Store temp audio in the OS temp or app data location.

### 4. Clipboard Output Provider

Status: `Not Started`

Complete? No

- Define a small output provider interface.
- Add clipboard output as the first provider.
- Send only final text to output providers.
- Handle clipboard failures by notifying the user and keeping the transcript available for retry or dismissal.
- Leave room for additional output destinations without changing the orchestrator.

### 5. First Batch Transcription Provider

Status: `Not Started`

Complete? No

- Define the batch transcription provider interface.
- Add provider metadata: `id`, `displayName`, `transcriptionMode`, and `requiresEndpoint`.
- Implement an initial batch provider, preferably a whisper.cpp adapter.
- Pass the completed temp audio file to the provider.
- Return final transcript text to the orchestrator.
- Support cancellation and timeout behavior.
- Delete temp files after success, cancellation, or failure.

### 6. First Streaming Transcription Provider

Status: `Not Started`

Complete? No

- Define the streaming transcription provider interface.
- Define a streaming session abstraction with `acceptAudio`, `stop`, and `cancel` operations.
- Add one initial streaming provider or mock provider.
- Feed audio chunks during recording.
- Return final transcript text only after recording stops.
- Do not output partial streaming text in the first build.

### 7. Built-In Provider Selection

Status: `Not Started`

Complete? No

- Add multiple built-in transcription provider registration.
- Add transcription provider selection in settings.
- Show only metadata needed for first routing and settings decisions.
- Support batch and streaming providers behind small interfaces.
- Clearly label providers that send audio or text to a remote endpoint.
- Apply provider changes only when the app is idle or to the next session.

### 8. Optional Text Cleanup Provider

Status: `Not Started`

Complete? No

- Add a text cleanup provider interface.
- Start with a no-op cleanup provider.
- Add cleanup enable/disable setting.
- Add cleanup prompt setting.
- Run cleanup only after recording stops and a final transcript exists.
- Use the raw transcript if cleanup fails or times out.
- Add tray action for enabling or disabling cleanup.

### 9. Temp-File Cleanup And Sanitized Logging

Status: `Not Started`

Complete? No

- Run stale temp-file cleanup on startup.
- Delete temp audio after success, cancellation, and failure.
- Retry cleanup on next app start when deletion fails.
- Add Serilog rolling file logs.
- Log app version, OS version, state transitions, provider IDs, endpoint type, durations, and sanitized errors.
- Do not log raw audio or transcript text by default.
- Avoid logging secrets.

### 10. Error Handling, Tray Status, And Settings Polish

Status: `Not Started`

Complete? No

- Show recoverable microphone, provider, hotkey, transcription, cleanup, clipboard, and temp-file errors.
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
| Settings window | Steps 1, 7, 8, 10 | `In Progress` | No |
| Global Start/Stop hotkey | Step 2 | `Not Started` | No |
| Global Cancel hotkey | Step 2 | `Not Started` | No |
| Microphone selection | Step 3 | `Not Started` | No |
| Microphone level meter | Step 3 | `Not Started` | No |
| Explicit state machine | Step 2 | `Not Started` | No |
| At least one batch transcription provider | Step 5 | `Not Started` | No |
| At least one streaming transcription provider | Step 6 | `Not Started` | No |
| Multiple built-in transcription provider selection | Step 7 | `Not Started` | No |
| Optional text cleanup provider | Step 8 | `Not Started` | No |
| Clipboard output provider | Step 4 | `Not Started` | No |
| Temp file cleanup | Step 9 | `Not Started` | No |
| Sanitized file logging | Step 9 | `Not Started` | No |

## Deferred Items

These are intentionally out of first-build scope unless the plan changes.

| Future Work | Status | Complete? |
| --- | --- | --- |
| Live cursor insertion | `Deferred` | No |
| Paste output by setting clipboard and sending Ctrl+V | `Deferred` | No |
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