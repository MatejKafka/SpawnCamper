#pragma once

#include "Win32.hpp"

class NamedPipeWriter {
    HANDLE m_output_handle;

public:
    explicit NamedPipeWriter(const std::filesystem::path& pipe_name) : m_output_handle(connect_to_server(pipe_name)) {}

    ~NamedPipeWriter() {
        if (m_output_handle) {
            Win32::CloseHandle(m_output_handle);
        }
    }

    void close() {
        if (m_output_handle) {
            Win32::CloseHandle(m_output_handle);
            m_output_handle = nullptr;
        }
    }

    void write(std::span<const std::byte> buffer) {
        if (!m_output_handle) {
            return; // the reader was stopped, skip writes and silently continue
        }

        try {
            write_inner(buffer);
        } catch (const Win32::Win32Error& e) {
            auto err = e.code().value();
            if (err == ERROR_BROKEN_PIPE || err == ERROR_NO_DATA) {
                close(); // the reader was stopped, skip writes and silently continue
                return;
            }
            throw;
        }
    }

private:
    // ReSharper disable once CppMemberFunctionMayBeConst
    void write_inner(std::span<const std::byte> buffer) {
        do {
            auto bytes_written = Win32::WriteFile(m_output_handle, buffer);
            buffer = buffer.subspan(bytes_written);
        } while (!buffer.empty());
    }

    static HANDLE connect_to_server(const std::filesystem::path& pipe_name) {
        while (true) {
            auto handle = connect_raw(pipe_name);
            if (handle != INVALID_HANDLE_VALUE) {
                return handle;
            }

            auto error = ::GetLastError();
            if (error == ERROR_FILE_NOT_FOUND) {
                throw Win32::Win32Error{error, "SpawnCamper server does not seem to be running"};
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
