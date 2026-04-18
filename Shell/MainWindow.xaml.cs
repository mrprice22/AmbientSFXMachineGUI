using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
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
        DataContext = new ShellViewModel(App.Coordinator, App.Profiles, App.Hotkeys);
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
