using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmbientSFXMachineGUI.Models;

public partial class MachineViewModel : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _iconPath = string.Empty;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private double _masterVolume = 100;
    [ObservableProperty] private string _rootPath = string.Empty;

    public ObservableCollection<AgentViewModel> Agents { get; } = new();
}
