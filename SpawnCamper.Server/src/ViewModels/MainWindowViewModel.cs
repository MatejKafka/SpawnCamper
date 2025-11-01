using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using SpawnCamper.Core;

namespace SpawnCamper.Server.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged {
    private readonly Dispatcher _dispatcher;
    private readonly TracedProcessTree _processTree;
    private ProcessNodeViewModel? _selectedProcess;

    public MainWindowViewModel(Dispatcher dispatcher, TracedProcessTree processTree) {
        _dispatcher = dispatcher;
        _processTree = processTree;
        RootProcesses = [];

        // Subscribe to root process changes
        if (_processTree.RootProcesses is INotifyCollectionChanged collection) {
            collection.CollectionChanged += OnRootProcessesChanged;
        }

        // Initialize with existing root processes (if any)
        foreach (var node in _processTree.RootProcesses) {
            RootProcesses.Add(new ProcessNodeViewModel(node));
        }
    }

    private void OnRootProcessesChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        if (_dispatcher.CheckAccess()) {
            HandleRootProcessesChanged(e);
        } else {
            _dispatcher.InvokeAsync(() => HandleRootProcessesChanged(e));
        }
    }

    private void HandleRootProcessesChanged(NotifyCollectionChangedEventArgs e) {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null) {
            foreach (TracedProcessTree.Node node in e.NewItems) {
                RootProcesses.Add(new ProcessNodeViewModel(node));
            }
        }
        // Handle other collection change types if needed (Reset, Remove, etc.)
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProcessNodeViewModel> RootProcesses {get;}

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

    private void OnPropertyChanged(string propertyName) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}