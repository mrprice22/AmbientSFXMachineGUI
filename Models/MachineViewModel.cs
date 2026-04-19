using System;
using System.Collections.ObjectModel;
using System.IO;
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

    /// <summary>Resolves a relative IconPath against RootPath so the image converter always receives an absolute path.</summary>
    public string ResolvedIconPath
    {
        get
        {
            if (string.IsNullOrEmpty(IconPath)) return string.Empty;
            if (Path.IsPathRooted(IconPath)) return IconPath;
            if (string.IsNullOrEmpty(RootPath)) return IconPath;
            return Path.GetFullPath(Path.Combine(RootPath, IconPath));
        }
    }

    partial void OnIconPathChanged(string value) => OnPropertyChanged(nameof(ResolvedIconPath));
    partial void OnRootPathChanged(string value) => OnPropertyChanged(nameof(ResolvedIconPath));
}
