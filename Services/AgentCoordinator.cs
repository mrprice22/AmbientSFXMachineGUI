using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using AmbientSFXMachineGUI.Models;

namespace AmbientSFXMachineGUI.Services;

public sealed class AgentCoordinator
{
    public ObservableCollection<AgentViewModel> Agents { get; } = new();
    public ObservableCollection<LogEntryViewModel> Log { get; } = new();

    private readonly ConcurrentDictionary<Guid, ActivePlayback> _active = new();
    private readonly Dictionary<AgentViewModel, AgentRuntime> _runtime = new();
    private const int LogCap = 500;

    private double _masterVolume = 100;
    private bool _mutedAll;

    public event EventHandler<LogEntryViewModel>? SoundPlayed;

    public AgentCoordinator()
    {
        Agents.CollectionChanged += OnAgentsCollectionChanged;
    }

    public void LoadAgentsFromDisk()
    {
        // TODO: scan snd/ directory, construct SoundAgent per folder,
        //       wrap each with AgentViewModel (via RegisterAgent), hook playback events → PublishLog.
    }

    public void Shutdown()
    {
        // TODO: stop all agent loops, dispose NAudio outputs.
    }

    public void ForcePlay(AgentViewModel agent)
    {
        if (!_runtime.TryGetValue(agent, out var rt)) return;
        rt.ForcePlayPending = true;
        agent.NextPlayIn = TimeSpan.Zero;
        // When SoundAgent is wired up, the runtime tick will observe ForcePlayPending,
        // clear it, and trigger the next sound immediately.
    }

    public void SetMasterVolume(double volume)
    {
        _masterVolume = Clamp01to100(volume);
        foreach (var agent in Agents) ApplyEffectiveVolume(agent);
    }

    public void SetMuteAll(bool muted)
    {
        _mutedAll = muted;
        foreach (var agent in Agents) ApplyEffectiveVolume(agent);
    }

    /// <summary>Effective gain in [0,1] = appMaster * agent.Volume * (mute? 0 : 1), both sliders 0-100.</summary>
    public float GetEffectiveVolume(AgentViewModel agent)
    {
        if (_mutedAll) return 0f;
        return (float)((_masterVolume / 100.0) * (agent.Volume / 100.0));
    }

    internal void PublishLog(LogEntryViewModel entry)
    {
        Log.Add(entry);
        while (Log.Count > LogCap) Log.RemoveAt(0);
        SoundPlayed?.Invoke(this, entry);
    }

    internal IReadOnlyCollection<ActivePlayback> ActivePlaybacks => (IReadOnlyCollection<ActivePlayback>)_active.Values;

    private void OnAgentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (AgentViewModel a in e.NewItems) AttachAgent(a);
        if (e.OldItems != null)
            foreach (AgentViewModel a in e.OldItems) DetachAgent(a);
    }

    private void AttachAgent(AgentViewModel agent)
    {
        if (_runtime.ContainsKey(agent)) return;
        _runtime[agent] = new AgentRuntime();
        agent.PropertyChanged += OnAgentPropertyChanged;
        agent.ForcePlayRequested += OnAgentForcePlayRequested;
        ApplyEnabled(agent);
        ApplyEffectiveVolume(agent);
    }

    private void DetachAgent(AgentViewModel agent)
    {
        agent.PropertyChanged -= OnAgentPropertyChanged;
        agent.ForcePlayRequested -= OnAgentForcePlayRequested;
        _runtime.Remove(agent);
    }

    private void OnAgentForcePlayRequested(object? sender, EventArgs e)
    {
        if (sender is AgentViewModel agent) ForcePlay(agent);
    }

    private void OnAgentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AgentViewModel agent) return;
        switch (e.PropertyName)
        {
            case nameof(AgentViewModel.IsEnabled):
                ApplyEnabled(agent);
                break;
            case nameof(AgentViewModel.Volume):
                ApplyEffectiveVolume(agent);
                break;
        }
    }

    private void ApplyEnabled(AgentViewModel agent)
    {
        if (!_runtime.TryGetValue(agent, out var rt)) return;
        rt.Enabled = agent.IsEnabled;
        // TODO: when SoundAgent is wired up, pause/resume its loop here.
    }

    private void ApplyEffectiveVolume(AgentViewModel agent)
    {
        if (!_runtime.TryGetValue(agent, out var rt)) return;
        rt.EffectiveGain = GetEffectiveVolume(agent);
        // TODO: when SoundAgent is wired up, push rt.EffectiveGain to any live NAudio output.
    }

    private static double Clamp01to100(double v) => v < 0 ? 0 : v > 100 ? 100 : v;

    private sealed class AgentRuntime
    {
        public bool Enabled { get; set; } = true;
        public float EffectiveGain { get; set; } = 1f;
        public bool ForcePlayPending { get; set; }
    }
}

public sealed class ActivePlayback
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string AgentName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public TimeSpan Position { get; set; }
    public TimeSpan Duration { get; set; }
}
