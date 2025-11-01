using System.Text;
using System.Text.RegularExpressions;
using SpawnCamper.Core.Utils;

namespace SpawnCamper.Server.Utils;

/// <summary>
/// A builder class for constructing PowerShell scripts with proper escaping and formatting.
/// </summary>
public partial class PowerShellScriptBuilder {
    private readonly StringBuilder _sb = new();

    /// <summary>
    /// Appends a line that clears all environment variables.
    /// </summary>
    public PowerShellScriptBuilder ClearEnvironment() {
        _sb.AppendLine("ls Env: | rm");
        return this;
    }

    /// <summary>
    /// Appends an environment variable assignment.
    /// </summary>
    public PowerShellScriptBuilder SetEnvironmentVariable(string name, string? value) {
        _sb.AppendLine($"{ToPowerShellEnvVarLiteral(name)} = {ToPowerShellLiteral(value)}");
        return this;
    }

    /// <summary>
    /// Appends multiple environment variable assignments.
    /// </summary>
    public PowerShellScriptBuilder SetEnvironmentVariables(Dictionary<string, string> variables) {
        foreach (var (name, value) in variables) {
            SetEnvironmentVariable(name, value);
        }
        return this;
    }

    /// <summary>
    /// Appends a cd (Set-Location) command.
    /// </summary>
    public PowerShellScriptBuilder ChangeDirectory(string path) {
        _sb.AppendLine($"cd {ToPowerShellLiteral(path, argument: true)}");
        return this;
    }

    /// <summary>
    /// Appends a command invocation from an application name and command line.
    /// </summary>
    public PowerShellScriptBuilder AppendInvocation(string applicationName, string commandLine) {
        var argv = Native.CommandLineToArgv(commandLine);
        if (argv.Length == 0) {
            return AppendArguments([applicationName]);
        } else {
            // argv[0] can be an arbitrary string, we want to use the actual executed path
            argv[0] = applicationName;
            return AppendArguments(argv);
        }
    }

    /// <summary>
    /// Appends a command invocation from an array of arguments.
    /// </summary>
    public PowerShellScriptBuilder AppendArguments(IEnumerable<string> argv) {
        var invocation = string.Join(" ", argv.Select(arg => ToPowerShellLiteral(arg, argument: true)));
        // if the exe path was quoted, we need to use the call operator (&)
        var command = invocation.StartsWith('\'') ? $"& {invocation}" : invocation;
        _sb.AppendLine(command);
        return this;
    }

    /// <summary>
    /// Appends a blank line.
    /// </summary>
    public PowerShellScriptBuilder AppendLine() {
        _sb.AppendLine();
        return this;
    }

    /// <summary>
    /// Returns the constructed PowerShell script.
    /// </summary>
    public override string ToString() {
        return _sb.ToString().Trim();
    }

    [GeneratedRegex(@"[a-zA-Z0-9_/\\:-]+")]
    private static partial Regex BareArgumentRegex();

    private static string ToPowerShellLiteral(string? value, bool argument = false) {
        if (value == null) {
            return "$null";
        }
        if (value == "") {
            return "''";
        }

        if (argument && BareArgumentRegex().IsMatch(value)) {
            return value; // no need to quote
        }

        // use single quotes and escape any single quotes inside
        return $"'{value.Replace("'", "''")}'";
    }

    private static string ToPowerShellEnvVarLiteral(string varName) {
        // Check if it needs braces: contains special characters like parentheses, spaces, etc.
        return varName.Any(c => !char.IsLetterOrDigit(c) && c != '_') ? $"${{env:{varName}}}" : $"$env:{varName}";
    }
}