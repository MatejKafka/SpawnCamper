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

        write_message_header(MessageType::ExitProcess);
        write_scalar(exit_code);
    }

    void log_new_process(LPCWSTR exe_path, LPCWSTR cmd_line, LPCWSTR working_dir, LPCWSTR env) {
        std::unique_lock lock(m_pipe_mutex);

        write_message_header(MessageType::ProcessStart);
        write_string(exe_path);
        write_string(cmd_line);
        write_string(working_dir);

        // write environment block
        auto env_size = peb_size(env);
        write_scalar(env_size);
        write(env, env_size);
    }

    void log_CreateProcess(DWORD pid, LPCWSTR lpApplicationName, LPCWSTR lpCommandLine) {
        std::unique_lock lock(m_pipe_mutex);

        write_message_header(MessageType::CreateProcessW);
        write_scalar(pid);
        write_string(lpApplicationName);
        write_string(lpCommandLine);
    }

private:
    enum class MessageType : uint16_t {
        ExitProcess,
        CreateProcessW,
        ProcessStart,
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

    void write_timestamp() {
        FILETIME ft;
        ::GetSystemTimePreciseAsFileTime(&ft);
        write_scalar((uint64_t)ft.dwHighDateTime << 32 | (uint64_t)ft.dwLowDateTime);
    }

    void write_message_header(MessageType msg_type) {
        write_timestamp();
        write_scalar(msg_type);
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
            if (error == ERROR_FILE_NOT_FOUND) {
                throw Win32::Win32Error{error, "ProcessTracer server does not seem to be running"};
            }
            if (error != ERROR_PIPE_BUSY) {
                throw Win32::Win32Error{error};
            }

            // the pipe exists, but all instances are busy; this can intermittently happen just after another client
            //  connects to the server, before it services the connection and reopens another instance of the pipe server
            if (!WaitNamedPipeW(pipe_name.c_str(), NMPWAIT_WAIT_FOREVER)) {
                throw Win32::Win32Error{};
            }
        }
    }

    static HANDLE connect_raw(const std::filesystem::path& pipe_name) {
        return ::CreateFileW(pipe_name.c_str(), GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
    }
};
