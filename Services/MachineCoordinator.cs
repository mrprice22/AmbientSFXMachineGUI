using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    private static string MachinesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AmbientAgents", "machines");

    public MachineViewModel CreateMachine(string name = "New Machine", string rootPath = "")
    {
        var machine = new MachineViewModel { Name = name, RootPath = rootPath };
        return AttachMachine(machine);
    }

    public void RemoveMachine(MachineViewModel machine)
    {
        machine.PropertyChanged -= OnMachinePropertyChanged;
        if (_coordinators.Remove(machine, out var coordinator))
            coordinator.Shutdown();
        Machines.Remove(machine);
    }

    public AgentCoordinator GetCoordinator(MachineViewModel machine)
        => _coordinators[machine];

    public void RegisterAgentFromFolder(MachineViewModel machine, string folderPath)
        => GetCoordinator(machine).RegisterAgentFromFolder(folderPath);

    public void ScanAgentsFromDisk(MachineViewModel machine)
    {
        if (string.IsNullOrEmpty(machine.RootPath)) return;
        var sndDir = Path.Combine(machine.RootPath, "snd");
        if (!Directory.Exists(sndDir)) return;
        foreach (var dir in Directory.GetDirectories(sndDir))
            RegisterAgentFromFolder(machine, dir);
    }

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
        var dir = MachinesDir;
        if (!Directory.Exists(dir))
        {
            CreateMachine("Default");
            return;
        }

        var records = Directory.GetFiles(dir, "*.json")
            .Select(f =>
            {
                try { return JsonSerializer.Deserialize<MachineRecord>(File.ReadAllText(f)); }
                catch { return null; }
            })
            .Where(r => r is not null)
            .OrderBy(r => r!.Order)
            .ToList();

        if (records.Count == 0)
        {
            CreateMachine("Default");
            return;
        }

        foreach (var r in records)
        {
            var machine = new MachineViewModel
            {
                Id           = r!.Id,
                Name         = r.Name,
                IconPath     = r.IconPath,
                IsEnabled    = r.IsEnabled,
                MasterVolume = r.MasterVolume,
                RootPath     = r.RootPath,
            };
            AttachMachine(machine);
            ScanAgentsFromDisk(machine);
        }
    }

    public void SaveMachinesToDisk()
    {
        var dir = MachinesDir;
        Directory.CreateDirectory(dir);

        var options = new JsonSerializerOptions { WriteIndented = true };
        for (int i = 0; i < Machines.Count; i++)
        {
            var m = Machines[i];
            var record = new MachineRecord
            {
                Id           = m.Id,
                Name         = m.Name,
                IconPath     = m.IconPath,
                IsEnabled    = m.IsEnabled,
                MasterVolume = m.MasterVolume,
                RootPath     = m.RootPath,
                Order        = i,
            };
            File.WriteAllText(Path.Combine(dir, $"{m.Id}.json"),
                              JsonSerializer.Serialize(record, options));
        }

        var activeIds = Machines.Select(m => $"{m.Id}.json").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            if (!activeIds.Contains(Path.GetFileName(file)))
                File.Delete(file);
        }
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

    private void OnMachinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MachineViewModel machine) return;
        if (!_coordinators.TryGetValue(machine, out var coordinator)) return;
        switch (e.PropertyName)
        {
            case nameof(MachineViewModel.MasterVolume):
                coordinator.SetMachineVolume(machine.MasterVolume);
                break;
            case nameof(MachineViewModel.IsEnabled):
                coordinator.SetGroupEnabled(machine.IsEnabled);
                break;
        }
    }

    private MachineViewModel AttachMachine(MachineViewModel machine)
    {
        var coordinator = new AgentCoordinator(machine.Agents, PublishLog);
        coordinator.SetMachineVolume(machine.MasterVolume);
        coordinator.SetGroupEnabled(machine.IsEnabled);
        _coordinators[machine] = coordinator;
        machine.PropertyChanged += OnMachinePropertyChanged;
        Machines.Add(machine);
        return machine;
    }

    private sealed class MachineRecord
    {
        public Guid   Id           { get; set; }
        public string Name         { get; set; } = string.Empty;
        public string IconPath     { get; set; } = string.Empty;
        public bool   IsEnabled    { get; set; } = true;
        public double MasterVolume { get; set; } = 100;
        public string RootPath     { get; set; } = string.Empty;
        public int    Order        { get; set; }
    }
}
