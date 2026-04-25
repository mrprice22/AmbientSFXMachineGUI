using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    private MachineViewModel? _agentsSourceMachine;
    private MachineViewModel? _soundboardSourceMachine;

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

        SelectedMachine = PickInitialMachine();
        Machines.CollectionChanged += (_, _) =>
        {
            if (SelectedMachine is null && Machines.Count > 0)
                SelectedMachine = PickInitialMachine();
            if (IsAgentsGrouped) RebuildAgentGroups();
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

    // Stable mirror collections kept in sync with SelectedMachine.Agents / SoundboardItems
    // via RebindAgentsMirror / RebindSoundboardMirror. The view binds to these once;
    // switching machines updates contents instead of swapping the collection reference,
    // which avoids losing the binding if SelectedMachine is ever briefly null during startup.
    public ObservableCollection<AgentViewModel> Agents { get; } = new();
    public ObservableCollection<SoundboardItem> SoundboardItems { get; } = new();

    // MACHINE-11: groups for the fallback "all machines" agents view shown when the machines pane is hidden.
    public ObservableCollection<AgentGroupViewModel> AgentGroups { get; } = new();

    [ObservableProperty] private MachineViewModel? _selectedMachine;
    [ObservableProperty] private double _masterVolume = 100;
    [ObservableProperty] private bool _isMutedAll;
    [ObservableProperty] private Profile? _activeProfile;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _libraryFilter = string.Empty;
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private AudioFileEntry? _selectedLibraryEntry;

    // MACHINE-11: when true, the agents panel shows all machines' agents grouped under collapsible
    // headers; when false, it shows a flat list scoped to SelectedMachine. Driven by the machines
    // pane visibility from MainWindow.
    [ObservableProperty] private bool _isAgentsGrouped;

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
        RebindAgentsMirror(newValue);
        RebindSoundboardMirror(newValue);
        App.DebugLog.LogUser("Shell", $"Selected machine → '{newValue?.Name ?? "none"}'");
        App.Settings.lastSelectedMachineId = newValue?.Id.ToString();
        App.Settings.Save();
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

    /// <summary>
    /// Chooses the machine to select at startup. Prefers the one persisted in AppSettings;
    /// otherwise picks the first machine that actually has agents (avoids landing on an empty
    /// auto-created "Default" machine when other populated machines exist); falls back to Machines[0].
    /// </summary>
    private MachineViewModel? PickInitialMachine()
    {
        if (Machines.Count == 0) return null;
        var persistedId = App.Settings.lastSelectedMachineId;
        if (!string.IsNullOrWhiteSpace(persistedId) && Guid.TryParse(persistedId, out var id))
        {
            var match = Machines.FirstOrDefault(m => m.Id == id);
            if (match is not null) return match;
        }
        return Machines.FirstOrDefault(m => m.Agents.Count > 0) ?? Machines[0];
    }

    // MACHINE-11
    partial void OnIsAgentsGroupedChanged(bool value)
    {
        if (value) RebuildAgentGroups();
        else ClearAgentGroups();
        App.DebugLog.LogUser("Shell", $"Agents view → {(value ? "grouped (machines pane hidden)" : "flat")}");
    }

    private void RebuildAgentGroups()
    {
        ClearAgentGroups();
        var collapsed = App.Settings.agentGroupCollapsed ??= new Dictionary<string, bool>();
        foreach (var machine in Machines)
        {
            var key = machine.Id.ToString();
            collapsed.TryGetValue(key, out var isCollapsed);
            var group = new AgentGroupViewModel(machine, isCollapsed);
            group.CollapseStateChanged += OnAgentGroupCollapseChanged;
            AgentGroups.Add(group);
        }
    }

    private void ClearAgentGroups()
    {
        foreach (var g in AgentGroups) g.CollapseStateChanged -= OnAgentGroupCollapseChanged;
        AgentGroups.Clear();
    }

    private void OnAgentGroupCollapseChanged(object? sender, EventArgs e)
    {
        if (sender is not AgentGroupViewModel group) return;
        var map = App.Settings.agentGroupCollapsed ??= new Dictionary<string, bool>();
        map[group.Machine.Id.ToString()] = group.IsCollapsed;
        App.Settings.Save();
    }

    private void RebindAgentsMirror(MachineViewModel? newMachine)
    {
        if (_agentsSourceMachine is not null)
            ((INotifyCollectionChanged)_agentsSourceMachine.Agents).CollectionChanged -= OnSourceAgentsChanged;
        _agentsSourceMachine = newMachine;
        Agents.Clear();
        if (newMachine is not null)
        {
            foreach (var a in newMachine.Agents) Agents.Add(a);
            ((INotifyCollectionChanged)newMachine.Agents).CollectionChanged += OnSourceAgentsChanged;
        }
    }

    private void OnSourceAgentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is not null)
                    foreach (AgentViewModel a in e.NewItems) Agents.Add(a);
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is not null)
                    foreach (AgentViewModel a in e.OldItems) Agents.Remove(a);
                break;
            default:
                Agents.Clear();
                if (_agentsSourceMachine is not null)
                    foreach (var a in _agentsSourceMachine.Agents) Agents.Add(a);
                break;
        }
    }

    private void RebindSoundboardMirror(MachineViewModel? newMachine)
    {
        if (_soundboardSourceMachine is not null)
            ((INotifyCollectionChanged)_soundboardSourceMachine.SoundboardItems).CollectionChanged -= OnSourceSoundboardChanged;
        _soundboardSourceMachine = newMachine;
        SoundboardItems.Clear();
        if (newMachine is not null)
        {
            foreach (var s in newMachine.SoundboardItems) SoundboardItems.Add(s);
            ((INotifyCollectionChanged)newMachine.SoundboardItems).CollectionChanged += OnSourceSoundboardChanged;
        }
    }

    private void OnSourceSoundboardChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is not null)
                    foreach (SoundboardItem s in e.NewItems) SoundboardItems.Add(s);
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is not null)
                    foreach (SoundboardItem s in e.OldItems) SoundboardItems.Remove(s);
                break;
            default:
                SoundboardItems.Clear();
                if (_soundboardSourceMachine is not null)
                    foreach (var s in _soundboardSourceMachine.SoundboardItems) SoundboardItems.Add(s);
                break;
        }
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

    partial void OnMasterVolumeChanged(double value)
    {
        _machineCoordinator.SetMasterVolume(value);
        App.DebugLog.LogUser("Shell", $"Master volume → {value:0}");
    }
    partial void OnIsMutedAllChanged(bool value)
    {
        _machineCoordinator.SetMuteAll(value);
        App.DebugLog.LogUser("Shell", value ? "Mute-all ON" : "Mute-all OFF");
    }
    partial void OnActiveProfileChanged(Profile? oldValue, Profile? newValue)
    {
        if (_suppressProfileApply) return;
        if (newValue is null || SelectedMachine is null) return;
        App.DebugLog.LogUser("Profile", $"Switching profile '{oldValue?.Name ?? "none"}' → '{newValue.Name}' (machine: {SelectedMachine.Name})");

        var diff = _profileService.Diff(SelectedMachine, newValue);
        if (diff.HasChanges)
        {
            var dialog = new ProfileDiffDialog(newValue.Name, diff)
            {
                Owner = System.Windows.Application.Current?.MainWindow,
            };
            if (dialog.ShowDialog() != true)
            {
                _suppressProfileApply = true;
                try { ActiveProfile = oldValue; }
                finally { _suppressProfileApply = false; }
                return;
            }
        }

        _profileService.Apply(SelectedMachine, _hotkeys, newValue);
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

    [RelayCommand]
    private void AuditionProfile()
    {
        if (SelectedMachine is null) return;
        if (_profileService.Profiles.Count == 0) return;
        var window = new ProfileAuditionWindow(
            _profileService,
            _hotkeys,
            SelectedMachine,
            _profileService.Profiles,
            ActiveProfile ?? _profileService.Profiles[0])
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        window.ShowDialog();
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

    /// <summary>
    /// AGENT-13: move <paramref name="source"/> to the position of <paramref name="target"/> within
    /// the same machine. Reorder is purely presentational — the AgentCoordinator runtime is keyed by
    /// AgentViewModel reference, so scheduling/cooldowns/library refs are unaffected.
    /// </summary>
    public void MoveAgent(AgentViewModel source, AgentViewModel target)
    {
        if (ReferenceEquals(source, target)) return;
        var machine = Machines.FirstOrDefault(m => m.Agents.Contains(source) && m.Agents.Contains(target));
        if (machine is null) return;
        var from = machine.Agents.IndexOf(source);
        var to   = machine.Agents.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        machine.Agents.Move(from, to);
        _machineCoordinator.SaveMachinesToDisk();
        App.DebugLog.LogUser("Agent",
            $"Reordered '{source.Name}' to position {to} in machine '{machine.Name}'");
    }

    [RelayCommand]
    private void MoveAgentUp(AgentViewModel? agent)
    {
        if (agent is null) return;
        var machine = Machines.FirstOrDefault(m => m.Agents.Contains(agent));
        if (machine is null) return;
        var idx = machine.Agents.IndexOf(agent);
        if (idx <= 0) return;
        machine.Agents.Move(idx, idx - 1);
        _machineCoordinator.SaveMachinesToDisk();
        App.DebugLog.LogUser("Agent",
            $"Moved '{agent.Name}' up to position {idx - 1} in machine '{machine.Name}'");
    }

    [RelayCommand]
    private void MoveAgentDown(AgentViewModel? agent)
    {
        if (agent is null) return;
        var machine = Machines.FirstOrDefault(m => m.Agents.Contains(agent));
        if (machine is null) return;
        var idx = machine.Agents.IndexOf(agent);
        if (idx < 0 || idx >= machine.Agents.Count - 1) return;
        machine.Agents.Move(idx, idx + 1);
        _machineCoordinator.SaveMachinesToDisk();
        App.DebugLog.LogUser("Agent",
            $"Moved '{agent.Name}' down to position {idx + 1} in machine '{machine.Name}'");
    }

    /// <summary>
    /// AGENT-14: create a new agent inside <paramref name="machine"/> (or SelectedMachine if null).
    /// Prompts for a folder name, creates &lt;machineRoot&gt;\snd\&lt;name&gt;\, and lets
    /// AgentCoordinator.RegisterAgentFromFolder write the default &lt;name&gt;.config so the new
    /// AgentViewModel uses the same defaults MachineImporter would have written.
    /// </summary>
    [RelayCommand]
    private void CreateAgent(MachineViewModel? machine)
    {
        machine ??= SelectedMachine;
        if (machine is null)
        {
            System.Windows.MessageBox.Show("Select a machine first.",
                "New Agent", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(machine.RootPath) || !Directory.Exists(machine.RootPath))
        {
            System.Windows.MessageBox.Show(
                $"Machine '{machine.Name}' has no on-disk folder; cannot create an agent.",
                "New Agent", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var sndDir = Path.Combine(machine.RootPath, "snd");
        try { Directory.CreateDirectory(sndDir); }
        catch (Exception ex)
        {
            App.DebugLog.LogError("Agent",
                $"Could not create snd folder for machine '{machine.Name}': {ex.Message}");
            System.Windows.MessageBox.Show(
                $"Could not create '{sndDir}': {ex.Message}",
                "New Agent",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return;
        }

        var dialog = new InputDialog("New Agent", "Agent name:", "NewAgent")
            { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;

        var name = dialog.Value?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name)) return;

        if (!IsValidFolderName(name, out var reason))
        {
            System.Windows.MessageBox.Show($"Cannot create agent '{name}': {reason}.",
                "New Agent", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var folderPath = Path.Combine(sndDir, name);
        if (Directory.Exists(folderPath)
            || machine.Agents.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            System.Windows.MessageBox.Show(
                $"An agent named '{name}' already exists in '{sndDir}'.",
                "New Agent", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        try
        {
            Directory.CreateDirectory(folderPath);
            // Switch the panel to the target machine so the new card appears live.
            SelectedMachine = machine;
            // RegisterAgentFromFolder writes default <name>.config when none exists, then attaches the VM.
            _machineCoordinator.RegisterAgentFromFolder(machine, folderPath);
            _machineCoordinator.SaveMachinesToDisk();
            App.DebugLog.LogUser("Agent",
                $"Created new agent '{name}' in machine '{machine.Name}' at {folderPath}");
        }
        catch (Exception ex)
        {
            App.DebugLog.LogError("Agent",
                $"Could not create agent '{name}' in machine '{machine.Name}': {ex.Message}");
            // Best-effort cleanup if the folder was created but registration failed.
            try { if (Directory.Exists(folderPath) && !Directory.EnumerateFileSystemEntries(folderPath).Any()) Directory.Delete(folderPath); }
            catch { }
            System.Windows.MessageBox.Show(
                $"Could not create agent '{name}': {ex.Message}",
                "New Agent",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void RenameAgent(AgentViewModel? agent)
    {
        if (agent is null) return;
        var machine = Machines.FirstOrDefault(m => m.Agents.Contains(agent));
        if (machine is null) return;

        var oldName = agent.Name;
        var oldFolderPath = agent.FolderPath;
        var sndDir = Path.GetDirectoryName(oldFolderPath);
        if (string.IsNullOrEmpty(sndDir))
        {
            System.Windows.MessageBox.Show("Cannot rename: agent has no on-disk folder.",
                "Rename Agent", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var dialog = new InputDialog("Rename Agent", "New name:", oldName)
            { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;

        var newName = dialog.Value?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(newName) || string.Equals(newName, oldName, StringComparison.Ordinal))
            return;

        if (!IsValidFolderName(newName, out var reason))
        {
            System.Windows.MessageBox.Show($"Cannot rename to '{newName}': {reason}.",
                "Rename Agent", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var newFolderPath = Path.Combine(sndDir, newName);
        if (Directory.Exists(newFolderPath))
        {
            System.Windows.MessageBox.Show($"A folder named '{newName}' already exists in {sndDir}.",
                "Rename Agent", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var oldPrefix = AddTrailingSep(oldFolderPath);
        var newPrefix = AddTrailingSep(newFolderPath);
        var wasEnabled = agent.IsEnabled;
        bool folderMoved = false;

        try
        {
            // AC#2: stop in-flight playback for this agent before touching the folder.
            if (wasEnabled) agent.IsEnabled = false;

            Directory.Move(oldFolderPath, newFolderPath);
            folderMoved = true;

            // Rename the convention-based <oldName>.config inside the folder so future writes match the new name.
            var oldConfig = Path.Combine(newFolderPath, oldName + ".config");
            var newConfig = Path.Combine(newFolderPath, newName + ".config");
            if (File.Exists(oldConfig) && !File.Exists(newConfig))
            {
                try { File.Move(oldConfig, newConfig); }
                catch (Exception ex)
                {
                    App.DebugLog.LogError("Agent",
                        $"Could not rename '{oldName}.config' → '{newName}.config': {ex.Message}");
                }
            }

            // AC#3: rewrite every machine's agent-file SoundFileViewModels whose path lives under the old folder.
            // Replacing the SoundFileViewModel triggers AudioLibrary's CollectionChanged: old usages drop, new
            // ones register against the new path — so library mappings are kept in sync automatically.
            foreach (var m in Machines)
            {
                foreach (var a in m.Agents)
                {
                    for (int i = 0; i < a.Files.Count; i++)
                    {
                        var old = a.Files[i];
                        if (!StartsWithIgnoreCase(old.FilePath, oldPrefix)) continue;
                        var newPath = newPrefix + old.FilePath.Substring(oldPrefix.Length);
                        a.Files[i] = new SoundFileViewModel(newPath)
                        {
                            IsEnabled                = old.IsEnabled,
                            VolumeOverride           = old.VolumeOverride,
                            CooldownOverrideSeconds  = old.CooldownOverrideSeconds,
                            IsFavorite               = old.IsFavorite,
                            PlayCountThisSession     = old.PlayCountThisSession,
                        };
                    }
                    a.FileCount = a.Files.Count;
                }

                // Soundboard items in this machine (in-memory; profile JSONs are rewritten below).
                foreach (var sb in m.SoundboardItems)
                {
                    if (StartsWithIgnoreCase(sb.FilePath, oldPrefix))
                        sb.FilePath = newPrefix + sb.FilePath.Substring(oldPrefix.Length);
                }
            }

            // Drop now-orphaned library entries that pointed at files inside the old folder.
            foreach (var entry in AudioLibrary.Entries.ToList())
            {
                if (entry.Usages.Count == 0
                    && StartsWithIgnoreCase(entry.AbsolutePath, oldPrefix))
                    AudioLibrary.RemoveEntry(entry);
            }

            // Update the agent's identity AFTER file paths so PropertyChanged listeners observe the new name
            // alongside the new folder.
            agent.FolderPath = newFolderPath;
            agent.Name = newName;

            // Rewrite every machine's profile JSONs — paths are absolute so foreign machines may also reference
            // the renamed folder, and pinned/named-agent entries inside this machine's profiles must be updated.
            foreach (var m in Machines)
                RewriteProfilesForRename(m.Id, oldName, newName, oldPrefix, newPrefix);

            // Reload the active machine's profiles so UI bindings see the rewritten data; clear any active
            // selection because the in-memory Profile object the binding held is now stale on disk.
            if (SelectedMachine is { } sel)
            {
                _suppressProfileApply = true;
                try
                {
                    _profileService.LoadAll(sel.Id);
                    ActiveProfile = null;
                }
                finally { _suppressProfileApply = false; }
            }

            App.DebugLog.LogUser("Agent",
                $"Renamed agent '{oldName}' → '{newName}' (machine: {machine.Name})");
        }
        catch (Exception ex)
        {
            App.DebugLog.LogError("Agent",
                $"Rename failed for '{oldName}' → '{newName}': {ex.Message}");
            // Best-effort revert if Directory.Move succeeded but a later step threw.
            if (folderMoved && Directory.Exists(newFolderPath) && !Directory.Exists(oldFolderPath))
            {
                try { Directory.Move(newFolderPath, oldFolderPath); }
                catch { }
            }
            System.Windows.MessageBox.Show(
                $"Could not rename agent '{oldName}': {ex.Message}",
                "Rename Agent",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            if (wasEnabled) agent.IsEnabled = true;
        }
    }

    private static readonly string[] ReservedFolderNames =
    {
        "CON","PRN","AUX","NUL",
        "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
        "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
    };

    private static bool IsValidFolderName(string name, out string reason)
    {
        if (string.IsNullOrWhiteSpace(name)) { reason = "name is empty"; return false; }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        { reason = "contains invalid characters"; return false; }
        if (name.EndsWith('.') || name.EndsWith(' '))
        { reason = "must not end with '.' or whitespace"; return false; }
        if (ReservedFolderNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        { reason = "is a reserved Windows name"; return false; }
        reason = string.Empty;
        return true;
    }

    private static string AddTrailingSep(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        char last = path[^1];
        return last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar
            ? path : path + Path.DirectorySeparatorChar;
    }

    private static bool StartsWithIgnoreCase(string? value, string prefix)
        => !string.IsNullOrEmpty(value)
           && value!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static void RewriteProfilesForRename(
        Guid machineId, string oldAgentName, string newAgentName,
        string oldPrefix, string newPrefix)
    {
        var dir = MachinePaths.ProfilesDir(machineId);
        if (!Directory.Exists(dir)) return;
        var options = new JsonSerializerOptions { WriteIndented = true };
        foreach (var jsonPath in Directory.GetFiles(dir, "*.json"))
        {
            Profile? profile;
            try { profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(jsonPath)); }
            catch { continue; }
            if (profile is null) continue;

            bool changed = false;

            foreach (var o in profile.SoundOverrides)
            {
                if (StartsWithIgnoreCase(o.Path, oldPrefix))
                {
                    o.Path = newPrefix + o.Path.Substring(oldPrefix.Length);
                    changed = true;
                }
            }

            foreach (var s in profile.Soundboard)
            {
                if (StartsWithIgnoreCase(s.FilePath, oldPrefix))
                {
                    s.FilePath = newPrefix + s.FilePath.Substring(oldPrefix.Length);
                    changed = true;
                }
            }

            foreach (var a in profile.Agents)
            {
                if (string.Equals(a.Name, oldAgentName, StringComparison.OrdinalIgnoreCase))
                {
                    a.Name = newAgentName;
                    changed = true;
                }
            }

            for (int i = 0; i < profile.PinnedAgents.Count; i++)
            {
                if (string.Equals(profile.PinnedAgents[i], oldAgentName, StringComparison.OrdinalIgnoreCase))
                {
                    profile.PinnedAgents[i] = newAgentName;
                    changed = true;
                }
            }

            if (changed)
            {
                try { File.WriteAllText(jsonPath, JsonSerializer.Serialize(profile, options)); }
                catch (Exception ex)
                {
                    App.DebugLog.LogError("Agent",
                        $"Could not rewrite profile '{Path.GetFileName(jsonPath)}': {ex.Message}");
                }
            }
        }
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

        App.DebugLog.LogUser("Shell", $"Import Machine requested for '{dialog.SelectedPath}'");

        var result = MachineImporter.TryImport(dialog.SelectedPath, _machineCoordinator);
        if (!result.Success || result.Machine is null)
        {
            System.Windows.MessageBox.Show(
                System.Windows.Application.Current?.MainWindow,
                result.ErrorMessage ?? "Machine import failed.",
                result.ErrorTitle   ?? "Import Machine",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        SelectedMachine = result.Machine;
        if (!result.Reused) _machineCoordinator.SaveMachinesToDisk();
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
