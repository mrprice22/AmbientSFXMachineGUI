using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AmbientSFXMachineGUI.Models;
using AmbientSFXMachineGUI.Services;

namespace AmbientSFXMachineGUI.Shell;

public partial class ShellViewModel : ObservableObject
{
    private readonly AgentCoordinator _coordinator;
    private readonly ProfileService _profileService;
    private readonly HotkeyService _hotkeys;

    public ShellViewModel(AgentCoordinator coordinator, ProfileService profileService, HotkeyService hotkeys)
    {
        _coordinator = coordinator;
        _profileService = profileService;
        _hotkeys = hotkeys;

        Agents = _coordinator.Agents;
        Log = _coordinator.Log;
        Profiles = _profileService.Profiles;
        ActivePlaybacks = new ObservableCollection<ActivePlayback>();
        Items = new ObservableCollection<SoundboardItem>();
    }

    public ObservableCollection<AgentViewModel> Agents { get; }
    public ObservableCollection<LogEntryViewModel> Log { get; }
    public ObservableCollection<Profile> Profiles { get; }
    public ObservableCollection<ActivePlayback> ActivePlaybacks { get; }
    public ObservableCollection<SoundboardItem> Items { get; }

    [ObservableProperty] private double _masterVolume = 100;
    [ObservableProperty] private bool _isMutedAll;
    [ObservableProperty] private Profile? _activeProfile;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isEditMode;

    partial void OnMasterVolumeChanged(double value) => _coordinator.SetMasterVolume(value);
    partial void OnIsMutedAllChanged(bool value) => _coordinator.SetMuteAll(value);
    partial void OnActiveProfileChanged(Profile? value)
    {
        if (value is not null) _profileService.Apply(value);
    }

    [RelayCommand]
    private void QuickSaveProfile()
    {
        // TODO: prompt for name, snapshot current state via ProfileService.SnapshotCurrent()
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new HotkeySettingsWindow(_hotkeys)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    [RelayCommand]
    private void ExportCsv()
    {
        // TODO: write Log collection to timestamped CSV via SaveFileDialog.
    }

    [RelayCommand]
    private void Clear() => Log.Clear();

    [RelayCommand]
    private void AddGroup()
    {
        // TODO: insert a labeled soundboard section divider.
    }
}
