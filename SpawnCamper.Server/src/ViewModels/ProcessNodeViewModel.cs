using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using SpawnCamper.Core;
using SpawnCamper.Core.Utils;

namespace SpawnCamper.Server.ViewModels;

/// <summary>
/// ViewModel wrapper for TracedProcessTree.Node, representing a successfully created process in the tree view.
/// </summary>
public partial class ProcessNodeViewModel : INotifyPropertyChanged {
    private readonly TracedProcessTree.Node _node;
    private readonly ObservableCollection<ProcessNodeViewModel> _children = [];

    public ProcessNodeViewModel(TracedProcessTree.Node node) {
        _node = node;

        // Subscribe to child collection changes
        _node.Children.CollectionChanged += OnChildrenCollectionChanged;
        // Initialize children
        foreach (var childNode in _node.Children) {
            _children.Add(new ProcessNodeViewModel(childNode));
        }
        // Subscribe to property changes on the process
        _node.Process.PropertyChanged += OnProcessPropertyChanged;
    }

    private void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null) {
            foreach (TracedProcessTree.Node node in e.NewItems) {
                _children.Add(new ProcessNodeViewModel(node));
            }
        }
        // Handle other collection change types if needed
    }

    private void OnProcessPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        // Forward property changes from the model to the UI
        switch (e.PropertyName) {
            case nameof(TracedProcess.ExitCode):
                OnPropertyChanged(nameof(ExitCode));
                OnPropertyChanged(nameof(ExitDisplayText));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(ShowSpinner));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsDetached));
                OnPropertyChanged(nameof(DurationDisplay)); // Duration may change when exit code is set
                break;
            case nameof(TracedProcess.EndTime):
                OnPropertyChanged(nameof(EndTime));
                OnPropertyChanged(nameof(DetachTimeDisplay));
                OnPropertyChanged(nameof(DurationDisplay));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(ShowSpinner));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsDetached));
                OnPropertyChanged(nameof(ExitDisplayText)); // Exit display depends on IsActive and IsDetached
                OnPropertyChanged(nameof(StatusText)); // Status text depends on IsActive
                break;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Expose the underlying process - now guaranteed to be non-null
    private TracedProcess Process => _node.Process;

    public int ProcessId => Process.ProcessId;
    public uint Depth => _node.Depth;
    public double IndentWidth => Depth * 18.0; // 18 pixels per level
    public ObservableCollection<ProcessNodeViewModel> Children => _children;

    // Environment-related properties
    [field: AllowNull, MaybeNull]
    public ObservableCollection<KeyValuePair<string, string>> EnvironmentVariables {
        get {
            field ??= new ObservableCollection<KeyValuePair<string, string>>(
                    Process.Environment.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase));
            return field;
        }
    }

    [field: AllowNull, MaybeNull]
    public ObservableCollection<EnvironmentVariableDifference> EnvironmentDifferences {
        get {
            if (field == null && Process.EnvironmentDiff != null) {
                field = new ObservableCollection<EnvironmentVariableDifference>();
                foreach (var (key, value) in
                         Process.EnvironmentDiff.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)) {
                    if (value == null) {
                        // Variable was removed
                        var parentValue = Process.Parent?.Environment.GetValueOrDefault(key);
                        if (parentValue != null) {
                            field.Add(new EnvironmentVariableDifference(
                                    key, parentValue, EnvironmentVariableDiffKind.Removed));
                        }
                    } else {
                        // Variable was added or changed
                        var parentValue = Process.Parent?.Environment.GetValueOrDefault(key);
                        if (parentValue != null && parentValue != value) {
                            // Changed - show both old and new
                            field.Add(new EnvironmentVariableDifference(
                                    key, parentValue, EnvironmentVariableDiffKind.Removed));
                            field.Add(new EnvironmentVariableDifference(
                                    key, value, EnvironmentVariableDiffKind.Added));
                        } else {
                            // Added
                            field.Add(new EnvironmentVariableDifference(
                                    key, value, EnvironmentVariableDiffKind.Added));
                        }
                    }
                }
            }
            return field ?? new ObservableCollection<EnvironmentVariableDifference>();
        }
    }

    public string ApplicationName => Process.ExePath;
    public string CommandLine => Process.CommandLine;
    public string WorkingDirectory => Process.WorkingDirectory;
    public bool IsActive => Process.EndTime == null;
    public bool IsDetached => Process is {EndTime: not null, ExitCode: null};
    public int? ExitCode => Process.ExitCode;
    public DateTime StartTime => Process.StartTime;
    public DateTime? EndTime => Process.EndTime;
    public bool ShowSpinner => IsActive;

    public string ExitDisplayText {
        get {
            if (IsActive) return "";
            if (ExitCode.HasValue) return ExitCode.Value.ToString();
            if (IsDetached) return "<unknown>";
            return "—";
        }
    }

    public bool IsRunning => IsActive;

    public string StatusText {
        get {
            if (IsActive) return "Running";
            if (ExitCode.HasValue) return $"Exited ({ExitCode.Value})";
            if (IsDetached) return "Terminated";
            return "Stopped";
        }
    }

    public string ProcessIdDisplay => ProcessId.ToString();

    public string AttachTimeDisplay => StartTime.ToLocalTime().ToString("HH:mm:ss.fff");

    public string DetachTimeDisplay => EndTime?.ToLocalTime().ToString("HH:mm:ss.fff") ?? "—";

    public string DurationDisplay {
        get {
            var endTime = EndTime ?? DateTime.UtcNow;
            var duration = endTime - StartTime;
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

    public string DisplayLabel => !string.IsNullOrWhiteSpace(CommandLine) ? CommandLine : ApplicationName;
    public string DisplayLabelSingleLine => ToSingleLine(DisplayLabel);

    public bool HasEnvironment => Process.Environment.Count > 0;
    public bool HasEnvironmentDifferences => Process.EnvironmentDiff is {Count: > 0};

    public string BinaryNameDisplay {
        get {
            var name = ExtractBinaryName(ApplicationName, CommandLine);
            return !string.IsNullOrEmpty(name) ? name : "(unknown)";
        }
    }

    // Commands
    [field: AllowNull, MaybeNull]
    public ICommand CopyCommandLineAsPowerShellCommand => field ??= new RelayCommand(
            () => Clipboard.SetText(ConvertCommandLineToPowerShell(ApplicationName, CommandLine)),
            () => !string.IsNullOrWhiteSpace(CommandLine));

    [field: AllowNull, MaybeNull]
    public ICommand CopyEnvironmentDifferencesAsPowerShellCommand => field ??= new RelayCommand(
            () => Clipboard.SetText(ConvertEnvironmentDifferencesToPowerShell(EnvironmentDifferences)),
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

    protected void OnPropertyChanged(string propertyName) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string ToSingleLine(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "";
        }

        var normalized = CollapseWhitespace(value.ReplaceLineEndings(" ")).Trim();
        const int maxLength = 180;
        if (normalized.Length > maxLength) {
            return normalized[..maxLength].TrimEnd() + "...";
        }

        return normalized;
    }

    private static string CollapseWhitespace(string value) {
        if (string.IsNullOrEmpty(value)) {
            return "";
        }

        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;

        foreach (var ch in value) {
            if (char.IsWhiteSpace(ch)) {
                if (previousWasWhitespace) {
                    continue;
                }
                builder.Append(' ');
                previousWasWhitespace = true;
            } else {
                builder.Append(ch);
                previousWasWhitespace = false;
            }
        }

        return builder.ToString().Trim();
    }

    private static string? ExtractBinaryName(string? applicationName, string? commandLine) {
        var fromApplication = NormalizeExecutableName(applicationName);
        if (!string.IsNullOrEmpty(fromApplication)) {
            return fromApplication;
        }

        var fromCommandLine = NormalizeExecutableName(ExtractExecutableFromCommandLine(commandLine));
        return fromCommandLine;
    }

    private static string? NormalizeExecutableName(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0) {
            return null;
        }

        if (trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal) &&
            trimmed.Length > 1) {
            trimmed = trimmed[1..^1];
        }

        try {
            var fileName = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(fileName) ? trimmed : fileName;
        } catch (ArgumentException) {
            return trimmed;
        }
    }

    private static string? ExtractExecutableFromCommandLine(string? commandLine) {
        if (string.IsNullOrWhiteSpace(commandLine)) {
            return null;
        }

        var text = commandLine.TrimStart();
        if (text.Length == 0) {
            return null;
        }

        if (text[0] == '"') {
            var endQuote = text.IndexOf('"', 1);
            if (endQuote > 1) {
                return text.Substring(1, endQuote - 1);
            }

            return text.Trim('"');
        }

        var spaceIndex = text.IndexOf(' ');
        return spaceIndex >= 0 ? text[..spaceIndex] : text;
    }

    private static string ConvertCommandLineToPowerShell(string applicationName, string commandLine) {
        var argv = Native.CommandLineToArgv(commandLine);
        if (argv.Length == 0) {
            return ArgvToPowerShell([applicationName]);
        } else {
            // argv[0] can be an arbitrary string, we want to use the actual executed path
            argv[0] = applicationName;
            return ArgvToPowerShell(argv);
        }
    }

    private static string ArgvToPowerShell(IEnumerable<string> argv) {
        var invocation = string.Join(" ", argv.Select(arg => ToPowerShellLiteral(arg, true)));
        // if exe path was quoted, we need to use the call operator (&)
        return invocation.StartsWith('\'') ? $"& {invocation}" : invocation;
    }

    [GeneratedRegex(@"[a-zA-Z0-9_/\\:-]+")]
    private static partial Regex BareArgumentRegex {get;}

    private static string ToPowerShellLiteral(string? value, bool argument = false) {
        if (value == null) {
            return "$null";
        }
        if (value == "") {
            return "''";
        }

        if (argument && BareArgumentRegex.IsMatch(value)) {
            return value; // no need to quote
        }

        // use single quotes and escape any single quotes inside
        return $"'{value.Replace("'", "''")}'";
    }

    private static string ToPowerShellEnvVarLiteral(string varName) {
        // Check if the variable name is a valid PowerShell identifier
        // Valid identifiers: start with letter or underscore, followed by letters, digits, or underscores
        if (string.IsNullOrEmpty(varName)) {
            return "$env:{}";
        }

        // Check if it needs braces: contains special characters like parentheses, spaces, etc.
        var needsBraces = varName.Any(c => !char.IsLetterOrDigit(c) && c != '_');

        if (needsBraces) {
            return $"${{env:{varName}}}";
        }

        return $"$env:{varName}";
    }

    private static string ConvertEnvironmentDifferencesToPowerShell(Collection<EnvironmentVariableDifference> differences) {
        var lines = new List<string>();
        var processedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var diff in differences) {
            // Skip if we've already processed this key (e.g., when a variable is changed, we see both removed and added)
            if (processedKeys.Contains(diff.Key)) {
                continue;
            }

            switch (diff.Kind) {
                case EnvironmentVariableDiffKind.Added: {
                    // Check if there's a corresponding removal (meaning the variable was changed)
                    var hasRemoval = differences.Any(d =>
                            d.Key.Equals(diff.Key, StringComparison.OrdinalIgnoreCase) &&
                            d.Kind == EnvironmentVariableDiffKind.Removed);

                    if (hasRemoval) {
                        // Changed variable - just set the new value
                        var escapedValue = ToPowerShellLiteral(diff.Value);
                        var envVarName = ToPowerShellEnvVarLiteral(diff.Key);
                        lines.Add($"{envVarName} = {escapedValue}");
                        processedKeys.Add(diff.Key);
                    } else {
                        // New variable
                        var escapedValue = ToPowerShellLiteral(diff.Value);
                        var envVarName = ToPowerShellEnvVarLiteral(diff.Key);
                        lines.Add($"{envVarName} = {escapedValue}");
                        processedKeys.Add(diff.Key);
                    }
                    break;
                }
                case EnvironmentVariableDiffKind.Removed: {
                    // Check if there's a corresponding addition (meaning the variable was changed)
                    var hasAddition =
                            differences.Any(d =>
                                    d.Key.Equals(diff.Key, StringComparison.OrdinalIgnoreCase) &&
                                    d.Kind == EnvironmentVariableDiffKind.Added);

                    if (!hasAddition) {
                        // Variable was deleted
                        var envVarName = ToPowerShellEnvVarLiteral(diff.Key);
                        lines.Add($"{envVarName} = $null");
                        processedKeys.Add(diff.Key);
                    }
                    // If there's an addition, we'll process it when we encounter the added entry
                    break;
                }
                default:
                    throw new SwitchExpressionException(diff.Kind);
            }
        }

        return string.Join("\n", lines);
    }

    private static string ConvertFullEnvironmentToPowerShell(
            IEnumerable<KeyValuePair<string, string>> environmentVariables) {
        var script = new StringBuilder();

        script.AppendLine("ls Env: | rm");
        foreach (var kv in environmentVariables) {
            script.AppendLine($"{ToPowerShellEnvVarLiteral(kv.Key)} = {ToPowerShellLiteral(kv.Value)}");
        }

        return script.ToString();
    }

    private static string ConvertFullInvocationToPowerShell(string applicationName,
            IEnumerable<KeyValuePair<string, string>> environmentVariables, string workingDirectory, string commandLine) {
        var script = new StringBuilder();

        script.AppendLine("ls Env: | rm");
        foreach (var kv in environmentVariables) {
            script.AppendLine($"{ToPowerShellEnvVarLiteral(kv.Key)} = {ToPowerShellLiteral(kv.Value)}");
        }
        script.AppendLine();

        script.AppendLine($"cd {ToPowerShellLiteral(workingDirectory, true)}");
        script.AppendLine();

        script.AppendLine(ConvertCommandLineToPowerShell(applicationName, commandLine));

        return script.ToString();
    }

    private static void LaunchInWinDbg(string applicationName,
            IEnumerable<KeyValuePair<string, string>> environmentVariables, string workingDirectory, string commandLine,
            bool breakOnStart) {
        if (string.IsNullOrWhiteSpace(commandLine)) {
            return;
        }

        try {
            // Parse the command line to extract arguments (skip argv[0])
            var text = commandLine.Trim();
            string arguments;

            if (text[0] == '"') {
                var endQuote = text.IndexOf('"', 1);
                if (endQuote > 1) {
                    arguments = endQuote + 1 < text.Length ? text.Substring(endQuote + 1).TrimStart() : "";
                } else {
                    arguments = "";
                }
            } else {
                var spaceIndex = text.IndexOf(' ');
                arguments = spaceIndex >= 0 ? text[(spaceIndex + 1)..].TrimStart() : "";
            }

            // Build WinDbg command line arguments
            var windbgArgs = new StringBuilder();

            if (breakOnStart) {
                // -G: ignore initial breakpoint (loader breakpoint, but stop at process entry point)
                windbgArgs.Append("-G ");
            } else {
                // -g: go on start
                // -G: ignore initial breakpoint
                windbgArgs.Append("-g -G ");
            }

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

            // Create process start info for WinDbg
            var startInfo = new System.Diagnostics.ProcessStartInfo {
                FileName = "windbgx.exe",
                Arguments = windbgArgs.ToString(),
                UseShellExecute = false,
                WorkingDirectory = workingDirectory
            };

            // Set environment variables - clear default and set only the ones from the process
            startInfo.Environment.Clear();
            foreach (var kv in environmentVariables) {
                startInfo.Environment[kv.Key] = kv.Value;
            }

            System.Diagnostics.Process.Start(startInfo);
        } catch (Exception ex) {
            MessageBox.Show($"Failed to launch WinDbg: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void LaunchInVisualStudio(string applicationName,
            IEnumerable<KeyValuePair<string, string>> environmentVariables,
            string workingDirectory, string commandLine) {
        if (string.IsNullOrWhiteSpace(commandLine)) {
            return;
        }

        try {
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
                    arguments = endQuote + 1 < text.Length ? text.Substring(endQuote + 1).TrimStart() : "";
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

            // Launch Visual Studio directly with environment setup
            var startInfo = new System.Diagnostics.ProcessStartInfo {
                FileName = devenvPath,
                Arguments = devenvArgs.ToString(),
                UseShellExecute = false,
                WorkingDirectory = workingDirectory
            };

            // Set environment variables
            startInfo.Environment.Clear();
            foreach (var kv in environmentVariables) {
                startInfo.Environment[kv.Key] = kv.Value;
            }

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