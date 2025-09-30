#pragma once

#include <expected>
#include <system_error>
#include <filesystem>
#include <span>
#include <Windows.h>

namespace Win32 {
    struct Win32Error final : std::system_error {
        explicit Win32Error(DWORD error_code = GetLastError())
            : std::system_error((int)error_code, std::system_category()) {}

        explicit Win32Error(DWORD error_code, const char* message)
            : std::system_error((int)error_code, std::system_category(), message) {}
    };

    inline std::filesystem::path GetModuleFileNameW(HMODULE hModule = nullptr) {
        auto result = std::wstring{};
        result.resize(MAX_PATH);

        while (true) {
            DWORD actual_length = ::GetModuleFileNameW(hModule, result.data(), (DWORD)result.size());

            if (actual_length == 0) {
                throw Win32Error{};
            }

            if (actual_length < result.size()) {
                // success, buffer is large enough
                // I don't think there's an easy way to `.into()` this
                return std::filesystem::path{std::wstring_view(result.data(), actual_length)};
            }

            // buffer too small, double the size and retry
            result.resize(result.size() * 2);
        }
    }

    inline std::filesystem::path GetCurrentDirectoryW() {
        auto result = std::wstring{};
        result.resize(MAX_PATH);

        while (true) {
            DWORD actual_length = ::GetCurrentDirectoryW((DWORD)result.size(), result.data());

            if (actual_length == 0) {
                throw Win32Error{};
            }

            if (actual_length < result.size()) {
                return std::filesystem::path{std::wstring_view(result.data(), actual_length)};
            }

            result.resize(result.size() * 2);
        }
    }

    inline HANDLE CreateFileW(
        const std::filesystem::path& lpFileName,
        DWORD dwDesiredAccess,
        DWORD dwShareMode,
        LPSECURITY_ATTRIBUTES lpSecurityAttributes,
        DWORD dwCreationDisposition,
        DWORD dwFlagsAndAttributes = 0,
        HANDLE hTemplateFile = nullptr
    ) {
        std::expected<int, Win32Error> x;

        auto handle = ::CreateFileW(
            lpFileName.c_str(),
            dwDesiredAccess,
            dwShareMode,
            lpSecurityAttributes,
            dwCreationDisposition,
            dwFlagsAndAttributes,
            hTemplateFile
        );
        if (handle == INVALID_HANDLE_VALUE) {
            throw Win32Error{};
        }
        return handle;
    }

    inline HANDLE CreateFileW(
        const std::filesystem::path& lpFileName,
        DWORD dwDesiredAccess,
        DWORD dwShareMode,
        DWORD dwCreationDisposition
    ) {
        return CreateFileW(lpFileName, dwDesiredAccess, dwShareMode, nullptr, dwCreationDisposition);
    }

    inline void CloseHandle(HANDLE handle) {
        if (!::CloseHandle(handle)) {
            throw Win32Error{};
        }
    }

    inline size_t WriteFile(HANDLE handle, std::span<const std::byte> buffer, LPOVERLAPPED overlapped = nullptr) {
        DWORD bytes_written;
        if (!::WriteFile(handle, buffer.data(), (DWORD)buffer.size_bytes(), &bytes_written, overlapped)) {
            throw Win32Error{};
        }
        return bytes_written;
    }

    inline bool WaitForSingleObject(HANDLE handle, DWORD timeout_ms = INFINITE) {
        switch (::WaitForSingleObject(handle, timeout_ms)) {
            case WAIT_ABANDONED:
            case WAIT_OBJECT_0:
                return true;
            case WAIT_TIMEOUT:
                return false;
            default:
                throw Win32Error{};
        }
    }

    inline DWORD GetExitCodeProcess(HANDLE handle) {
        DWORD exit_code;
        if (!::GetExitCodeProcess(handle, &exit_code)) {
            throw Win32Error{};
        }
        return exit_code;
    }

    inline HANDLE GetStdHandle(DWORD std_handle) {
        auto handle = ::GetStdHandle(std_handle);
        if (handle == INVALID_HANDLE_VALUE) {
            throw Win32Error{};
        }
        return handle;
    }

    inline auto GetEnvironmentStringsW() {
        auto env = ::GetEnvironmentStringsW();
        if (env == nullptr) {
            throw Win32Error{};
        }
        auto deleter = [](wchar_t* p) { FreeEnvironmentStringsW(p); };
        return std::unique_ptr<wchar_t, decltype(deleter)>{env, deleter};
    }

    /// Iterator over an environment block (null-terminated list of null-terminated strings).
    template<typename CharT>
    class PebIterator {
        CharT* m_ptr;

    public:
        explicit PebIterator(CharT* env) : m_ptr(env) {}

        auto begin() const { return *this; }
        auto end() const { return PebIterator(nullptr); }

        PebIterator& operator++() {
            while (*m_ptr != 0) ++m_ptr;
            ++m_ptr;
            return *this;
        }

        bool operator==(const PebIterator& other) const {
            if (other.m_ptr == nullptr && *m_ptr == 0) return true;
            return other.m_ptr == m_ptr;
        }

        bool operator!=(const PebIterator& other) const {
            return !(*this == other);
        }

        const CharT* operator*() const { return m_ptr; }
    };
}
