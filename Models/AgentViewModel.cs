using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmbientSFXMachineGUI.Models;

public partial class AgentViewModel : ObservableObject
{
    public string FolderPath { get; }
    public string Name { get; }

    public AgentViewModel(string folderPath)
    {
        FolderPath = folderPath;
        Name = System.IO.Path.GetFileName(folderPath);
        Files = new ObservableCollection<SoundFileViewModel>();
    }

    [ObservableProperty] private string _mode = "random";
    [ObservableProperty] private int _fileCount;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private double _volume = 100;
    [ObservableProperty] private string? _lastPlayedFile;
    [ObservableProperty] private TimeSpan _nextPlayIn;
    [ObservableProperty] private bool _isInTurboMode;
    [ObservableProperty] private int _remainingTurboPlays;
    [ObservableProperty] private float _lastPanValue;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private bool _isExpanded;

    public ObservableCollection<SoundFileViewModel> Files { get; }

    public event EventHandler? ForcePlayRequested;

    [RelayCommand]
    private void ForcePlay() => ForcePlayRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenConfigEditor()
    {
        // TODO: surface config editor sheet
    }

    [RelayCommand]
    private void TogglePinned() => IsPinned = !IsPinned;
}
