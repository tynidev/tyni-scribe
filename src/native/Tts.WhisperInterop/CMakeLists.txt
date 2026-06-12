cmake_minimum_required(VERSION 3.20)

project(TtsWhisperInterop LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_SERVER OFF CACHE BOOL "" FORCE)
set(WHISPER_SDL2 OFF CACHE BOOL "" FORCE)
set(WHISPER_CURL OFF CACHE BOOL "" FORCE)
set(GGML_OPENMP OFF CACHE BOOL "" FORCE)

add_subdirectory(third_party/whisper.cpp EXCLUDE_FROM_ALL)

add_library(tts-whisper-interop SHARED
    include/tts_whisper_interop.h
    src/tts_whisper_interop.cpp
    third_party/whisper.cpp/examples/common-whisper.cpp
)

target_include_directories(tts-whisper-interop
    PRIVATE
        include
        third_party/whisper.cpp/include
        third_party/whisper.cpp/examples
)

target_link_libraries(tts-whisper-interop PRIVATE whisper)

if (MSVC)
    target_compile_definitions(tts-whisper-interop PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN)
    target_compile_options(tts-whisper-interop PRIVATE /W4 /permissive- /wd4100 /wd4127 /wd4244 /wd4267)
endif()