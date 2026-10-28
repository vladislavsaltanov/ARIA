cmake_minimum_required(VERSION 3.22.1)

project(aria_engine LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

option(ARIA_HOST_TESTS "Build host unit tests instead of the Android library" OFF)
option(ARIA_SANITIZE_THREAD "Build host unit tests with ThreadSanitizer" OFF)

set(ARIA_WARNING_FLAGS -Wall -Wextra -Werror)
set(ARIA_ENGINE_SOURCES
    src/click.cpp
    src/envelope.cpp
    src/pcm_pool.cpp
    src/plan_decoder.cpp
    src/renderer.cpp
)

if(ARIA_HOST_TESTS)
    include(FetchContent)

    if(ARIA_SANITIZE_THREAD)
        add_compile_options(-fsanitize=thread -fno-omit-frame-pointer -g -O1)
        add_link_options(-fsanitize=thread)
    endif()

    set(INSTALL_GTEST OFF CACHE BOOL "" FORCE)
    FetchContent_Declare(
        googletest
        GIT_REPOSITORY https://github.com/google/googletest.git
        GIT_TAG 03597a01ee50ed33e9dfd640b249b4be3799d395
    )
    set(JSON_BuildTests OFF CACHE BOOL "" FORCE)
    set(JSON_Install OFF CACHE BOOL "" FORCE)
    FetchContent_Declare(
        nlohmann_json
        URL https://github.com/nlohmann/json/releases/download/v3.11.3/json.tar.xz
        URL_HASH SHA256=d6c65aca6b1ed68e7a182f4757257b107ae403032760ed6ef121c9d55e81757d
    )
    FetchContent_MakeAvailable(googletest nlohmann_json)

    find_package(Threads REQUIRED)

    enable_testing()
    include(GoogleTest)

    get_filename_component(ARIA_FIXTURES_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../tests/fixtures/engine" ABSOLUTE)
    get_filename_component(ARIA_PARITY_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../tests/fixtures/parity" ABSOLUTE)

    add_executable(aria_engine_tests
        tests/click_test.cpp
        tests/envelope_test.cpp
        tests/pcm_pool_test.cpp
        tests/plan_decoder_test.cpp
        tests/renderer_voice_test.cpp
        ${ARIA_ENGINE_SOURCES}
    )
    target_include_directories(aria_engine_tests PRIVATE src)
    target_compile_options(aria_engine_tests PRIVATE ${ARIA_WARNING_FLAGS})
    target_compile_definitions(aria_engine_tests PRIVATE ARIA_FIXTURES_DIR="${ARIA_FIXTURES_DIR}" ARIA_PARITY_DIR="${ARIA_PARITY_DIR}")
    target_link_libraries(aria_engine_tests PRIVATE GTest::gtest_main nlohmann_json::nlohmann_json Threads::Threads)
    gtest_discover_tests(aria_engine_tests PROPERTIES TIMEOUT 30)
elseif(ANDROID)
    find_package(oboe REQUIRED CONFIG)

    add_library(aria_engine SHARED ${ARIA_ENGINE_SOURCES})
    target_include_directories(aria_engine PRIVATE src)
    target_compile_options(aria_engine PRIVATE ${ARIA_WARNING_FLAGS})
    target_link_libraries(aria_engine PRIVATE oboe::oboe)
else()
    message(FATAL_ERROR "Configure with -DARIA_HOST_TESTS=ON on the host or build through Gradle for Android")
endif()
