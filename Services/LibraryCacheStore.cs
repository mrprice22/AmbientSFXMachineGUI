using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace AmbientSFXMachineGUI.Services;

/// <summary>
/// Persists path -> (sha256, size, mtime) to library.json and serves as a
/// validation oracle for the hasher: a cached hash is reused only when the
/// file's current size and mtime still match.
/// </summary>
public sealed class LibraryCacheStore : IDisposable
{
    public sealed record CachedEntry(string Hash, long Size, long MtimeUtcTicks);

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly object _lock = new();
    private readonly Dictionary<string, CachedEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;
    private readonly Timer _saveTimer;
    private bool _dirty;

    public LibraryCacheStore(string path)
    {
        _path = path;
        Load();
        _saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string? TryGet(string path, long size, long mtimeUtcTicks)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(path, out var e) && e.Size == size && e.MtimeUtcTicks == mtimeUtcTicks)
                return e.Hash;
        }
        return null;
    }

    public void Set(string path, string hash, long size, long mtimeUtcTicks)
    {
        lock (_lock)
        {
            _entries[path] = new CachedEntry(hash, size, mtimeUtcTicks);
            _dirty = true;
        }
        _saveTimer.Change(2000, Timeout.Infinite);
    }

    public void Flush()
    {
        Dictionary<string, CachedEntry> snapshot;
        lock (_lock)
        {
            if (!_dirty) return;
            snapshot = new Dictionary<string, CachedEntry>(_entries, StringComparer.OrdinalIgnoreCase);
            _dirty = false;
        }
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, JsonOpts));
            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else File.Move(tmp, _path);
        }
        catch
        {
            // Cache is best-effort; failures shouldn't break the app.
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, CachedEntry>>(json, JsonOpts);
            if (loaded is null) return;
            foreach (var (k, v) in loaded) _entries[k] = v;
        }
        catch { }
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Flush();
    }
}
