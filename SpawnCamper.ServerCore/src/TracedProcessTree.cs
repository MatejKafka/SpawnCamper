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

    public ReadOnlyObservableCollection<Node> RootProcesses {get;}

    public TracedProcessTree() {
        RootProcesses = new ReadOnlyObservableCollection<Node>(_rootProcesses);
    }

    public TracedProcess? GetProcess(int id) => _pidMap.Get(id)?.Process;

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
                // recording depth is useful in the GUI tree rendering, although it slightly breaks the abstraction
                var node = new Node(newProcess, [], parent == null ? 0 : parent.Value.Depth + 1);

                _pidMap[i.ProcessId] = node;
                if (parent == null) {
                    _rootProcesses.Add(node);
                } else {
                    parent.Value.Children.Add(node);
                }
                break;
            }

            case LogServer.ProcessExit ex: {
                _pidMap[e.ProcessId].Process.ExitCode = ex.ExitCode;
                break;
            }

            default: {
                throw new ArgumentOutOfRangeException(nameof(e));
            }
        }
    }

    /// A tree node type that separates the process information from the hierarchy.
    public record struct Node(TracedProcess Process, ObservableCollection<Node> Children, uint Depth);
}