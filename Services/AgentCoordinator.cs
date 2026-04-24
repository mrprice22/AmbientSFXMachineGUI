using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using AmbientSFXMachineGUI.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using NAudio.Wave;

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

    public event EventHandler<ActivePlayback>? PlaybackStarted;
    public event EventHandler<ActivePlayback>? PlaybackEnded;

    /// <summary>Registers an in-progress playback so the Now Playing panel can observe it. Called by the playback loop.</summary>
    public ActivePlayback RegisterPlayback(AgentViewModel agent, string filePath, AudioFileReader? reader = null)
    {
        var playback = new ActivePlayback
        {
            Agent     = agent,
            AgentName = agent.Name,
            FilePath  = filePath,
            Reader    = reader,
            Duration  = reader?.TotalTime ?? TimeSpan.Zero,
        };
        _active[playback.Id] = playback;
        PlaybackStarted?.Invoke(this, playback);
        return playback;
    }

    public void UnregisterPlayback(ActivePlayback playback)
    {
        if (_active.TryRemove(playback.Id, out _))
            PlaybackEnded?.Invoke(this, playback);
    }

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
                AmbientSFXMachineGUI.App.DebugLog?.LogUser("Agent",
                    $"{agent.Name}: {(agent.IsEnabled ? "enabled" : "disabled")}");
                break;
            case nameof(AgentViewModel.Volume):
                ApplyEffectiveVolume(agent);
                AmbientSFXMachineGUI.App.DebugLog?.LogUser("Agent",
                    $"{agent.Name}: volume → {agent.Volume:0}");
                break;
            case nameof(AgentViewModel.IsPinned):
                AmbientSFXMachineGUI.App.DebugLog?.LogUser("Agent",
                    $"{agent.Name}: {(agent.IsPinned ? "pinned" : "unpinned")}");
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

public sealed partial class ActivePlayback : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MachineId { get; init; }
    public AgentViewModel? Agent { get; init; }
    public string AgentName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string FileName => string.IsNullOrEmpty(FilePath) ? string.Empty : Path.GetFileName(FilePath);

    /// <summary>NAudio reader the Now Playing panel polls every 100ms to refresh Position. May be null while the engine loop is stubbed.</summary>
    public AudioFileReader? Reader { get; init; }

    [ObservableProperty] private TimeSpan _position;
    [ObservableProperty] private TimeSpan _duration;
    [ObservableProperty] private double _volume = 100;
    [ObservableProperty] private bool _isSoloed;

    partial void OnVolumeChanged(double value)
    {
        if (Reader is null) return;
        try { Reader.Volume = (float)(Math.Clamp(value, 0, 200) / 100.0); }
        catch { }
    }

    /// <summary>Called by the Now Playing panel timer to pull the latest position off the reader.</summary>
    public void RefreshFromReader()
    {
        if (Reader is null) return;
        try
        {
            Position = Reader.CurrentTime;
            if (Duration == TimeSpan.Zero) Duration = Reader.TotalTime;
        }
        catch { }
    }
}
