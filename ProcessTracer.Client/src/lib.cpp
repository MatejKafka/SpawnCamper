#include <cstdio>
#include <Windows.h>
#include <detours.h>
#include <memory>

#include "Win32.hpp"
#include "LoggerClient.hpp"

static std::string g_dll_path;
static std::unique_ptr<LoggerClient> g_logger;

namespace Real {
    static auto CreateProcessW = ::CreateProcessW;
    static auto ExitProcess = ::ExitProcess;
}

/// Helper function that aborts the process when an exception is thrown from our code.
auto catch_abort(auto fn) {
    // ReSharper disable once CppDFAUnreachableCode
    try {
        return fn();
    } catch (const std::exception& e) {
        fprintf(stderr, "ProcessTracer ERROR: %s\n", e.what());
        std::abort();
    } catch (...) {
        std::abort();
    }
}

#define CATCH_ABORT(fn) catch_abort([&] { return fn; })

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
        auto orig_err = GetLastError();

        // if we just left lpEnvironment at NULL, we'd risk a race condition where the environment could change
        //  between the point where we read it and where CreateProcess does
        auto process_env = lpEnvironment ? nullptr : CATCH_ABORT(Win32::GetEnvironmentStringsW());
        if (!lpEnvironment) {
            dwCreationFlags |= CREATE_UNICODE_ENVIRONMENT;
            lpEnvironment = process_env.get();
        }

        // same for the working directory
        auto working_dir = lpCurrentDirectory
                               ? std::nullopt
                               : std::optional{CATCH_ABORT(Win32::GetCurrentDirectoryW())};
        if (!lpCurrentDirectory) {
            lpCurrentDirectory = working_dir.value().c_str();
        }

        auto success = DetourCreateProcessWithDllExW(
            lpApplicationName, lpCommandLine, lpProcessAttributes, lpThreadAttributes, bInheritHandles, dwCreationFlags,
            lpEnvironment, lpCurrentDirectory, lpStartupInfo, lpProcessInformation,
            g_dll_path.c_str(), Real::CreateProcessW);

        if (!success) {
            // preserve the correct last error
            orig_err = GetLastError();
        }

        // only log the invocation after it finishes, so that we get the process ID
        catch_abort([&] {
            auto pid = success ? lpProcessInformation->dwProcessId : 0;
            if (dwCreationFlags & CREATE_UNICODE_ENVIRONMENT) {
                g_logger->log_CreateProcess(lpApplicationName, lpCommandLine, lpCurrentDirectory,
                                            (wchar_t*)lpEnvironment, pid);
            } else {
                g_logger->log_CreateProcess(lpApplicationName, lpCommandLine, lpCurrentDirectory,
                                            (char*)lpEnvironment, pid);
            }
        });

        SetLastError(orig_err);
        return success;
    }

    static DECLSPEC_NORETURN VOID WINAPI ExitProcess(
        _In_ UINT uExitCode
    ) {
        catch_abort([&] {
            g_logger->log_ExitProcess(uExitCode);
        });
        Real::ExitProcess(uExitCode);
    }
}

static void setup_detour(bool attach) {
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());

    if (attach) {
        DetourAttach(&Real::CreateProcessW, Detours::CreateProcessW);
        DetourAttach(&Real::ExitProcess, Detours::ExitProcess);
    } else {
        DetourDetach(&Real::CreateProcessW, Detours::CreateProcessW);
        DetourDetach(&Real::ExitProcess, Detours::ExitProcess);
    }

    DetourTransactionCommit();
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

            catch_abort([&] {
                g_logger = std::make_unique<LoggerClient>(LR"(\\.\pipe\ProcessTracer-Server)");
                g_dll_path = Win32::GetModuleFileNameW(hInst).string();
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
