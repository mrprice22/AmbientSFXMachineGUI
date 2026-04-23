using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using AmbientSFXMachineGUI.Models;

namespace AmbientSFXMachineGUI.Services;

public sealed class AgentCoordinator
{
    private readonly ObservableCollection<AgentViewModel> _agents;
    private readonly Action<LogEntryViewModel> _publishLog;
    private readonly ConcurrentDictionary<Guid, ActivePlayback> _active = new();
    private readonly Dictionary<AgentViewModel, AgentRuntime> _runtime = new();

    private double _masterVolume = 100;
    private double _machineMasterVolume = 100;
    private bool _mutedAll;
    private bool _groupEnabled = true;

    public AgentCoordinator(ObservableCollection<AgentViewModel> agents, Action<LogEntryViewModel> publishLog)
    {
        _agents = agents;
        _publishLog = publishLog;
        _agents.CollectionChanged += OnAgentsCollectionChanged;
    }

    public void RegisterAgentFromFolder(string folderPath)
    {
        if (_agents.Any(a => a.FolderPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase)))
            return;

        if (!Directory.GetFiles(folderPath, "*.config").Any())
            new AgentConfigModel().WriteToDisk(folderPath);

        var vm = new AgentViewModel(folderPath);
        var cfg = AgentConfigModel.ReadFromDisk(folderPath);
        vm.IsEnabled = cfg.Enabled;
        vm.Volume    = cfg.Volume;
        vm.Mode      = cfg.Mode;

        var audioFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                                  .Where(f => !f.EndsWith(".config", StringComparison.OrdinalIgnoreCase))
                                  .ToList();
        foreach (var f in audioFiles) vm.Files.Add(new SoundFileViewModel(f));
        vm.FileCount = audioFiles.Count;

        _agents.Add(vm);
    }

    public void LoadAgentsFromDisk()
    {
        // TODO: scan snd/ directory, construct SoundAgent per folder,
        //       wrap each with AgentViewModel (via RegisterAgentFromFolder), hook playback events → PublishLog.
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
    }

    public void SetMasterVolume(double volume)
    {
        _masterVolume = Clamp01to100(volume);
        foreach (var agent in _agents) ApplyEffectiveVolume(agent);
    }

    public void SetMachineVolume(double volume)
    {
        _machineMasterVolume = Clamp01to100(volume);
        foreach (var agent in _agents) ApplyEffectiveVolume(agent);
    }

    public void SetMuteAll(bool muted)
    {
        _mutedAll = muted;
        foreach (var agent in _agents) ApplyEffectiveVolume(agent);
    }

    public void SetGroupEnabled(bool enabled)
    {
        _groupEnabled = enabled;
        foreach (var agent in _agents) { ApplyEnabled(agent); ApplyEffectiveVolume(agent); }
    }

    /// <summary>Effective gain in [0,1] = appMaster * machineMaster * agent.Volume (all 0–100 sliders).</summary>
    public float GetEffectiveVolume(AgentViewModel agent)
    {
        if (_mutedAll || !_groupEnabled) return 0f;
        return (float)((_masterVolume / 100.0) * (_machineMasterVolume / 100.0) * (agent.Volume / 100.0));
    }

    internal IReadOnlyCollection<ActivePlayback> ActivePlaybacks
        => (IReadOnlyCollection<ActivePlayback>)_active.Values;

    internal void PublishLog(LogEntryViewModel entry) => _publishLog(entry);

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
        rt.Enabled = _groupEnabled && agent.IsEnabled;
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
    public Guid MachineId { get; init; }
    public string AgentName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public TimeSpan Position { get; set; }
    public TimeSpan Duration { get; set; }
}
