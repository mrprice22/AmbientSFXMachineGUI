using System;
using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using AmbientSFXMachineGUI.Models;
using WinForms = System.Windows.Forms;

namespace AmbientSFXMachineGUI.Panels.DebugLogPanel;

public partial class DebugLogPanelView : UserControl
{
    private INotifyCollectionChanged? _hooked;

    public DebugLogPanelView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EntryList.ItemsSource = App.DebugLog.Entries;
        _hooked = App.DebugLog.Entries;
        _hooked.CollectionChanged += OnEntriesChanged;
        RefreshFolderLabel();
        ScrollToEnd();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hooked is not null) _hooked.CollectionChanged -= OnEntriesChanged;
        _hooked = null;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add) ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (EntryList.ItemsSource is not IList list || list.Count == 0) return;
        var last = list[list.Count - 1];
        if (last is not null) EntryList.ScrollIntoView(last);
    }

    private void RefreshFolderLabel()
    {
        FolderLabel.Text = $"Folder: {App.DebugLog.CurrentFolder}";
    }

    private void OnChangeFolderClick(object sender, RoutedEventArgs e)
    {
        using var dlg = new WinForms.FolderBrowserDialog
        {
            Description = "Select folder for session .log files",
            UseDescriptionForTitle = true,
            SelectedPath = App.DebugLog.CurrentFolder,
        };
        if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
        App.DebugLog.ChangeFolder(dlg.SelectedPath);
        RefreshFolderLabel();
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var folder = App.DebugLog.CurrentFolder;
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            App.DebugLog.LogError("DebugLogPanel", $"Open log folder failed: {ex.Message}");
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var entry in App.DebugLog.Entries) sb.AppendLine(entry.ToLogLine());
            Clipboard.SetText(sb.ToString());
        }
        catch (Exception ex)
        {
            App.DebugLog.LogError("DebugLogPanel", $"Copy to clipboard failed: {ex.Message}");
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        App.DebugLog.Entries.Clear();
        App.DebugLog.LogUser("DebugLogPanel", "In-memory log cleared");
    }
}
