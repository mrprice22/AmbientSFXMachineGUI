using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using AmbientSFXMachineGUI.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmbientSFXMachineGUI.Shell;

public partial class AddToAgentsDialog : Window
{
    private readonly ObservableCollection<AgentPickItem> _items = new();
    private readonly ICollectionView _view;

    public IReadOnlyList<AgentPickItem> SelectedAgents
        => _items.Where(p => p.IsSelected).ToList();

    public AddToAgentsDialog(AudioFileEntry entry, IEnumerable<MachineViewModel> machines)
    {
        InitializeComponent();

        PromptText.Text = $"Add \"{entry.FileName}\" to which agent(s)?";
        SubText.Text = entry.AbsolutePath;

        foreach (var machine in machines)
        foreach (var agent in machine.Agents)
        {
            var alreadyHas = agent.Files.Any(f =>
                string.Equals(f.FilePath, entry.AbsolutePath, StringComparison.OrdinalIgnoreCase));
            if (alreadyHas) continue;
            var pick = new AgentPickItem(machine, agent);
            pick.PropertyChanged += OnPickChanged;
            _items.Add(pick);
        }

        _view = CollectionViewSource.GetDefaultView(_items);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AgentPickItem.MachineName)));
        _view.SortDescriptions.Add(new SortDescription(nameof(AgentPickItem.MachineName), ListSortDirection.Ascending));
        _view.SortDescriptions.Add(new SortDescription(nameof(AgentPickItem.AgentName), ListSortDirection.Ascending));
        _view.Filter = FilterPredicate;
        PickList.ItemsSource = _view;

        UpdateCount();
    }

    private bool FilterPredicate(object obj)
    {
        if (string.IsNullOrWhiteSpace(FilterBox?.Text)) return true;
        if (obj is not AgentPickItem p) return false;
        var q = FilterBox.Text.Trim();
        return p.AgentName.Contains(q, StringComparison.OrdinalIgnoreCase)
            || p.MachineName.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void OnFilterChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => _view?.Refresh();

    private void OnPickChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AgentPickItem.IsSelected)) UpdateCount();
    }

    private void UpdateCount()
    {
        var n = _items.Count(i => i.IsSelected);
        CountText.Text = n == 0 ? string.Empty : $"{n} selected";
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

public partial class AgentPickItem : ObservableObject
{
    public MachineViewModel Machine { get; }
    public AgentViewModel Agent { get; }
    public string MachineName => Machine.Name;
    public string AgentName => Agent.Name;

    [ObservableProperty] private bool _isSelected;

    public AgentPickItem(MachineViewModel machine, AgentViewModel agent)
    {
        Machine = machine;
        Agent = agent;
    }
}
