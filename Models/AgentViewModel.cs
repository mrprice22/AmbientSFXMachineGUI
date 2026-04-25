using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmbientSFXMachineGUI.Models;

public partial class AgentViewModel : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty] private string _folderPath = string.Empty;
    [ObservableProperty] private string _name = string.Empty;

    public AgentViewModel(string folderPath)
    {
        _folderPath = folderPath;
        _name = System.IO.Path.GetFileName(folderPath);
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
    [ObservableProperty] private bool _isHiddenBySearch;

    public ObservableCollection<SoundFileViewModel> Files { get; }

    public event EventHandler? ForcePlayRequested;

    [RelayCommand]
    private void ForcePlay() => ForcePlayRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenConfigEditor()
    {
        var window = new AmbientSFXMachineGUI.Shell.ConfigEditorWindow(this)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    [RelayCommand]
    private void TogglePinned() => IsPinned = !IsPinned;
}
