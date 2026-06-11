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
        +--> [ Batch Transcription Provider ]
        |
        +--> [ Streaming Transcription Provider ]
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
- Route audio to the selected transcription provider.
- Accumulate or finalize transcript text.
- Run optional text cleanup after recording stops.
- Send final text to enabled output providers.
- Handle cancellation and failures.
- Keep settings changes from affecting an active session.

The orchestrator should be the only module that decides what state the app is in. Tray status, hotkey behavior, and UI availability should derive from this state.

### Transcription Providers

Support multiple built-in transcription providers behind small interfaces.

The app should support both provider types:

- Batch providers: receive a completed audio file and return final text.
- Streaming providers: receive audio chunks during recording and return final text when stopped.

Streaming transcription is in scope, but streaming output is not. Streaming providers may produce partial text internally or for a future preview UI, but the first output path only uses the final transcript after Stop.

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

Initial provider:

- Clipboard output: copies final text to the system clipboard.

Possible future providers:

- Paste output: set clipboard and send Ctrl+V.
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

## Minimal Interfaces

These are conceptual interfaces, not final language-specific code.

```text
BatchTranscriptionProvider
- id
- displayName
- transcribeFile(audioFile, options) -> transcript
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

## Configuration

Use one typed configuration file with defaults.

Recommended settings:

- Selected microphone device.
- Start/Stop hotkey.
- Cancel hotkey.
- Selected transcription provider.
- Transcription provider endpoint, when needed.
- Enable text cleanup.
- Selected cleanup provider.
- Cleanup provider endpoint, when needed.
- Cleanup prompt.
- Enabled output providers.

Include a config version field, but do not build migration machinery until a second config version exists.

## Error Handling

Define simple fallback behavior for common failures.

- Microphone unavailable: show error and return to Idle.
- Hotkey conflict: show error in settings and keep previous binding.
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
- Delete temp audio in success, cancellation, and failure paths.
- Run stale temp-file cleanup on startup.
- Clearly label providers that send audio or text to a remote endpoint.

Logging rules:

- Log app version and OS version.
- Log state transitions.
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
4. Implement clipboard output provider.
5. Implement first batch transcription provider.
6. Implement first streaming transcription provider with final-text output only.
7. Add multiple built-in provider selection.
8. Add optional text cleanup provider with raw-transcript fallback.
9. Add temp-file cleanup and sanitized logging.
10. Polish errors, tray status, and settings validation.

## Design Decisions Locked For Now

- Streaming transcription is supported, but output waits until recording stops.
- Clipboard is the first output destination.
- Output is handled through output providers so more destinations can be added later.
- Multiple transcription providers are supported as built-in providers, not external plugins.
- The microphone level meter is part of the initial app, not a later enhancement.
- The app favors simple explicit state and small interfaces over a large plugin framework.
