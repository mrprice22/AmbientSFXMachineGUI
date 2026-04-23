using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmbientSFXMachineGUI.Models;

public readonly record struct UsageRef(Guid MachineId, Guid AgentId);

public partial class AudioFileEntry : ObservableObject
{
    [ObservableProperty] private string _absolutePath = string.Empty;
    [ObservableProperty] private string _sha256 = string.Empty;
    [ObservableProperty] private TimeSpan _duration;
    [ObservableProperty] private long _byteSize;

    public string FileName => string.IsNullOrEmpty(AbsolutePath) ? string.Empty : Path.GetFileName(AbsolutePath);

    private readonly ObservableCollection<UsageRef> _usages = new();
    public IReadOnlyList<UsageRef> Usages => _usages;

    public event EventHandler? UsagesChanged;

    internal bool AddUsage(UsageRef usage)
    {
        if (_usages.Contains(usage)) return false;
        _usages.Add(usage);
        UsagesChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(Usages));
        return true;
    }

    internal bool RemoveUsage(UsageRef usage)
    {
        if (!_usages.Remove(usage)) return false;
        UsagesChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(Usages));
        return true;
    }

    internal int RemoveUsagesMatching(Predicate<UsageRef> predicate)
    {
        int removed = 0;
        for (int i = _usages.Count - 1; i >= 0; i--)
        {
            if (predicate(_usages[i]))
            {
                _usages.RemoveAt(i);
                removed++;
            }
        }
        if (removed > 0)
        {
            UsagesChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(Usages));
        }
        return removed;
    }

    partial void OnAbsolutePathChanged(string value) => OnPropertyChanged(nameof(FileName));
}
