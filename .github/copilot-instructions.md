# Copilot Instructions

This repository is a Windows speech-to-text daemon. Before making architecture or implementation decisions, read:

- [plan.md](../docs/plan.md) for product behavior, state machine rules, provider boundaries, error handling, privacy rules, and first-build scope.
- [stack.md](../docs/stack.md) for the technical stack, Windows audio strategy, native C++ extension path, transcription options, and open risks.
- [implementation-steps.md](../docs/implementation-steps.md) for the current implementation status and next planned work.
- [build.md](../docs/build.md) for how to build the project, including prerequisites, native interop DLL build, app build, and run instructions.

## Project Direction

- Build the first version as a C#/.NET 8 WPF Windows utility.
- Use WASAPI shared capture through NAudio as the first audio backend.
- Keep ASIO as a later advanced backend, preferably through a small native C++ bridge if it becomes necessary.
- Keep the app shaped as a tray/background utility with a settings window, not a dashboard or landing-page style app.
- Prefer small explicit interfaces over broad plugin frameworks until real requirements justify more abstraction.

## Architecture Rules

- `SessionOrchestrator` owns app state transitions.
- Preserve the explicit states: `Idle`, `Recording`, `Processing`, `Outputting`, and `Error`.
- Settings changes during an active session should apply to the next session, not mutate the current one.
- Streaming providers may produce partial text internally, but first-build output happens only after recording stops.
- Output providers receive final text only.
- Clipboard output is the first output provider.
- Text cleanup runs after final transcription and should fall back to raw transcript text on failure or timeout.

## Privacy And Logging

- Do not log raw audio by default.
- Do not log transcript text by default.
- Do not log secrets or full remote endpoint credentials.
- Log provider IDs, endpoint type, timings, state transitions, and sanitized errors.
- Store temporary audio in an app-specific temp location and delete it after success, cancellation, or failure.
- Run stale temp-file cleanup on startup once that subsystem exists.

## Implementation Notes

- Keep UI work on the WPF UI thread and keep audio, provider calls, cleanup, and output work asynchronous and cancellable.
- Use dependency injection and hosted services consistently with the existing app shell.
- Extend existing services and view models rather than bypassing them with ad hoc state.
- Validate changes with `dotnet build Tts.sln` from the repository root.