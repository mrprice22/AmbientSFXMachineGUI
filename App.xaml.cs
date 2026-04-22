using System.Windows;
using AmbientSFXMachineGUI.Services;
using AmbientSFXMachineGUI.Shell;

namespace AmbientSFXMachineGUI;

public partial class App : Application
{
    public static MachineCoordinator MachineCoordinator { get; } = new();
    public static ProfileService Profiles { get; } = new();
    public static HotkeyService Hotkeys { get; } = new();
    public static TrayService Tray { get; } = new();

    private bool _muted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MachineCoordinator.LoadMachinesFromDisk();

        Tray.Initialize();
        Tray.SetWindowVisibleState(true);
        Tray.ShowHideToggled += OnTrayShowHideToggled;
        Tray.MuteAllToggled += OnTrayMuteAllToggled;
        Tray.ExitRequested += OnTrayExitRequested;
        Tray.MachineMuteToggled += OnTrayMachineMuteToggled;
        Tray.MachineSoloRequested += OnTrayMachineSoloRequested;
        Tray.MachineShowCardsRequested += OnTrayMachineShowCardsRequested;
        Tray.AttachMachines(MachineCoordinator.Machines);

        Hotkeys.Register("app.muteAll", () => Dispatcher.Invoke(() => OnTrayMuteAllToggled(this, System.EventArgs.Empty)));
        Hotkeys.Register("app.toggleWindow", () => Dispatcher.Invoke(() =>
        {
            if (MainWindow is MainWindow main) main.ToggleVisibility();
        }));
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
        MachineCoordinator.SetMuteAll(_muted);
        Tray.SetMutedState(_muted);
    }

    private void OnTrayMachineMuteToggled(object? sender, System.Guid id)
    {
        Dispatcher.Invoke(() =>
        {
            foreach (var m in MachineCoordinator.Machines)
                if (m.Id == id) { m.IsEnabled = !m.IsEnabled; return; }
        });
    }

    private void OnTrayMachineSoloRequested(object? sender, System.Guid id)
    {
        Dispatcher.Invoke(() =>
        {
            foreach (var m in MachineCoordinator.Machines)
                m.IsEnabled = m.Id == id;
        });
    }

    private void OnTrayMachineShowCardsRequested(object? sender, System.Guid id)
    {
        Dispatcher.Invoke(() =>
        {
            if (MainWindow is not MainWindow main) return;
            if (main.DataContext is Shell.ShellViewModel vm)
            {
                foreach (var m in MachineCoordinator.Machines)
                    if (m.Id == id) { vm.SelectedMachine = m; break; }
            }
            if (!main.IsVisible || main.WindowState == WindowState.Minimized) main.ToggleVisibility();
            else main.Activate();
        });
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
        MachineCoordinator.SaveMachinesToDisk();
        Tray.Dispose();
        Hotkeys.Dispose();
        MachineCoordinator.Shutdown();
        base.OnExit(e);
    }
}
