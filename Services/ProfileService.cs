using System;
using System.Collections.ObjectModel;
using AmbientSFXMachineGUI.Models;

namespace AmbientSFXMachineGUI.Services;

public sealed class ProfileService
{
    public ObservableCollection<Profile> Profiles { get; } = new();
    public Profile? ActiveProfile { get; private set; }

    public event EventHandler<Profile>? ProfileChanged;

    public static string ProfilesDirectory(Guid machineId) =>
        MachinePaths.ProfilesDir(machineId);

    public void LoadAll(Guid machineId)
    {
        // TODO (PROF-01): enumerate *.json in ProfilesDirectory(machineId), deserialize, populate Profiles.
    }

    public void Save(Guid machineId, Profile profile)
    {
        // TODO (PROF-01): serialize to ProfilesDirectory(machineId)/<name>.json via System.Text.Json.
    }

    public Profile SnapshotCurrent(Guid machineId, string name)
    {
        // TODO (PROF-01/PROF-03): build Profile from MachineCoordinator state, save, add to Profiles.
        return new Profile { Name = name };
    }

    public void Apply(Profile profile)
    {
        // TODO (PROF-02): iterate agents respecting PinnedAgents, apply states,
        //       then raise ProfileChanged for ViewModels to refresh.
        ActiveProfile = profile;
        ProfileChanged?.Invoke(this, profile);
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
}

public sealed class ProfileDiff
{
    public int AgentsToggled { get; set; }
    public int VolumesChanged { get; set; }
    public int SoundsEnabledChanged { get; set; }
}
