# Speech-to-Text Daemon Plan

## Goal

Build a Windows background speech-to-text utility that records from a selected microphone with global hotkeys, transcribes speech through configurable providers, optionally cleans up the final text with an LLM, and writes the finished result to one or more output destinations.

The design should stay simple for the first build while leaving clear extension points for additional transcription providers and output targets later.

## Core Principles

- Support both batch and streaming transcription providers.
- Always treat output as a final-text action after the recording stops.
- Clipboard output first, insert text into active application later.
- Keep interfaces small and practical.
- Avoid logging raw audio or transcript text by default.
- Delete temporary audio files after success, cancellation, or failure.

## High-Level Architecture

```text
[ Audio Hardware ]
        |
        v
[ Input Manager ] ---- hotkeys / mic level ----> [ UI Shell / Tray ]
        |
        v
[ Session Orchestrator ]
        |
        +--> [ Audio Processing Provider ] ---> [ Batch Transcription Provider ]
        |
        +--> [ Streaming Transcription Provider ]
        |
        v
[ Final Transcript ]
        |
        v
[ Optional Text Cleanup Provider ]
        |
        v
[ Output Providers ] ---> Clipboard now, more destinations later
```

## Modules

### Input Manager

Owns OS-facing input concerns.

Responsibilities:

- Enumerate microphone devices.
- Store and apply the selected microphone.
- Register global Start/Stop and Cancel hotkeys.
- Capture microphone audio for the active session.
- Provide microphone level data for the settings UI.
- Write audio to a temporary file for batch providers.
- Feed audio chunks to a streaming session for streaming providers.

The microphone level meter is part of the first build because it helps confirm that the selected device is working and receiving usable input.

### Session Orchestrator

Owns the application state machine and the transcription workflow.

Responsibilities:

- Start and stop recording sessions.
- Run completed captured audio through the selected audio processing provider before batch transcription.
- Route audio to the selected transcription provider.
- Accumulate or finalize transcript text.
- Run optional text cleanup after recording stops.
- Send final text to enabled output providers.
- Record sanitized timing data for each session stage.
- Handle cancellation and failures.
- Keep settings changes from affecting an active session.

The orchestrator should be the only module that decides what state the app is in. Tray status, hotkey behavior, and UI availability should derive from this state.

### Audio Processing Providers

Optionally transform completed captured audio before it reaches batch transcription providers. The first version should include a no-op processor so the pipeline is explicit even before cleanup features are added.

Responsibilities:

- Receive a completed captured audio file.
- Inspect audio format metadata such as duration, channels, sample rate, and peak level when available.
- Return either the original file or a newly written processed audio file.
- Support future processing such as level normalization, mono/stereo detection, channel conversion, silence trimming, and format conversion.
- Preserve privacy rules: do not log raw audio content and delete temporary processed audio after success, cancellation, or failure.

Audio processing is for completed files in the first build. Live streaming audio chunks should remain attached directly to the recording path until a streaming-specific processor is actually needed.

### Transcription Providers

Support multiple built-in transcription providers behind small interfaces.

The app should support both provider types:

- Batch providers: receive a completed audio file and return final text.
- Streaming providers: receive audio chunks during recording and return final text when stopped.

Streaming transcription is in scope, but streaming output is not. Streaming providers may produce partial text internally or for a future preview UI, but the first output path only uses the final transcript after Stop.

#### faster-whisper / CTranslate2 Local Provider

The planned `faster-whisper-local` provider remains a batch provider and should return final transcript text only. It should use a Python-free normal runtime path if practical, with C# calling a Windows x64 native DLL through a C ABI.

The native project scaffold lives under `src/native/Tts.CTranslate2Interop` and reserves an ABI for creating an engine, loading a converted CTranslate2 model directory, transcribing a WAV, requesting cancellation, unloading the model, disposing native memory, reading a sanitized last error, and freeing returned strings. The app should serialize access to this engine until CTranslate2 model/thread-safety is proven for this usage.

Until the direct C++ implementation is complete, the runnable implementation uses an isolated local Python process under `%LOCALAPPDATA%\tts\tools\faster-whisper-python` to call `faster-whisper`. This keeps the provider boundary stable and exercises the CTranslate2 model path, but it is not the final Python-free native design.

