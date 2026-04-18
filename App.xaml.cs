using System.Windows;
using AmbientSFXMachineGUI.Services;
using AmbientSFXMachineGUI.Shell;

namespace AmbientSFXMachineGUI;

public partial class App : Application
{
    public static AgentCoordinator Coordinator { get; } = new();
    public static ProfileService Profiles { get; } = new();
    public static HotkeyService Hotkeys { get; } = new();
    public static TrayService Tray { get; } = new();

    private bool _muted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Coordinator.LoadAgentsFromDisk();

        Tray.Initialize();
        Tray.SetWindowVisibleState(true);
        Tray.ShowHideToggled += OnTrayShowHideToggled;
        Tray.MuteAllToggled += OnTrayMuteAllToggled;
        Tray.ExitRequested += OnTrayExitRequested;

        Hotkeys.LoadDefaults();

        // Keep the app alive when all windows are hidden (tray still running).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    private void OnTrayShowHideToggled(object? sender, System.EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (MainWindow is MainWindow main) main.ToggleVisibility();
        });
    }

    private void OnTrayMuteAllToggled(object? sender, System.EventArgs e)
    {
        _muted = !_muted;
        Coordinator.SetMuteAll(_muted);
        Tray.SetMutedState(_muted);
    }

    private void OnTrayExitRequested(object? sender, System.EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (MainWindow is MainWindow main) main.ForceClose();
            Shutdown();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tray.Dispose();
        Hotkeys.Dispose();
        Coordinator.Shutdown();
        base.OnExit(e);
    }
}
