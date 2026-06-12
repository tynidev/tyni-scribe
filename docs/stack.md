# Speech-to-Text Daemon Tech Stack

## Recommendation

Use a native Windows-first stack:

```text
App shell / UX:        C# + .NET 8 or .NET 9 + WPF
App orchestration:    C# hosted background services
Audio capture:        WASAPI shared mode first, ASIO as an advanced optional backend
Audio libraries:      NAudio for first build; native C++ bridge with RtAudio, PortAudio, JUCE, or ASIO SDK if ASIO needs to become serious
Critical paths:       C++ native layer for ASIO callbacks, ring buffers, sample conversion, and optional local model bindings
Transcription:        Provider interface in C#, with local and remote provider adapters
Local STT option:     whisper.cpp, faster-whisper, or ONNX Runtime behind a provider boundary
Config:               Typed JSON config in AppData
Logging:              Serilog with sanitized rolling file logs
Packaging:            MSIX or single-file self-contained installer
```

This gives the project a snappy desktop UX, good Windows integration, straightforward tray and hotkey support, and a practical path into native Windows audio and AI model execution without forcing the whole app into C++ from day one.

## Recommended Architecture Shape

Use a hybrid architecture: C# for the product shell and C++ for the small number of paths where native control matters.

```text
C# / WPF app
- UI and tray lifecycle
- settings and config
- global hotkeys
- session state machine
- provider routing
- remote API calls
- logging
- clipboard and output providers

C++ native layer
- ASIO capture backend
- optional low-level WASAPI backend
- audio callback handling
- lock-free or low-allocation ring buffers
- sample format conversion and resampling
- optional whisper.cpp or ONNX Runtime model bindings
```

This keeps normal application development fast while still leaving room for serious audio work. The app should not be fully C++ unless ASIO/pro-audio routing becomes the central product requirement.

## Important Audio Decision

For the first build, prefer **WASAPI shared mode** for microphone capture.

That is the Windows audio path most likely to let this app use an audio interface while other applications are also using it. ASIO is excellent for low-latency professional audio, but it is often exclusive or effectively single-client depending on the interface driver. Some devices and drivers are multi-client, and some expose both ASIO and WDM/WASAPI endpoints that can be used at the same time, but the app cannot guarantee that universally.

In practice:

- Use `WASAPI shared` as the default capture backend.
- Add `WASAPI exclusive` only as an optional advanced mode.
- Add `ASIO` as an optional advanced backend for devices where the driver supports the desired behavior.
- Make the UI clearly show which backend is selected.
- Expect simultaneous use with DAWs, OBS, Discord, browsers, and meeting apps to work best through WASAPI shared mode.

The app does not need ultra-low-latency ASIO for the core workflow because output happens after recording stops. Stability and coexistence matter more than shaving a few milliseconds off capture latency.

C++ can make the ASIO backend better engineered, but it does not make ASIO multi-client. If an interface driver only allows one ASIO client, the app cannot force sharing. C++ gives better callback control, lower-level buffer management, and cleaner access to native ASIO libraries, while shared-device behavior still depends on the driver.

## Primary Stack: .NET WPF + WASAPI

### Why This Fits

C# and WPF are a strong fit for a Windows background utility:

- Native-feeling and snappy UI.
- Good system tray support.
- Good global hotkey support through Win32 interop.
- Easy clipboard integration.
- Easy settings window implementation.
- Strong async workflow support for recording, transcription, cleanup, and output.
- Mature packaging options.
- Clean path to native interop when needed.

WPF is a better first choice than Electron for this app because the utility should feel lightweight, quick to open, and quietly present in the tray. WinUI 3 is also viable, but WPF is still simpler for mature desktop utility patterns like tray lifecycle, global hotkeys, and low-friction deployment.

### Suggested Libraries

```text
UI / MVVM
- WPF
- CommunityToolkit.Mvvm
- H.NotifyIcon.Wpf or Hardcodet.NotifyIcon.Wpf

App host
- Microsoft.Extensions.Hosting
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Options

Audio
- NAudio for WASAPI capture, device enumeration, level metering, and WAV writing
- Optional later: CSCore as an alternative Windows audio library

Hotkeys / OS integration
- Win32 RegisterHotKey through P/Invoke or a small helper package
- TextCopy or direct WPF/Win32 clipboard integration

Logging
- Serilog
- Serilog.Sinks.File

Config
- System.Text.Json
- JSON file under `%AppData%/<AppName>/config.json`

Packaging
- MSIX for a polished Windows install
- Self-contained single-file publish for simple internal distribution
```

