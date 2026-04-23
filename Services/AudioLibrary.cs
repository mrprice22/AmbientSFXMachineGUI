using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using AmbientSFXMachineGUI.Models;

namespace AmbientSFXMachineGUI.Services;

/// <summary>
/// Process-wide registry of every audio file referenced by any machine/agent.
/// Primary key: SHA-256 content hash (populated by LIB-02). Secondary index: absolute path.
/// Until a hash is assigned, entries are addressable only by path.
/// </summary>
public sealed class AudioLibrary
{
    private readonly object _lock = new();
    private readonly Dictionary<string, AudioFileEntry> _byHash = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AudioFileEntry> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<AudioFileEntry> _entries = new();

    // Tracks which AgentViewModel.Files collections we're subscribed to, plus the usage tuple for each.
    private readonly Dictionary<AgentViewModel, AgentSubscription> _agentSubs = new();
    private readonly Dictionary<MachineViewModel, MachineSubscription> _machineSubs = new();

    public ReadOnlyObservableCollection<AudioFileEntry> Entries { get; }

    public AudioLibrary()
    {
        Entries = new ReadOnlyObservableCollection<AudioFileEntry>(_entries);
    }

    public AudioFileEntry? TryGetByPath(string absolutePath)
    {
        lock (_lock)
        {
            return _byPath.TryGetValue(absolutePath, out var entry) ? entry : null;
        }
    }

    public AudioFileEntry? TryGetByHash(string sha256)
    {
        if (string.IsNullOrEmpty(sha256)) return null;
        lock (_lock)
        {
            return _byHash.TryGetValue(sha256, out var entry) ? entry : null;
        }
    }

    /// <summary>
    /// Called by the hasher (LIB-02) once a SHA-256 is computed. If another entry already holds
    /// that hash, the two are merged and this entry is retired; callers that still hold a reference
    /// should re-resolve via <see cref="TryGetByHash"/>.
    /// </summary>
    public AudioFileEntry AssignHash(AudioFileEntry entry, string sha256)
    {
        if (string.IsNullOrEmpty(sha256)) throw new ArgumentException("sha256 must be non-empty", nameof(sha256));
        lock (_lock)
        {
            if (_byHash.TryGetValue(sha256, out var canonical) && canonical != entry)
            {
                foreach (var usage in entry.Usages.ToList())
                {
                    canonical.AddUsage(usage);
                    entry.RemoveUsage(usage);
                }
                _byPath.Remove(entry.AbsolutePath);
                _entries.Remove(entry);
                return canonical;
            }
            entry.Sha256 = sha256;
            _byHash[sha256] = entry;
            return entry;
        }
    }

    /// <summary>Attach the coordinator so we observe every current and future machine.</summary>
    public void Attach(MachineCoordinator coordinator)
    {
        foreach (var machine in coordinator.Machines) AttachMachine(machine);
        coordinator.Machines.CollectionChanged += (_, e) =>
        {
            if (e.OldItems != null)
                foreach (MachineViewModel m in e.OldItems) DetachMachine(m);
            if (e.NewItems != null)
                foreach (MachineViewModel m in e.NewItems) AttachMachine(m);
        };
    }

    private void AttachMachine(MachineViewModel machine)
    {
        if (_machineSubs.ContainsKey(machine)) return;

        NotifyCollectionChangedEventHandler handler = (_, e) =>
        {
            if (e.OldItems != null)
                foreach (AgentViewModel a in e.OldItems) DetachAgent(machine, a);
            if (e.NewItems != null)
                foreach (AgentViewModel a in e.NewItems) AttachAgent(machine, a);
        };
        machine.Agents.CollectionChanged += handler;
        _machineSubs[machine] = new MachineSubscription(handler);

        foreach (var agent in machine.Agents) AttachAgent(machine, agent);
    }

    private void DetachMachine(MachineViewModel machine)
    {
        if (!_machineSubs.Remove(machine, out var sub)) return;
        machine.Agents.CollectionChanged -= sub.Handler;
        foreach (var agent in machine.Agents.ToList()) DetachAgent(machine, agent);
    }

    private void AttachAgent(MachineViewModel machine, AgentViewModel agent)
    {
        if (_agentSubs.ContainsKey(agent)) return;

        NotifyCollectionChangedEventHandler handler = (_, e) =>
        {
            if (e.OldItems != null)
                foreach (SoundFileViewModel f in e.OldItems) UnregisterFileUsage(f.FilePath, machine.Id, agent.Id);
            if (e.NewItems != null)
                foreach (SoundFileViewModel f in e.NewItems) RegisterFileUsage(f.FilePath, machine.Id, agent.Id);
            if (e.Action == NotifyCollectionChangedAction.Reset)
                RemoveUsagesMatching(u => u.MachineId == machine.Id && u.AgentId == agent.Id);
        };
        agent.Files.CollectionChanged += handler;
        _agentSubs[agent] = new AgentSubscription(machine, handler);

        foreach (var f in agent.Files) RegisterFileUsage(f.FilePath, machine.Id, agent.Id);
    }

    private void DetachAgent(MachineViewModel machine, AgentViewModel agent)
    {
        if (!_agentSubs.Remove(agent, out var sub)) return;
        agent.Files.CollectionChanged -= sub.Handler;
        RemoveUsagesMatching(u => u.MachineId == machine.Id && u.AgentId == agent.Id);
    }

    private AudioFileEntry RegisterFileUsage(string absolutePath, Guid machineId, Guid agentId)
    {
        lock (_lock)
        {
            if (!_byPath.TryGetValue(absolutePath, out var entry))
            {
                entry = new AudioFileEntry { AbsolutePath = absolutePath };
                if (File.Exists(absolutePath))
                {
                    try { entry.ByteSize = new FileInfo(absolutePath).Length; } catch { }
                }
                _byPath[absolutePath] = entry;
                _entries.Add(entry);
            }
            entry.AddUsage(new UsageRef(machineId, agentId));
            return entry;
        }
    }

    private void UnregisterFileUsage(string absolutePath, Guid machineId, Guid agentId)
    {
        lock (_lock)
        {
            if (!_byPath.TryGetValue(absolutePath, out var entry)) return;
            entry.RemoveUsage(new UsageRef(machineId, agentId));
        }
    }

    private void RemoveUsagesMatching(Predicate<UsageRef> predicate)
    {
        lock (_lock)
        {
            foreach (var entry in _entries.ToList()) entry.RemoveUsagesMatching(predicate);
        }
    }

    private sealed record MachineSubscription(NotifyCollectionChangedEventHandler Handler);
    private sealed record AgentSubscription(MachineViewModel Machine, NotifyCollectionChangedEventHandler Handler);
}
