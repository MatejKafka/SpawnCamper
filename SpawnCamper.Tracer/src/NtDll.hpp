#pragma once

#include <stdexcept>
#include <Windows.h>
#include <winternl.h>

namespace NtDll {
    namespace _ {
        inline decltype(&NtQueryInformationProcess) NtQueryInformationProcess;

        template<typename FnT>
        static void ensure_fn_loaded(FnT& fn_ptr, const char* fn_name) {
            if (fn_ptr) return;
            // this handle does not increase refcount, we shouldn't call `FreeLibrary`
            HMODULE hNtdll = GetModuleHandleW(L"ntdll.dll");
            if (!hNtdll) std::abort();
            fn_ptr = (FnT)GetProcAddress(hNtdll, fn_name);
        }
    }

    inline DWORD GetParentProcessId() {
        _::ensure_fn_loaded(_::NtQueryInformationProcess, "NtQueryInformationProcess");

        PROCESS_BASIC_INFORMATION pbi{};
        ULONG len = 0;
        NTSTATUS status = _::NtQueryInformationProcess(
            GetCurrentProcess(), ProcessBasicInformation, &pbi, sizeof(pbi), &len);

        if (!NT_SUCCESS(status)) {
            // don't think we should ever reach this
            throw std::runtime_error("NtQueryInformationProcess call failed");
        }

        // `Reserved3` is the parent process ID
        return (DWORD)(ULONG_PTR)pbi.Reserved3;
    }
}
