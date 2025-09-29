#pragma once

#include <filesystem>
#include <mutex>
#include <span>
#include <Windows.h>

#include "Win32.hpp"

class LoggerClient {
    HANDLE m_output_handle;
    std::mutex m_pipe_mutex;

public:
    explicit LoggerClient(const std::filesystem::path& pipe_name) : m_output_handle(connect_to_server(pipe_name)) {}

    ~LoggerClient() {
        Win32::CloseHandle(m_output_handle);
    }

    // TODO: buffer the output?

    void log_ExitProcess(DWORD exit_code) {
        std::unique_lock lock(m_pipe_mutex);

        write_scalar((uint16_t)MessageType::ExitProcess);
        write_scalar(exit_code);
    }

    template<typename EnvCharT>
    void log_CreateProcess(LPCWSTR lpApplicationName, LPCWSTR lpCommandLine, LPCWSTR lpCurrentDirectory,
                           const EnvCharT* lpEnvironment, DWORD pid) {
        std::unique_lock lock(m_pipe_mutex);

        write_scalar((uint16_t)MessageType::CreateProcessW);
        write_scalar(pid);
        write_string(lpApplicationName);
        write_string(lpCommandLine);
        write_string(lpCurrentDirectory);

        // write environment block, in the original encoding
        auto env_size = peb_size(lpEnvironment);
        // the code page may be set per-process, so we need to store the code page of the original process
        //  so that the server can decode it
        // 1200 = UTF-16LE
        write_scalar(std::is_same_v<EnvCharT, wchar_t> ? 1200 : GetACP());
        write_scalar(env_size);
        write(lpEnvironment, env_size);
    }

private:
    enum class MessageType : uint16_t {
        ExitProcess,
        CreateProcessW,
    };

    // ReSharper disable once CppMemberFunctionMayBeConst
    void write(std::span<const std::byte> buffer) {
        do {
            auto bytes_written = Win32::WriteFile(m_output_handle, buffer);
            buffer = buffer.subspan(bytes_written);
        } while (!buffer.empty());
    }

    void write(const void* buffer, size_t size) {
        write(std::span{(const std::byte*)buffer, size});
    }

    void write_scalar(auto value) {
        write(&value, sizeof(value));
    }

    template<typename CharT>
    void write_string(const CharT* str) {
        if (str == nullptr) {
            // 0xff..ff = nullptr
            write_scalar((size_t)-1);
            return;
        }
        auto len = std::char_traits<CharT>::length(str) * sizeof(CharT);
        write_scalar(len);
        write(str, len);
    }

    /// Returns the size in bytes of a process environment block, excluding the final null terminator.
    size_t peb_size(const auto* peb) {
        auto orig = peb;
        while (*peb) {
            // skip next string
            while (*peb != 0) ++peb;
            ++peb;
        }
        return (peb - orig) * sizeof(*peb);
    }

    static HANDLE connect_to_server(const std::filesystem::path& pipe_name) {
        while (true) {
            auto handle = connect_raw(pipe_name);
            if (handle != INVALID_HANDLE_VALUE) {
                return handle;
            }

            auto error = ::GetLastError();
            if (error != ERROR_PIPE_BUSY) {
                Win32::propagate_win32_error(error);
            }

            // the pipe exists, but all instances are busy; this can intermittently happen just after another client
            //  connects to the server, before it services the connection and reopens another instance of the pipe server
            if (!WaitNamedPipeW(pipe_name.c_str(), NMPWAIT_WAIT_FOREVER)) {
                Win32::propagate_win32_error();
            }
        }
    }

    static HANDLE connect_raw(const std::filesystem::path& pipe_name) {
        return ::CreateFileW(pipe_name.c_str(), GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
    }
};
