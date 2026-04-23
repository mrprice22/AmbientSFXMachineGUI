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
        ApplyCore(machine, hotkeys, profile);
        ActiveProfile = profile;
        ProfileChanged?.Invoke(this, profile);
    }

    private static void ApplyCore(MachineViewModel machine, HotkeyService hotkeys, Profile profile)
    {
        machine.MasterVolume = profile.MachineMasterVolume;

        var agentStates = profile.Agents.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var agent in machine.Agents)
        {
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

    public ProfileDiff Diff(MachineViewModel machine, Profile target)
    {
        var diff = new ProfileDiff();

        if (Math.Abs(machine.MasterVolume - target.MachineMasterVolume) > 0.001)
        {
            diff.MasterVolumeFrom = machine.MasterVolume;
            diff.MasterVolumeTo   = target.MachineMasterVolume;
        }

        var agentStates = target.Agents.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var agent in machine.Agents)
        {
            if (agent.IsPinned) continue;
            if (!agentStates.TryGetValue(agent.Name, out var state)) continue;
            if (agent.IsEnabled != state.Enabled)
                diff.AgentsToggled.Add(new AgentToggleChange(agent.Name, agent.IsEnabled, state.Enabled));
            if (Math.Abs(agent.Volume - state.Volume) > 0.001)
                diff.AgentVolumeChanges.Add(new AgentVolumeChange(agent.Name, agent.Volume, state.Volume));
        }

        var overrides = target.SoundOverrides
            .GroupBy(o => o.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var agent in machine.Agents)
        {
            if (agent.IsPinned) continue;
            foreach (var file in agent.Files)
            {
                if (!overrides.TryGetValue(file.FilePath, out var o)) continue;
                if (file.IsEnabled != o.Enabled)
                    diff.SoundsToggled.Add(new SoundToggleChange(agent.Name, file.FileName, file.IsEnabled, o.Enabled));
            }
        }

        var currentSb = machine.SoundboardItems.Select(i => i.FilePath + "|" + i.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetSb  = target.Soundboard.Select(i => i.FilePath + "|" + i.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        diff.SoundboardAdded   = targetSb.Except(currentSb, StringComparer.OrdinalIgnoreCase).Count();
        diff.SoundboardRemoved = currentSb.Except(targetSb, StringComparer.OrdinalIgnoreCase).Count();

        return diff;
    }

    public AuditionHandle Audition(MachineViewModel machine, HotkeyService hotkeys, Profile profile, TimeSpan duration)
    {
        var snapshot = BuildFromMachine(machine, hotkeys, "__audition__");
        ApplyCore(machine, hotkeys, profile);
        return new AuditionHandle(machine, hotkeys, snapshot, duration);
    }

    public sealed class AuditionHandle : IDisposable
    {
        private readonly MachineViewModel _machine;
        private readonly HotkeyService _hotkeys;
        private readonly Profile _snapshot;
        private readonly System.Windows.Threading.DispatcherTimer? _timer;
        private bool _reverted;

        public event EventHandler? Reverted;

        internal AuditionHandle(MachineViewModel machine, HotkeyService hotkeys, Profile snapshot, TimeSpan duration)
        {
            _machine = machine;
            _hotkeys = hotkeys;
            _snapshot = snapshot;
            if (duration > TimeSpan.Zero)
            {
                _timer = new System.Windows.Threading.DispatcherTimer { Interval = duration };
                _timer.Tick += (_, _) => Dispose();
                _timer.Start();
            }
        }

        public void Dispose()
        {
            if (_reverted) return;
            _reverted = true;
            _timer?.Stop();
            ApplyCore(_machine, _hotkeys, _snapshot);
            Reverted?.Invoke(this, EventArgs.Empty);
        }
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
    public double? MasterVolumeFrom { get; set; }
    public double? MasterVolumeTo { get; set; }
    public List<AgentToggleChange> AgentsToggled { get; } = new();
    public List<AgentVolumeChange> AgentVolumeChanges { get; } = new();
    public List<SoundToggleChange> SoundsToggled { get; } = new();
    public int SoundboardAdded { get; set; }
    public int SoundboardRemoved { get; set; }

    public bool HasChanges =>
        MasterVolumeFrom.HasValue
        || AgentsToggled.Count > 0
        || AgentVolumeChanges.Count > 0
        || SoundsToggled.Count > 0
        || SoundboardAdded > 0
        || SoundboardRemoved > 0;
}

public sealed record AgentToggleChange(string AgentName, bool From, bool To);
public sealed record AgentVolumeChange(string AgentName, double From, double To);
public sealed record SoundToggleChange(string AgentName, string FileName, bool From, bool To);
