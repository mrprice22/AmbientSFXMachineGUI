using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AmbientSFXMachineGUI.Services;
using AmbientSFXMachineGUI.Shell;

namespace AmbientSFXMachineGUI;

public partial class App : Application
{
    public static AppSettings Settings { get; } = AppSettings.Load();
    public static DebugLogService DebugLog { get; } = new(Settings);
    public static MachineCoordinator MachineCoordinator { get; } = new(DebugLog);
    public static AudioLibrary AudioLibrary { get; } = new();
    public static LibraryCacheStore LibraryCache { get; } = new(MachinePaths.LibraryCachePath);
    public static LibraryHasher LibraryHasher { get; } =
        new(AudioLibrary, LibraryCache, Current.Dispatcher);
    public static LibraryDuplicates LibraryDuplicates { get; } =
        new(AudioLibrary, Current.Dispatcher);
    public static ProfileService Profiles { get; } = new();
    public static HotkeyService Hotkeys { get; } = new();
    public static TrayService Tray { get; } = new();

    private bool _muted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // LOG-05: capture crashes so the debug log survives unhandled exceptions.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                DebugLog.LogException("AppDomain.UnhandledException", ex);
            DebugLog.Flush();
        };
        DispatcherUnhandledException += (_, args) =>
        {
            DebugLog.LogException("Dispatcher.UnhandledException", args.Exception);
            DebugLog.Flush();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DebugLog.LogException("TaskScheduler.UnobservedTaskException", args.Exception);
            DebugLog.Flush();
        };

        DebugLog.LogUser("App", "Application starting");
        AudioLibrary.Attach(MachineCoordinator);
        MachineCoordinator.LoadMachinesFromDisk();

        Tray.Initialize();
        Tray.SetWindowVisibleState(true);
        Tray.ShowHideToggled += OnTrayShowHideToggled;
        Tray.MuteAllToggled += OnTrayMuteAllToggled;
        Tray.ExitRequested += OnTrayExitRequested;
        Tray.MachineMuteToggled += OnTrayMachineMuteToggled;
        Tray.MachineSoloRequested += OnTrayMachineSoloRequested;
        Tray.MachineShowCardsRequested += OnTrayMachineShowCardsRequested;
        Tray.MachineCloseRequested += OnTrayMachineCloseRequested;
        Tray.AttachMachines(MachineCoordinator.Machines);

        Hotkeys.Register("app.muteAll", () =>
        {
            DebugLog.LogUser("Hotkey", "app.muteAll pressed");
            Dispatcher.Invoke(() => OnTrayMuteAllToggled(this, System.EventArgs.Empty));
        });
        Hotkeys.Register("app.toggleWindow", () =>
        {
            DebugLog.LogUser("Hotkey", "app.toggleWindow pressed");
            Dispatcher.Invoke(() =>
            {
                if (MainWindow is MainWindow main) main.ToggleVisibility();
            });
        });
        Hotkeys.Register("app.nextProfile", () =>
        {
            DebugLog.LogUser("Hotkey", "app.nextProfile pressed");
            Dispatcher.Invoke(() =>
            {
                if (MainWindow is MainWindow main && main.DataContext is Shell.ShellViewModel vm
                    && vm.CycleNextProfileCommand.CanExecute(null))
                {
                    vm.CycleNextProfileCommand.Execute(null);
                }
            });
        });
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

    // MACHINE-13: confirmed via the inline tray submenu, so just unload here.
    private void OnTrayMachineCloseRequested(object? sender, System.Guid id)
    {
        Dispatcher.Invoke(() =>
        {
            var machine = MachineCoordinator.Machines.FirstOrDefault(m => m.Id == id);
            if (machine is null) return;
            DebugLog.LogUser("Tray", $"Close machine '{machine.Name}' (unload)");
            MachineCoordinator.UnloadMachine(machine);
            Tray.ShowBalloon("Machine closed", $"'{machine.Name}' has been unloaded from the session.");
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
        DebugLog.LogUser("App", "Application exiting");
        MachineCoordinator.SaveMachinesToDisk();
        LibraryHasher.Dispose();
        LibraryCache.Dispose();
        Tray.Dispose();
        Hotkeys.Dispose();
        MachineCoordinator.Shutdown();
        DebugLog.Flush();
        base.OnExit(e);
    }
}
