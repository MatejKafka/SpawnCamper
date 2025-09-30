using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SpawnCamper.Server.UI.ViewModels;

namespace SpawnCamper.Server.UI;

public partial class MainWindow {
    private readonly MainWindowViewModel _viewModel;
    private readonly LogServer _logServer = new("SpawnCamper");
    private readonly CancellationTokenSource _cts = new();
    private Task? _serverTask;

    public MainWindow() {
        InitializeComponent();
        _viewModel = new MainWindowViewModel(Dispatcher);
        DataContext = _viewModel;
    }

    protected override void OnInitialized(EventArgs e) {
        base.OnInitialized(e);
        StartServer();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e) {
        if (HandleCopyFullInvocation(e)) {
            return;
        }

        if (HandleProcessNavigation(e)) {
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private bool HandleCopyFullInvocation(KeyEventArgs e) {
        // Handle Ctrl+C to copy full invocation
        if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
            var selectedProcess = _viewModel.SelectedProcess;
            if (selectedProcess != null && selectedProcess.CopyFullInvocationCommand.CanExecute(null)) {
                selectedProcess.CopyFullInvocationCommand.Execute(null);
                e.Handled = true;
                return true;
            }
        }
        return false;
    }

    private void StartServer() {
        _serverTask = Task.Run(async () => {
            try {
                await _logServer.RunAsync(evt => {
                    LogEvent(evt);
                    _viewModel.HandleEvent(evt);
                }, _cts.Token);
            } catch (OperationCanceledException) {
                // expected on shutdown
            } catch (Exception ex) {
                Dispatcher.Invoke(() => ShowError(ex));
            }
        });
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
            case LogServer.ProcessExit exit:
                Log($"ExitProcess({exit.ExitCode})");
                break;
            case LogServer.ProcessCreate invocation:
                Log($"CreateProcess({invocation.ChildId}, \"{invocation.CommandLine}\", \"{invocation.ApplicationName}\")");
                break;
            case LogServer.ProcessStart start:
                Log($"{start}");
                break;
        }
    }

    private void ShowError(Exception exception) {
        MessageBox.Show(this,
                $"An error occurred while processing log events:\n{exception.Message}",
                "SpawnCamper.Server",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
    }

    protected override async void OnClosed(EventArgs e) {
        await _cts.CancelAsync();
        try {
            if (_serverTask != null) {
                await _serverTask;
            }
        } catch {
            // ignored – shutting down
        } finally {
            _cts.Dispose();
        }

        base.OnClosed(e);
    }

    private void ProcessTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
        _viewModel.SelectedProcess = e.NewValue as ProcessNodeViewModel;
    }

    private void SelectableTextBox_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (sender is not TextBox textBox) {
            return;
        }

        if (!textBox.IsKeyboardFocusWithin) {
            textBox.Focus();
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 3) {
            textBox.SelectAll();
            e.Handled = true;
        }
    }

    private bool HandleProcessNavigation(KeyEventArgs e) {
        if (ProcessTree == null) {
            return false;
        }

        if (e.Key != Key.Up && e.Key != Key.Down) {
            return false;
        }

        var orderedProcesses = GetVisibleProcesses();
        if (orderedProcesses.Count == 0) {
            return false;
        }

        var current = _viewModel.SelectedProcess;
        var target = DetermineTargetProcess(e.Key, current, orderedProcesses);

        ProcessTree.Focus();
        SelectProcess(target);
        e.Handled = true;
        return true;
    }

    private static ProcessNodeViewModel DetermineTargetProcess(Key key, ProcessNodeViewModel? current,
            IReadOnlyList<ProcessNodeViewModel> orderedProcesses) {
        if (current == null) {
            return key == Key.Up ? orderedProcesses[^1] : orderedProcesses[0];
        }

        var index = -1;
        for (var i = 0; i < orderedProcesses.Count; i++) {
            if (ReferenceEquals(orderedProcesses[i], current)) {
                index = i;
                break;
            }
        }
        if (index < 0) {
            return orderedProcesses[0];
        }

        return key switch {
            Key.Up when orderedProcesses.Count == 1 => orderedProcesses[0],
            Key.Up when index > 0 => orderedProcesses[index - 1],
            Key.Up => orderedProcesses[0],
            Key.Down when orderedProcesses.Count == 1 => orderedProcesses[0],
            Key.Down when index < orderedProcesses.Count - 1 => orderedProcesses[index + 1],
            Key.Down => orderedProcesses[^1],
            _ => orderedProcesses[0]
        };
    }

    private List<ProcessNodeViewModel> GetVisibleProcesses() {
        var ordered = new List<ProcessNodeViewModel>();

        if (ProcessTree == null) {
            return ordered;
        }

        ProcessTree.UpdateLayout();

        foreach (var item in ProcessTree.Items) {
            if (ProcessTree.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem container) {
                CollectVisibleProcesses(container, ordered);
            }
        }

        if (ordered.Count == 0) {
            foreach (var root in _viewModel.RootProcesses) {
                ordered.Add(root);
            }
        }

        return ordered;
    }

    private static void CollectVisibleProcesses(TreeViewItem container, ICollection<ProcessNodeViewModel> ordered) {
        if (container.DataContext is not ProcessNodeViewModel process) {
            return;
        }

        ordered.Add(process);

        if (!container.IsExpanded) {
            return;
        }

        container.ApplyTemplate();
        container.UpdateLayout();

        foreach (var item in container.Items) {
            if (container.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem childContainer) {
                CollectVisibleProcesses(childContainer, ordered);
            }
        }
    }

    private void SelectProcess(ProcessNodeViewModel target) {
        _viewModel.SelectedProcess = target;

        if (ProcessTree == null) {
            return;
        }

        ProcessTree.UpdateLayout();
        if (FindTreeViewItem(target) is {} container) {
            container.IsSelected = true;
            container.BringIntoView();
            container.Focus();
        }
    }

    private TreeViewItem? FindTreeViewItem(ProcessNodeViewModel target) {
        if (ProcessTree?.ItemContainerGenerator.ContainerFromItem(target) is TreeViewItem direct) {
            return direct;
        }

        if (ProcessTree == null) {
            return null;
        }

        foreach (var item in ProcessTree.Items) {
            if (ProcessTree.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem container) {
                var result = FindTreeViewItem(container, target);
                if (result != null) {
                    return result;
                }
            }
        }

        return null;
    }

    private TreeViewItem? FindTreeViewItem(TreeViewItem container, ProcessNodeViewModel target) {
        if (Equals(container.DataContext, target)) {
            return container;
        }

        container.ApplyTemplate();
        container.UpdateLayout();

        if (container.ItemContainerGenerator.ContainerFromItem(target) is TreeViewItem directChild) {
            return directChild;
        }

        foreach (var item in container.Items) {
            if (container.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem childContainer) {
                var result = FindTreeViewItem(childContainer, target);
                if (result != null) {
                    return result;
                }
            }
        }

        return null;
    }
}