using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;

namespace AmbientSFXMachineGUI.Shell;

public partial class MainWindow : Window
{
    private static string LayoutFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AmbientAgents",
        "layout.xml");

    // ContentIds of every dockable panel declared in MainWindow.xaml. Used to detect
    // anchorables that have been orphaned (Closed, not Hidden) so Show All / Reset
    // can recover them from _defaultLayoutSnapshot.
    private static readonly string[] ExpectedContentIds =
        { "machines", "agents", "log", "now", "library", "debuglog", "soundboard" };

    private XDocument? _defaultLayoutSnapshot;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel(App.MachineCoordinator, App.Profiles, App.Hotkeys, App.LibraryHasher, App.AudioLibrary, App.LibraryDuplicates);
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Capture the default (XAML-defined) layout BEFORE applying any user layout
        // so Reset / Show-All-fallback can restore the fresh-install state.
        SnapshotDefaultLayout();

        if (!File.Exists(LayoutFilePath)) return;
        try
        {
            var serializer = new XmlLayoutSerializer(DockManager);
            using var stream = File.OpenRead(LayoutFilePath);
            serializer.Deserialize(stream);
        }
        catch
        {
            // Corrupt or incompatible layout — ignore and fall back to default.
        }
    }

    private void SnapshotDefaultLayout()
    {
        try
        {
            var serializer = new XmlLayoutSerializer(DockManager);
            using var ms = new MemoryStream();
            serializer.Serialize(ms);
            ms.Position = 0;
            _defaultLayoutSnapshot = XDocument.Load(ms);
        }
        catch (Exception ex)
        {
            App.DebugLog.LogError("Shell", $"Failed to snapshot default layout: {ex.Message}");
        }
    }

    private bool TryApplyDefaultLayout()
    {
        if (_defaultLayoutSnapshot is null) return false;
        try
        {
            var serializer = new XmlLayoutSerializer(DockManager);
            using var reader = _defaultLayoutSnapshot.CreateReader();
            serializer.Deserialize(reader);
            return true;
        }
        catch (Exception ex)
        {
            App.DebugLog.LogError("Shell", $"Apply default layout failed: {ex.Message}");
            return false;
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            App.Tray.SetWindowVisibleState(false);
        }
    }

    public void ToggleVisibility()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            Hide();
            App.Tray.SetWindowVisibleState(false);
        }
        else
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            App.Tray.SetWindowVisibleState(true);
        }
    }

    private System.Collections.Generic.IEnumerable<LayoutAnchorable> AllAnchorables()
    {
        var layout = DockManager.Layout;
        if (layout is null) yield break;
        foreach (var a in layout.Descendents().OfType<LayoutAnchorable>())
            yield return a;
        if (layout.Hidden is not null)
            foreach (var a in layout.Hidden)
                yield return a;
    }

    private LayoutAnchorable? FindAnchorable(string contentId) =>
        AllAnchorables().FirstOrDefault(a => string.Equals(a.ContentId, contentId, StringComparison.Ordinal));

    private void OnViewMenuSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem viewMenu) return;
        foreach (var item in viewMenu.Items.OfType<MenuItem>())
        {
            if (item.Tag is not string contentId) continue;
            var anchorable = FindAnchorable(contentId);
            // IsVisible reflects "in live tree (not Hidden)". Tabs in a multi-tab
            // pane still report IsVisible=true even when not the active tab — that
            // matches "the panel is reachable from the current layout."
            item.IsChecked = anchorable is not null && anchorable.IsVisible;
            // Orphaned anchorables (Closed, removed entirely) can't be toggled
            // individually; the user must use Reset to Default Layout to recover.
            item.IsEnabled = anchorable is not null;
        }
    }

    private void OnViewMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        if (item.Tag is not string contentId) return;
        var anchorable = FindAnchorable(contentId);
        if (anchorable is null)
        {
            App.DebugLog.LogError("Shell", $"View menu: panel '{contentId}' is orphaned; use Reset to Default Layout to recover.");
            item.IsChecked = false;
            return;
        }
        if (item.IsChecked)
        {
            anchorable.Show();
            // Surface as the active tab when sharing a pane with siblings — without
            // this, "Show" can land a freshly-restored anchorable behind another tab
            // and look like it didn't appear.
            anchorable.IsActive = true;
        }
        else
        {
            anchorable.Hide();
        }
        var label = item.Header?.ToString()?.Replace("_", string.Empty) ?? contentId;
        App.DebugLog.LogUser("Shell", $"View menu: '{label}' → {(item.IsChecked ? "shown" : "hidden")}");
    }

    private void OnShowAllPanelsClick(object sender, RoutedEventArgs e)
    {
        // Materialize first — Show() mutates the layout tree mid-enumeration.
        var hidden = AllAnchorables().Where(a => !a.IsVisible).ToList();
        foreach (var anchorable in hidden) anchorable.Show();

        // After unhiding, verify every expected panel is actually present and visible.
        // Anchorables Closed via X (not Hidden) are orphaned — Show() can't reach them
        // and PreviousContainer may be invalid if the parent pane collapsed. In either
        // case, fall back to applying the captured default layout.
        var visibleNow = AllAnchorables()
            .Where(a => a.IsVisible)
            .Select(a => a.ContentId)
            .ToHashSet(StringComparer.Ordinal);
        var stillMissing = ExpectedContentIds
            .Where(id => !visibleNow.Contains(id))
            .ToList();

        if (stillMissing.Count == 0)
        {
            App.DebugLog.LogUser("Shell", $"View menu: Show All Panels → {hidden.Count} restored");
            return;
        }

        if (TryApplyDefaultLayout())
        {
            App.DebugLog.LogUser(
                "Shell",
                $"View menu: Show All Panels → unhid {hidden.Count}, applied default layout to recover {stillMissing.Count} missing ({string.Join(", ", stillMissing)})");
        }
        else
        {
            App.DebugLog.LogError(
                "Shell",
                $"View menu: Show All Panels could not restore: {string.Join(", ", stillMissing)} (no default snapshot)");
        }
    }

    private void OnResetLayoutClick(object sender, RoutedEventArgs e)
    {
        if (TryApplyDefaultLayout())
        {
            App.DebugLog.LogUser("Shell", "View menu: Reset to Default Layout");
        }
        else
        {
            App.DebugLog.LogError("Shell", "Reset to Default Layout failed: no default snapshot available");
        }
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            App.Tray.SetWindowVisibleState(false);
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LayoutFilePath)!);
            var serializer = new XmlLayoutSerializer(DockManager);
            using var stream = File.Create(LayoutFilePath);
            serializer.Serialize(stream);
        }
        catch
        {
            // Best-effort persistence — don't block shutdown on IO errors.
        }
        base.OnClosing(e);
    }
}
