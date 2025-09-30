namespace ProcessTracer.Server.UI.ViewModels;

public enum EnvironmentVariableDiffKind {
    Added,
    Removed
}

public class EnvironmentVariableDifference {
    public EnvironmentVariableDifference(string key, string? value, EnvironmentVariableDiffKind kind) {
        Key = key;
        Value = value ?? string.Empty;
        Kind = kind;
    }

    public string Key { get; }

    public string Value { get; }

    public EnvironmentVariableDiffKind Kind { get; }

    public bool IsAdded => Kind == EnvironmentVariableDiffKind.Added;

    public bool IsRemoved => Kind == EnvironmentVariableDiffKind.Removed;

    public string Prefix => IsAdded ? "+" : "-";

    public string ValueText => $"{Key}={Value}";

    public string DisplayText => $"{Prefix} {ValueText}";
}
