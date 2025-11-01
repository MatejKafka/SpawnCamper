using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using SpawnCamper.Core;

namespace SpawnCamper.Server.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged {
    private readonly Dispatcher _dispatcher;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ProcessNodeViewModel> RootProcesses {get;}

    public ProcessNodeViewModel? SelectedProcess {
        get;
        set => UpdateProperty(out field, value);
    }

    public MainWindowViewModel(Dispatcher dispatcher, TracedProcessTree processTree) {
        _dispatcher = dispatcher;
        RootProcesses = [];

        // Subscribe to root process changes
        ((INotifyCollectionChanged)processTree.RootProcesses).CollectionChanged += OnRootProcessesChanged;

        // Initialize with existing root processes (if any)
        foreach (var node in processTree.RootProcesses) {
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

    private void UpdateProperty<T>(out T prop, T value, [CallerMemberName] string propName = "") {
        prop = value;
        // strip `ref ` or `out `
        propName = propName[(propName.IndexOf(' ') + 1)..];
        PropertyChanged?.Invoke(this, new(propName));
    }
}