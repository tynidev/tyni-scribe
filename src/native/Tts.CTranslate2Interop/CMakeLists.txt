cmake_minimum_required(VERSION 3.20)

project(TtsCTranslate2Interop LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(tts-ctranslate2-interop SHARED
    include/tts_ctranslate2_interop.h
    src/tts_ctranslate2_interop.cpp
)

target_include_directories(tts-ctranslate2-interop PRIVATE include)

if (MSVC)
    target_compile_definitions(tts-ctranslate2-interop PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN)
    target_compile_options(tts-ctranslate2-interop PRIVATE /W4 /permissive- /wd4100)
endif()