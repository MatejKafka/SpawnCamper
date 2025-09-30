include(FetchContent)

if(DEFINED Wil_FIND_VERSION)
    set(GIT_REF v${Wil_FIND_VERSION})
else()
    set(GIT_REF master)
endif()

set(WIL_BUILD_TESTS OFF)

FetchContent_Declare(
        Wil
        GIT_REPOSITORY https://github.com/microsoft/wil
        GIT_TAG ${GIT_REF}
)
FetchContent_MakeAvailable(Wil)

add_library(Wil INTERFACE IMPORTED)
target_include_directories(Wil INTERFACE "${wil_SOURCE_DIR}/include")
