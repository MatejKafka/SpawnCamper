using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using SpawnCamper.Core;

namespace SpawnCamper.Server.UI.ViewModels;

/// <summary>
/// ViewModel wrapper for TracedProcessTree.ProcessInvocation, representing either a successful
/// process or a failed process creation in the tree view.
/// </summary>
public class ProcessNodeViewModel : INotifyPropertyChanged {
    private readonly TracedProcessTree.ProcessInvocation _invocation;
    private readonly ObservableCollection<ProcessNodeViewModel> _children = [];

    public ProcessNodeViewModel(TracedProcessTree.ProcessInvocation invocation) {
        _invocation = invocation;

        // Subscribe to child collection changes if this is a successful process
        if (_invocation.Child is {} node) {
            node.Children.CollectionChanged += OnChildrenCollectionChanged;
            // Initialize children
            foreach (var childInvocation in node.Children) {
                _children.Add(new ProcessNodeViewModel(childInvocation));
            }
            // Subscribe to property changes on the process
            node.Process.PropertyChanged += OnProcessPropertyChanged;
        }
    }

    private void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null) {
            foreach (TracedProcessTree.ProcessInvocation invocation in e.NewItems) {
                _children.Add(new ProcessNodeViewModel(invocation));
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
                break;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Expose the underlying process or failed invocation information
    private TracedProcess? Process => _invocation.Child?.Process;
    private TracedProcessTree.FailedInvocation? FailedInvocation => _invocation.FailedInvocation;

    public bool IsCreationFailure => !_invocation.Success;
    public bool HasStarted => Process != null;

    public int ProcessId => Process?.ProcessId ?? 0;
    public ObservableCollection<ProcessNodeViewModel> Children => _children;

    // Environment-related properties
    public ObservableCollection<KeyValuePair<string, string>>? EnvironmentVariables {
        get {
            if (field == null && Process != null) {
                field = new ObservableCollection<KeyValuePair<string, string>>(
                        Process.Environment.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase));
            }
            return field ?? [];
        }
    }

    [field: AllowNull, MaybeNull]
    public ObservableCollection<EnvironmentVariableDifference> EnvironmentDifferences {
        get {
            if (field == null && Process?.EnvironmentDiff != null) {
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

    public string EnvironmentDifferencesDisplay => EnvironmentDifferences.Count == 0
            ? ""
            : string.Join(Environment.NewLine, EnvironmentDifferences.Select(diff => diff.DisplayText));

    public ProcessNodeViewModel? Parent => Process?.Parent == null
            ? null
            : new ProcessNodeViewModel(
                    new TracedProcessTree.ProcessInvocation(new TracedProcessTree.Node(Process.Parent, [])));

    public bool IsRoot => Process?.Parent == null;
    public string? ApplicationName => Process?.ExePath ?? FailedInvocation?.ExePath;
    public string? CommandLine => Process?.CommandLine ?? FailedInvocation?.CommandLine;
    public string? WorkingDirectory => Process?.WorkingDirectory;
    public bool IsActive => Process?.EndTime == null && HasStarted && !IsCreationFailure;
    public bool IsDetached => Process is {EndTime: not null, ExitCode: null};
    public int? ExitCode => Process?.ExitCode;
    public DateTime? AttachTime => Process?.StartTime;
    public DateTime? EndTime => Process?.EndTime;
    public bool ShowSpinner => IsActive && !IsCreationFailure;

    public string ExitDisplayText {
        get {
            if (IsCreationFailure) return "Failed";
            if (IsActive) return "";
            if (ExitCode.HasValue) return ExitCode.Value.ToString();
            if (IsDetached) return "<unknown>";
            return "—";
        }
    }

    public bool IsRunning => IsActive && HasStarted && !IsCreationFailure;

    public string StatusText {
        get {
            if (IsCreationFailure) return "CreateProcess failed";
            if (IsActive) return "Running";
            if (ExitCode.HasValue) return $"Exited ({ExitCode.Value})";
            if (IsDetached) return "Terminated";
            if (HasStarted) return "Stopped";
            return "Waiting to start";
        }
    }

    public string ProcessIdDisplay => IsCreationFailure ? "—" : ProcessId.ToString();

    public string AttachTimeDisplay => AttachTime?.ToLocalTime().ToString("HH:mm:ss.fff") ?? "—";

    public string DetachTimeDisplay => EndTime?.ToLocalTime().ToString("HH:mm:ss.fff") ?? "—";

    public string DurationDisplay {
        get {
            if (AttachTime == null) {
                return "—";
            }
            var endTime = EndTime ?? DateTime.UtcNow;
            var duration = endTime - AttachTime.Value;
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

    public string? FailureReason {
        get {
            if (!IsCreationFailure) return null;
            var cmdLine = FailedInvocation?.CommandLine;
            var exePath = FailedInvocation?.ExePath;
            return string.IsNullOrWhiteSpace(cmdLine)
                    ? string.IsNullOrWhiteSpace(exePath)
                            ? "CreateProcess failed"
                            : $"CreateProcess failed: {exePath}"
                    : $"CreateProcess failed: {cmdLine}";
        }
    }

    public string DisplayLabel {
        get {
            if (IsCreationFailure) {
                return FailureReason ?? "CreateProcess failed";
            }
            if (!string.IsNullOrWhiteSpace(CommandLine)) {
                return CommandLine;
            }
            if (!string.IsNullOrWhiteSpace(ApplicationName)) {
                return ApplicationName;
            }
            return ProcessId.ToString();
        }
    }

    public string DisplayLabelSingleLine => ToSingleLine(DisplayLabel);

    public string CommandLineDisplay => IsCreationFailure
            ? FailureReason ?? CommandLine ?? ApplicationName ?? "(CreateProcess failed)"
            : string.IsNullOrWhiteSpace(CommandLine)
                    ? ApplicationName ?? "(not reported)"
                    : CommandLine;

    public bool HasEnvironment => Process is {Environment.Count: > 0};
    public bool HasEnvironmentDifferences => Process?.EnvironmentDiff is {Count: > 0};

    public string BinaryNameDisplay {
        get {
            var name = ExtractBinaryName(ApplicationName, CommandLine);
            return !string.IsNullOrEmpty(name) ? name : IsCreationFailure ? "(CreateProcess failed)" : "(unknown)";
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
            () => Clipboard.SetText(ConvertFullEnvironmentToPowerShell(EnvironmentVariables!)),
            () => HasEnvironment);

    [field: AllowNull, MaybeNull]
    public ICommand CopyFullInvocationCommand => field ??= new RelayCommand(
            () => Clipboard.SetText(ConvertFullInvocationToPowerShell(
                    ApplicationName, EnvironmentVariables!, WorkingDirectory, CommandLine)),
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

    private static string ConvertCommandLineToPowerShell(string? applicationName, string? commandLine) {
        if (string.IsNullOrWhiteSpace(commandLine)) {
            return "";
        }

        // Use the absolute path from applicationName if available
        var executable = applicationName;
        if (string.IsNullOrWhiteSpace(executable)) {
            // Fallback: parse from command line if applicationName is not available
            var text = commandLine.Trim();
            if (text.Length == 0) {
                return "";
            }

            if (text[0] == '"') {
                var endQuote = text.IndexOf('"', 1);
                if (endQuote > 1) {
                    executable = text.Substring(1, endQuote - 1);
                } else {
                    executable = text.Trim('"');
                }
            } else {
                var spaceIndex = text.IndexOf(' ');
                if (spaceIndex >= 0) {
                    executable = text[..spaceIndex];
                } else {
                    executable = text;
                }
            }
        }

        // Parse the command line to extract arguments (skip argv[0])
        var text2 = commandLine.Trim();
        string arguments;

        if (text2[0] == '"') {
            var endQuote = text2.IndexOf('"', 1);
            if (endQuote > 1) {
                arguments = endQuote + 1 < text2.Length ? text2.Substring(endQuote + 1).TrimStart() : "";
            } else {
                arguments = "";
            }
        } else {
            var spaceIndex = text2.IndexOf(' ');
            if (spaceIndex >= 0) {
                arguments = text2[(spaceIndex + 1)..].TrimStart();
            } else {
                arguments = "";
            }
        }

        // Determine if we need the call operator (&) and quoting
        // We need it if:
        // 1. The executable is an absolute path (contains : or starts with \ or /)
        // 2. The executable contains spaces or special characters
        var isAbsolutePath = executable.Contains(':') || executable.StartsWith('\\') || executable.StartsWith('/');
        var needsQuoting = executable.Any(c =>
                char.IsWhiteSpace(c) || c == '\'' || c == '"' || c == '`' || c == '$' || c == '&' || c == '|' || c == ';' ||
                c == '<' || c == '>' || c == '(' || c == ')');
        var needsCallOperator = isAbsolutePath || needsQuoting;

        string executablePart;
        if (needsCallOperator) {
            // Use single quotes and escape any single quotes inside
            var escaped = executable.Replace("'", "''");
            executablePart = $"& '{escaped}'";
        } else {
            // Use the executable as-is
            executablePart = executable;
        }

        // If there are arguments, append them
        if (!string.IsNullOrEmpty(arguments)) {
            return $"{executablePart} {arguments}";
        }

        return executablePart;
    }

    private static string EscapeForPowerShell(string value) {
        if (string.IsNullOrEmpty(value)) {
            return "''";
        }

        // Use single quotes and escape any single quotes inside
        var escaped = value.Replace("'", "''");
        return $"'{escaped}'";
    }

    private static string FormatEnvVarName(string varName) {
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

            if (diff.IsAdded) {
                // Check if there's a corresponding removal (meaning the variable was changed)
                var hasRemoval =
                        differences.Any(d => d.Key.Equals(diff.Key, StringComparison.OrdinalIgnoreCase) && d.IsRemoved);

                if (hasRemoval) {
                    // Changed variable - just set the new value
                    var escapedValue = EscapeForPowerShell(diff.Value);
                    var envVarName = FormatEnvVarName(diff.Key);
                    lines.Add($"{envVarName} = {escapedValue}");
                    processedKeys.Add(diff.Key);
                } else {
                    // New variable
                    var escapedValue = EscapeForPowerShell(diff.Value);
                    var envVarName = FormatEnvVarName(diff.Key);
                    lines.Add($"{envVarName} = {escapedValue}");
                    processedKeys.Add(diff.Key);
                }
            } else if (diff.IsRemoved) {
                // Check if there's a corresponding addition (meaning the variable was changed)
                var hasAddition =
                        differences.Any(d => d.Key.Equals(diff.Key, StringComparison.OrdinalIgnoreCase) && d.IsAdded);

                if (!hasAddition) {
                    // Variable was deleted
                    var envVarName = FormatEnvVarName(diff.Key);
                    lines.Add($"{envVarName} = $null");
                    processedKeys.Add(diff.Key);
                }
                // If there's an addition, we'll process it when we encounter the added entry
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string
            ConvertFullEnvironmentToPowerShell(IEnumerable<KeyValuePair<string, string>> environmentVariables) {
        var script = new StringBuilder();

        // First, clear all existing environment variables
        script.AppendLine("# Clear all existing environment variables");
        script.AppendLine(
                "Get-ChildItem Env: | ForEach-Object { Remove-Item -Path \"Env:$($_.Name)\" -ErrorAction SilentlyContinue }");
        script.AppendLine();

        // Then set all the process environment variables
        script.AppendLine("# Set environment variables to match the process");
        foreach (var kv in environmentVariables) {
            var escapedValue = EscapeForPowerShell(kv.Value);
            var envVarName = FormatEnvVarName(kv.Key);
            script.AppendLine($"{envVarName} = {escapedValue}");
        }

        return script.ToString();
    }

    private static string ConvertFullInvocationToPowerShell(string? applicationName,
            IEnumerable<KeyValuePair<string, string>> environmentVariables,
            string? workingDirectory, string? commandLine) {
        var script = new StringBuilder();

        // First, clear all existing environment variables
        script.AppendLine("# Clear all existing environment variables");
        script.AppendLine(
                "Get-ChildItem Env: | ForEach-Object { Remove-Item -Path \"Env:$($_.Name)\" -ErrorAction SilentlyContinue }");
        script.AppendLine();

        // Then set all the process environment variables
        script.AppendLine("# Set environment variables to match the process");
        foreach (var kv in environmentVariables) {
            var escapedValue = EscapeForPowerShell(kv.Value);
            var envVarName = FormatEnvVarName(kv.Key);
            script.AppendLine($"{envVarName} = {escapedValue}");
        }
        script.AppendLine();

        // Change to the working directory if specified
        if (!string.IsNullOrWhiteSpace(workingDirectory)) {
            script.AppendLine("# Change to working directory");
            var escapedPath = EscapeForPowerShell(workingDirectory);
            script.AppendLine($"Set-Location -Path {escapedPath}");
            script.AppendLine();
        }

        // Execute the command
        if (!string.IsNullOrWhiteSpace(commandLine)) {
            script.AppendLine("# Execute the command");
            var powershellCmd = ConvertCommandLineToPowerShell(applicationName, commandLine);
            script.AppendLine(powershellCmd);
        }

        return script.ToString();
    }

    private static void LaunchInWinDbg(string? applicationName,
            IEnumerable<KeyValuePair<string, string>>? environmentVariables,
            string? workingDirectory, string? commandLine, bool breakOnStart) {
        if (string.IsNullOrWhiteSpace(commandLine)) {
            return;
        }

        try {
            // Use the absolute path from applicationName
            var executable = applicationName;
            if (string.IsNullOrWhiteSpace(executable)) {
                MessageBox.Show("Application path is not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                if (spaceIndex >= 0) {
                    arguments = text[(spaceIndex + 1)..].TrimStart();
                } else {
                    arguments = "";
                }
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
            if (executable.Contains(' ') || executable.Contains('"')) {
                windbgArgs.Append($"\"{executable}\"");
            } else {
                windbgArgs.Append(executable);
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
                UseShellExecute = false
            };

            // Set working directory if specified
            if (!string.IsNullOrWhiteSpace(workingDirectory)) {
                startInfo.WorkingDirectory = workingDirectory;
            }

            // Set environment variables
            if (environmentVariables != null) {
                // Clear default environment variables and set only the ones from the process
                startInfo.Environment.Clear();
                foreach (var kv in environmentVariables) {
                    startInfo.Environment[kv.Key] = kv.Value;
                }
            }

            System.Diagnostics.Process.Start(startInfo);
        } catch (Exception ex) {
            MessageBox.Show($"Failed to launch WinDbg: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void LaunchInVisualStudio(string? applicationName,
            IEnumerable<KeyValuePair<string, string>>? environmentVariables,
            string? workingDirectory, string? commandLine) {
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

            // Use the absolute path from applicationName
            var executable = applicationName;
            if (string.IsNullOrWhiteSpace(executable)) {
                MessageBox.Show("Application path is not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            devenvArgs.Append($"\"{executable}\"");

            // Add arguments if present
            if (!string.IsNullOrEmpty(arguments)) {
                devenvArgs.Append(' ');
                devenvArgs.Append(arguments);
            }

            // Launch Visual Studio directly with environment setup
            var startInfo = new System.Diagnostics.ProcessStartInfo {
                FileName = devenvPath,
                Arguments = devenvArgs.ToString(),
                UseShellExecute = false
            };

            // Set environment variables
            if (environmentVariables != null) {
                startInfo.Environment.Clear();
                foreach (var kv in environmentVariables) {
                    startInfo.Environment[kv.Key] = kv.Value;
                }
            }

            // Set working directory
            if (!string.IsNullOrWhiteSpace(workingDirectory)) {
                startInfo.WorkingDirectory = workingDirectory;
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