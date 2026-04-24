using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using AmbientSFXMachineGUI.Models;

namespace AmbientSFXMachineGUI.Services;

/// <summary>
/// Session-wide debug log for user actions, agent activity, and errors (LOG-05).
/// In-memory ring buffer capped at MemoryCap; flushed to a per-session .log file
/// every FlushThreshold new entries and on clean shutdown / unhandled exceptions.
/// </summary>
public sealed class DebugLogService
{
    public const int MemoryCap = 2000;
    public const int FlushThreshold = 1000;

    private readonly object _sync = new();
    private readonly List<DebugLogEntry> _pending = new();
    private readonly AppSettings _settings;
    private readonly string _sessionFileName;
    private string _currentFolder;

    public ObservableCollection<DebugLogEntry> Entries { get; } = new();

    public string CurrentFolder
    {
        get { lock (_sync) return _currentFolder; }
    }

    public string CurrentFilePath
    {
        get { lock (_sync) return Path.Combine(_currentFolder, _sessionFileName); }
    }

    public DebugLogService(AppSettings settings)
    {
        _settings = settings;
        _currentFolder = settings.GetDebugLogFolderOrDefault();
        _sessionFileName = $"session-{DateTime.Now:yyyyMMdd-HHmmss}.log";
    }

    public void LogUser(string source, string message) => Append(DebugLogCategory.User, source, message);
    public void LogAgent(string source, string message) => Append(DebugLogCategory.Agent, source, message);
    public void LogError(string source, string message) => Append(DebugLogCategory.Error, source, message);

    public void LogException(string source, Exception ex)
        => Append(DebugLogCategory.Error, source, ex.ToString());

    private void Append(DebugLogCategory category, string source, string message)
    {
        var entry = new DebugLogEntry
        {
            Category  = category,
            Source    = source ?? string.Empty,
            Message   = message ?? string.Empty,
        };

        bool shouldFlush;
        lock (_sync)
        {
            _pending.Add(entry);
            shouldFlush = _pending.Count >= FlushThreshold;
        }

        var app = Application.Current;
        if (app is not null)
            app.Dispatcher.BeginInvoke(new Action(() => AppendToObservable(entry)));
        else
            AppendToObservable(entry);

        if (shouldFlush) Flush();
    }

    private void AppendToObservable(DebugLogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > MemoryCap) Entries.RemoveAt(0);
    }

    /// <summary>
    /// Writes any buffered entries to disk. Safe to call multiple times; swallows IO errors
    /// because this is a diagnostics channel and must not crash the app or re-enter during shutdown.
    /// </summary>
    public void Flush()
    {
        DebugLogEntry[] batch;
        string folder;
        string file;
        lock (_sync)
        {
            if (_pending.Count == 0) return;
            batch = _pending.ToArray();
            _pending.Clear();
            folder = _currentFolder;
            file = Path.Combine(folder, _sessionFileName);
        }

        try
        {
            Directory.CreateDirectory(folder);
            var sb = new StringBuilder(batch.Length * 96);
            foreach (var e in batch) sb.AppendLine(e.ToLogLine());
            File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    public void ChangeFolder(string newFolder)
    {
        if (string.IsNullOrWhiteSpace(newFolder)) return;
        Flush();
        lock (_sync) _currentFolder = newFolder;
        _settings.debugLogPath = newFolder;
        _settings.Save();
        LogUser("DebugLog", $"Log folder changed to: {newFolder}");
    }
}
