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
        Items = new ObservableCollection<SoundboardItem>();

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
    public ObservableCollection<SoundboardItem> Items { get; }

    public ObservableCollection<AgentViewModel> Agents
        => SelectedMachine?.Agents ?? _emptyAgents;

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
        if (Items.Any(i => string.Equals(i.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase)))
            return;
        Items.Add(new SoundboardItem
        {
            Label    = Path.GetFileNameWithoutExtension(entry.FileName),
            FilePath = entry.FilePath,
        });
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
        // TODO: insert a labeled soundboard section divider.
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
