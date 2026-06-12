#include "tts_whisper_interop.h"

#include "common-whisper.h"
#include "whisper.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <new>
#include <string>
#include <thread>
#include <vector>

struct tts_whisper_engine {
    std::mutex mutex;
    whisper_context * context = nullptr;
    std::string loaded_model_path;
    std::string last_error;
    std::atomic_bool cancel_requested = false;
    std::atomic_bool timeout_reached = false;
    std::chrono::steady_clock::time_point deadline;
    bool has_deadline = false;
};

namespace {
    void set_error(tts_whisper_engine * engine, const char * message) {
        if (engine != nullptr) {
            engine->last_error = message == nullptr ? "native-engine-failed" : message;
        }
    }

    void unload_model_no_lock(tts_whisper_engine * engine) {
        if (engine->context != nullptr) {
            whisper_free(engine->context);
            engine->context = nullptr;
            engine->loaded_model_path.clear();
        }
    }

    bool should_abort(void * user_data) {
        auto * engine = static_cast<tts_whisper_engine *>(user_data);
        if (engine == nullptr) {
            return false;
        }

        if (engine->cancel_requested.load()) {
            return true;
        }

        if (engine->has_deadline && std::chrono::steady_clock::now() >= engine->deadline) {
            engine->timeout_reached.store(true);
            return true;
        }

        return false;
    }

    char * copy_to_c_string(const std::string & value) {
        const auto byte_count = value.size() + 1;
        auto * buffer = static_cast<char *>(std::malloc(byte_count));
        if (buffer == nullptr) {
            return nullptr;
        }

        std::memcpy(buffer, value.c_str(), byte_count);
        return buffer;
    }

    int default_thread_count() {
        const auto hardware_threads = std::thread::hardware_concurrency();
        if (hardware_threads == 0) {
            return 4;
        }

        return std::max(1, std::min(4, static_cast<int>(hardware_threads)));
    }
}

