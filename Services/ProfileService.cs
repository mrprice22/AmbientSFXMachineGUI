using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using AmbientSFXMachineGUI.Models;

namespace AmbientSFXMachineGUI.Services;

public sealed class ProfileService
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private static readonly char[] _invalidFileChars = Path.GetInvalidFileNameChars();

    public ObservableCollection<Profile> Profiles { get; } = new();
    public Profile? ActiveProfile { get; private set; }

    public event EventHandler<Profile>? ProfileChanged;

    public static string ProfilesDirectory(Guid machineId) =>
        MachinePaths.ProfilesDir(machineId);

    public void LoadAll(Guid machineId)
    {
        Profiles.Clear();
        ActiveProfile = null;
        var dir = ProfilesDirectory(machineId);
        if (!Directory.Exists(dir)) return;

        foreach (var path in Directory.GetFiles(dir, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            Profile? profile = null;
            try { profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path)); }
            catch { /* skip corrupt */ }
            if (profile is not null && !string.IsNullOrWhiteSpace(profile.Name))
                Profiles.Add(profile);
        }
    }

    public void Save(Guid machineId, Profile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name)) return;
        var dir = ProfilesDirectory(machineId);
        Directory.CreateDirectory(dir);
        var fileName = SanitizeFileName(profile.Name) + ".json";
        File.WriteAllText(Path.Combine(dir, fileName), JsonSerializer.Serialize(profile, _json));
    }

    public Profile SnapshotCurrent(MachineViewModel machine, HotkeyService hotkeys, string name)
    {
        var profile = BuildFromMachine(machine, hotkeys, name);
        Save(machine.Id, profile);

        var existing = Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) Profiles[Profiles.IndexOf(existing)] = profile;
        else Profiles.Add(profile);
        return profile;
    }

    public void Apply(MachineViewModel machine, HotkeyService hotkeys, Profile profile)
    {
        var pinned = new HashSet<string>(profile.PinnedAgents ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        machine.MasterVolume = profile.MachineMasterVolume;

        var agentStates = profile.Agents.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var agent in machine.Agents)
        {
            agent.IsPinned = pinned.Contains(agent.Name);
            if (agent.IsPinned) continue;
            if (!agentStates.TryGetValue(agent.Name, out var state)) continue;
            agent.IsEnabled = state.Enabled;
            agent.Volume = state.Volume;
        }

        var overrides = profile.SoundOverrides
            .GroupBy(o => o.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var agent in machine.Agents)
        {
            if (agent.IsPinned) continue;
            foreach (var file in agent.Files)
            {
                if (!overrides.TryGetValue(file.FilePath, out var o)) continue;
                file.IsEnabled = o.Enabled;
                file.VolumeOverride = o.VolumeOverride;
                file.CooldownOverrideSeconds = o.CooldownOverrideSeconds;
            }
        }

        machine.SoundboardItems.Clear();
        foreach (var entry in profile.Soundboard)
        {
            machine.SoundboardItems.Add(new SoundboardItem
            {
                Label     = entry.Label,
                FilePath  = entry.FilePath,
                Group     = entry.Group,
                Hotkey    = entry.Hotkey,
                Volume    = entry.Volume,
                IsDivider = entry.IsDivider,
            });
        }

        hotkeys.ApplyMachineBindings(machine.Id, profile.Hotkeys ?? new Dictionary<string, string?>());

        ActiveProfile = profile;
        ProfileChanged?.Invoke(this, profile);
    }

    public static Profile BuildFromMachine(MachineViewModel machine, HotkeyService hotkeys, string name)
    {
        var profile = new Profile
        {
            Name = name,
            MachineMasterVolume = machine.MasterVolume,
            PinnedAgents = machine.Agents.Where(a => a.IsPinned).Select(a => a.Name).ToList(),
        };

        foreach (var agent in machine.Agents)
        {
            profile.Agents.Add(new AgentProfileState
            {
                Name    = agent.Name,
                Enabled = agent.IsEnabled,
                Volume  = agent.Volume,
            });

            foreach (var file in agent.Files)
            {
                profile.SoundOverrides.Add(new SoundOverride
                {
                    Path                    = file.FilePath,
                    Enabled                 = file.IsEnabled,
                    VolumeOverride          = file.VolumeOverride,
                    CooldownOverrideSeconds = file.CooldownOverrideSeconds,
                });
            }
        }

        foreach (var item in machine.SoundboardItems)
        {
            profile.Soundboard.Add(new SoundboardEntry
            {
                Label     = item.Label,
                FilePath  = item.FilePath,
                Group     = item.Group,
                Hotkey    = item.Hotkey,
                Volume    = item.Volume,
                IsDivider = item.IsDivider,
            });
        }

        profile.Hotkeys = new Dictionary<string, string?>(hotkeys.GetMachineBindings());
        return profile;
    }

    public ProfileDiff Diff(Profile target)
    {
        // TODO (PROF-04): compute summary of changes versus current state.
        return new ProfileDiff();
    }

    public IDisposable Audition(Profile profile, TimeSpan duration)
    {
        // TODO (PROF-05): snapshot current state, apply profile, schedule revert after duration.
        throw new NotImplementedException();
    }

    private static string SanitizeFileName(string name)
    {
        var chars = name.Select(c => _invalidFileChars.Contains(c) ? '_' : c).ToArray();
        var cleaned = new string(chars).Trim();
        return string.IsNullOrEmpty(cleaned) ? "profile" : cleaned;
    }
}

public sealed class ProfileDiff
{
    public int AgentsToggled { get; set; }
    public int VolumesChanged { get; set; }
    public int SoundsEnabledChanged { get; set; }
}
