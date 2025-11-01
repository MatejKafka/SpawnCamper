using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SpawnCamper.Core.Utils;

public static partial class Win32 {
    [LibraryImport("shell32.dll", SetLastError = true)]
    public static unsafe partial char** CommandLineToArgvW(
            [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static unsafe partial void* LocalFree(void* hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out int processId);
}