using System;

namespace AmbientSFXMachineGUI.Models;

public enum DebugLogCategory
{
    User,
    Agent,
    Error,
}

public sealed class DebugLogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public DebugLogCategory Category { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public string CategoryBadge => Category switch
    {
        DebugLogCategory.User  => "USER",
        DebugLogCategory.Agent => "AGENT",
        DebugLogCategory.Error => "ERROR",
        _ => "INFO",
    };

    public string ToLogLine()
        => $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{CategoryBadge,-5}] {Source}: {Message}";
}