## Audio Backend Strategy

Use a small internal audio backend interface so the app can start simple and grow safely.

```text
AudioCaptureBackend
- id
- displayName
- mode: wasapi-shared | wasapi-exclusive | asio
- enumerateDevices()
- startCapture(deviceId, format)
- stopCapture()
- getLevel()
```

Recommended implementation order:

1. `WasapiSharedAudioBackend` using NAudio.
2. `WasapiExclusiveAudioBackend` only if users need it.
3. `NativeAudioEngine` C++ project with a stable C ABI or C++/CLI bridge.
4. `AsioAudioBackend` implemented through the native audio engine after the rest of the app is solid.

The C# layer should see every backend through the same managed interface. The C++ layer should hide ASIO driver details, callback timing, buffer ownership, and sample conversion from the rest of the app.

### ASIO Implementation Options

For ASIO, there are three realistic paths:

| Option | Fit | Notes |
| --- | --- | --- |
| NAudio ASIO | Good proof of concept | Convenient from C#, but less ideal if ASIO becomes a major feature. |
| RtAudio or PortAudio native bridge | Good serious option | C/C++ library exposes WASAPI and ASIO. Build a small native DLL and call it from C#. |
| JUCE native bridge | Best pro-audio option | Excellent ASIO/WASAPI support, but introduces JUCE licensing and a larger native framework. |
| Direct ASIO SDK wrapper | Most control | Strongest native ASIO path, but more driver-specific complexity and licensing diligence. |

Best path: start with NAudio for WASAPI. If ASIO becomes important, build a small native audio bridge rather than moving the whole app to C++.

The native bridge should avoid doing heavy work inside real-time callbacks. Capture callbacks should copy or move audio into a preallocated buffer quickly, then let worker threads handle WAV writing, metering, resampling, and transcription handoff.

## Transcription Stack

Keep transcription providers behind the interfaces already described in [plan.md](plan.md).

Good first provider choices:

| Provider Type | Stack | Why |
| --- | --- | --- |
| Local batch | `whisper.cpp` executable or native library | Simple, private, works well with completed WAV files. |
| Local batch / streaming-ish | faster-whisper through a local Python service | Very good quality/performance, but adds Python runtime complexity. |
| Local Windows-native | ONNX Runtime with Whisper models | Clean deployment story if model support is enough for the target quality. |
| Remote batch/streaming | OpenAI, Azure Speech, Deepgram, etc. | Easy high-quality provider adapters, but sends audio/text off-device. |

For the first build, the lowest-risk path is:

```text
Batch provider:     whisper.cpp adapter
Streaming provider: remote streaming API adapter or a mock provider first
Cleanup provider:   no-op provider, then optional LLM HTTP adapter
```

Local streaming transcription can be added later, but the first architecture does not need to output partial text.

## Native AI Model Strategy

C++ is useful for local model execution when it wraps an established engine. It should not become a custom inference project.

Good native model paths:

| Engine | Fit | Notes |
| --- | --- | --- |
| whisper.cpp | Best first local native option | Simple deployment, strong CPU path, GPU options depending on build. |
| ONNX Runtime | Good Windows-native option | Works well with DirectML or CPU when model support fits the target quality. |
| CUDA-backed native service | Best high-end performance | Useful later, but increases GPU/runtime packaging complexity. |

Recommended order:

1. Call `whisper.cpp` as a child process for the first batch provider.
2. Add a native wrapper only after the product workflow is proven.
3. Keep the C# provider interface stable so the app can switch between process-based and native model providers.

This avoids early native complexity while preserving the option to make local transcription faster and more integrated later.

## UX Stack

Use WPF with MVVM and keep the app shaped like a utility, not a full dashboard.

Recommended UX pieces:

- Tray icon with state indicator.
- Settings window.
- Compact recording status surface.
- Microphone selector.
- Backend selector: `WASAPI shared`, `WASAPI exclusive`, `ASIO` when available.
- Microphone level meter.
- Hotkey editor.
- Provider settings.
- Output settings.
- Cleanup prompt editor.

