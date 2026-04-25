using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmbientSFXMachineGUI.Models;

// MACHINE-11: one group header per machine in the grouped agents view.
public partial class AgentGroupViewModel : ObservableObject
{
    public MachineViewModel Machine { get; }

    public ObservableCollection<AgentViewModel> Agents => Machine.Agents;

    [ObservableProperty] private bool _isCollapsed;

    public event EventHandler? CollapseStateChanged;

    public AgentGroupViewModel(MachineViewModel machine, bool isCollapsed)
    {
        Machine = machine;
        _isCollapsed = isCollapsed;
    }

    partial void OnIsCollapsedChanged(bool value) => CollapseStateChanged?.Invoke(this, EventArgs.Empty);
}
