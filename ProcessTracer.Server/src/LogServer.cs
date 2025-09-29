using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;

namespace ProcessTracer.Server;

public class LogServer(string pipeName) {
    public abstract record ProcessEvent(int ProcessId);

    public record ProcessAttach(int ProcessId) : ProcessEvent(ProcessId);

    public record ProcessDetach(int ProcessId) : ProcessEvent(ProcessId);

    public record ProcessExit(int ProcessId, int ExitCode) : ProcessEvent(ProcessId);

    public record ProcessInvocation(
            int ProcessId,
            int ChildId,
            string? ApplicationName,
            string? CommandLine,
            string WorkingDirectory,
            Dictionary<string, string> Environment) : ProcessEvent(ProcessId);

    private class Client(NamedPipeServerStream pipe, Action<ProcessEvent> eventCb) : IDisposable {
        private readonly LogReader _reader = new(pipe);
        private readonly int _clientId = pipe.GetClientProcessId();

        public void Dispose() {
            _reader.Dispose();
        }

        private async ValueTask ReadMessageAsync(CancellationToken token) {
            var type = await _reader.ReadAsync<ushort>(token);
            switch (type) {
                case 0:
                    eventCb(new ProcessExit(_clientId, await _reader.ReadAsync<int>(token)));
                    break;
                case 1:
                    eventCb(new ProcessInvocation(
                            _clientId,
                            await _reader.ReadAsync<int>(token),
                            await _reader.ReadString(Encoding.Unicode, token),
                            await _reader.ReadString(Encoding.Unicode, token),
                            // the client always sets the working directory
                            (await _reader.ReadString(Encoding.Unicode, token))!,
                            await _reader.ReadEnvironmentBlockAsync(token)));
                    break;
                default:
                    throw new SwitchExpressionException(type);
            }
        }

        public async Task RunAsync(CancellationToken token) {
            eventCb(new ProcessAttach(_clientId));
            while (pipe.IsConnected) {
                try {
                    await ReadMessageAsync(token);
                } catch (EndOfStreamException) {
                    break;
                }
            }
            eventCb(new ProcessDetach(_clientId));
        }
    }

    public async Task RunAsync(Action<ProcessEvent> eventCb, CancellationToken token) {
        while (true) {
            var pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            try {
                await pipeServer.WaitForConnectionAsync(token);
            } catch {
                await pipeServer.DisposeAsync();
                throw;
            }

            // do not block the connection loop
            RunTask(new Client(pipeServer, eventCb).RunAsync(token));
        }
    }

    // we intentionally want an escaped exception to kill the process
    // ReSharper disable once AsyncVoidMethod
    private static async void RunTask(Task t) {
        await t;
    }
}