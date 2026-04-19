using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using AmbientSFXMachineGUI.Models;

namespace AmbientSFXMachineGUI.Services;

public sealed class MachineCoordinator
{
    public ObservableCollection<MachineViewModel> Machines { get; } = new();
    public ObservableCollection<LogEntryViewModel> Log { get; } = new();
    private const int LogCap = 500;

    private readonly Dictionary<MachineViewModel, AgentCoordinator> _coordinators = new();

    public event EventHandler<LogEntryViewModel>? SoundPlayed;

    public MachineViewModel CreateMachine(string name = "New Machine", string rootPath = "")
    {
        var machine = new MachineViewModel { Name = name, RootPath = rootPath };
        var coordinator = new AgentCoordinator(machine.Agents, PublishLog);
        _coordinators[machine] = coordinator;
        Machines.Add(machine);
        return machine;
    }

    public void RemoveMachine(MachineViewModel machine)
    {
        if (_coordinators.Remove(machine, out var coordinator))
            coordinator.Shutdown();
        Machines.Remove(machine);
    }

    public AgentCoordinator GetCoordinator(MachineViewModel machine)
        => _coordinators[machine];

    public void RegisterAgentFromFolder(MachineViewModel machine, string folderPath)
        => GetCoordinator(machine).RegisterAgentFromFolder(folderPath);

    public void SetMasterVolume(double volume)
    {
        foreach (var coordinator in _coordinators.Values)
            coordinator.SetMasterVolume(volume);
    }

    public void SetMuteAll(bool muted)
    {
        foreach (var coordinator in _coordinators.Values)
            coordinator.SetMuteAll(muted);
    }

    public void LoadMachinesFromDisk()
    {
        // Persistence implemented in MACHINE-08; seed a default machine for now.
        var machine = CreateMachine("Default");
        GetCoordinator(machine).LoadAgentsFromDisk();
    }

    public void Shutdown()
    {
        foreach (var coordinator in _coordinators.Values)
            coordinator.Shutdown();
    }

    internal void PublishLog(LogEntryViewModel entry)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Log.Add(entry);
            while (Log.Count > LogCap) Log.RemoveAt(0);
            SoundPlayed?.Invoke(this, entry);
        });
    }
}
