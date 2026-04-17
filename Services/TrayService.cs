using System;
using System.Windows.Forms;

namespace AmbientSFXMachineGUI.Services;

public sealed class TrayService : IDisposable
{
    private NotifyIcon? _icon;

    public event EventHandler? ShowRequested;
    public event EventHandler? MuteAllToggled;
    public event EventHandler? ExitRequested;

    public void Initialize()
    {
        // TODO: construct NotifyIcon, load agent.ico, build context menu:
        //       Show/Hide, Mute All, Exit → fire corresponding events.
    }

    public void ShowBalloon(string title, string text)
    {
        _icon?.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