This provider must not reuse whisper.cpp ggml `.bin` files. Its models belong under `%LOCALAPPDATA%\tts\models\faster-whisper` as converted CTranslate2 model directories. Normal UI controls should keep friendly model IDs and compute-type choices instead of exposing raw paths.

Direct CTranslate2 integration is feasible but incomplete: CTranslate2 supplies the optimized Whisper model runtime, while a full native app provider still needs Whisper audio preprocessing, log-mel feature extraction, 30-second chunking, tokenizer loading, prompt token construction, generation result decoding, and aggregation without relying on Python at runtime.

### Text Cleanup Provider

Optionally transforms the final transcript with a configured LLM prompt. For first pass use a No-Op text cleanup provider that does not change the text at all.

Responsibilities:

- Receive final raw transcript text.
- Send it to the configured cleanup provider when enabled.
- Return transformed text.
- Use the raw transcript as fallback if cleanup fails or times out.

Post-processing always runs after recording stops. It does not change the streaming transcription flow.

### Output Providers

Write final text to one or more destinations.

The first output provider should be clipboard output. The design should allow more output providers later without changing the orchestrator.

Initial providers:

- Clipboard output: copies final text to the system clipboard.
- Paste output: sets the clipboard to the final text and sends Ctrl+V to the active window.

Possible future providers:

- File output: append or write transcript to a file.
- Webhook output: send transcript to an HTTP endpoint.
- Active-window typing output: simulate typing into the focused app.

Output providers should receive final text only. They should not receive partial streaming tokens.

### UI Shell and Tray

Provides settings and background app lifecycle.

Responsibilities:

- Show settings window.
- Minimize or close to tray while keeping the app running.
- Show tray status for Idle, Recording, Processing, Outputting, and Error.
- Provide tray actions for Open Settings, Enable/Disable Cleanup, and Quit.
- Show microphone device selection.
- Show microphone level meter.
- Show transcription provider selection.
- Show transcription-provider settings in product terms, such as local model/profile and language, not internal executable paths.
- Show output provider selection.
- Show hotkey settings.
- Show cleanup prompt settings.

A detailed diagnostics UI is not needed for the first build. Simple sanitized file logs are enough.

## State Machine

Keep the state machine small and explicit.

```text
Idle
Recording
Processing
Outputting
Error
```

State behavior:

- `Idle`: ready to start a new recording.
- `Recording`: microphone is active and audio is being captured.
- `Processing`: transcription finalization and optional cleanup are running.
- `Outputting`: final text is being written to enabled output providers.
- `Error`: a recoverable failure occurred and the app should return to Idle after notifying the user.

Hotkey behavior:

- Start/Stop in `Idle`: start recording.
- Start/Stop in `Recording`: stop recording and begin processing.
- Start/Stop in `Processing` or `Outputting`: ignore or show a busy notification.
- Cancel in `Recording`: stop capture, delete temp files, discard text, return to Idle.
- Cancel in `Processing`: cancel active work when possible, delete temp files, discard text, return to Idle.
- Cancel in `Idle`: no-op.

Settings behavior:

- Provider, microphone, and output settings apply only when Idle.
- If settings are changed during an active session, apply them to the next session.

## Stage Timing

Track timings for every user-visible session stage and write them to a sanitized CSV timing log so slow paths can be analyzed later without logging audio or transcript content.

Use monotonic timers such as `Stopwatch`, not wall-clock timestamp subtraction, for durations. Timing records should include duration, success/failure/canceled status, provider IDs when relevant, and sanitized error categories. They should not include raw transcript text, raw audio, secrets, or full remote endpoint credentials.

Recommended initial stage names:

| Stage | Measures |
| --- | --- |
| `capture-finalization` | Time from Stop being requested while recording until the completed captured audio file is flushed, closed, and ready for processing. This is the "after recording stops before processing begins" interval. |
| `audio-processing` | Time spent by the completed-file audio processing provider, including no-op processors. |
| `transcription` | Time spent producing final transcript text from the selected transcription provider. |
| `text-cleanup` | Time spent transforming the final transcript through the selected text cleanup provider. |
| `clipboard-output` | Time spent writing final text to the clipboard output provider. Future output providers should get their own provider-specific stage names. |
| `temp-file-cleanup` | Time spent deleting captured and processed temporary audio files at the end of a session. |

