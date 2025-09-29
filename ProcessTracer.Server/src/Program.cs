using ProcessTracer.Server;

void Log(LogServer.ProcessEvent e, string message) {
    Console.Error.WriteLine($"[{e.ProcessId}] {message}");
}

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) => {
    args.Cancel = true;
    cts.Cancel();
};

try {
    await new LogServer("ProcessTracer-Server").RunAsync(e => {
        switch (e) {
            case LogServer.ProcessAttach:
                Log(e, "attach");
                break;
            case LogServer.ProcessDetach:
                Log(e, "detach");
                break;
            case LogServer.ProcessExit exit:
                Log(e, $"exit {exit.ExitCode}");
                break;
            case LogServer.ProcessInvocation invocation:
                Log(e, $"child process {invocation.ChildId}: {invocation.CommandLine} " +
                       $"({invocation.ApplicationName}, {invocation.Environment.Count} env vars)");
                break;
        }
    }, cts.Token);
} catch (OperationCanceledException) {
    Console.Error.WriteLine("Received Ctrl-C, exiting...");
    // default Ctrl-C exit code
    Environment.Exit(-1073741510);
}