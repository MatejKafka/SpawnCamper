using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace SpawnCamper.Server.UI.ViewModels;

public class ProcessNodeViewModel : INotifyPropertyChanged {
    private readonly Dictionary<string, string> _environmentMap = new(StringComparer.OrdinalIgnoreCase);
    private string? _applicationName;
    private string? _commandLine;
    private string? _workingDirectory;
    private bool _isActive;
    private bool _isDetached;
    private int? _exitCode;
    private ProcessNodeViewModel? _parent;
    private bool _hasStarted;
    private bool _isCreationFailure;
    private string? _failureReason;
    private DateTime? _attachTime;
    private DateTime? _detachTime;
    private ICommand? _copyCommandLineAsPowerShellCommand;
    private ICommand? _copyEnvironmentDifferencesAsPowerShellCommand;
    private ICommand? _copyFullEnvironmentAsPowerShellCommand;
    private ICommand? _copyFullInvocationCommand;
    private ICommand? _launchInWinDbgCommand;
    private ICommand? _launchInWinDbgBreakCommand;
    private ICommand? _launchInVisualStudioCommand;
    private ICommand? _launchInVisualStudioBreakCommand;

    public ProcessNodeViewModel(int processId) {
        ProcessId = processId;
        Children = new ObservableCollection<ProcessNodeViewModel>();
        EnvironmentVariables = new ObservableCollection<KeyValuePair<string, string>>();
        EnvironmentDifferences = new ObservableCollection<EnvironmentVariableDifference>();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int ProcessId {get;}

    public ObservableCollection<ProcessNodeViewModel> Children {get;}

    public ObservableCollection<KeyValuePair<string, string>> EnvironmentVariables {get;}

    public ObservableCollection<EnvironmentVariableDifference> EnvironmentDifferences {get;}

    public string EnvironmentDifferencesDisplay => EnvironmentDifferences.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, EnvironmentDifferences.Select(diff => diff.DisplayText));

    public ProcessNodeViewModel? Parent {
        get => _parent;
        private set {
            if (_parent == value) {
                return;
            }
            _parent = value;
            OnPropertyChanged(nameof(Parent));
            OnPropertyChanged(nameof(IsRoot));
        }
    }

    public bool IsRoot => Parent == null;

    public string? ApplicationName {
        get => _applicationName;
        private set {
            if (_applicationName == value) {
                return;
            }
            _applicationName = value;
            OnPropertyChanged(nameof(ApplicationName));
            OnPropertyChanged(nameof(DisplayLabel));
            OnPropertyChanged(nameof(DisplayLabelSingleLine));
            OnPropertyChanged(nameof(BinaryNameDisplay));
        }
    }

    public string? CommandLine {
        get => _commandLine;
        private set {
            if (_commandLine == value) {
                return;
            }
            _commandLine = value;
            OnPropertyChanged(nameof(CommandLine));
            OnPropertyChanged(nameof(DisplayLabel));
            OnPropertyChanged(nameof(DisplayLabelSingleLine));
            OnPropertyChanged(nameof(BinaryNameDisplay));
        }
    }

    public string? WorkingDirectory {
        get => _workingDirectory;
        private set {
            if (_workingDirectory == value) {
                return;
            }
            _workingDirectory = value;
            OnPropertyChanged(nameof(WorkingDirectory));
        }
    }

