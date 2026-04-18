using System;
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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel(App.Coordinator, App.Profiles, App.Hotkeys);
        Loaded += OnLoaded;
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

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
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