Also track `total-session` from recording start to the final return to `Idle`, and track `recording-duration` separately from processing timings because it is user-controlled input length rather than app work.

Write timings to `%AppData%/SpeechToTextDaemon/logs/timings.csv`. Append exactly one row per recording session when the session returns to `Idle`, including successful, canceled, and recoverable failure sessions. Create the directory and CSV header if the file does not exist. Keep the file append path independent from general diagnostic logs so timing data can be opened directly in spreadsheet or analysis tools.

Recommended CSV columns:

```text
schemaVersion,sessionId,startedUtc,completedUtc,status,errorCategory,
microphoneDeviceId,transcriptionProviderId,audioProcessorProviderId,cleanupProviderId,outputProviderIds,
recordingDurationMs,totalSessionMs,captureFinalizationMs,audioProcessingMs,transcriptionMs,textCleanupMs,clipboardOutputMs,tempFileCleanupMs,
providerSettingsJson
```

For stages that did not run, leave the duration field empty rather than writing `0`, so analysis can distinguish skipped work from near-zero work. Provider IDs and microphone IDs are acceptable because they are configuration identifiers. Leave provider ID fields empty when the provider category was disabled or not used for that session, such as `cleanupProviderId` when text cleanup is disabled.

`providerSettingsJson` should contain a compact sanitized snapshot of performance-relevant provider settings, such as transcription model ID, language, compute type, timeout seconds, enabled output provider IDs, and booleans indicating whether advanced path overrides were set. Do not include file paths, transcript text, cleanup prompt text, endpoint secrets, raw endpoint URLs, or audio content.

## Minimal Interfaces

These are conceptual interfaces, not final language-specific code.

```text
BatchTranscriptionProvider
- id
- displayName
- transcribeFile(audioFile, options) -> transcript
```

```text
AudioProcessingProvider
- id
- displayName
- processFile(inputAudioFile, options) -> outputAudioFile
```

```text
StreamingTranscriptionProvider
- id
- displayName
- startSession(audioFormat, options) -> StreamingTranscriptionSession
```

```text
StreamingTranscriptionSession
- acceptAudio(chunk)
- stop() -> transcript
- cancel()
```

```text
TextCleanupProvider
- id
- displayName
- transform(text, prompt, options) -> transformedText
```

```text
OutputProvider
- id
- displayName
- isEnabled(config) -> boolean
- write(text, context)
```

## Provider Metadata

Avoid a large capability metadata system for now. The app only needs enough metadata to drive settings and routing.

Recommended initial metadata:

```text
id
displayName
transcriptionMode: batch | streaming
requiresEndpoint: boolean
```

Add richer metadata only when the UI needs it, such as language support, timestamp support, GPU requirements, or maximum audio duration.

Provider-specific settings should be owned by the provider and exposed in product terms. The shared provider-setting descriptor primitives belong under the service/provider-settings contract, while persisted values belong under configuration and provider-specific parser/default logic belongs inside each provider folder. For example, a local whisper.cpp provider can expose a friendly model/profile selection and language setting, while the executable path and raw model file paths remain app-managed deployment details. Advanced path overrides may exist in configuration for development or portable installs, but they should not be part of the normal settings UI. The first local whisper.cpp model catalog should include tiny/base/small English models plus Large v3 Turbo, with model IDs mapped internally to app-managed model files.

Warm local whisper.cpp model reuse is available through two local provider paths behind the existing batch provider boundary. `whisper-cpp-warm-local` uses a long-lived local `whisper-server.exe` worker that keeps one selected model loaded and restarts when the selected model or language changes. `whisper-cpp-native-local` uses the Windows x64 `tts-whisper-interop.dll` C ABI wrapper built from the pinned `ggml-org/whisper.cpp` submodule under `src/native/Tts.WhisperInterop`. The existing `whisper-cli.exe` provider remains the default compatibility path while the warm and native paths mature through full app-session testing and packaging polish.

