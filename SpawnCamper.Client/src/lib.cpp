#include <Windows.h>
#include <detours.h>
#include <memory>

#include "Win32.hpp"
#include "NtDll.hpp"
#include "LoggerClient.hpp"
#include "Utils.hpp"

constexpr auto SERVER_PIPE_NAME = LR"(\\.\pipe\SpawnCamper)";

static std::string g_dll_path;
static std::unique_ptr<LoggerClient> g_logger;

namespace Real {
    static auto CreateProcessW = ::CreateProcessW;
    static auto CreateProcessA = ::CreateProcessA;
    static auto ExitProcess = ::ExitProcess;
    static auto TerminateProcess = ::TerminateProcess;
}

namespace Detours {
    static BOOL WINAPI CreateProcessW(
        _In_opt_ LPCWSTR lpApplicationName,
        _Inout_opt_ LPWSTR lpCommandLine,
        _In_opt_ LPSECURITY_ATTRIBUTES lpProcessAttributes,
        _In_opt_ LPSECURITY_ATTRIBUTES lpThreadAttributes,
        _In_ BOOL bInheritHandles,
        _In_ DWORD dwCreationFlags,
        _In_opt_ LPVOID lpEnvironment,
        _In_opt_ LPCWSTR lpCurrentDirectory,
        _In_ LPSTARTUPINFOW lpStartupInfo,
        _Out_ LPPROCESS_INFORMATION lpProcessInformation
    ) {
        auto success = DetourCreateProcessWithDllExW(
            lpApplicationName, lpCommandLine, lpProcessAttributes, lpThreadAttributes, bInheritHandles, dwCreationFlags,
            lpEnvironment, lpCurrentDirectory, lpStartupInfo, lpProcessInformation,
            g_dll_path.c_str(), Real::CreateProcessW);

        if (success) {
            return success;
        }

        auto orig_err = GetLastError();

        // log the failed invocation attempt
        Utils::catch_abort([&] {
            g_logger->log_CreateProcess_failure(lpApplicationName, lpCommandLine);
        });

        SetLastError(orig_err);
        return success;
    }

    static BOOL WINAPI CreateProcessA(
        _In_opt_ LPCSTR lpApplicationName,
        _Inout_opt_ LPSTR lpCommandLine,
        _In_opt_ LPSECURITY_ATTRIBUTES lpProcessAttributes,
        _In_opt_ LPSECURITY_ATTRIBUTES lpThreadAttributes,
        _In_ BOOL bInheritHandles,
        _In_ DWORD dwCreationFlags,
        _In_opt_ LPVOID lpEnvironment,
        _In_opt_ LPCSTR lpCurrentDirectory,
        _In_ LPSTARTUPINFOA lpStartupInfo,
        _Out_ LPPROCESS_INFORMATION lpProcessInformation
    ) {
        auto success = DetourCreateProcessWithDllExA(
            lpApplicationName, lpCommandLine, lpProcessAttributes, lpThreadAttributes, bInheritHandles, dwCreationFlags,
            lpEnvironment, lpCurrentDirectory, lpStartupInfo, lpProcessInformation,
            g_dll_path.c_str(), Real::CreateProcessA);

        if (success) {
            return success;
        }

        auto orig_err = GetLastError();

        // log the failed invocation attempt
        Utils::catch_abort([&] {
            g_logger->log_CreateProcess_failure(lpApplicationName, lpCommandLine);
        });

        SetLastError(orig_err);
        return success;
    }

    static DECLSPEC_NORETURN VOID WINAPI ExitProcess(
        _In_ UINT uExitCode
    ) {
        Utils::catch_abort([&] {
            g_logger->log_ExitProcess(uExitCode);
        });
        Real::ExitProcess(uExitCode);
    }

    static BOOL WINAPI TerminateProcess(
        _In_ HANDLE hProcess,
        _In_ UINT uExitCode
    ) {
        // some processes (like Git's sh.exe, and probably anything using MSYS2)
        //  kill themselves using `TerminateProcess` instead of using `ExitProcess`
        if (hProcess == GetCurrentProcess()) {
            Utils::catch_abort([&] {
                g_logger->log_ExitProcess(uExitCode);
            });
        }
        return Real::TerminateProcess(hProcess, uExitCode);
    }
}

static void setup_detour(bool attach) {
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());

    if (attach) {
        DetourAttach(&Real::CreateProcessW, Detours::CreateProcessW);
        DetourAttach(&Real::CreateProcessA, Detours::CreateProcessA);
        DetourAttach(&Real::ExitProcess, Detours::ExitProcess);
        DetourAttach(&Real::TerminateProcess, Detours::TerminateProcess);
    } else {
        DetourDetach(&Real::CreateProcessW, Detours::CreateProcessW);
        DetourDetach(&Real::CreateProcessA, Detours::CreateProcessA);
        DetourDetach(&Real::ExitProcess, Detours::ExitProcess);
        DetourDetach(&Real::TerminateProcess, Detours::TerminateProcess);
    }

    DetourTransactionCommit();
}

static void log_attach() {
    auto parent_pid = NtDll::GetParentProcessId();
    auto exe_path = Win32::GetModuleFileNameW(nullptr);
    auto working_dir = Win32::GetCurrentDirectoryW();
    auto env = Win32::GetEnvironmentStringsW();
    g_logger->log_new_process(parent_pid, exe_path.c_str(), GetCommandLineW(), working_dir.c_str(), env.get());
}

BOOL WINAPI DllMain(HINSTANCE hInst, DWORD dwReason, LPVOID) {
    if (DetourIsHelperProcess()) {
        return TRUE;
    }

    switch (dwReason) {
        case DLL_PROCESS_ATTACH:
            DetourRestoreAfterWith();
            // disable DLL_THREAD_ATTACH/DLL_THREAD_DETACH callbacks, we don't need them
            DisableThreadLibraryCalls(hInst);

            Utils::catch_abort([&] {
                g_logger = std::make_unique<LoggerClient>(SERVER_PIPE_NAME);
                g_dll_path = Win32::GetModuleFileNameW(hInst).string();
                // send process information to the logger server
                log_attach();
            });

            setup_detour(true);
            break;

        case DLL_PROCESS_DETACH:
            setup_detour(false);
            break;

        default:
            break;
    }
    return TRUE;
}