    public bool IsActive {
        get => _isActive;
        private set {
            if (_isActive == value) {
                return;
            }
            _isActive = value;
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(ShowSpinner));
            OnPropertyChanged(nameof(ExitDisplayText));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsRunning));
        }
    }

    public bool IsDetached {
        get => _isDetached;
        private set {
            if (_isDetached == value) {
                return;
            }
            _isDetached = value;
            OnPropertyChanged(nameof(IsDetached));
            OnPropertyChanged(nameof(ExitDisplayText));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public int? ExitCode {
        get => _exitCode;
        private set {
            if (_exitCode == value) {
                return;
            }
            _exitCode = value;
            OnPropertyChanged(nameof(ExitCode));
            OnPropertyChanged(nameof(ExitDisplayText));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public bool HasStarted {
        get => _hasStarted;
        private set {
            if (_hasStarted == value) {
                return;
            }
            _hasStarted = value;
            OnPropertyChanged(nameof(HasStarted));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ProcessIdDisplay));
            OnPropertyChanged(nameof(CommandLineDisplay));
        }
    }

    public bool IsCreationFailure {
        get => _isCreationFailure;
        private set {
            if (_isCreationFailure == value) {
                return;
            }
            _isCreationFailure = value;
            OnPropertyChanged(nameof(IsCreationFailure));
            OnPropertyChanged(nameof(ExitDisplayText));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ProcessIdDisplay));
            OnPropertyChanged(nameof(CommandLineDisplay));
            OnPropertyChanged(nameof(ShowSpinner));
            OnPropertyChanged(nameof(BinaryNameDisplay));
            OnPropertyChanged(nameof(DisplayLabel));
            OnPropertyChanged(nameof(DisplayLabelSingleLine));
        }
    }

    public string? FailureReason {
        get => _failureReason;
        private set {
            if (_failureReason == value) {
                return;
            }
            _failureReason = value;
            OnPropertyChanged(nameof(FailureReason));
            OnPropertyChanged(nameof(CommandLineDisplay));
            OnPropertyChanged(nameof(BinaryNameDisplay));
            OnPropertyChanged(nameof(DisplayLabel));
            OnPropertyChanged(nameof(DisplayLabelSingleLine));
        }
    }

    public bool ShowSpinner => IsActive && !IsCreationFailure;

    public string ExitDisplayText {
        get {
            if (IsCreationFailure) {
                return "Failed";
            }
            if (IsActive) {
                return string.Empty;
            }
            if (ExitCode.HasValue) {
                return ExitCode.Value.ToString();
            }
            if (IsDetached) {
                return "<unknown>";
            }
            return "—";
        }
    }

    public bool IsRunning => IsActive && HasStarted && !IsCreationFailure;

    public string StatusText {
        get {
            if (IsCreationFailure) {
                return "CreateProcess failed";
            }
            if (IsActive) {
                return "Running";
            }
            if (ExitCode.HasValue) {
                return $"Exited ({ExitCode.Value})";
            }
            if (IsDetached) {
                return "Terminated";
            }
            if (HasStarted) {
                return "Stopped";
            }
            return "Waiting to start";
        }
    }

    public string ProcessIdDisplay => IsCreationFailure ? "—" : ProcessId.ToString();

    public DateTime? AttachTime {
        get => _attachTime;
        private set {
            if (_attachTime == value) {
                return;
            }
            _attachTime = value;
            OnPropertyChanged(nameof(AttachTime));
            OnPropertyChanged(nameof(AttachTimeDisplay));
            OnPropertyChanged(nameof(DurationDisplay));
        }
    }

    public DateTime? DetachTime {
        get => _detachTime;
        private set {
            if (_detachTime == value) {
                return;
            }
            _detachTime = value;
            OnPropertyChanged(nameof(DetachTime));
            OnPropertyChanged(nameof(DetachTimeDisplay));
            OnPropertyChanged(nameof(DurationDisplay));
        }
    }

    public string AttachTimeDisplay => _attachTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "—";

    public string DetachTimeDisplay => _detachTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "—";

    public string DurationDisplay {
        get {
            if (_attachTime == null) {
                return "—";
            }
            var endTime = _detachTime ?? DateTime.UtcNow;
            var duration = endTime - _attachTime.Value;
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

    public bool HasEnvironment => EnvironmentVariables.Count > 0;

    public bool HasEnvironmentDifferences => EnvironmentDifferences.Count > 0;

    public ICommand CopyCommandLineAsPowerShellCommand {
        get {
            return _copyCommandLineAsPowerShellCommand ??= new RelayCommand(() => {
                var powershellCmd = ConvertCommandLineToPowerShell(ApplicationName, CommandLine);
                if (!string.IsNullOrEmpty(powershellCmd)) {
                    Clipboard.SetText(powershellCmd);
                }
            }, () => !string.IsNullOrWhiteSpace(CommandLine));
        }
    }

    public ICommand CopyEnvironmentDifferencesAsPowerShellCommand {
        get {
            return _copyEnvironmentDifferencesAsPowerShellCommand ??= new RelayCommand(() => {
                var powershellScript = ConvertEnvironmentDifferencesToPowerShell(EnvironmentDifferences);
                if (!string.IsNullOrEmpty(powershellScript)) {
                    Clipboard.SetText(powershellScript);
                }
            }, () => HasEnvironmentDifferences);
        }
    }

    public ICommand CopyFullEnvironmentAsPowerShellCommand {
        get {
            return _copyFullEnvironmentAsPowerShellCommand ??= new RelayCommand(() => {
                var powershellScript = ConvertFullEnvironmentToPowerShell(EnvironmentVariables);
                if (!string.IsNullOrEmpty(powershellScript)) {
                    Clipboard.SetText(powershellScript);
                }
            }, () => HasEnvironment);
        }
    }

    public ICommand CopyFullInvocationCommand {
        get {
            return _copyFullInvocationCommand ??= new RelayCommand(() => {
                var powershellScript =
                        ConvertFullInvocationToPowerShell(ApplicationName, EnvironmentVariables, WorkingDirectory, CommandLine);
                if (!string.IsNullOrEmpty(powershellScript)) {
                    Clipboard.SetText(powershellScript);
                }
            }, () => HasEnvironment && !string.IsNullOrWhiteSpace(CommandLine));
        }
    }

    public ICommand LaunchInWinDbgCommand {
        get {
            return _launchInWinDbgCommand ??=
                    new RelayCommand(
                            () => {
                                LaunchInWinDbg(ApplicationName, EnvironmentVariables, WorkingDirectory, CommandLine, breakOnStart: false);
                            }, () => !string.IsNullOrWhiteSpace(CommandLine));
        }
    }

    public ICommand LaunchInWinDbgBreakCommand {
        get {
            return _launchInWinDbgBreakCommand ??=
                    new RelayCommand(
                            () => {LaunchInWinDbg(ApplicationName, EnvironmentVariables, WorkingDirectory, CommandLine, breakOnStart: true);},
                            () => !string.IsNullOrWhiteSpace(CommandLine));
        }
    }

    public ICommand LaunchInVisualStudioCommand {
        get {
            return _launchInVisualStudioCommand ??=
                    new RelayCommand(
                            () => {
                                LaunchInVisualStudio(ApplicationName, EnvironmentVariables, WorkingDirectory, CommandLine,
                                        breakOnStart: false);
                            }, () => !string.IsNullOrWhiteSpace(CommandLine));
        }
    }

    public ICommand LaunchInVisualStudioBreakCommand {
        get {
            return _launchInVisualStudioBreakCommand ??= new RelayCommand(
                    () => {LaunchInVisualStudio(ApplicationName, EnvironmentVariables, WorkingDirectory, CommandLine, breakOnStart: true);},
                    () => !string.IsNullOrWhiteSpace(CommandLine));
        }
    }

    public string BinaryNameDisplay {
        get {
            var name = ExtractBinaryName(ApplicationName, CommandLine);
            if (!string.IsNullOrEmpty(name)) {
                return name;
            }

            if (IsCreationFailure) {
                return "(CreateProcess failed)";
            }

            return "(unknown)";
        }
    }

    internal void ApplyStartData(string applicationName, string commandLine, string workingDirectory,
            IReadOnlyDictionary<string, string> environment) {
        ApplicationName = applicationName;
        CommandLine = commandLine;
        WorkingDirectory = workingDirectory;

        _environmentMap.Clear();
        EnvironmentVariables.Clear();
        foreach (var kv in environment.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)) {
            _environmentMap[kv.Key] = kv.Value;
            EnvironmentVariables.Add(kv);
        }
        OnPropertyChanged(nameof(HasEnvironment));

        RecomputeEnvironmentDifferences();

        IsCreationFailure = false;
        FailureReason = null;
        HasStarted = true;
        IsActive = true;
        IsDetached = false;
        ExitCode = null;
        OnPropertyChanged(nameof(CommandLineDisplay));
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(DisplayLabelSingleLine));
        OnPropertyChanged(nameof(BinaryNameDisplay));
    }

    internal static ProcessNodeViewModel FromCreateFailure(LogServer.ProcessCreate create) {
        var node = new ProcessNodeViewModel(0) {
            ApplicationName = create.ApplicationName,
            CommandLine = create.CommandLine
        };
        node.ApplyCreateFailure(create);
        return node;
    }

    internal void MarkAttached(DateTime timestamp) {
        AttachTime = timestamp;
        if (HasStarted && !IsCreationFailure) {
            IsActive = true;
        }
        IsDetached = false;
    }

    internal void MarkExited(int exitCode) {
        ExitCode = exitCode;
        IsActive = false;
        IsDetached = false;
        if (!HasStarted) {
            HasStarted = true;
        }
    }

    internal void MarkDetached(DateTime timestamp) {
        DetachTime = timestamp;
        IsActive = false;
        if (!ExitCode.HasValue) {
            IsDetached = true;
        }
        if (!HasStarted) {
            HasStarted = true;
        }
    }

    internal void AssignParent(ProcessNodeViewModel? parent) {
        Parent = parent;
        RecomputeEnvironmentDifferences();
    }

    internal void ApplyCreateFailure(LogServer.ProcessCreate create) {
        ApplicationName = create.ApplicationName;
        CommandLine = create.CommandLine;
        WorkingDirectory = null;
        _environmentMap.Clear();
        EnvironmentVariables.Clear();
        EnvironmentDifferences.Clear();
        OnPropertyChanged(nameof(HasEnvironment));
        OnPropertyChanged(nameof(HasEnvironmentDifferences));
        OnPropertyChanged(nameof(EnvironmentDifferencesDisplay));

        FailureReason = string.IsNullOrWhiteSpace(create.CommandLine)
                ? string.IsNullOrWhiteSpace(create.ApplicationName)
                        ? "CreateProcess failed"
                        : $"CreateProcess failed: {create.ApplicationName}"
                : $"CreateProcess failed: {create.CommandLine}";

        IsCreationFailure = true;
        HasStarted = false;
        IsActive = false;
        IsDetached = false;
        ExitCode = null;
        OnPropertyChanged(nameof(CommandLineDisplay));
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(DisplayLabelSingleLine));
        OnPropertyChanged(nameof(BinaryNameDisplay));
    }

    protected void OnPropertyChanged(string propertyName) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void RecomputeEnvironmentDifferences() {
        EnvironmentDifferences.Clear();

        var parentMap = Parent?._environmentMap;
        var orderedKeys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _environmentMap.Keys) {
            orderedKeys.Add(key);
        }
        if (parentMap != null) {
            foreach (var key in parentMap.Keys) {
                orderedKeys.Add(key);
            }
        }

        foreach (var key in orderedKeys) {
            var childHas = _environmentMap.TryGetValue(key, out var childValue);
            string? parentValue = null;
            var parentHas = parentMap != null && parentMap.TryGetValue(key, out parentValue);

            if (childHas && parentHas) {
                if (!string.Equals(childValue, parentValue, StringComparison.Ordinal)) {
                    EnvironmentDifferences.Add(new EnvironmentVariableDifference(key, parentValue,
                            EnvironmentVariableDiffKind.Removed));
                    EnvironmentDifferences.Add(new EnvironmentVariableDifference(key, childValue,
                            EnvironmentVariableDiffKind.Added));
                }
            } else if (childHas) {
                EnvironmentDifferences.Add(new EnvironmentVariableDifference(key, childValue,
                        EnvironmentVariableDiffKind.Added));
            } else if (parentHas) {
                EnvironmentDifferences.Add(new EnvironmentVariableDifference(key, parentValue,
                        EnvironmentVariableDiffKind.Removed));
            }
        }

        OnPropertyChanged(nameof(HasEnvironmentDifferences));
        OnPropertyChanged(nameof(EnvironmentDifferencesDisplay));
    }

    private static string ToSingleLine(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
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
            return string.Empty;
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
            return string.Empty;
        }

        // Use the absolute path from applicationName if available
        var executable = applicationName;
        if (string.IsNullOrWhiteSpace(executable)) {
            // Fallback: parse from command line if applicationName is not available
            var text = commandLine.Trim();
            if (text.Length == 0) {
                return string.Empty;
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
                arguments = endQuote + 1 < text2.Length ? text2.Substring(endQuote + 1).TrimStart() : string.Empty;
            } else {
                arguments = string.Empty;
            }
        } else {
            var spaceIndex = text2.IndexOf(' ');
            if (spaceIndex >= 0) {
                arguments = text2[(spaceIndex + 1)..].TrimStart();
            } else {
                arguments = string.Empty;
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

    private static string ConvertEnvironmentDifferencesToPowerShell(IEnumerable<EnvironmentVariableDifference> differences) {
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

    private static string ConvertFullInvocationToPowerShell(string? applicationName, IEnumerable<KeyValuePair<string, string>> environmentVariables,
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

    private static void LaunchInWinDbg(string? applicationName, IEnumerable<KeyValuePair<string, string>> environmentVariables,
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
                    arguments = endQuote + 1 < text.Length ? text.Substring(endQuote + 1).TrimStart() : string.Empty;
                } else {
                    arguments = string.Empty;
                }
            } else {
                var spaceIndex = text.IndexOf(' ');
                if (spaceIndex >= 0) {
                    arguments = text[(spaceIndex + 1)..].TrimStart();
                } else {
                    arguments = string.Empty;
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

    private static void LaunchInVisualStudio(string? applicationName, IEnumerable<KeyValuePair<string, string>> environmentVariables,
            string? workingDirectory, string? commandLine, bool breakOnStart) {
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
                    arguments = endQuote + 1 < text.Length ? text.Substring(endQuote + 1).TrimStart() : string.Empty;
                } else {
                    arguments = string.Empty;
                }
            } else {
                var spaceIndex = text.IndexOf(' ');
                if (spaceIndex >= 0) {
                    arguments = text[(spaceIndex + 1)..].TrimStart();
                } else {
                    arguments = string.Empty;
                }
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