namespace SpawnCamper.Server.ViewModels;

public enum EnvironmentVariableDiffKind { Added, Removed }

public record EnvironmentVariableDifference(string Key, string? Value, EnvironmentVariableDiffKind Kind);