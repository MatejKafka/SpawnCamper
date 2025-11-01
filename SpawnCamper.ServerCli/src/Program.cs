using SpawnCamper.Core;
using System.Text.Json;

namespace SpawnCamper.ServerCli;

public static class Program {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = false,
    };

    public static async Task<int> Main(string[] args) {
        var pipeName = args.Length > 0 ? args[0] : "SpawnCamper";

        await Console.Error.WriteLineAsync($"Starting SpawnCamper CLI server (pipe: {pipeName})...");

        var logServer = new LogServer(pipeName);
        var processTree = new TracedProcessTree();
        var cts = new CancellationTokenSource();

        // Handle Ctrl+C gracefully
        Console.CancelKeyPress += (_, e) => {
            e.Cancel = true;
            cts.Cancel();
        };

        var treeMutex = new object();
        try {
            await logServer.RunAsync(evt => {
                // only a single task should work with the processTree in parallel
                lock (treeMutex) {
                    HandleEvent(evt, processTree);
                }
            }, cts.Token);
        } catch (OperationCanceledException) {
            await Console.Error.WriteLineAsync("Server shutting down...");
            return 0;
        } catch (Exception ex) {
            await Console.Error.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        return 0;
    }

    private static void HandleEvent(LogServer.ProcessEvent e, TracedProcessTree processTree) {
        processTree.HandleEvent(e);

        // Output finished invocations
        switch (e) {
            case LogServer.ProcessDetach detach:
                // Process has detached - output if we have info about it
                var process = processTree.GetProcess(detach.ProcessId);
                if (process != null) {
                    OutputFinishedInvocation(process);
                }
                break;
        }
    }

    private static void LogEvent(LogServer.ProcessEvent e) {
        void Log(string message) {
            Console.Error.WriteLine($"[{e.ProcessId}] {message}");
        }

        switch (e) {
            case LogServer.ProcessAttach:
                Log("attach");
                break;
            case LogServer.ProcessDetach:
                Log("detach");
                break;
            case LogServer.ProcessExit:
                Log("exit");
                break;
            case LogServer.ProcessInfo:
                Log("info");
                break;
        }
    }

    private static void OutputFinishedInvocation(TracedProcess process) {
        Console.Out.WriteLine(JsonSerializer.Serialize(new {
            processId = process.ProcessId,
            parentProcessId = process.Parent?.ProcessId,
            exePath = process.ExePath,
            commandLine = process.CommandLine,
            workingDirectory = process.WorkingDirectory,
            environment = process.Environment,
            startTime = process.StartTime,
            endTime = process.EndTime,
            exitCode = process.ExitCode,
        }, JsonOptions));
    }
}