The UI should remain responsive by keeping audio capture, transcription, cleanup, and output work off the UI thread. Use async services and cancellation tokens throughout the session orchestration layer.

## Snappiness Choices

To keep the program feeling fast:

- Use WPF instead of Electron for the first Windows build.
- Keep the tray process warm in the background.
- Load large transcription models lazily or in a background warmup step.
- Avoid blocking the UI thread during device enumeration and provider calls.
- Stream audio to a temp file instead of holding entire recordings in memory.
- Use cancellation tokens for recording stop, processing timeout, and app shutdown.
- Keep settings writes small and atomic.
- Do stale temp-file cleanup after startup, not before the UI appears.
- Keep real-time audio callbacks in native code if ASIO is added.
- Keep model loading explicit, lazy, and cancellable from the C# orchestration layer.

## Alternative Stacks

### C++ / JUCE App

```text
App shell / UX:  JUCE
Audio:           JUCE AudioDeviceManager with WASAPI and ASIO
Transcription:   Native adapters or child processes
Packaging:       Native Windows installer
```

This is the strongest pure audio stack, especially for ASIO. The tradeoff is that normal desktop utility UX, settings, tray behavior, HTTP APIs, JSON config, and app orchestration are more labor-intensive than in .NET.

Choose this only if ASIO and pro-audio routing are the central feature of the product.

### Tauri + Rust

```text
App shell / UX:  Tauri + React/Svelte/Vue
Backend:         Rust
Audio:           CPAL for WASAPI-style capture
Packaging:       Tauri bundler
```

This can be fast and lightweight, with a nicer web UI development loop than WPF. The downside is that ASIO support is not as straightforward, and Windows desktop utility features may take more glue code.

Choose this if web UI ergonomics matter more than deep ASIO support.

### Electron + Node

```text
App shell / UX:  Electron + React
Backend:         Node.js / native modules
Audio:           Native module required for serious Windows audio
Packaging:       electron-builder
```

This is productive for UI, but it is heavier than this app needs and pushes Windows audio capture into native modules anyway. It is not the best default for a snappy tray utility.

Choose this only if the team strongly prefers web UI and accepts the larger runtime.

## Recommended First Build Stack

```text
Language:          C#
Runtime:           .NET 8 LTS or .NET 9
UI:                WPF
Pattern:           MVVM with CommunityToolkit.Mvvm
Background host:   Microsoft.Extensions.Hosting
Audio v1:          NAudio + WASAPI shared capture
Audio v2:          Optional WASAPI exclusive
Audio v3:          Native C++ audio engine
Audio v4:          ASIO backend through the native C++ audio engine
STT v1:            whisper.cpp batch provider
STT v2:            One remote streaming provider
STT v3:            Optional native whisper.cpp or ONNX Runtime bridge
Cleanup v1:        No-op provider
Cleanup v2:        HTTP LLM provider
Output v1:         Clipboard provider
Config:            Typed JSON in AppData
Logs:              Serilog rolling file logs, sanitized by default
Packaging:         MSIX or self-contained Windows installer
```

## Open Technical Risks

- ASIO multi-client behavior depends on the audio interface driver and cannot be guaranteed by the app.
- Some interfaces may not allow WASAPI capture while their ASIO driver is active in another program.
- ASIO SDK licensing and redistribution need to be checked before shipping an ASIO backend.
- Native C++ bridges add build, packaging, crash isolation, and debugging complexity.
- Real-time audio code must avoid allocation, blocking locks, and slow work inside callbacks.
- Local transcription model size and startup cost can hurt perceived snappiness unless model loading is managed carefully.
- Global hotkeys may conflict with other software and need clear validation in settings.

## Decision

Build the first version as a **C#/.NET WPF Windows utility using NAudio with WASAPI shared capture**.

Design the app as a **hybrid C# plus C++ system**. C# owns the UX, orchestration, settings, providers, and outputs. C++ is reserved for critical audio and optional local model paths where native control is worth the complexity.

Add ASIO through a native C++ backend after the WASAPI workflow is solid. This gives the best balance between Windows UX, snappy behavior, realistic shared-device access, and future pro-audio support.
