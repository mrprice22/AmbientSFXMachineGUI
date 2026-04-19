using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AmbientSFXMachineGUI.Models;
using AmbientSFXMachineGUI.Services;
using System.Windows.Forms;

namespace AmbientSFXMachineGUI.Shell;

public partial class ShellViewModel : ObservableObject
{
    private readonly MachineCoordinator _machineCoordinator;
    private readonly ProfileService _profileService;
    private readonly HotkeyService _hotkeys;
    private static readonly ObservableCollection<AgentViewModel> _emptyAgents = new();

    public ShellViewModel(MachineCoordinator machineCoordinator, ProfileService profileService, HotkeyService hotkeys)
    {
        _machineCoordinator = machineCoordinator;
        _profileService = profileService;
        _hotkeys = hotkeys;

        Machines = _machineCoordinator.Machines;
        Log = _machineCoordinator.Log;
        Profiles = _profileService.Profiles;
        ActivePlaybacks = new ObservableCollection<ActivePlayback>();
        Items = new ObservableCollection<SoundboardItem>();

        if (Machines.Count > 0)
            SelectedMachine = Machines[0];
        Machines.CollectionChanged += (_, _) =>
        {
            if (SelectedMachine is null && Machines.Count > 0)
                SelectedMachine = Machines[0];
        };
    }

    public ObservableCollection<MachineViewModel> Machines { get; }
    public ObservableCollection<LogEntryViewModel> Log { get; }
    public ObservableCollection<Profile> Profiles { get; }
    public ObservableCollection<ActivePlayback> ActivePlaybacks { get; }
    public ObservableCollection<SoundboardItem> Items { get; }

    public ObservableCollection<AgentViewModel> Agents
        => SelectedMachine?.Agents ?? _emptyAgents;

    [ObservableProperty] private MachineViewModel? _selectedMachine;
    [ObservableProperty] private double _masterVolume = 100;
    [ObservableProperty] private bool _isMutedAll;
    [ObservableProperty] private Profile? _activeProfile;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isEditMode;

    partial void OnSelectedMachineChanged(MachineViewModel? value)
        => OnPropertyChanged(nameof(Agents));

    partial void OnMasterVolumeChanged(double value) => _machineCoordinator.SetMasterVolume(value);
    partial void OnIsMutedAllChanged(bool value) => _machineCoordinator.SetMuteAll(value);
    partial void OnActiveProfileChanged(Profile? value)
    {
        if (value is not null) _profileService.Apply(value);
    }

    [RelayCommand]
    private void QuickSaveProfile()
    {
        // TODO: prompt for name, snapshot current state via ProfileService.SnapshotCurrent()
    }

    private MiniModeWindow? _miniWindow;

    [RelayCommand]
    private void ToggleMiniMode()
    {
        if (_miniWindow is { IsVisible: true })
        {
            _miniWindow.Close();
            _miniWindow = null;
            if (System.Windows.Application.Current.MainWindow is { } main) main.Show();
            return;
        }

        _miniWindow = new MiniModeWindow(this);
        _miniWindow.Closed += (_, _) => _miniWindow = null;
        _miniWindow.Show();
        if (System.Windows.Application.Current.MainWindow is { } m) m.Hide();
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

    public void ImportAgentFolder(string folderPath)
    {
        if (SelectedMachine is not null)
            _machineCoordinator.RegisterAgentFromFolder(SelectedMachine, folderPath);
    }

    [RelayCommand]
    private void ImportMachine()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description        = "Select machine folder (must contain appSettings.config)",
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        var machine = MachineImporter.Import(dialog.SelectedPath, _machineCoordinator);
        SelectedMachine = machine;
        _machineCoordinator.SaveMachinesToDisk();
    }

    [RelayCommand]
    private void Clear() => Log.Clear();

    [RelayCommand]
    private void AddGroup()
    {
        // TODO: insert a labeled soundboard section divider.
    }
}
