using System.Collections.ObjectModel;

namespace SpawnCamper.Core;

internal static class DictionaryExtensions {
    public static TValue? Get<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key) where TValue : struct {
        return dictionary.TryGetValue(key, out var value) ? value : null;
    }
}

public class TracedProcessTree {
    private readonly Dictionary<int, Node> _pidMap = [];
    private readonly ObservableCollection<Node> _rootProcesses = [];

    public TracedProcessTree() {
        RootProcesses = new ReadOnlyObservableCollection<Node>(_rootProcesses);
    }

    public ReadOnlyObservableCollection<Node> RootProcesses { get; }

    public void HandleEvent(LogServer.ProcessEvent e) {
        switch (e) {
            case LogServer.ProcessAttach: {
                // ignore attach, since we receive this event even when a process is just checking
                //  whether the server pipe exists
                break;
            }

            case LogServer.ProcessDetach: {
                // if the process is not recorded yet, ignore this, probably just an existence check on the pipe
                if (_pidMap.TryGetValue(e.ProcessId, out var node)) {
                    node.Process.EndTime = e.Timestamp;
                }
                break;
            }

            case LogServer.ProcessInfo i: {
                var parent = _pidMap.Get(i.ParentProcessId);
                var newProcess = new TracedProcess(
                        i.ProcessId, parent?.Process, i.Timestamp,
                        i.ExePath, i.CommandLine, i.WorkingDirectory, i.Environment);
                var node = new Node(newProcess, []);

                _pidMap[i.ProcessId] = node;
                parent?.Children.Add(new(node));
                if (parent == null) {
                    _rootProcesses.Add(node);
                }
                break;
            }

            case LogServer.ProcessExit ex: {
                _pidMap[e.ProcessId].Process.ExitCode = ex.ExitCode;
                break;
            }

            case LogServer.ProcessCreateFailure c: {
                _pidMap[e.ProcessId].Children.Add(new(c.ExePath, c.CommandLine));
                break;
            }

            default: {
                throw new ArgumentOutOfRangeException(nameof(e));
            }
        }
    }

    /// A tree node type that separates the process information from the hierarchy.
    public record struct Node(TracedProcess Process, ObservableCollection<ProcessInvocation> Children);

    public record struct FailedInvocation(string? ExePath, string? CommandLine);

    /// Discriminated union of either a child process or a failed process invocation.
    public readonly struct ProcessInvocation {
        public readonly Node? Child = null;
        private readonly FailedInvocation _failedInvocation = default;

        public bool Success => Child != null;
        public FailedInvocation? FailedInvocation => Success ? null : _failedInvocation;

        public ProcessInvocation(Node? child) {
            Child = child;
        }

        public ProcessInvocation(string? exePath, string? cmdLine) {
            _failedInvocation = new FailedInvocation(exePath, cmdLine);
        }
    }
}