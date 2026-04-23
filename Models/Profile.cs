using System.Collections.Generic;

namespace AmbientSFXMachineGUI.Models;

public sealed class Profile
{
    public string Name { get; set; } = string.Empty;
    public double MachineMasterVolume { get; set; } = 100;
    public List<AgentProfileState> Agents { get; set; } = new();
    public List<SoundOverride> SoundOverrides { get; set; } = new();
    public List<SoundboardEntry> Soundboard { get; set; } = new();
    public Dictionary<string, string?> Hotkeys { get; set; } = new();
    public List<string> PinnedAgents { get; set; } = new();
}

public sealed class AgentProfileState
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public double Volume { get; set; } = 100;
}

public sealed class SoundOverride
{
    public string Path { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public double VolumeOverride { get; set; } = 100;
    public int? CooldownOverrideSeconds { get; set; }
}

public sealed class SoundboardEntry
{
    public string Label { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? Group { get; set; }
    public string? Hotkey { get; set; }
    public double Volume { get; set; } = 100;
    public bool IsDivider { get; set; }
}