## Configuration

Use one typed configuration file with defaults.

Recommended settings:

- Selected microphone device.
- Start/Stop hotkey.
- Cancel hotkey.
- Selected transcription provider.
- Selected audio processing provider.
- Transcription provider settings, such as local model/profile, language, timeout, or remote endpoint type when needed.
- Audio processing provider settings, keyed by provider ID.
- Enable text cleanup.
- Selected cleanup provider.
- Cleanup provider endpoint, when needed.
- Cleanup prompt.
- Enabled output providers.
- Output provider settings, keyed by provider ID.

Include a config version field, but do not build migration machinery until a second config version exists.

## Error Handling

Define simple fallback behavior for common failures.

- Microphone unavailable: show error and return to Idle.
- Hotkey conflict: show error in settings and keep previous binding.
- Audio processing failure: show error and return to Idle unless the selected processor explicitly supports fallback to the original file.
- Transcription provider unavailable: show error and return to Idle.
- Transcription timeout: cancel provider work, delete temp files, return to Idle.
- Empty recording: show a quiet notification and return to Idle.
- Cleanup failure: use raw transcript and continue to output.
- Clipboard failure: show error and keep transcript in memory until dismissed or retried.
- Temp file cleanup failure: log sanitized error and retry cleanup on next app start.

## Privacy and Logging

Privacy rules:

- Do not log raw audio.
- Do not log transcript text by default.
- Store temp audio in the OS temp/app data location.
- Store timing CSV logs under `%AppData%/SpeechToTextDaemon/logs/timings.csv`.
- Delete temp audio in success, cancellation, and failure paths.
- Delete processed temp audio with the same lifecycle rules as captured temp audio.
- Run stale temp-file cleanup on startup.
- Clearly label providers that send audio or text to a remote endpoint.

Logging rules:

- Log app version and OS version.
- Log state transitions.
- Append sanitized stage timings and total session duration to the CSV timing log.
- Log selected provider IDs, not transcript content.
- Log endpoint type, but avoid secrets.
- Log request durations and sanitized errors.

A detailed diagnostics UI can be added later if logs alone are not enough.

## First Build Scope

In scope:

- Windows background app with tray lifecycle.
- Settings window.
- Global Start/Stop hotkey.
- Global Cancel hotkey.
- Microphone selection.
- Microphone level meter.
- Completed-file audio processing provider pipeline.
- Explicit state machine.
- At least one batch transcription provider.
- At least one streaming transcription provider.
- Multiple built-in transcription provider selection.
- Optional text cleanup provider.
- Clipboard output provider.
- Temp file cleanup.
- Sanitized file logging.

Out of scope for the first build:

- Live cursor insertion.
- Streaming output while recording.
- Dynamic provider plugin loading.
- Full diagnostics UI.
- Import/export settings.
- Advanced provider capability metadata.

## Recommended Build Order

1. Build app shell, tray lifecycle, and settings storage.
2. Implement state machine, hotkeys, and cancellation.
3. Implement microphone selection, recording, and level meter.
4. Add completed-file audio processing provider pipeline.
5. Implement clipboard output provider.
6. Implement first batch transcription provider.
7. Implement first streaming transcription provider with final-text output only.
8. Add multiple built-in provider selection.
9. Add optional text cleanup provider with raw-transcript fallback.
10. Add temp-file cleanup and sanitized logging.
11. Polish errors, tray status, and settings validation.

## Design Decisions Locked For Now

- Streaming transcription is supported, but output waits until recording stops.
- Clipboard is the first output destination.
- Output is handled through output providers so more destinations can be added later.
- Multiple transcription providers are supported as built-in providers, not external plugins.
- Warm local whisper.cpp reuse has both a long-lived worker provider and an in-process native C ABI provider. The current process-based CLI provider remains available as the default compatibility path.
- The microphone level meter is part of the initial app, not a later enhancement.
- Audio processing starts as a completed-file provider pipeline, not a live audio callback processor.
- The app favors simple explicit state and small interfaces over a large plugin framework.
