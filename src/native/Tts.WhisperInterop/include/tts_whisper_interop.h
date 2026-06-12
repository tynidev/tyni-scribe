#pragma once

#include <stdint.h>

#ifdef _WIN32
#define TTS_WHISPER_API __declspec(dllexport)
#else
#define TTS_WHISPER_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct tts_whisper_engine tts_whisper_engine;

typedef enum tts_whisper_status {
    TTS_WHISPER_STATUS_OK = 0,
    TTS_WHISPER_STATUS_CANCELED = 1,
    TTS_WHISPER_STATUS_MODEL_NOT_FOUND = 2,
    TTS_WHISPER_STATUS_MODEL_LOAD_FAILED = 3,
    TTS_WHISPER_STATUS_INVALID_AUDIO = 4,
    TTS_WHISPER_STATUS_TRANSCRIPTION_FAILED = 5,
    TTS_WHISPER_STATUS_NOT_INITIALIZED = 6,
    TTS_WHISPER_STATUS_INVALID_ARGUMENT = 7,
    TTS_WHISPER_STATUS_TIMEOUT = 8,
    TTS_WHISPER_STATUS_NATIVE_FAILURE = 100
} tts_whisper_status;

TTS_WHISPER_API tts_whisper_status tts_whisper_engine_create(tts_whisper_engine** engine);

TTS_WHISPER_API tts_whisper_status tts_whisper_engine_load_model(
    tts_whisper_engine* engine,
    const char* model_path_utf8);

TTS_WHISPER_API tts_whisper_status tts_whisper_engine_unload_model(tts_whisper_engine* engine);

TTS_WHISPER_API tts_whisper_status tts_whisper_engine_transcribe_wav(
    tts_whisper_engine* engine,
    const char* wav_path_utf8,
    const char* language_utf8,
    int32_t timeout_seconds,
    char** transcript_utf8);

TTS_WHISPER_API void tts_whisper_engine_request_cancel(tts_whisper_engine* engine);

TTS_WHISPER_API const char* tts_whisper_engine_last_error(tts_whisper_engine* engine);

TTS_WHISPER_API void tts_whisper_string_free(char* value);

TTS_WHISPER_API void tts_whisper_engine_dispose(tts_whisper_engine* engine);

#ifdef __cplusplus
}
#endif