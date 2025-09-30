using System.IO;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;

namespace SpawnCamper.Server;

public class LogServer(string pipeName) {
    public abstract record ProcessEvent(DateTime Timestamp, int ProcessId);

    public record ProcessAttach(DateTime Timestamp, int ProcessId) : ProcessEvent(Timestamp, ProcessId);

    public record ProcessDetach(DateTime Timestamp, int ProcessId) : ProcessEvent(Timestamp, ProcessId);

    public record ProcessExit(DateTime Timestamp, int ProcessId, int ExitCode) : ProcessEvent(Timestamp, ProcessId);

    /// Call to CreateProcess.
    public record ProcessCreate(
            DateTime Timestamp,
            int ProcessId,
            int ChildId,
            string? ApplicationName,
            string? CommandLine) : ProcessEvent(Timestamp, ProcessId);

    /// Log from a started-up process.
    public record ProcessStart(
            DateTime Timestamp,
            int ProcessId,
            string ApplicationName,
            string CommandLine,
            string WorkingDirectory,
            Dictionary<string, string> Environment) : ProcessEvent(Timestamp, ProcessId);

    private class Client(NamedPipeServerStream pipe, Action<ProcessEvent> eventCb) : IDisposable {
        private readonly LogReader _reader = new(pipe);
        private readonly int _clientId = pipe.GetClientProcessId();

        public void Dispose() {
            _reader.Dispose();
        }

        private enum MessageType {
            ExitProcess,
            CreateProcess,
            ProcessStart,
        };

        private async ValueTask ReadMessageAsync(CancellationToken token) {
            var timestamp = DateTime.FromFileTimeUtc((long) await _reader.ReadAsync<ulong>(token));
            var type = (MessageType) await _reader.ReadAsync<ushort>(token);
            switch (type) {
                case MessageType.ExitProcess:
                    eventCb(new ProcessExit(timestamp, _clientId, await _reader.ReadAsync<int>(token)));
                    break;
                case MessageType.CreateProcess:
                    var childId = await _reader.ReadAsync<int>(token);
                    var encoding = await _reader.ReadEncoding(token);
                    eventCb(new ProcessCreate(
                            timestamp,
                            _clientId,
                            childId,
                            await _reader.ReadString(encoding, token),
                            await _reader.ReadString(encoding, token)));
                    break;
                case MessageType.ProcessStart:
                    eventCb(new ProcessStart(
                            timestamp,
                            _clientId,
                            (await _reader.ReadString(Encoding.Unicode, token))!,
                            (await _reader.ReadString(Encoding.Unicode, token))!,
                            (await _reader.ReadString(Encoding.Unicode, token))!,
                            await _reader.ReadEnvironmentBlockAsync(Encoding.Unicode, token)));
                    break;
                default:
                    throw new SwitchExpressionException(type);
            }
        }

        public async Task RunAsync(CancellationToken token) {
            eventCb(new ProcessAttach(DateTime.UtcNow, _clientId));
            while (pipe.IsConnected) {
                try {
                    await ReadMessageAsync(token);
                } catch (EndOfStreamException) {
                    break;
                }
            }
            eventCb(new ProcessDetach(DateTime.UtcNow, _clientId));
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