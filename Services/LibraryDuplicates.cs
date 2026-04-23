using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using AmbientSFXMachineGUI.Models;

namespace AmbientSFXMachineGUI.Services;

public enum DuplicateKind { Exact, Likely }

public sealed class DuplicateGroup
{
    public DuplicateKind Kind { get; }
    public string Key { get; }
    public IReadOnlyList<AudioFileEntry> Entries { get; }
    public int FileCount => Entries.Count;
    public int TotalUsages => Entries.Sum(e => e.Usages.Count);
    public string Header => Kind == DuplicateKind.Exact
        ? $"{Key}  ({FileCount} files, {TotalUsages} usages)"
        : $"{Key}  ({FileCount} files, {TotalUsages} usages)";

    public DuplicateGroup(DuplicateKind kind, string key, IReadOnlyList<AudioFileEntry> entries)
    {
        Kind = kind;
        Key = key;
        Entries = entries;
    }
}

/// <summary>
/// Computes duplicate groupings over <see cref="AudioLibrary.Entries"/>:
///   Exact  — entries sharing an identical SHA-256.
///   Likely — entries with the same case/extension-insensitive filename and
///            durations within ±1% but different (or missing) hashes.
/// Refreshes whenever entries are added/removed or their Sha256/Duration change.
/// </summary>
public sealed class LibraryDuplicates
{
    private readonly AudioLibrary _library;
    private readonly Dispatcher _dispatcher;
    private readonly HashSet<AudioFileEntry> _tracked = new();
    private readonly ObservableCollection<DuplicateGroup> _exact = new();
    private readonly ObservableCollection<DuplicateGroup> _likely = new();
    private bool _refreshScheduled;

    public ReadOnlyObservableCollection<DuplicateGroup> ExactGroups { get; }
    public ReadOnlyObservableCollection<DuplicateGroup> LikelyGroups { get; }

    public LibraryDuplicates(AudioLibrary library, Dispatcher dispatcher)
    {
        _library = library;
        _dispatcher = dispatcher;
        ExactGroups = new ReadOnlyObservableCollection<DuplicateGroup>(_exact);
        LikelyGroups = new ReadOnlyObservableCollection<DuplicateGroup>(_likely);

        ((INotifyCollectionChanged)_library.Entries).CollectionChanged += OnEntriesChanged;
        foreach (var e in _library.Entries) Track(e);
        ScheduleRefresh();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (AudioFileEntry old in e.OldItems) Untrack(old);
        if (e.NewItems != null)
            foreach (AudioFileEntry added in e.NewItems) Track(added);
        ScheduleRefresh();
    }

    private void Track(AudioFileEntry entry)
    {
        if (_tracked.Add(entry))
            entry.PropertyChanged += OnEntryPropertyChanged;
    }

    private void Untrack(AudioFileEntry entry)
    {
        if (_tracked.Remove(entry))
            entry.PropertyChanged -= OnEntryPropertyChanged;
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioFileEntry.Sha256)
            || e.PropertyName == nameof(AudioFileEntry.Duration)
            || e.PropertyName == nameof(AudioFileEntry.AbsolutePath))
            ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        if (_refreshScheduled) return;
        _refreshScheduled = true;
        _dispatcher.BeginInvoke(new Action(Refresh), DispatcherPriority.Background);
    }

    private void Refresh()
    {
        _refreshScheduled = false;
        var entries = _library.Entries.ToList();

        _exact.Clear();
        foreach (var g in entries
                     .Where(e => !string.IsNullOrEmpty(e.Sha256))
                     .GroupBy(e => e.Sha256, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1)
                     .OrderByDescending(g => g.Count()))
        {
            _exact.Add(new DuplicateGroup(
                DuplicateKind.Exact,
                $"SHA-256 {g.Key[..Math.Min(12, g.Key.Length)]}…",
                g.OrderBy(e => e.AbsolutePath, StringComparer.OrdinalIgnoreCase).ToList()));
        }

        _likely.Clear();
        // Group by stem (filename without extension, case-insensitive).
        var byStem = entries
            .Where(e => e.Duration > TimeSpan.Zero)
            .GroupBy(e => Path.GetFileNameWithoutExtension(e.AbsolutePath) ?? string.Empty,
                     StringComparer.OrdinalIgnoreCase);

        foreach (var stemGroup in byStem)
        {
            if (string.IsNullOrEmpty(stemGroup.Key)) continue;
            var list = stemGroup.ToList();
            if (list.Count < 2) continue;

            // Within the stem group, cluster by ±1% duration. Use simple O(n^2)
            // union-find — group counts are tiny in practice.
            var clusters = ClusterByDuration(list, tolerance: 0.01);
            foreach (var cluster in clusters)
            {
                if (cluster.Count < 2) continue;
                // Suppress clusters that are wholly an exact-dup group (same hash across all members).
                if (cluster.All(e => !string.IsNullOrEmpty(e.Sha256))
                    && cluster.Select(e => e.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
                    continue;

                _likely.Add(new DuplicateGroup(
                    DuplicateKind.Likely,
                    stemGroup.Key,
                    cluster.OrderBy(e => e.AbsolutePath, StringComparer.OrdinalIgnoreCase).ToList()));
            }
        }
    }

    private static List<List<AudioFileEntry>> ClusterByDuration(List<AudioFileEntry> items, double tolerance)
    {
        var sorted = items.OrderBy(e => e.Duration.TotalSeconds).ToList();
        var clusters = new List<List<AudioFileEntry>>();
        List<AudioFileEntry>? current = null;
        double anchorSeconds = 0;

        foreach (var e in sorted)
        {
            var s = e.Duration.TotalSeconds;
            if (current is null || Math.Abs(s - anchorSeconds) / Math.Max(anchorSeconds, 0.001) > tolerance)
            {
                current = new List<AudioFileEntry> { e };
                anchorSeconds = s;
                clusters.Add(current);
            }
            else
            {
                current.Add(e);
            }
        }
        return clusters;
    }
}
