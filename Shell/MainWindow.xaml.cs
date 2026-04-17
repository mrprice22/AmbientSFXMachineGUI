using System.Windows;

namespace AmbientSFXMachineGUI.Shell;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel(App.Coordinator, App.Profiles, App.Hotkeys);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // TODO: persist AvalonDock layout to %AppData%\AmbientAgents\layout.xml
        base.OnClosing(e);
    }
}
