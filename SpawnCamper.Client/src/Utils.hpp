#pragma once

#include <cstdio>
#include <cstdlib>
#include <exception>

namespace Utils {
    /// Helper function that aborts the process when an exception is thrown from our code.
    /// This won't be triggered by our Win32 calls, but sometimes we e.g., allocate heap memory etc. through `new`.
    auto catch_abort(auto fn) {
        // ReSharper disable once CppDFAUnreachableCode
        try {
            return fn();
        } catch (const std::exception& e) {
            fprintf(stderr, "SpawnCamper ERROR: %s\n", e.what());
            std::abort();
        } catch (...) {
            fprintf(stderr, "SpawnCamper ERROR: unknown error\n");
            std::abort();
        }
    }
}
