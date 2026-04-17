using System.Windows;
using AmbientSFXMachineGUI.Services;

namespace AmbientSFXMachineGUI;

public partial class App : Application
{
    public static AgentCoordinator Coordinator { get; } = new();
    public static ProfileService Profiles { get; } = new();
    public static HotkeyService Hotkeys { get; } = new();
    public static TrayService Tray { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Coordinator.LoadAgentsFromDisk();
        Tray.Initialize();
        Hotkeys.LoadDefaults();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tray.Dispose();
        Hotkeys.Dispose();
        Coordinator.Shutdown();
        base.OnExit(e);
    }
}
