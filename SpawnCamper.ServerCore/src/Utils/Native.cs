using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SpawnCamper.Core.Utils;

public class Native {
    public static int GetNamedPipeClientProcessId(SafePipeHandle pipe) {
        if (!Win32.GetNamedPipeClientProcessId(pipe, out var processId)) {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }
        return processId;
    }

    public static unsafe void LocalFree(void* hMem) {
        if (Win32.LocalFree(hMem) != null) {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }
    }

    public static unsafe string[] CommandLineToArgv(string lpCmdLine) {
        lpCmdLine = lpCmdLine.Trim();
        if (lpCmdLine == "") {
            return [];
        }

        var ptr = Win32.CommandLineToArgvW(lpCmdLine, out var count);
        if (ptr == null) {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            throw new UnreachableException();
        }

        try {
            var argv = new string[count];
            for (var i = 0; i < count; i++) {
                argv[i] = new string(ptr[i]);
            }
            return argv;
        } finally {
            LocalFree(ptr);
        }
    }
}