using System;
using CommunityToolkit.Mvvm.Input;

namespace AmbientSFXMachineGUI.Models;

public sealed class LibraryUsageItem
{
    private readonly Action<MachineViewModel, AgentViewModel> _navigate;

    public LibraryUsageItem(MachineViewModel machine, AgentViewModel agent,
                            Action<MachineViewModel, AgentViewModel> navigate)
    {
        Machine = machine;
        Agent = agent;
        _navigate = navigate;
        NavigateCommand = new RelayCommand(() => _navigate(Machine, Agent));
    }

    public MachineViewModel Machine { get; }
    public AgentViewModel Agent { get; }
    public string MachineName => Machine.Name;
    public string AgentName => Agent.Name;
    public IRelayCommand NavigateCommand { get; }
}
