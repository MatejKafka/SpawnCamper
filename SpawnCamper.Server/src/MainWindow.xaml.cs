using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using SpawnCamper.Core;
using SpawnCamper.Server.ViewModels;

namespace SpawnCamper.Server;

public partial class MainWindow {
    private readonly CancellationTokenSource _cts = new();
    private readonly LogServer _logServer = new("SpawnCamper");
    private readonly TracedProcessTree _processTree = new();
    private readonly Channel<LogServer.ProcessEvent> _eventChannel =
            Channel.CreateUnbounded<LogServer.ProcessEvent>(new UnboundedChannelOptions {SingleReader = true});

    private readonly MainWindowViewModel _viewModel;
    private Task? _serverTask;
    private Task? _eventProcessingTask;

    public MainWindow() {
        InitializeComponent();
        _viewModel = new MainWindowViewModel(Dispatcher, _processTree);
        DataContext = _viewModel;
    }

    protected override void OnInitialized(EventArgs e) {
        base.OnInitialized(e);
        _eventProcessingTask = ProcessEventsAsync(_cts.Token);
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
        // Handle Ctrl+C to copy full invocation, unless text is selected in a TextBox or FlowDocument
        if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
            // Check if the focused element has selected text
            if (HasSelectedText()) {
                // Let the default copy behavior handle it
                return false;
            }

            var selectedProcess = _viewModel.SelectedProcess;
            if (selectedProcess != null && selectedProcess.CopyFullInvocationCommand.CanExecute(null)) {
                selectedProcess.CopyFullInvocationCommand.Execute(null);
                e.Handled = true;
                return true;
            }
        }
        return false;
    }

    private bool HasSelectedText() {
        var focusedElement = FocusManager.GetFocusedElement(this);

        // Check if a TextBox has selected text
        if (focusedElement is TextBox textBox && !string.IsNullOrEmpty(textBox.SelectedText)) {
            return true;
        }

        // Check if a FlowDocumentScrollViewer or RichTextBox has selected text
        if (focusedElement is FlowDocumentScrollViewer {Selection.IsEmpty: false}) {
            return true;
        }

        // Check for RichTextBox (in case it's used elsewhere)
        if (focusedElement is RichTextBox {Selection.IsEmpty: false}) {
            return true;
        }

        return false;
    }

    private void StartServer() {
        _serverTask = Task.Run(async () => {
            try {
                await _logServer.RunAsync(evt => {
                    // enqueue events without blocking - client processes won't wait for GUI
                    _eventChannel.Writer.TryWrite(evt);
                }, _cts.Token);
            } catch (OperationCanceledException) {
                // expected on shutdown
            } catch (Exception ex) {
                Dispatcher.Invoke(() => ShowServerError(ex));
            }
        });
    }

    private async Task ProcessEventsAsync(CancellationToken token) {
        while (await _eventChannel.Reader.WaitToReadAsync(token)) {
            // batch-process all available events; WPF will only re-render the UI once we let go of the UI thread,
            //  so this effectively batches the processing to avoid expensive repaints
            var i = 0;
            while (_eventChannel.Reader.TryRead(out var e)) {
                i++;
                _processTree.HandleEvent(e);
            }
            Console.Error.WriteLine($"events: {i}");
        }
    }

    private void ShowServerError(Exception exception) {
        MessageBox.Show(this,
                $"An error occurred while processing log events:\n{exception.Message}\n{exception.StackTrace}",
                "SpawnCamper server",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
    }

    protected override async void OnClosed(EventArgs e) {
        await _cts.CancelAsync();

        // signal that no more events will be sent
        _eventChannel.Writer.Complete();

        try {
            if (_serverTask != null) {
                await _serverTask;
            }
            if (_eventProcessingTask != null) {
                await _eventProcessingTask;
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

    private void FlowDocumentScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) {
        // FlowDocumentScrollViewer captures mouse wheel events even with scrolling disabled
        // Forward them to the parent ScrollViewer
        if (sender is not System.Windows.Controls.FlowDocumentScrollViewer viewer) {
            return;
        }

        // Find the parent ScrollViewer
        var scrollViewer = FindVisualParent<ScrollViewer>(viewer);
        if (scrollViewer == null) {
            return;
        }

        // Re-raise the event on the ScrollViewer
        var newEvent = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = scrollViewer
        };
        scrollViewer.RaiseEvent(newEvent);
        e.Handled = true;
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
        while (parent != null) {
            if (parent is T typedParent) {
                return typedParent;
            }
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
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
            List<ProcessNodeViewModel> orderedProcesses) {
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
            _ => orderedProcesses[0],
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