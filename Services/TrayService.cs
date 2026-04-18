using System;
using System.Drawing;
using System.Windows.Forms;

namespace AmbientSFXMachineGUI.Services;

public sealed class TrayService : IDisposable
{
    private NotifyIcon? _icon;
    private ToolStripMenuItem? _showHideItem;
    private ToolStripMenuItem? _muteItem;

    public event EventHandler? ShowHideToggled;
    public event EventHandler? MuteAllToggled;
    public event EventHandler? ExitRequested;

    public void Initialize()
    {
        if (_icon != null) return;

        var menu = new ContextMenuStrip();
        _showHideItem = new ToolStripMenuItem("Show / Hide Window");
        _showHideItem.Click += (_, _) => ShowHideToggled?.Invoke(this, EventArgs.Empty);
        _muteItem = new ToolStripMenuItem("Mute All") { CheckOnClick = true };
        _muteItem.Click += (_, _) => MuteAllToggled?.Invoke(this, EventArgs.Empty);
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(_showHideItem);
        menu.Items.Add(_muteItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "AmbientAgents",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => ShowHideToggled?.Invoke(this, EventArgs.Empty);
    }

    public void SetWindowVisibleState(bool visible)
    {
        if (_showHideItem != null)
            _showHideItem.Text = visible ? "Hide Window" : "Show Window";
    }

    public void SetMutedState(bool muted)
    {
        if (_muteItem != null) _muteItem.Checked = muted;
    }

    public void ShowBalloon(string title, string text)
    {
        _icon?.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted != null) return extracted;
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_icon != null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}
