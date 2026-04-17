using System;
using System.Collections.ObjectModel;
using System.IO;
using AmbientSFXMachineGUI.Models;

namespace AmbientSFXMachineGUI.Services;

public sealed class ProfileService
{
    public ObservableCollection<Profile> Profiles { get; } = new();
    public Profile? ActiveProfile { get; private set; }

    public event EventHandler<Profile>? ProfileChanged;

    public static string ProfilesDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AmbientAgents", "profiles");

    public void LoadAll()
    {
        // TODO: enumerate *.json in ProfilesDirectory, deserialize, populate Profiles.
    }

    public void Save(Profile profile)
    {
        // TODO: serialize to ProfilesDirectory/<name>.json via System.Text.Json.
    }

    public Profile SnapshotCurrent(string name)
    {
        // TODO: build Profile from AgentCoordinator state, save, add to Profiles.
        return new Profile { Name = name };
    }

    public void Apply(Profile profile)
    {
        // TODO: iterate agents respecting PinnedAgents, apply states,
        //       then raise ProfileChanged for ViewModels to refresh.
        ActiveProfile = profile;
        ProfileChanged?.Invoke(this, profile);
    }

    public ProfileDiff Diff(Profile target)
    {
        // TODO: compute summary of changes versus current state.
        return new ProfileDiff();
    }

    public IDisposable Audition(Profile profile, TimeSpan duration)
    {
        // TODO: snapshot current state, apply profile, schedule revert after duration.
        throw new NotImplementedException();
    }
}

public sealed class ProfileDiff
{
    public int AgentsToggled { get; set; }
    public int VolumesChanged { get; set; }
    public int SoundsEnabledChanged { get; set; }
}
