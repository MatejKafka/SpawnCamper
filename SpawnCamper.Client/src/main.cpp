#include <Windows.h>
#include <detours.h>
#include <iostream>
#include <string>

#include "Utils.hpp"
#include "Win32.hpp"

static const wchar_t* find_argv1(const wchar_t* cmd_line) {
    // https://learn.microsoft.com/en-us/cpp/c-language/parsing-c-command-line-arguments?view=msvc-170
    // The first argument (argv[0]) is treated specially. It represents the program name. Because it
    // must be a valid pathname, parts surrounded by double quote marks (") are allowed. The double
    // quote marks aren't included in the argv[0] output. The parts surrounded by double quote marks
    // prevent interpretation of a space or tab character as the end of the argument.

    // CommandLineToArgvW treats whitespace outside of quotation marks as argument delimiters.
    // However, if lpCmdLine starts with any amount of whitespace, CommandLineToArgvW will consider
    // the first argument to be an empty string. Excess whitespace at the end of lpCmdLine is ignored.

    // find the end of argv[0]
    auto inside_quotes = false;
    auto it = cmd_line;
    for (; *it != 0; it++) {
        if (*it == L'"') {
            inside_quotes = !inside_quotes;
            continue;
        }
        if (!inside_quotes && (*it == L' ' || *it == L'\t')) {
            // found the end
            break;
        }
    }

    // skip whitespace before the first arg
    while (*it != 0 && (*it == L' ' || *it == L'\t')) it++;

    return it;
}

void real_main() {
    auto exe_path = Win32::GetModuleFileNameW();
    exe_path.replace_filename(L"hook64.dll");
    // Detours takes a `char*` even in the W variant
    auto dll_path_str = exe_path.string();

    auto orig_cmdline = ::GetCommandLineW();
    // skip argv[0], the rest of the command line is invoked as a new process
    auto args = std::wstring(find_argv1(orig_cmdline));

    if (args.empty()) {
        std::cerr << "ERROR: command to run not specified\n";
        exit(1);
    }

    auto startup_info = STARTUPINFO{sizeof(STARTUPINFO)};
    auto process_info = PROCESS_INFORMATION{};
    auto success = DetourCreateProcessWithDllExW(
        nullptr, args.data(),
        nullptr, nullptr, false, 0, nullptr, nullptr,
        &startup_info, &process_info, dll_path_str.c_str(), nullptr);
    if (!success) {
        throw Win32::Win32Error{};
    }

    Win32::WaitForSingleObject(process_info.hProcess);
    ExitProcess(Win32::GetExitCodeProcess(process_info.hProcess));
}

int main() {
    Utils::catch_abort([&] {
        real_main();
    });
}
