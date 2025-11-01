using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using SpawnCamper.Core;
using SpawnCamper.Server.Utils;

namespace SpawnCamper.Server.ViewModels;

/// <summary>
/// ViewModel wrapper for TracedProcessTree.Node, representing a successfully created process in the tree view.
/// </summary>
public class ProcessNodeViewModel : INotifyPropertyChanged {
    private readonly TracedProcessTree.Node _node;

    public ProcessNodeViewModel(TracedProcessTree.Node node) {
        _node = node;

        // Subscribe to child collection changes
        _node.Children.CollectionChanged += OnChildrenCollectionChanged;
        // Initialize children
        foreach (var childNode in _node.Children) {
            Children.Add(new ProcessNodeViewModel(childNode));
        }
        // Subscribe to property changes on the process
        _node.Process.PropertyChanged += OnProcessPropertyChanged;
    }

    private void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null) {
            foreach (TracedProcessTree.Node node in e.NewItems) {
                Children.Add(new ProcessNodeViewModel(node));
            }
        }
    }

    private void OnProcessPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        // Forward property changes from the model to the UI
        switch (e.PropertyName) {
            case nameof(TracedProcess.ExitCode):
                OnPropertyChanged(nameof(ExitCode));
                OnPropertyChanged(nameof(ExitStatusDisplay));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsDetached));
                OnPropertyChanged(nameof(HasFailed));
                OnPropertyChanged(nameof(DurationDisplay)); // Duration may change when exit code is set
                break;
            case nameof(TracedProcess.EndTime):
                OnPropertyChanged(nameof(EndTime));
                OnPropertyChanged(nameof(DetachTimeDisplay));
                OnPropertyChanged(nameof(DurationDisplay));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsDetached));
                OnPropertyChanged(nameof(ExitStatusDisplay)); // Exit display depends on IsActive and IsDetached
                break;
        }
    }

    private void OnPropertyChanged(string propertyName) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private TracedProcess Process => _node.Process;
    public int ProcessId => Process.ProcessId;
    public double IndentWidth => _node.Depth * 18.0; // 18 pixels per level
    public ObservableCollection<ProcessNodeViewModel> Children {get;} = [];

    public Dictionary<string, string> EnvironmentVariables => Process.Environment;

    [field: AllowNull, MaybeNull]
    public List<EnvironmentVariableDifference> EnvironmentDifferences {
        get {
            if (field != null) {
                return field;
            }
            if (Process.EnvironmentDiff == null) {
                return [];
            }

            field = [];
            foreach (var (key, (parent, child)) in Process.EnvironmentDiff) {
                if (parent != null) {
                    field.Add(new EnvironmentVariableDifference(key, parent, EnvironmentVariableDiffKind.Removed));
                }
                if (child != null) {
                    field.Add(new EnvironmentVariableDifference(key, child, EnvironmentVariableDiffKind.Added));
                }
            }
            return field;
        }
    }

    public string ApplicationName => Process.ExePath;
    public string CommandLine => Process.CommandLine;
    public string WorkingDirectory => Process.WorkingDirectory;
    public bool IsActive => Process.EndTime == null;
    public bool IsDetached => Process is {EndTime: not null, ExitCode: null};
    public int? ExitCode => Process.ExitCode;
    public bool HasFailed => ExitCode.HasValue && ExitCode.Value != 0;
    public DateTime StartTime => Process.StartTime;
    public DateTime? EndTime => Process.EndTime;

    public string ExitStatusDisplay {
        get {
            if (IsActive) return "Running...";
            if (ExitCode.HasValue) return ExitCode.Value.ToString();
            if (IsDetached) return "Unknown (terminated)";
            return "—";
        }
    }

    public string AttachTimeDisplay => StartTime.ToLocalTime().ToString("HH:mm:ss.fff");
    public string DetachTimeDisplay => EndTime?.ToLocalTime().ToString("HH:mm:ss.fff") ?? "—";

    public string DurationDisplay {
        get {
            if (EndTime == null) {
                return "—";
            }

            var duration = EndTime.Value - StartTime;
            if (duration.TotalDays >= 1) {
                return $"{duration.TotalDays:F2} days";
            }
            if (duration.TotalHours >= 1) {
                return $"{duration.TotalHours:F2} hours";
            }
            if (duration.TotalMinutes >= 1) {
                return $"{duration.TotalMinutes:F2} minutes";
            }
            if (duration.TotalSeconds >= 1) {
                return $"{duration.TotalSeconds:F2} seconds";
            }
            return $"{duration.TotalMilliseconds:F0} ms";
        }
    }

    public bool HasEnvironment => Process.Environment.Count > 0;
    public bool HasEnvironmentDifferences => Process.EnvironmentDiff is {Count: > 0};

    public string BinaryNameDisplay => Path.GetFileName(ApplicationName);

    // Commands
    [field: AllowNull, MaybeNull]
    public ICommand CopyCommandLineAsPowerShellCommand => field ??= new RelayCommand(
            () => Clipboard.SetText(ConvertCommandLineToPowerShell(ApplicationName, CommandLine)),
            () => !string.IsNullOrWhiteSpace(CommandLine));

    [field: AllowNull, MaybeNull]
    public ICommand CopyEnvironmentDifferencesAsPowerShellCommand => field ??= new RelayCommand(
            () => Clipboard.SetText(ConvertEnvironmentDifferencesToPowerShell(Process.EnvironmentDiff!)),
            () => HasEnvironmentDifferences);

    [field: AllowNull, MaybeNull]
    public ICommand CopyFullEnvironmentAsPowerShellCommand => field ??= new RelayCommand(
            () => Clipboard.SetText(ConvertFullEnvironmentToPowerShell(EnvironmentVariables)),
            () => HasEnvironment);

    [field: AllowNull, MaybeNull]
    public ICommand CopyFullInvocationCommand => field ??= new RelayCommand(
            () => Clipboard.SetText(ConvertFullInvocationToPowerShell(
                    ApplicationName, EnvironmentVariables, WorkingDirectory, CommandLine)),
            () => HasEnvironment && !string.IsNullOrWhiteSpace(CommandLine));

    [field: AllowNull, MaybeNull]
    public ICommand LaunchInWinDbgCommand => field ??= new RelayCommand(
            () => LaunchInWinDbg(ApplicationName, EnvironmentVariables, WorkingDirectory, CommandLine, breakOnStart: false),
            () => !string.IsNullOrWhiteSpace(CommandLine));

    [field: AllowNull, MaybeNull]
    public ICommand LaunchInWinDbgBreakCommand => field ??= new RelayCommand(
            () => LaunchInWinDbg(ApplicationName, EnvironmentVariables, WorkingDirectory, CommandLine, breakOnStart: true),
            () => !string.IsNullOrWhiteSpace(CommandLine));

    [field: AllowNull, MaybeNull]
    public ICommand LaunchInVisualStudioCommand => field ??= new RelayCommand(
            () => LaunchInVisualStudio(ApplicationName, EnvironmentVariables, WorkingDirectory, CommandLine),
            () => !string.IsNullOrWhiteSpace(CommandLine));

    private static string ConvertCommandLineToPowerShell(string applicationName, string commandLine) {
        return new PowerShellScriptBuilder()
                .AppendInvocation(applicationName, commandLine)
                .ToString();
    }

    private static string ConvertEnvironmentDifferencesToPowerShell(Dictionary<string, (string?, string?)> envDiff) {
        var builder = new PowerShellScriptBuilder();
        foreach (var (name, (_, value)) in envDiff) {
            builder.SetEnvironmentVariable(name, value);
        }
        return builder.ToString();
    }

    private static string ConvertFullEnvironmentToPowerShell(Dictionary<string, string> env) {
        return new PowerShellScriptBuilder()
                .ClearEnvironment()
                .SetEnvironmentVariables(env)
                .ToString();
    }

    private static string ConvertFullInvocationToPowerShell(string applicationName, Dictionary<string, string> env,
            string workingDirectory, string commandLine) {
        return new PowerShellScriptBuilder()
                .ClearEnvironment()
                .SetEnvironmentVariables(env)
                .AppendLine()
                .ChangeDirectory(workingDirectory)
                .AppendLine()
                .AppendInvocation(applicationName, commandLine)
                .ToString();
    }

    private static void LaunchInWinDbg(string applicationName, Dictionary<string, string> env, string workingDirectory,
            string commandLine, bool breakOnStart) {
        if (string.IsNullOrWhiteSpace(commandLine)) {
            return;
        }

        // Parse the command line to extract arguments (skip argv[0])
        var text = commandLine.Trim();
        string arguments;

        if (text[0] == '"') {
            var endQuote = text.IndexOf('"', 1);
            if (endQuote > 1) {
                arguments = endQuote + 1 < text.Length ? text[(endQuote + 1)..].TrimStart() : "";
            } else {
                arguments = "";
            }
        } else {
            var spaceIndex = text.IndexOf(' ');
            arguments = spaceIndex >= 0 ? text[(spaceIndex + 1)..].TrimStart() : "";
        }

        // Build WinDbg command line arguments
        var windbgArgs = new StringBuilder();

        // -G: ignore initial breakpoint (loader breakpoint, but stop at process entry point)
        windbgArgs.Append("-G ");

        // break whenever any exception is raised
        var cmds = "sxe eh; sxe vcpp; sxe rto; sxe rtt";
        if (!breakOnStart) {
            cmds += "; g"; // automatically start execution
        }
        // set commands that are automatically executed at the start
        windbgArgs.Append($"-c \"{cmds}\" ");

        // Add executable path
        if (applicationName.Contains(' ') || applicationName.Contains('"')) {
            windbgArgs.Append($"\"{applicationName}\"");
        } else {
            windbgArgs.Append(applicationName);
        }

        // Add arguments if present
        if (!string.IsNullOrEmpty(arguments)) {
            windbgArgs.Append(' ');
            windbgArgs.Append(arguments);
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo {
            FileName = "windbgx.exe",
            Arguments = windbgArgs.ToString(),
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };

        startInfo.Environment.Clear();
        foreach (var kv in env) {
            startInfo.Environment[kv.Key] = kv.Value;
        }

        try {
            System.Diagnostics.Process.Start(startInfo);
        } catch (Exception ex) {
            MessageBox.Show($"Failed to launch WinDbg: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void LaunchInVisualStudio(string applicationName, Dictionary<string, string> env,
            string workingDirectory, string commandLine) {
        if (string.IsNullOrWhiteSpace(commandLine)) {
            return;
        }

        // Find Visual Studio installation path from registry
        var devenvPath = FindDevEnvPath();
        if (devenvPath == null) {
            MessageBox.Show("Could not find Visual Studio installation. Please ensure Visual Studio is installed.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Parse the command line to extract arguments (skip argv[0])
        var text = commandLine.Trim();
        string arguments;

        if (text[0] == '"') {
            var endQuote = text.IndexOf('"', 1);
            if (endQuote > 1) {
                arguments = endQuote + 1 < text.Length ? text[(endQuote + 1)..].TrimStart() : "";
            } else {
                arguments = "";
            }
        } else {
            var spaceIndex = text.IndexOf(' ');
            arguments = spaceIndex >= 0 ? text[(spaceIndex + 1)..].TrimStart() : "";
        }

        // Build Visual Studio command line arguments
        // /DebugExe: Start debugging the specified executable
        var devenvArgs = new StringBuilder();
        devenvArgs.Append("/DebugExe ");

        // Add executable path (always quote it for VS)
        devenvArgs.Append($"\"{applicationName}\"");

        // Add arguments if present
        if (!string.IsNullOrEmpty(arguments)) {
            devenvArgs.Append(' ');
            devenvArgs.Append(arguments);
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo {
            FileName = devenvPath,
            Arguments = devenvArgs.ToString(),
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };

        startInfo.Environment.Clear();
        foreach (var kv in env) {
            startInfo.Environment[kv.Key] = kv.Value;
        }

        try {
            System.Diagnostics.Process.Start(startInfo);
        } catch (Exception ex) {
            MessageBox.Show($"Failed to launch Visual Studio: {ex.Message}", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
        }
    }

    private static string? FindDevEnvPath() {
        try {
            // Try to find devenv.exe from the App Paths registry key
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\devenv.exe");

            if (key?.GetValue("") is not string path) {
                return null;
            }

            if (path.StartsWith('"') && path.EndsWith('"')) {
                path = path[1..^1];
            }
            return path;
        } catch {
            return null;
        }
    }
}