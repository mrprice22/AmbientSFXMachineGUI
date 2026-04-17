using System;

namespace AmbientSFXMachineGUI.Models;

public sealed class LogEntryViewModel
{
    public DateTime Timestamp { get; init; }
    public string AgentName { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
}
