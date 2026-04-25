using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using AmbientSFXMachineGUI.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

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
        // Agents are populated by MachineCoordinator.ScanAgentsFromDisk which calls RegisterAgentFromFolder.
    }

    public void Shutdown()
    {
        foreach (var kv in _runtime.ToList())
            DetachAgent(kv.Key);
    }

    public void ForcePlay(AgentViewModel agent)
    {
        if (!_runtime.TryGetValue(agent, out var rt)) return;
        AmbientSFXMachineGUI.App.DebugLog?.LogUser("Agent", $"{agent.Name}: force-play");
        FireNow(agent, rt);
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

    /// <summary>Registers an in-progress playback so the Now Playing panel can observe it.</summary>
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
        var rt = new AgentRuntime();
        _runtime[agent] = rt;
        agent.PropertyChanged += OnAgentPropertyChanged;
        agent.ForcePlayRequested += OnAgentForcePlayRequested;
        ReloadConfig(agent, rt);
        ApplyEnabled(agent);
        ApplyEffectiveVolume(agent);
        if (rt.Enabled) ScheduleNext(agent, rt);
    }

    private void DetachAgent(AgentViewModel agent)
    {
        agent.PropertyChanged -= OnAgentPropertyChanged;
        agent.ForcePlayRequested -= OnAgentForcePlayRequested;
        if (_runtime.TryGetValue(agent, out var rt))
            StopRuntime(rt);
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
            case nameof(AgentViewModel.Mode):
                if (_runtime.TryGetValue(agent, out var rtMode))
                {
                    rtMode.Config.Mode = agent.Mode;
                    rtMode.ShuffleOrder = null;
                    rtMode.CurrentIndex = 0;
                }
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
        bool wasEnabled = rt.Enabled;
        rt.Enabled = _groupEnabled && agent.IsEnabled;
        if (rt.Enabled && !wasEnabled)
        {
            ScheduleNext(agent, rt);
        }
        else if (!rt.Enabled && wasEnabled)
        {
            StopRuntime(rt);
        }
    }

    private void ApplyEffectiveVolume(AgentViewModel agent)
    {
        if (!_runtime.TryGetValue(agent, out var rt)) return;
        rt.EffectiveGain = GetEffectiveVolume(agent);
        lock (rt.ActiveLock)
        {
            foreach (var entry in rt.Active.Values)
            {
                try { entry.Reader.Volume = (float)(rt.EffectiveGain * entry.SoundOverride); }
                catch { }
            }
        }
    }

    private void ReloadConfig(AgentViewModel agent, AgentRuntime rt)
    {
        try { rt.Config = AgentConfigModel.ReadFromDisk(agent.FolderPath); }
        catch (Exception ex)
        {
            AmbientSFXMachineGUI.App.DebugLog?.LogError("Agent " + agent.Name,
                $"Could not read config: {ex.Message}");
        }
        rt.Config.Mode = agent.Mode;
        rt.Config.Volume = (int)agent.Volume;
    }

    private void StopRuntime(AgentRuntime rt)
    {
        rt.Enabled = false;
        try { rt.Timer?.Dispose(); } catch { }
        rt.Timer = null;
        List<RuntimeActive> snapshot;
        lock (rt.ActiveLock)
        {
            snapshot = rt.Active.Values.ToList();
            rt.Active.Clear();
        }
        foreach (var a in snapshot)
        {
            try { a.Output.Stop(); } catch { }
            try { a.Output.Dispose(); } catch { }
            try { a.Reader.Dispose(); } catch { }
            UnregisterPlayback(a.Playback);
        }
    }

    private void ScheduleNext(AgentViewModel agent, AgentRuntime rt)
    {
        try { rt.Timer?.Dispose(); } catch { }
        rt.Timer = null;
        if (!rt.Enabled) return;

        ReloadConfig(agent, rt);
        int waitMs = ComputeWaitMs(rt);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null)
            dispatcher.BeginInvoke(new Action(() => agent.NextPlayIn = TimeSpan.FromMilliseconds(waitMs)));

        rt.Timer = new System.Threading.Timer(_ => OnTimerFire(agent, rt), null, waitMs, Timeout.Infinite);
    }

    private static int ComputeWaitMs(AgentRuntime rt)
    {
        var c = rt.Config;
        var rng = rt.Random;
        if (rt.CooldownAfterTurbo > 0)
        {
            int ms = rt.CooldownAfterTurbo * 1000;
            rt.CooldownAfterTurbo = 0;
            return ms;
        }
        if (rt.PlayCounter == 0 && c.OverrideStartupSeconds > 0)
            return rng.Next(0, c.OverrideStartupSeconds + 1) * 1000;
        if (rt.InTurboMode)
        {
            int lo = Math.Max(0, c.MinSeconds);
            int hi = Math.Max(lo, c.MaxSeconds);
            return rng.Next(lo * 1000, hi * 1000 + 1);
        }
        int minTotal = c.MinMinutes * 60 + c.MinSeconds;
        int maxTotal = c.MaxMinutes * 60 + c.MaxSeconds;
        if (maxTotal < minTotal) maxTotal = minTotal;
        if (maxTotal <= 0) maxTotal = minTotal = 1;
        return rng.Next(minTotal, maxTotal + 1) * 1000;
    }

    private void FireNow(AgentViewModel agent, AgentRuntime rt)
    {
        try { rt.Timer?.Dispose(); } catch { }
        rt.Timer = null;
        ThreadPool.QueueUserWorkItem(_ => OnTimerFire(agent, rt));
    }

    private void OnTimerFire(AgentViewModel agent, AgentRuntime rt)
    {
        if (!rt.Enabled) return;
        var file = SelectNextFile(agent, rt);
        if (file == null)
        {
            AmbientSFXMachineGUI.App.DebugLog?.LogAgent(agent.Name, "No enabled audio files; rescheduling.");
            ScheduleNext(agent, rt);
            return;
        }
        if (!File.Exists(file.FilePath))
        {
            AmbientSFXMachineGUI.App.DebugLog?.LogError("Agent " + agent.Name,
                $"File missing: {file.FilePath}");
            ScheduleNext(agent, rt);
            return;
        }
        PlayFile(agent, rt, file);
    }

    private SoundFileViewModel? SelectNextFile(AgentViewModel agent, AgentRuntime rt)
    {
        var enabled = agent.Files.Where(f => f.IsEnabled).ToList();
        if (enabled.Count == 0) return null;

        var now = DateTime.UtcNow;
        var available = enabled.Where(f =>
        {
            if (f.CooldownOverrideSeconds is int cs && cs > 0
                && rt.LastPerFileFire.TryGetValue(f.FilePath, out var last)
                && (now - last).TotalSeconds < cs) return false;
            return true;
        }).ToList();
        if (available.Count == 0) available = enabled;

        switch ((rt.Config.Mode ?? "random").ToLowerInvariant())
        {
            case "sequential":
            {
                if (rt.CurrentIndex >= available.Count) rt.CurrentIndex = 0;
                var f = available[rt.CurrentIndex];
                rt.CurrentIndex = (rt.CurrentIndex + 1) % available.Count;
                return f;
            }
            case "shuffle":
            {
                if (rt.ShuffleOrder == null || rt.CurrentIndex >= rt.ShuffleOrder.Count
                    || rt.ShuffleOrder.Count != available.Count)
                {
                    rt.ShuffleOrder = available.OrderBy(_ => rt.Random.Next()).ToList();
                    rt.CurrentIndex = 0;
                }
                return rt.ShuffleOrder[rt.CurrentIndex++];
            }
            default:
                return available[rt.Random.Next(available.Count)];
        }
    }

    private void PlayFile(AgentViewModel agent, AgentRuntime rt, SoundFileViewModel file)
    {
        AudioFileReader? reader = null;
        WaveOutEvent? output = null;
        ActivePlayback? playback = null;
        try
        {
            reader = new AudioFileReader(file.FilePath);
            double soundOverride = Math.Clamp(file.VolumeOverride, 0, 200) / 100.0;
            reader.Volume = (float)(rt.EffectiveGain * soundOverride);

            var c = rt.Config;
            float pan = 0f;
            ISampleProvider provider = reader;
            if (c.BalanceMin != 50 || c.BalanceMax != 50)
            {
                int blo = Math.Min(c.BalanceMin, c.BalanceMax);
                int bhi = Math.Max(c.BalanceMin, c.BalanceMax);
                pan = (rt.Random.Next(blo, bhi + 1) - 50) / 50f;
                if (rt.Random.Next(100) < c.BalanceInvertChance) pan = -pan;
                pan = Math.Clamp(pan, -1f, 1f);
                ISampleProvider mono = reader.WaveFormat.Channels == 2
                    ? new StereoToMonoSampleProvider(reader)
                    : reader;
                provider = new PanningSampleProvider(mono) { Pan = pan };
            }

            output = new WaveOutEvent();
            output.Init(provider);

            playback = RegisterPlayback(agent, file.FilePath, reader);
            var entry = new RuntimeActive(output, reader, playback, soundOverride);
            lock (rt.ActiveLock) rt.Active[playback.Id] = entry;

            float capturedPan = pan;
            string capturedFileName = file.FileName;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    agent.LastPlayedFile = capturedFileName;
                    agent.LastPanValue = capturedPan;
                    agent.IsInTurboMode = rt.InTurboMode;
                    agent.RemainingTurboPlays = rt.RemainingTurboPlays;
                    file.PlayCountThisSession++;
                }));
            }

            rt.LastPerFileFire[file.FilePath] = DateTime.UtcNow;

            var capturedOut = output;
            var capturedReader = reader;
            var capturedPlayback = playback;
            output.PlaybackStopped += (_, _) =>
            {
                lock (rt.ActiveLock) rt.Active.Remove(capturedPlayback.Id);
                try { capturedOut.Dispose(); } catch { }
                try { capturedReader.Dispose(); } catch { }
                UnregisterPlayback(capturedPlayback);
                OnPlaybackComplete(agent, rt);
            };

            output.Play();

            _publishLog(new LogEntryViewModel
            {
                Timestamp = DateTime.Now,
                AgentName = agent.Name,
                FileName  = file.FileName,
                FilePath  = file.FilePath,
            });
        }
        catch (Exception ex)
        {
            AmbientSFXMachineGUI.App.DebugLog?.LogError("Agent " + agent.Name,
                $"Play failed for {file.FileName}: {ex.Message}");
            try { output?.Dispose(); } catch { }
            try { reader?.Dispose(); } catch { }
            if (playback != null)
            {
                lock (rt.ActiveLock) rt.Active.Remove(playback.Id);
                UnregisterPlayback(playback);
            }
            OnPlaybackComplete(agent, rt);
        }
    }

    private void OnPlaybackComplete(AgentViewModel agent, AgentRuntime rt)
    {
        rt.PlayCounter++;

        var c = rt.Config;
        if (!rt.InTurboMode && c.TurboChance > 0 && rt.Random.Next(100) < c.TurboChance)
        {
            rt.InTurboMode = true;
            int tlo = Math.Max(1, c.TurboMinFires);
            int thi = Math.Max(tlo, c.TurboMaxFires);
            rt.RemainingTurboPlays = rt.Random.Next(tlo, thi + 1);
        }
        else if (rt.InTurboMode)
        {
            rt.RemainingTurboPlays--;
            if (rt.RemainingTurboPlays <= 0)
            {
                rt.InTurboMode = false;
                int minTotal = c.MinMinutes * 60 + c.MinSeconds;
                int maxTotal = c.MaxMinutes * 60 + c.MaxSeconds;
                if (maxTotal < minTotal) maxTotal = minTotal;
                rt.CooldownAfterTurbo = rt.Random.Next(minTotal, Math.Max(minTotal, maxTotal) + 1);
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null)
        {
            dispatcher.BeginInvoke(new Action(() =>
            {
                agent.IsInTurboMode = rt.InTurboMode;
                agent.RemainingTurboPlays = rt.RemainingTurboPlays;
            }));
        }

        ScheduleNext(agent, rt);
    }

    private static double Clamp01to100(double v) => v < 0 ? 0 : v > 100 ? 100 : v;

    private sealed record RuntimeActive(
        WaveOutEvent Output,
        AudioFileReader Reader,
        ActivePlayback Playback,
        double SoundOverride);

    private sealed class AgentRuntime
    {
        public bool Enabled { get; set; } = true;
        public float EffectiveGain { get; set; } = 1f;
        public AgentConfigModel Config { get; set; } = new();
        public System.Threading.Timer? Timer { get; set; }
        public Random Random { get; } = new();
        public int CurrentIndex;
        public List<SoundFileViewModel>? ShuffleOrder;
        public bool InTurboMode;
        public int RemainingTurboPlays;
        public int CooldownAfterTurbo;
        public int PlayCounter;
        public readonly object ActiveLock = new();
        public readonly Dictionary<Guid, RuntimeActive> Active = new();
        public readonly Dictionary<string, DateTime> LastPerFileFire =
            new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>NAudio reader the Now Playing panel polls every 100ms to refresh Position.</summary>
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
