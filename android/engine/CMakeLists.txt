cmake_minimum_required(VERSION 3.22.1)

project(aria_engine LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

option(ARIA_HOST_TESTS "Build host unit tests instead of the Android library" OFF)

set(ARIA_WARNING_FLAGS -Wall -Wextra -Werror)

if(ARIA_HOST_TESTS)
    include(FetchContent)

    set(INSTALL_GTEST OFF CACHE BOOL "" FORCE)
    FetchContent_Declare(
        googletest
        GIT_REPOSITORY https://github.com/google/googletest.git
        GIT_TAG 03597a01ee50ed33e9dfd640b249b4be3799d395
    )
    FetchContent_MakeAvailable(googletest)

    enable_testing()
    include(GoogleTest)

    add_executable(aria_engine_tests tests/smoke_test.cpp)
    target_compile_options(aria_engine_tests PRIVATE ${ARIA_WARNING_FLAGS})
    target_link_libraries(aria_engine_tests PRIVATE GTest::gtest_main)
    gtest_discover_tests(aria_engine_tests)
elseif(ANDROID)
    find_package(oboe REQUIRED CONFIG)

    set(ARIA_ENGINE_EMPTY_SOURCE "${CMAKE_CURRENT_BINARY_DIR}/aria_engine_empty.cpp")
    file(WRITE "${ARIA_ENGINE_EMPTY_SOURCE}" "")

    add_library(aria_engine SHARED "${ARIA_ENGINE_EMPTY_SOURCE}")
    target_compile_options(aria_engine PRIVATE ${ARIA_WARNING_FLAGS})
    target_link_libraries(aria_engine PRIVATE oboe::oboe)
else()
    message(FATAL_ERROR "Configure with -DARIA_HOST_TESTS=ON on the host or build through Gradle for Android")
endif()
