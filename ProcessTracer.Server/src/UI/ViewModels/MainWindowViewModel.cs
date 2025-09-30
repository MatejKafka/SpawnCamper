using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;

namespace ProcessTracer.Server.UI.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged {
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<int, ProcessNodeViewModel> _processes = new();
    private readonly Dictionary<int, int> _pendingParents = new();
    private readonly HashSet<int> _processesWithData = new();
    private ProcessNodeViewModel? _selectedProcess;

    public MainWindowViewModel(Dispatcher dispatcher) {
        _dispatcher = dispatcher;
        RootProcesses = new ObservableCollection<ProcessNodeViewModel>();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProcessNodeViewModel> RootProcesses { get; }

    public ProcessNodeViewModel? SelectedProcess {
        get => _selectedProcess;
        set {
            if (_selectedProcess == value) {
                return;
            }
            _selectedProcess = value;
            OnPropertyChanged(nameof(SelectedProcess));
        }
    }

    public void HandleEvent(LogServer.ProcessEvent evt) {
        if (_dispatcher.CheckAccess()) {
            HandleEventCore(evt);
        } else {
            _dispatcher.InvokeAsync(() => HandleEventCore(evt));
        }
    }

    private void HandleEventCore(LogServer.ProcessEvent evt) {
        switch (evt) {
            case LogServer.ProcessAttach attach:
                HandleAttach(attach);
                break;
            case LogServer.ProcessCreate create:
                _processesWithData.Add(create.ProcessId);
                HandleCreate(create);
                break;
            case LogServer.ProcessStart start:
                _processesWithData.Add(start.ProcessId);
                HandleStart(start);
                break;
            case LogServer.ProcessExit exit:
                _processesWithData.Add(exit.ProcessId);
                HandleExit(exit);
                break;
            case LogServer.ProcessDetach detach:
                HandleDetach(detach);
                break;
        }
    }

    private void HandleAttach(LogServer.ProcessAttach attach) {
        var process = GetOrCreateProcess(attach.ProcessId);
        process.MarkAttached(attach.Timestamp);
    }

    private void HandleCreate(LogServer.ProcessCreate create) {
        if (create.ChildId == 0) {
            var parent = GetOrCreateProcess(create.ProcessId);
            EnsureProcessVisible(parent);
            var failureNode = ProcessNodeViewModel.FromCreateFailure(create);
            failureNode.AssignParent(parent);
            parent.Children.Add(failureNode);
            return;
        }

        _pendingParents[create.ChildId] = create.ProcessId;
    }

    private void HandleStart(LogServer.ProcessStart start) {
        var process = GetOrCreateProcess(start.ProcessId);
        process.ApplyStartData(start.ApplicationName, start.CommandLine, start.WorkingDirectory, start.Environment);

        if (_pendingParents.TryGetValue(start.ProcessId, out var parentId)) {
            var parent = GetOrCreateProcess(parentId);
            EnsureProcessVisible(parent);
            AttachChild(parent, process);
            _pendingParents.Remove(start.ProcessId);
        } else if (process.Parent != null) {
            // already attached
        } else {
            EnsureProcessVisible(process);
        }
    }

    private void HandleExit(LogServer.ProcessExit exit) {
        var process = GetOrCreateProcess(exit.ProcessId);
        EnsureProcessVisible(process);
        process.MarkExited(exit.ExitCode);
        _pendingParents.Remove(exit.ProcessId);
    }

    private void HandleDetach(LogServer.ProcessDetach detach) {
        // If the process never sent any data beyond attach/detach, ignore it completely
        if (!_processesWithData.Contains(detach.ProcessId)) {
            _processes.Remove(detach.ProcessId);
            _pendingParents.Remove(detach.ProcessId);
            return;
        }

        var process = GetOrCreateProcess(detach.ProcessId);
        EnsureProcessVisible(process);
        process.MarkDetached(detach.Timestamp);
        _pendingParents.Remove(detach.ProcessId);
    }

    private ProcessNodeViewModel GetOrCreateProcess(int processId) {
        if (_processes.TryGetValue(processId, out var process)) {
            return process;
        }

        process = new ProcessNodeViewModel(processId);
        _processes.Add(processId, process);
        return process;
    }

    private void EnsureProcessVisible(ProcessNodeViewModel process) {
        if (process.Parent != null) {
            return;
        }
        if (!RootProcesses.Contains(process)) {
            RootProcesses.Add(process);
        }
    }

    private void AttachChild(ProcessNodeViewModel parent, ProcessNodeViewModel child) {
        if (child.Parent == parent) {
            return;
        }

        if (child.Parent != null) {
            child.Parent.Children.Remove(child);
        } else {
            RootProcesses.Remove(child);
        }

        if (!parent.Children.Contains(child)) {
            parent.Children.Add(child);
        }

        child.AssignParent(parent);
    }

    private void OnPropertyChanged(string propertyName) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}