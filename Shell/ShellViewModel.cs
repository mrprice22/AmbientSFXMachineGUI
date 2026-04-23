using System.Collections.ObjectModel;
using System.IO;
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

    public ShellViewModel(MachineCoordinator machineCoordinator, ProfileService profileService, HotkeyService hotkeys, LibraryHasher libraryHasher)
    {
        _machineCoordinator = machineCoordinator;
        _profileService = profileService;
        _hotkeys = hotkeys;
        LibraryHasher = libraryHasher;

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

    public LibraryHasher LibraryHasher { get; }
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
    {
        OnPropertyChanged(nameof(Agents));
        _hotkeys.SetActiveMachine(value?.Id);
    }

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
    private void CreateMachine()
    {
        var dialog = new CreateMachineDialog { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;

        Directory.CreateDirectory(Path.Combine(dialog.RootPath, "snd"));

        var machine = _machineCoordinator.CreateMachine(dialog.MachineName, dialog.RootPath);
        machine.IconPath = RelativeIfPossible(dialog.IconPath, dialog.RootPath);
        SelectedMachine  = machine;
        _machineCoordinator.SaveMachinesToDisk();
    }

    [RelayCommand]
    private void RenameMachine(MachineViewModel machine)
    {
        var dialog = new InputDialog("Rename Machine", "Name:", machine.Name)
            { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;
        machine.Name = dialog.Value.Trim();
        _machineCoordinator.SaveMachinesToDisk();
    }

    [RelayCommand]
    private void ChangeIcon(MachineViewModel machine)
    {
        using var ofd = new OpenFileDialog { Title = "Select icon", Filter = "Images|*.ico;*.png|All files|*.*" };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        machine.IconPath = RelativeIfPossible(ofd.FileName, machine.RootPath);
        _machineCoordinator.SaveMachinesToDisk();
    }

    [RelayCommand]
    private void DeleteMachine(MachineViewModel machine)
    {
        var result = System.Windows.MessageBox.Show(
            $"Remove '{machine.Name}' from the app?\n\n" +
            "This only unlinks the machine. Audio files and .config files on disk are NOT touched.",
            "Remove Machine",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.Yes) return;
        _machineCoordinator.RemoveMachine(machine);
        _machineCoordinator.SaveMachinesToDisk();
    }

    public void MoveMachine(int fromIndex, int toIndex)
    {
        _machineCoordinator.Machines.Move(fromIndex, toIndex);
        _machineCoordinator.SaveMachinesToDisk();
    }

    private static string RelativeIfPossible(string path, string rootPath)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(rootPath)) return path;
        try
        {
            if (path.StartsWith(rootPath, System.StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(rootPath, path);
        }
        catch { }
        return path;
    }

    [RelayCommand]
    private void Clear() => Log.Clear();

    [RelayCommand]
    private void AddGroup()
    {
        // TODO: insert a labeled soundboard section divider.
    }
}