extern "C" {

TTS_WHISPER_API tts_whisper_status tts_whisper_engine_create(tts_whisper_engine ** engine) {
    if (engine == nullptr) {
        return TTS_WHISPER_STATUS_INVALID_ARGUMENT;
    }

    *engine = nullptr;

    try {
        *engine = new tts_whisper_engine();
        return TTS_WHISPER_STATUS_OK;
    } catch (const std::bad_alloc &) {
        return TTS_WHISPER_STATUS_NATIVE_FAILURE;
    }
}

TTS_WHISPER_API tts_whisper_status tts_whisper_engine_load_model(
    tts_whisper_engine * engine,
    const char * model_path_utf8) {
    if (engine == nullptr || model_path_utf8 == nullptr || model_path_utf8[0] == '\0') {
        return TTS_WHISPER_STATUS_INVALID_ARGUMENT;
    }

    std::lock_guard<std::mutex> guard(engine->mutex);
    engine->last_error.clear();

    const std::string model_path(model_path_utf8);
    if (engine->context != nullptr && engine->loaded_model_path == model_path) {
        return TTS_WHISPER_STATUS_OK;
    }

    unload_model_no_lock(engine);

    auto context_params = whisper_context_default_params();
    auto * context = whisper_init_from_file_with_params(model_path.c_str(), context_params);
    if (context == nullptr) {
        set_error(engine, "model-load-failed");
        return TTS_WHISPER_STATUS_MODEL_LOAD_FAILED;
    }

    engine->context = context;
    engine->loaded_model_path = model_path;
    return TTS_WHISPER_STATUS_OK;
}

TTS_WHISPER_API tts_whisper_status tts_whisper_engine_unload_model(tts_whisper_engine * engine) {
    if (engine == nullptr) {
        return TTS_WHISPER_STATUS_INVALID_ARGUMENT;
    }

    std::lock_guard<std::mutex> guard(engine->mutex);
    engine->last_error.clear();
    unload_model_no_lock(engine);
    return TTS_WHISPER_STATUS_OK;
}

TTS_WHISPER_API tts_whisper_status tts_whisper_engine_transcribe_wav(
    tts_whisper_engine * engine,
    const char * wav_path_utf8,
    const char * language_utf8,
    int32_t timeout_seconds,
    char ** transcript_utf8) {
    if (transcript_utf8 != nullptr) {
        *transcript_utf8 = nullptr;
    }

    if (engine == nullptr || wav_path_utf8 == nullptr || wav_path_utf8[0] == '\0' || transcript_utf8 == nullptr) {
        return TTS_WHISPER_STATUS_INVALID_ARGUMENT;
    }

    std::lock_guard<std::mutex> guard(engine->mutex);
    engine->last_error.clear();

    if (engine->context == nullptr) {
        set_error(engine, "not-initialized");
        return TTS_WHISPER_STATUS_NOT_INITIALIZED;
    }

    std::vector<float> pcmf32;
    std::vector<std::vector<float>> pcmf32s;
    if (!read_audio_data(std::string(wav_path_utf8), pcmf32, pcmf32s, false)) {
        set_error(engine, "invalid-audio");
        return TTS_WHISPER_STATUS_INVALID_AUDIO;
    }

    auto params = whisper_full_default_params(WHISPER_SAMPLING_GREEDY);
    params.n_threads = default_thread_count();
    params.no_timestamps = true;
    params.print_special = false;
    params.print_progress = false;
    params.print_realtime = false;
    params.print_timestamps = false;
    params.temperature = 0.0f;
    params.language = language_utf8 == nullptr || language_utf8[0] == '\0' ? "en" : language_utf8;
    params.abort_callback = should_abort;
    params.abort_callback_user_data = engine;

    engine->cancel_requested.store(false);
    engine->timeout_reached.store(false);
    engine->has_deadline = timeout_seconds > 0;
    if (engine->has_deadline) {
        engine->deadline = std::chrono::steady_clock::now() + std::chrono::seconds(timeout_seconds);
    }

    const int result = whisper_full(engine->context, params, pcmf32.data(), static_cast<int>(pcmf32.size()));
    engine->has_deadline = false;

    if (engine->cancel_requested.load()) {
        set_error(engine, "transcription-canceled");
        return TTS_WHISPER_STATUS_CANCELED;
    }

    if (engine->timeout_reached.load()) {
        set_error(engine, "transcription-timeout");
        return TTS_WHISPER_STATUS_TIMEOUT;
    }

    if (result != 0) {
        set_error(engine, "transcription-failed");
        return TTS_WHISPER_STATUS_TRANSCRIPTION_FAILED;
    }

    std::string transcript;
    const int segment_count = whisper_full_n_segments(engine->context);
    for (int index = 0; index < segment_count; ++index) {
        const char * segment_text = whisper_full_get_segment_text(engine->context, index);
        if (segment_text != nullptr) {
            transcript += segment_text;
        }
    }

    *transcript_utf8 = copy_to_c_string(transcript);
    if (*transcript_utf8 == nullptr) {
        set_error(engine, "native-engine-failed");
        return TTS_WHISPER_STATUS_NATIVE_FAILURE;
    }

    return TTS_WHISPER_STATUS_OK;
}

TTS_WHISPER_API void tts_whisper_engine_request_cancel(tts_whisper_engine * engine) {
    if (engine != nullptr) {
        engine->cancel_requested.store(true);
    }
}

TTS_WHISPER_API const char * tts_whisper_engine_last_error(tts_whisper_engine * engine) {
    if (engine == nullptr || engine->last_error.empty()) {
        return nullptr;
    }

    return engine->last_error.c_str();
}

TTS_WHISPER_API void tts_whisper_string_free(char * value) {
    std::free(value);
}

TTS_WHISPER_API void tts_whisper_engine_dispose(tts_whisper_engine * engine) {
    if (engine == nullptr) {
        return;
    }

    {
        std::lock_guard<std::mutex> guard(engine->mutex);
        unload_model_no_lock(engine);
    }

    delete engine;
}

}