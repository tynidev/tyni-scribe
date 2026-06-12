#pragma once

#include <stdint.h>

#ifdef _WIN32
#define TTS_CTRANSLATE2_API __declspec(dllexport)
#else
#define TTS_CTRANSLATE2_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct tts_ctranslate2_engine tts_ctranslate2_engine;

typedef enum tts_ctranslate2_status {
    TTS_CTRANSLATE2_STATUS_OK = 0,
    TTS_CTRANSLATE2_STATUS_CANCELED = 1,
    TTS_CTRANSLATE2_STATUS_MODEL_NOT_FOUND = 2,
    TTS_CTRANSLATE2_STATUS_MODEL_LOAD_FAILED = 3,
    TTS_CTRANSLATE2_STATUS_INVALID_AUDIO = 4,
    TTS_CTRANSLATE2_STATUS_TRANSCRIPTION_FAILED = 5,
    TTS_CTRANSLATE2_STATUS_NOT_INITIALIZED = 6,
    TTS_CTRANSLATE2_STATUS_INVALID_ARGUMENT = 7,
    TTS_CTRANSLATE2_STATUS_TIMEOUT = 8,
    TTS_CTRANSLATE2_STATUS_DEPENDENCY_UNAVAILABLE = 9,
    TTS_CTRANSLATE2_STATUS_NOT_IMPLEMENTED = 10,
    TTS_CTRANSLATE2_STATUS_NATIVE_FAILURE = 100
} tts_ctranslate2_status;

TTS_CTRANSLATE2_API tts_ctranslate2_status tts_ctranslate2_engine_create(tts_ctranslate2_engine** engine);

TTS_CTRANSLATE2_API tts_ctranslate2_status tts_ctranslate2_engine_load_model(
    tts_ctranslate2_engine* engine,
    const char* model_directory_utf8,
    const char* compute_type_utf8);

TTS_CTRANSLATE2_API tts_ctranslate2_status tts_ctranslate2_engine_unload_model(tts_ctranslate2_engine* engine);

TTS_CTRANSLATE2_API tts_ctranslate2_status tts_ctranslate2_engine_transcribe_wav(
    tts_ctranslate2_engine* engine,
    const char* wav_path_utf8,
    const char* language_utf8,
    int32_t timeout_seconds,
    char** transcript_utf8);

TTS_CTRANSLATE2_API void tts_ctranslate2_engine_request_cancel(tts_ctranslate2_engine* engine);

TTS_CTRANSLATE2_API const char* tts_ctranslate2_engine_last_error(tts_ctranslate2_engine* engine);

TTS_CTRANSLATE2_API void tts_ctranslate2_string_free(char* value);

TTS_CTRANSLATE2_API void tts_ctranslate2_engine_dispose(tts_ctranslate2_engine* engine);

#ifdef __cplusplus
}
#endif