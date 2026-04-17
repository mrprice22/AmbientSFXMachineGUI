using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AmbientSFXMachineGUI.Models;

namespace AmbientSFXMachineGUI.Services;

public sealed class AgentCoordinator
{
    public ObservableCollection<AgentViewModel> Agents { get; } = new();
    public ObservableCollection<LogEntryViewModel> Log { get; } = new();

    private readonly ConcurrentDictionary<Guid, ActivePlayback> _active = new();
    private const int LogCap = 500;

    public event EventHandler<LogEntryViewModel>? SoundPlayed;

    public void LoadAgentsFromDisk()
    {
        // TODO: scan snd/ directory, construct SoundAgent per folder,
        //       wrap each with AgentViewModel, hook playback events → PublishLog.
    }

    public void Shutdown()
    {
        // TODO: stop all agent loops, dispose NAudio outputs.
    }

    public void ForcePlay(AgentViewModel agent)
    {
        // TODO: bypass wait timer on underlying SoundAgent.
    }

    public void SetMasterVolume(double volume)
    {
        // TODO: scale all agent outputs; cache previous value for mute/unmute.
    }

    public void SetMuteAll(bool muted)
    {
        // TODO: mute/unmute all active outputs.
    }

    internal void PublishLog(LogEntryViewModel entry)
    {
        Log.Add(entry);
        while (Log.Count > LogCap) Log.RemoveAt(0);
        SoundPlayed?.Invoke(this, entry);
    }

    internal IReadOnlyCollection<ActivePlayback> ActivePlaybacks => (IReadOnlyCollection<ActivePlayback>)_active.Values;
}

public sealed class ActivePlayback
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string AgentName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public TimeSpan Position { get; set; }
    public TimeSpan Duration { get; set; }
}
