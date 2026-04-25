using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;

namespace AmbientSFXMachineGUI.Shell;

public partial class MainWindow : Window
{
    private static string LayoutFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AmbientAgents",
        "layout.xml");

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
            item.IsChecked = anchorable?.IsVisible ?? false;
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
            App.DebugLog.LogError("Shell", $"View menu: panel '{contentId}' not found in layout");
            return;
        }
        if (item.IsChecked) anchorable.Show(); else anchorable.Hide();
        var label = item.Header?.ToString()?.Replace("_", string.Empty) ?? contentId;
        App.DebugLog.LogUser("Shell", $"View menu: '{label}' → {(item.IsChecked ? "shown" : "hidden")}");
    }

    private void OnShowAllPanelsClick(object sender, RoutedEventArgs e)
    {
        // Materialize first — Show() mutates the layout tree mid-enumeration.
        var hidden = AllAnchorables().Where(a => !a.IsVisible).ToList();
        foreach (var anchorable in hidden) anchorable.Show();
        App.DebugLog.LogUser("Shell", $"View menu: Show All Panels → {hidden.Count} restored");
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
