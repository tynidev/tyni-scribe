#include "tts_ctranslate2_interop.h"

#include <atomic>
#include <cstdlib>
#include <mutex>
#include <new>
#include <string>

struct tts_ctranslate2_engine {
    std::mutex mutex;
    std::string last_error;
    std::atomic_bool cancel_requested = false;
};

namespace {
    void set_error(tts_ctranslate2_engine * engine, const char * message) {
        if (engine != nullptr) {
            engine->last_error = message == nullptr ? "ctranslate2-native-failed" : message;
        }
    }
}

extern "C" {

TTS_CTRANSLATE2_API tts_ctranslate2_status tts_ctranslate2_engine_create(tts_ctranslate2_engine ** engine) {
    if (engine == nullptr) {
        return TTS_CTRANSLATE2_STATUS_INVALID_ARGUMENT;
    }

    *engine = nullptr;

    try {
        *engine = new tts_ctranslate2_engine();
        return TTS_CTRANSLATE2_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return TTS_CTRANSLATE2_STATUS_NATIVE_FAILURE;
    }
}

TTS_CTRANSLATE2_API tts_ctranslate2_status tts_ctranslate2_engine_load_model(
    tts_ctranslate2_engine * engine,
    const char * model_directory_utf8,
    const char * compute_type_utf8) {
    if (engine == nullptr || model_directory_utf8 == nullptr || model_directory_utf8[0] == '\0') {
        return TTS_CTRANSLATE2_STATUS_INVALID_ARGUMENT;
    }

    std::lock_guard<std::mutex> guard(engine->mutex);
    engine->last_error.clear();
    set_error(engine, "ctranslate2-native-implementation-missing");
    return TTS_CTRANSLATE2_STATUS_DEPENDENCY_UNAVAILABLE;
}

TTS_CTRANSLATE2_API tts_ctranslate2_status tts_ctranslate2_engine_unload_model(tts_ctranslate2_engine * engine) {
    if (engine == nullptr) {
        return TTS_CTRANSLATE2_STATUS_INVALID_ARGUMENT;
    }

    std::lock_guard<std::mutex> guard(engine->mutex);
    engine->last_error.clear();
    engine->cancel_requested.store(false);
    return TTS_CTRANSLATE2_STATUS_OK;
}

TTS_CTRANSLATE2_API tts_ctranslate2_status tts_ctranslate2_engine_transcribe_wav(
    tts_ctranslate2_engine * engine,
    const char * wav_path_utf8,
    const char * language_utf8,
    int32_t timeout_seconds,
    char ** transcript_utf8) {
    if (transcript_utf8 != nullptr) {
        *transcript_utf8 = nullptr;
    }

    if (engine == nullptr || wav_path_utf8 == nullptr || wav_path_utf8[0] == '\0' || transcript_utf8 == nullptr) {
        return TTS_CTRANSLATE2_STATUS_INVALID_ARGUMENT;
    }

    std::lock_guard<std::mutex> guard(engine->mutex);
    engine->last_error.clear();

    if (engine->cancel_requested.load()) {
        set_error(engine, "transcription-canceled");
        return TTS_CTRANSLATE2_STATUS_CANCELED;
    }

    set_error(engine, "ctranslate2-native-implementation-missing");
    return TTS_CTRANSLATE2_STATUS_DEPENDENCY_UNAVAILABLE;
}

TTS_CTRANSLATE2_API void tts_ctranslate2_engine_request_cancel(tts_ctranslate2_engine * engine) {
    if (engine != nullptr) {
        engine->cancel_requested.store(true);
    }
}

TTS_CTRANSLATE2_API const char * tts_ctranslate2_engine_last_error(tts_ctranslate2_engine * engine) {
    if (engine == nullptr || engine->last_error.empty()) {
        return nullptr;
    }

    return engine->last_error.c_str();
}

TTS_CTRANSLATE2_API void tts_ctranslate2_string_free(char * value) {
    std::free(value);
}

TTS_CTRANSLATE2_API void tts_ctranslate2_engine_dispose(tts_ctranslate2_engine * engine) {
    delete engine;
}

}