using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SpawnCamper.Core;

internal static partial class NamedPipeServerStreamExtensions {
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out int processId);

    public static int GetClientProcessId(this NamedPipeServerStream pipeServer) {
        if (!GetNamedPipeClientProcessId(pipeServer.SafePipeHandle, out var processId)) {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }
        return processId;
    }
}