using System.Collections.Generic;

namespace AmbientSFXMachineGUI.Models;

public sealed class Profile
{
    public string Name { get; set; } = string.Empty;
    public List<AgentProfileState> Agents { get; set; } = new();
    public List<SoundOverride> SoundOverrides { get; set; } = new();
    public List<SoundboardItem> Soundboard { get; set; } = new();
    public Dictionary<string, string> Hotkeys { get; set; } = new();
    public List<string> PinnedAgents { get; set; } = new();
}

public sealed class AgentProfileState
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Volume { get; set; } = 100;
}

public sealed class SoundOverride
{
    public string Path { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int VolumeOverride { get; set; } = 100;
    public int? CooldownOverrideSeconds { get; set; }
}
