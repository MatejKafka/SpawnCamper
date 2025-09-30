cmake_minimum_required(VERSION 3.31)
project(SpawnCamper)

set(CMAKE_CXX_STANDARD 23)

list(APPEND CMAKE_MODULE_PATH ${CMAKE_CURRENT_LIST_DIR})

find_package(Detours REQUIRED)
link_libraries(Detours)


add_compile_definitions(UNICODE _UNICODE)
add_compile_definitions(WIN32_LEAN_AND_MEAN NOMINMAX)

# enable more warnings
add_compile_options(/W4)
add_compile_options(/analyze)
# enable LTO
set(CMAKE_INTERPROCEDURAL_OPTIMIZATION_RELEASE TRUE)
# disable incremental linking
add_link_options(/INCREMENTAL:NO)
# link msvc runtime library statically
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

# disable C++ module scanning (it's quite slow)
set(CMAKE_CXX_SCAN_FOR_MODULES OFF)

set(CMAKE_RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_SOURCE_DIR}/../bin")


add_library(hook SHARED src/lib.cpp)
# this function must be exported to make 64<->32 bit DLL loading work
# https://github.com/microsoft/Detours/wiki/OverviewHelpers
target_link_options(hook PRIVATE "/export:DetourFinishHelperProcess,@1,NONAME")

add_executable(SpawnCamper src/main.cpp)
add_dependencies(SpawnCamper hook)

if(CMAKE_SIZEOF_VOID_P EQUAL 8)
    set_target_properties(hook PROPERTIES OUTPUT_NAME "hook64")
elseif(CMAKE_SIZEOF_VOID_P EQUAL 4)
    set_target_properties(SpawnCamper PROPERTIES RUNTIME_OUTPUT_DIRECTORY ".")
    set_target_properties(hook PROPERTIES OUTPUT_NAME "hook32")
endif()
