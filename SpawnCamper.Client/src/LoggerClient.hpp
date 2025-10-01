#pragma once

#include <filesystem>
#include <mutex>
#include <span>
#include <Windows.h>

#include "NamedPipeWriter.hpp"

class LoggerClient {
    NamedPipeWriter m_writer;
    std::mutex m_pipe_mutex;

public:
    explicit LoggerClient(const std::filesystem::path& pipe_name) : m_writer(pipe_name) {}

    // TODO: buffer the output?

    void log_ExitProcess(DWORD exit_code) {
        std::unique_lock lock(m_pipe_mutex);

        write_message_header(MessageType::ExitProcess);
        write<uint32_t>(exit_code);
        write<uint32_t>(TERMINATOR_MAGIC);
    }

    void log_new_process(LPCWSTR exe_path, LPCWSTR cmd_line, LPCWSTR working_dir, LPCWSTR env) {
        std::unique_lock lock(m_pipe_mutex);

        write_message_header(MessageType::ProcessStart);
        write_string(exe_path);
        write_string(cmd_line);
        write_string(working_dir);
        write_env_block(env);
        write<uint32_t>(TERMINATOR_MAGIC);
    }

    template<typename CharT>
    void log_CreateProcess(DWORD pid, const CharT* lpApplicationName, const CharT* lpCommandLine) {
        std::unique_lock lock(m_pipe_mutex);

        write_message_header(MessageType::CreateProcess_);
        write<uint32_t>(pid);
        // the code page may be set per-process, so we need to store the code page of the original process
        //  so that the server can decode it
        // 1200 = UTF-16LE
        write<uint32_t>(std::is_same_v<CharT, wchar_t> ? 1200 : GetACP());
        write_string(lpApplicationName);
        write_string(lpCommandLine);
        write<uint32_t>(TERMINATOR_MAGIC);
    }

private:
    static constexpr uint32_t TERMINATOR_MAGIC = 0x012345678;
    enum class MessageType : uint16_t {
        ExitProcess,
        CreateProcess_,
        ProcessStart,
    };

    void write(const void* buffer, size_t size) {
        m_writer.write(std::span{(const std::byte*)buffer, size});
    }

    // force the caller to explicitly specify the type
    template<typename T>
    void write(std::type_identity_t<T> value) requires std::is_scalar_v<T> {
        write(&value, sizeof(value));
    }

    void write_timestamp() {
        FILETIME ft;
        ::GetSystemTimePreciseAsFileTime(&ft);
        write<uint64_t>((uint64_t)ft.dwHighDateTime << 32 | (uint64_t)ft.dwLowDateTime);
    }

    void write_message_header(MessageType msg_type) {
        write_timestamp();
        write<MessageType>(msg_type);
    }

    template<typename CharT>
    void write_string(const CharT* str) {
        if (str == nullptr) {
            // 0xff..ff = nullptr
            write<uint64_t>((size_t)-1);
            return;
        }
        auto len = std::char_traits<CharT>::length(str) * sizeof(CharT);
        write<uint64_t>(len);
        write(str, len);
    }

    void write_env_block(const wchar_t* env_block) {
        auto env_size = peb_size(env_block);
        write<uint64_t>(env_size);
        write(env_block, env_size);
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
};
