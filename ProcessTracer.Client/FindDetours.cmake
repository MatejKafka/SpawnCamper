include(FetchContent)

if(DEFINED Detours_FIND_VERSION)
    set(GIT_REF v${Detours_FIND_VERSION})
else()
    set(GIT_REF main)
endif()

FetchContent_Declare(
        Detours
        GIT_REPOSITORY https://github.com/microsoft/Detours
        GIT_TAG ${GIT_REF}
)
FetchContent_MakeAvailable(Detours)

if(CMAKE_SIZEOF_VOID_P EQUAL 8)
    set(LIB_PATH "${detours_SOURCE_DIR}/lib.X64/detours.lib")
elseif(CMAKE_SIZEOF_VOID_P EQUAL 4)
    set(LIB_PATH "${detours_SOURCE_DIR}/lib.X86/detours.lib")
endif()

add_custom_command(
        OUTPUT ${LIB_PATH}
        COMMAND nmake
        # the top-level Makefile invokes the one in `src`, and it doesn't understand the actual targets
        WORKING_DIRECTORY "${detours_SOURCE_DIR}/src"
        COMMENT "Building detours.lib using 'nmake'"
        VERBATIM
)

add_library(Detours STATIC IMPORTED)
# no idea if this makes sense, but it seems to work
target_sources(Detours INTERFACE ${LIB_PATH})
set_target_properties(Detours PROPERTIES IMPORTED_LOCATION ${LIB_PATH})
target_include_directories(Detours INTERFACE "${detours_SOURCE_DIR}/src")
