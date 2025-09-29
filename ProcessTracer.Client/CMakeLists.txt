cmake_minimum_required(VERSION 3.31)
project(ProcessTracer)

set(CMAKE_CXX_STANDARD 23)

list(APPEND CMAKE_MODULE_PATH ${CMAKE_CURRENT_LIST_DIR})

find_package(Detours REQUIRED)
link_libraries(Detours)

#find_package(Wil REQUIRED)
#link_libraries(Wil)


add_compile_definitions(UNICODE _UNICODE)
add_compile_definitions(WIN32_LEAN_AND_MEAN NOMINMAX)

# enable more warnings
add_compile_options(/W4)
add_compile_options(/analyze)
# enable LTO
set(CMAKE_INTERPROCEDURAL_OPTIMIZATION_RELEASE TRUE)
# link msvc runtime library statically
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

# disable C++ module scanning (it's quite slow)
set(CMAKE_CXX_SCAN_FOR_MODULES OFF)

set(CMAKE_RUNTIME_OUTPUT_DIRECTORY_DEBUG "../target/Debug")
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY_RELEASE "../target/Release")

add_link_options(/INCREMENTAL:NO)


add_library(InjectedLib SHARED src/lib.cpp)
# this function must be exported to make 64<->32 bit DLL loading work
# https://github.com/microsoft/Detours/wiki/OverviewHelpers
target_link_options(InjectedLib PRIVATE "/export:DetourFinishHelperProcess,@1,NONAME")

add_executable(ProcessTracer src/main.cpp)
add_dependencies(ProcessTracer InjectedLib)
add_compile_definitions(ProcessTracer PUBLIC WIN32_EXCEPTIONS)

if(CMAKE_SIZEOF_VOID_P EQUAL 8)
    set_target_properties(InjectedLib PROPERTIES OUTPUT_NAME "InjectedLib64")
elseif(CMAKE_SIZEOF_VOID_P EQUAL 4)
    set_target_properties(ProcessTracer PROPERTIES RUNTIME_OUTPUT_DIRECTORY_DEBUG ".")
    set_target_properties(InjectedLib PROPERTIES OUTPUT_NAME "InjectedLib32")
endif()
