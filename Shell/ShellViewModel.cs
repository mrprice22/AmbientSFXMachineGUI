using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;
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
    private static readonly ObservableCollection<SoundboardItem> _emptySoundboard = new();

    public ShellViewModel(MachineCoordinator machineCoordinator, ProfileService profileService, HotkeyService hotkeys, LibraryHasher libraryHasher, AudioLibrary audioLibrary, LibraryDuplicates libraryDuplicates)
    {
        _machineCoordinator = machineCoordinator;
        _profileService = profileService;
        _hotkeys = hotkeys;
        LibraryHasher = libraryHasher;
        AudioLibrary = audioLibrary;
        LibraryDuplicates = libraryDuplicates;

        Machines = _machineCoordinator.Machines;
        Log = _machineCoordinator.Log;
        Profiles = _profileService.Profiles;
        ActivePlaybacks = new ObservableCollection<ActivePlayback>();

        _machineCoordinator.PlaybackStarted += (_, p) => { if (!ActivePlaybacks.Contains(p)) ActivePlaybacks.Add(p); };
        _machineCoordinator.PlaybackEnded   += (_, p) =>
        {
            ActivePlaybacks.Remove(p);
            if (ReferenceEquals(_soloedPlayback, p)) ClearSolo();
        };

        LibraryView = CollectionViewSource.GetDefaultView(AudioLibrary.Entries);
        LibraryView.Filter = MatchesLibraryFilter;
        LibraryView.SortDescriptions.Add(new SortDescription(nameof(AudioFileEntry.FileName), ListSortDirection.Ascending));

        _unusedView = new CollectionViewSource { Source = AudioLibrary.Entries }.View;
        _unusedView.Filter = o => o is AudioFileEntry e && e.Usages.Count == 0;
        _unusedView.SortDescriptions.Add(new SortDescription(nameof(AudioFileEntry.FileName), ListSortDirection.Ascending));
        ((INotifyCollectionChanged)AudioLibrary.Entries).CollectionChanged += OnLibraryEntriesChanged;
        foreach (var entry in AudioLibrary.Entries) HookEntry(entry);

        if (Machines.Count > 0)
            SelectedMachine = Machines[0];
        Machines.CollectionChanged += (_, _) =>
        {
            if (SelectedMachine is null && Machines.Count > 0)
                SelectedMachine = Machines[0];
        };
    }

    private bool _suppressProfileApply;

    public LibraryHasher LibraryHasher { get; }
    public AudioLibrary AudioLibrary { get; }
    public LibraryDuplicates LibraryDuplicates { get; }
    public ICollectionView LibraryView { get; }
    private readonly ICollectionView _unusedView;
    public ICollectionView UnusedView => _unusedView;
    public ObservableCollection<MachineViewModel> Machines { get; }
    public ObservableCollection<LogEntryViewModel> Log { get; }
    public ObservableCollection<Profile> Profiles { get; }
    public ObservableCollection<ActivePlayback> ActivePlaybacks { get; }

    public ObservableCollection<AgentViewModel> Agents
        => SelectedMachine?.Agents ?? _emptyAgents;

    public ObservableCollection<SoundboardItem> SoundboardItems
        => SelectedMachine?.SoundboardItems ?? _emptySoundboard;

    [ObservableProperty] private MachineViewModel? _selectedMachine;
    [ObservableProperty] private double _masterVolume = 100;
    [ObservableProperty] private bool _isMutedAll;
    [ObservableProperty] private Profile? _activeProfile;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _libraryFilter = string.Empty;
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private AudioFileEntry? _selectedLibraryEntry;

    public ObservableCollection<LibraryUsageItem> SelectedEntryUsages { get; } = new();

    public event EventHandler<Guid>? AgentFocusRequested;

    private AudioFileEntry? _trackedUsageEntry;

    partial void OnSelectedLibraryEntryChanged(AudioFileEntry? value)
    {
        if (_trackedUsageEntry is not null) _trackedUsageEntry.UsagesChanged -= OnSelectedEntryUsagesChanged;
        _trackedUsageEntry = value;
        if (value is not null) value.UsagesChanged += OnSelectedEntryUsagesChanged;
        RebuildSelectedEntryUsages();
    }

    private void OnSelectedEntryUsagesChanged(object? sender, EventArgs e) => RebuildSelectedEntryUsages();

    private void RebuildSelectedEntryUsages()
    {
        SelectedEntryUsages.Clear();
        var entry = SelectedLibraryEntry;
        if (entry is null) return;
        foreach (var usage in entry.Usages)
        {
            var machine = Machines.FirstOrDefault(m => m.Id == usage.MachineId);
            var agent = machine?.Agents.FirstOrDefault(a => a.Id == usage.AgentId);
            if (machine is null || agent is null) continue;
            SelectedEntryUsages.Add(new LibraryUsageItem(machine, agent, NavigateToUsage));
        }
    }

    private void NavigateToUsage(MachineViewModel machine, AgentViewModel agent)
    {
        SelectedMachine = machine;
        AgentFocusRequested?.Invoke(this, agent.Id);
    }

    [RelayCommand]
    private void SelectLibraryEntry(AudioFileEntry? entry) => SelectedLibraryEntry = entry;

    [RelayCommand]
    private void CloseUsagesDrawer() => SelectedLibraryEntry = null;

    partial void OnLibraryFilterChanged(string value) => LibraryView.Refresh();

    partial void OnSearchQueryChanged(string value) => ApplySearchFilter();

    private void ApplySearchFilter()
    {
        var query = SearchQuery?.Trim() ?? string.Empty;
        var hasQuery = query.Length > 0;
        foreach (var machine in Machines)
        {
            foreach (var agent in machine.Agents)
            {
                var anyMatch = false;
                foreach (var file in agent.Files)
                {
                    var match = !hasQuery || FuzzyMatch(file.FileName, query);
                    file.IsHiddenBySearch = !match;
                    if (match) anyMatch = true;
                }
                agent.IsHiddenBySearch = hasQuery && !anyMatch;
                if (hasQuery && anyMatch) agent.IsExpanded = true;
            }
        }
    }

    private static bool FuzzyMatch(string source, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        if (string.IsNullOrEmpty(source)) return false;
        if (source.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        int si = 0, qi = 0;
        while (si < source.Length && qi < query.Length)
        {
            if (char.ToLowerInvariant(source[si]) == char.ToLowerInvariant(query[qi])) qi++;
            si++;
        }
        return qi == query.Length;
    }

    private void OnLibraryEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (AudioFileEntry entry in e.OldItems) UnhookEntry(entry);
        if (e.NewItems != null)
            foreach (AudioFileEntry entry in e.NewItems) HookEntry(entry);
    }

    private void HookEntry(AudioFileEntry entry) => entry.UsagesChanged += OnEntryUsagesChanged;
    private void UnhookEntry(AudioFileEntry entry) => entry.UsagesChanged -= OnEntryUsagesChanged;
    private void OnEntryUsagesChanged(object? sender, EventArgs e) => _unusedView.Refresh();

    [RelayCommand]
    private void AddLibraryEntryToAgents(AudioFileEntry? entry)
    {
        if (entry is null) return;
        var dialog = new AddToAgentsDialog(entry, Machines)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true) return;

        foreach (var pick in dialog.SelectedAgents)
        {
            var agent = pick.Agent;
            if (agent.Files.Any(f => string.Equals(f.FilePath, entry.AbsolutePath, StringComparison.OrdinalIgnoreCase)))
                continue;
            agent.Files.Add(new SoundFileViewModel(entry.AbsolutePath));
            agent.FileCount = agent.Files.Count;
        }
    }

    [RelayCommand]
    private void MergeDuplicates(AudioFileEntry? canonical)
    {
        if (canonical is null) return;
        var group = LibraryDuplicates.ExactGroups.FirstOrDefault(g => g.Entries.Contains(canonical))
                 ?? LibraryDuplicates.LikelyGroups.FirstOrDefault(g => g.Entries.Contains(canonical));
        if (group is null) return;

        var others = group.Entries.Where(e => e != canonical).ToList();
        if (others.Count == 0) return;
        var affected = others.Sum(e => e.Usages.Count);

        var result = System.Windows.MessageBox.Show(
            $"Merge {others.Count} duplicate entr{(others.Count == 1 ? "y" : "ies")} " +
            $"({affected} agent reference{(affected == 1 ? "" : "s")}) into:\n\n" +
            $"{canonical.AbsolutePath}\n\n" +
            "Every agent that references one of the others will be updated to reference the canonical file. " +
            "Files on disk are not touched.",
            "Merge Duplicates",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.OK) return;

        foreach (var other in others)
        {
            foreach (var usage in other.Usages.ToList())
            {
                var machine = Machines.FirstOrDefault(m => m.Id == usage.MachineId);
                var agent = machine?.Agents.FirstOrDefault(a => a.Id == usage.AgentId);
                if (agent is null) continue;

                bool canonicalAlreadyPresent = agent.Files.Any(f =>
                    string.Equals(f.FilePath, canonical.AbsolutePath, StringComparison.OrdinalIgnoreCase));

                for (int i = agent.Files.Count - 1; i >= 0; i--)
                {
                    if (!string.Equals(agent.Files[i].FilePath, other.AbsolutePath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (canonicalAlreadyPresent)
                    {
                        agent.Files.RemoveAt(i);
                    }
                    else
                    {
                        var old = agent.Files[i];
                        agent.Files[i] = new SoundFileViewModel(canonical.AbsolutePath)
                        {
                            IsEnabled = old.IsEnabled,
                            VolumeOverride = old.VolumeOverride,
                            CooldownOverrideSeconds = old.CooldownOverrideSeconds,
                            IsFavorite = old.IsFavorite,
                        };
                        canonicalAlreadyPresent = true;
                    }
                }
                agent.FileCount = agent.Files.Count;
            }
        }
    }

    [RelayCommand]
    private void RevealInExplorer(AudioFileEntry? entry)
    {
        if (entry is null) return;
        var path = entry.AbsolutePath;
        try
        {
            if (File.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            else
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
            }
        }
        catch { }
    }

    [RelayCommand]
    private void CopyLibraryEntryPath(AudioFileEntry? entry)
    {
        if (entry is null) return;
        try { System.Windows.Clipboard.SetText(entry.AbsolutePath); }
        catch { }
    }

    [RelayCommand]
    private void RemoveUnused(IList? selected)
    {
        if (selected is null || selected.Count == 0) return;
        var targets = selected.OfType<AudioFileEntry>().ToList();
        foreach (var entry in targets) AudioLibrary.RemoveEntry(entry);
    }

    private bool MatchesLibraryFilter(object obj)
    {
        if (string.IsNullOrWhiteSpace(LibraryFilter)) return true;
        if (obj is not AudioFileEntry entry) return false;
        var q = LibraryFilter.Trim();
        return entry.FileName.Contains(q, StringComparison.OrdinalIgnoreCase)
            || entry.AbsolutePath.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSelectedMachineChanged(MachineViewModel? oldValue, MachineViewModel? newValue)
    {
        OnPropertyChanged(nameof(Agents));
        OnPropertyChanged(nameof(SoundboardItems));
        _hotkeys.SetActiveMachine(newValue?.Id);
        RebindActiveSoundboardHotkeys(oldValue, newValue);

        _suppressProfileApply = true;
        try
        {
            if (newValue is not null) _profileService.LoadAll(newValue.Id);
            else _profileService.Profiles.Clear();
            ActiveProfile = null;
        }
        finally { _suppressProfileApply = false; }
    }

    private readonly HashSet<SoundboardItem> _hookedSoundboardItems = new();

    private void RebindActiveSoundboardHotkeys(MachineViewModel? oldMachine, MachineViewModel? newMachine)
    {
        foreach (var item in _hookedSoundboardItems)
        {
            item.PropertyChanged -= OnSoundboardItemPropertyChanged;
            _hotkeys.UnregisterDynamic(SoundboardHotkeyKey(item));
        }
        _hookedSoundboardItems.Clear();

        if (oldMachine is not null) oldMachine.SoundboardItems.CollectionChanged -= OnActiveSoundboardCollectionChanged;
        if (newMachine is null) return;
        newMachine.SoundboardItems.CollectionChanged += OnActiveSoundboardCollectionChanged;
        foreach (var item in newMachine.SoundboardItems) HookSoundboardItem(item);
    }

    private void OnActiveSoundboardCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (SoundboardItem item in e.OldItems) UnhookSoundboardItem(item);
        if (e.NewItems != null)
            foreach (SoundboardItem item in e.NewItems) HookSoundboardItem(item);
    }

    private void HookSoundboardItem(SoundboardItem item)
    {
        if (!_hookedSoundboardItems.Add(item)) return;
        item.PropertyChanged += OnSoundboardItemPropertyChanged;
        RegisterSoundboardHotkey(item);
    }

    private void UnhookSoundboardItem(SoundboardItem item)
    {
        if (!_hookedSoundboardItems.Remove(item)) return;
        item.PropertyChanged -= OnSoundboardItemPropertyChanged;
        _hotkeys.UnregisterDynamic(SoundboardHotkeyKey(item));
    }

    private void OnSoundboardItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SoundboardItem.Hotkey)) return;
        if (sender is SoundboardItem item) RegisterSoundboardHotkey(item);
    }

    private void RegisterSoundboardHotkey(SoundboardItem item)
    {
        var key = SoundboardHotkeyKey(item);
        _hotkeys.UnregisterDynamic(key);
        if (item.IsDivider || string.IsNullOrWhiteSpace(item.Hotkey)) return;
        _hotkeys.RegisterDynamic(key, item.Hotkey, () =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (PlaySoundboardItemCommand.CanExecute(item))
                    PlaySoundboardItemCommand.Execute(item);
            });
        });
    }

    private static string SoundboardHotkeyKey(SoundboardItem item) => "soundboard." + item.GetHashCode();

    [RelayCommand]
    private void AssignSoundboardHotkey(SoundboardItem? item)
    {
        if (item is null || item.IsDivider) return;
        var capture = new HotkeyCaptureWindow { Owner = System.Windows.Application.Current.MainWindow };
        if (capture.ShowDialog() != true || string.IsNullOrWhiteSpace(capture.CapturedCombo)) return;
        item.Hotkey = capture.CapturedCombo;
    }

    [RelayCommand]
    private void ClearSoundboardHotkey(SoundboardItem? item)
    {
        if (item is null) return;
        item.Hotkey = null;
    }

    partial void OnMasterVolumeChanged(double value) => _machineCoordinator.SetMasterVolume(value);
    partial void OnIsMutedAllChanged(bool value) => _machineCoordinator.SetMuteAll(value);
    partial void OnActiveProfileChanged(Profile? value)
    {
        if (_suppressProfileApply) return;
        if (value is null || SelectedMachine is null) return;
        _profileService.Apply(SelectedMachine, _hotkeys, value);
    }

    [RelayCommand]
    private void CycleNextProfile()
    {
        if (SelectedMachine is null) return;
        if (_profileService.Profiles.Count == 0) return;
        var list = _profileService.Profiles;
        var idx = ActiveProfile is null ? -1 : list.IndexOf(ActiveProfile);
        var next = list[(idx + 1) % list.Count];
        ActiveProfile = next;
    }

    [RelayCommand]
    private void QuickSaveProfile()
    {
        if (SelectedMachine is null) return;
        var suggested = ActiveProfile?.Name ?? "Profile";
        var dialog = new InputDialog("Save Profile", "Name:", suggested)
            { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;

        var profile = _profileService.SnapshotCurrent(SelectedMachine, _hotkeys, dialog.Value.Trim());
        _suppressProfileApply = true;
        try { ActiveProfile = profile; }
        finally { _suppressProfileApply = false; }
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
        var suggested = $"playback-log-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
        using var sfd = new SaveFileDialog
        {
            Title      = "Export Playback Log",
            Filter     = "CSV (Comma delimited)|*.csv|All files|*.*",
            FileName   = suggested,
            DefaultExt = ".csv",
            AddExtension = true,
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        var snapshot = Log.ToList();
        using var writer = new StreamWriter(sfd.FileName, false, new System.Text.UTF8Encoding(true));
        writer.WriteLine("Timestamp,Agent,FileName,FilePath");
        foreach (var entry in snapshot)
        {
            writer.Write(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            writer.Write(',');
            writer.Write(CsvEscape(entry.AgentName));
            writer.Write(',');
            writer.Write(CsvEscape(entry.FileName));
            writer.Write(',');
            writer.Write(CsvEscape(entry.FilePath));
            writer.WriteLine();
        }
    }

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        bool needsQuotes = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        if (!needsQuotes) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public void ImportAgentFolder(string folderPath)
    {
        if (SelectedMachine is not null)
            _machineCoordinator.RegisterAgentFromFolder(SelectedMachine, folderPath);
    }

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".aiff", ".aif", ".opus"
    };

    public static bool IsAudioFile(string path)
        => !string.IsNullOrEmpty(path)
           && File.Exists(path)
           && AudioExtensions.Contains(Path.GetExtension(path));

    /// <summary>Registers dropped audio files as library entries with no agent attachment (LIB-08).</summary>
    public int RegisterLibraryFiles(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (var path in paths)
        {
            if (!IsAudioFile(path)) continue;
            AudioLibrary.RegisterPathOnly(path);
            added++;
        }
        return added;
    }

    /// <summary>Adds dropped audio files to an agent; library usage is registered automatically via AudioLibrary's Files hook.</summary>
    public int AddFilesToAgent(AgentViewModel agent, IEnumerable<string> paths)
    {
        int added = 0;
        foreach (var path in paths)
        {
            if (!IsAudioFile(path)) continue;
            if (agent.Files.Any(f => string.Equals(f.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            agent.Files.Add(new SoundFileViewModel(path));
            added++;
        }
        if (added > 0) agent.FileCount = agent.Files.Count;
        return added;
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
    private void AddLogEntryToSoundboard(LogEntryViewModel? entry)
    {
        if (entry is null || string.IsNullOrEmpty(entry.FilePath)) return;
        if (SelectedMachine is null) return;
        var items = SelectedMachine.SoundboardItems;
        if (items.Any(i => string.Equals(i.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase)))
            return;
        items.Add(new SoundboardItem
        {
            Label    = Path.GetFileNameWithoutExtension(entry.FileName),
            FilePath = entry.FilePath,
        });
    }

    [RelayCommand]
    private void AddFileToSoundboard(SoundFileViewModel? file)
    {
        if (file is null || string.IsNullOrEmpty(file.FilePath)) return;
        if (SelectedMachine is null) return;
        var items = SelectedMachine.SoundboardItems;
        if (items.Any(i => string.Equals(i.FilePath, file.FilePath, StringComparison.OrdinalIgnoreCase)))
        {
            file.IsFavorite = true;
            return;
        }
        items.Add(new SoundboardItem
        {
            Label    = Path.GetFileNameWithoutExtension(file.FileName),
            FilePath = file.FilePath,
        });
        file.IsFavorite = true;
    }

    [RelayCommand]
    private void PlaySoundboardItem(SoundboardItem? item)
    {
        if (item is null || item.IsDivider || string.IsNullOrEmpty(item.FilePath)) return;
        if (IsEditMode) return;
        if (!File.Exists(item.FilePath)) return;
        try
        {
            var reader = new NAudio.Wave.AudioFileReader(item.FilePath)
            {
                Volume = IsMutedAll
                    ? 0f
                    : (float)(Math.Clamp(item.Volume, 0, 200) / 100.0 * (MasterVolume / 100.0)),
            };
            var output = new NAudio.Wave.WaveOutEvent();
            output.Init(reader);
            output.PlaybackStopped += (_, _) =>
            {
                try { output.Dispose(); } catch { }
                try { reader.Dispose(); } catch { }
            };
            output.Play();
        }
        catch { }
    }

    [RelayCommand]
    private void DisableLogEntryFile(LogEntryViewModel? entry)
    {
        if (entry is null || string.IsNullOrEmpty(entry.FilePath)) return;
        int count = 0;
        foreach (var file in EnumerateMatchingFiles(entry.FilePath))
        {
            if (file.IsEnabled) { file.IsEnabled = false; count++; }
        }
        if (count == 0)
        {
            System.Windows.MessageBox.Show(
                $"No agent currently references:\n\n{entry.FilePath}",
                "Disable File",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void SetLogEntryVolumeOverride(LogEntryViewModel? entry)
    {
        if (entry is null || string.IsNullOrEmpty(entry.FilePath)) return;
        var matches = EnumerateMatchingFiles(entry.FilePath).ToList();
        if (matches.Count == 0)
        {
            System.Windows.MessageBox.Show(
                $"No agent currently references:\n\n{entry.FilePath}",
                "Set Volume Override",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }
        var current = matches[0].VolumeOverride;
        var dialog = new InputDialog("Set Volume Override",
            $"Volume for {entry.FileName} (0-200%):",
            current.ToString("0"))
        { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;
        if (!double.TryParse(dialog.Value, out var value)) return;
        value = Math.Clamp(value, 0, 200);
        foreach (var file in matches) file.VolumeOverride = value;
    }

    [RelayCommand]
    private void OpenLogEntryFolder(LogEntryViewModel? entry)
    {
        if (entry is null || string.IsNullOrEmpty(entry.FilePath)) return;
        var path = entry.FilePath;
        try
        {
            if (File.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            else
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
            }
        }
        catch { }
    }

    private IEnumerable<SoundFileViewModel> EnumerateMatchingFiles(string filePath)
    {
        foreach (var machine in Machines)
            foreach (var agent in machine.Agents)
                foreach (var file in agent.Files)
                    if (string.Equals(file.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                        yield return file;
    }

    [RelayCommand]
    private void AddGroup()
    {
        if (SelectedMachine is null) return;
        var dialog = new InputDialog("Add Group", "Label:", "Group")
            { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;
        SelectedMachine.SoundboardItems.Add(new SoundboardItem
        {
            Label      = dialog.Value.Trim(),
            IsDivider  = true,
        });
    }

    [RelayCommand]
    private void RenameSoundboardItem(SoundboardItem? item)
    {
        if (item is null) return;
        var dialog = new InputDialog(item.IsDivider ? "Rename Group" : "Rename Button", "Label:", item.Label)
            { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;
        item.Label = dialog.Value.Trim();
    }

    [RelayCommand]
    private void DeleteSoundboardItem(SoundboardItem? item)
    {
        if (item is null || SelectedMachine is null) return;
        var items = SelectedMachine.SoundboardItems;
        var index = items.IndexOf(item);
        if (index < 0) return;
        items.RemoveAt(index);
        if (!item.IsDivider)
        {
            foreach (var file in EnumerateMatchingFiles(item.FilePath))
                file.IsFavorite = false;
        }
    }

    public void MoveSoundboardItem(SoundboardItem source, SoundboardItem? target)
    {
        if (SelectedMachine is null || ReferenceEquals(source, target)) return;
        var items = SelectedMachine.SoundboardItems;
        var from = items.IndexOf(source);
        if (from < 0) return;
        int to = target is null ? items.Count - 1 : items.IndexOf(target);
        if (to < 0) return;
        if (from == to) return;
        items.Move(from, to);
    }

    private ActivePlayback? _soloedPlayback;
    private readonly Dictionary<AgentViewModel, bool> _preSoloAgentState = new();

    [RelayCommand]
    private void ToggleSolo(ActivePlayback? playback)
    {
        if (playback is null) return;
        if (ReferenceEquals(_soloedPlayback, playback)) { ClearSolo(); return; }
        if (_soloedPlayback is not null) ClearSolo();

        _preSoloAgentState.Clear();
        foreach (var machine in Machines)
            foreach (var agent in machine.Agents)
            {
                _preSoloAgentState[agent] = agent.IsEnabled;
                if (!ReferenceEquals(agent, playback.Agent) && agent.IsEnabled)
                    agent.IsEnabled = false;
            }

        foreach (var other in ActivePlaybacks)
        {
            if (ReferenceEquals(other, playback) || other.Reader is null) continue;
            try { other.Reader.Volume = 0f; } catch { }
        }

        playback.IsSoloed = true;
        _soloedPlayback = playback;
    }

    private void ClearSolo()
    {
        if (_soloedPlayback is null) return;
        foreach (var (agent, wasEnabled) in _preSoloAgentState)
        {
            if (agent.IsEnabled != wasEnabled) agent.IsEnabled = wasEnabled;
        }
        _preSoloAgentState.Clear();

        foreach (var other in ActivePlaybacks)
        {
            if (ReferenceEquals(other, _soloedPlayback) || other.Reader is null) continue;
            try { other.Reader.Volume = (float)(Math.Clamp(other.Volume, 0, 200) / 100.0); } catch { }
        }

        _soloedPlayback.IsSoloed = false;
        _soloedPlayback = null;
    }
}
