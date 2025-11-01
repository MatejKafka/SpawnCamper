using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpawnCamper.Core;

public record TracedProcess(
        int ProcessId,
        TracedProcess? Parent,
        DateTime StartTime,
        string ExePath,
        string CommandLine,
        string WorkingDirectory,
        Dictionary<string, string> Environment
) : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;

    public readonly Dictionary<string, string?>? EnvironmentDiff =
            Parent == null ? null : CalculateEnvironmentDiff(Parent.Environment, Environment);

    public int? ExitCode {
        get;
        set => UpdateProperty(out field, value);
    }

    public DateTime? EndTime {
        get;
        set => UpdateProperty(out field, value);
    }

    public TimeSpan? Duration => EndTime - StartTime;

    private static Dictionary<string, string?> CalculateEnvironmentDiff(
            Dictionary<string, string> parent, Dictionary<string, string> child) {
        var diff = new Dictionary<string, string?>();
        // erase keys that are missing from the child
        foreach (var k in parent.Keys.Where(k => !child.ContainsKey(k))) {
            diff[k] = null;
        }
        // set values that differ
        foreach (var (k, v) in child) {
            var parentVal = parent.GetValueOrDefault(k);
            if (v != parentVal) {
                diff[k] = v;
            }
        }
        return diff;
    }

    private void UpdateProperty<T>(out T prop, T value, [CallerMemberName] string propName = "") {
        if (value == null) {
            throw new InvalidOperationException($"Cannot reassign property {propName} to null.");
        }
        prop = value;
        // strip `ref ` or `out `
        propName = propName[(propName.IndexOf(' ') + 1)..];
        PropertyChanged?.Invoke(this, new(propName));
    }
}