using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AmbientSFXMachineGUI.Models;

namespace AmbientSFXMachineGUI.Services;

public sealed class TrayService : IDisposable
{
    private NotifyIcon? _icon;
    private ContextMenuStrip? _menu;
    private ToolStripMenuItem? _showHideItem;
    private ToolStripMenuItem? _muteItem;
    private ToolStripSeparator? _machinesSeparator;
    private ObservableCollection<MachineViewModel>? _machines;
    private readonly Dictionary<MachineViewModel, ToolStripMenuItem> _machineItems = new();

    public event EventHandler? ShowHideToggled;
    public event EventHandler? MuteAllToggled;
    public event EventHandler? ExitRequested;
    public event EventHandler<Guid>? MachineMuteToggled;
    public event EventHandler<Guid>? MachineSoloRequested;
    public event EventHandler<Guid>? MachineShowCardsRequested;
    // MACHINE-13: Close (unload) the machine from the running app.
    public event EventHandler<Guid>? MachineCloseRequested;

    public void Initialize()
    {
        if (_icon != null) return;

        _menu = new ContextMenuStrip();
        _showHideItem = new ToolStripMenuItem("Show / Hide Window");
        _showHideItem.Click += (_, _) => ShowHideToggled?.Invoke(this, EventArgs.Empty);
        _muteItem = new ToolStripMenuItem("Mute All") { CheckOnClick = true };
        _muteItem.Click += (_, _) => MuteAllToggled?.Invoke(this, EventArgs.Empty);
        _machinesSeparator = new ToolStripSeparator();
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _menu.Items.Add(_showHideItem);
        _menu.Items.Add(_muteItem);
        _menu.Items.Add(_machinesSeparator);
        // Machine submenus get inserted between this separator and the next.
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "AmbientAgents",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _icon.DoubleClick += (_, _) => ShowHideToggled?.Invoke(this, EventArgs.Empty);
    }

    public void AttachMachines(ObservableCollection<MachineViewModel> machines)
    {
        if (_machines != null)
            _machines.CollectionChanged -= OnMachinesChanged;
        _machines = machines;
        _machines.CollectionChanged += OnMachinesChanged;
        RebuildMachineMenus();
    }

    private void OnMachinesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildMachineMenus();

    private void RebuildMachineMenus()
    {
        if (_menu == null || _machinesSeparator == null || _machines == null) return;

        foreach (var (machine, _) in _machineItems)
            machine.PropertyChanged -= OnMachinePropertyChanged;
        foreach (var item in _machineItems.Values)
        {
            _menu.Items.Remove(item);
            item.Dispose();
        }
        _machineItems.Clear();

        var insertAt = _menu.Items.IndexOf(_machinesSeparator) + 1;
        foreach (var machine in _machines)
        {
            var item = BuildMachineItem(machine);
            _menu.Items.Insert(insertAt++, item);
            _machineItems[machine] = item;
            machine.PropertyChanged += OnMachinePropertyChanged;
        }
    }

    private ToolStripMenuItem BuildMachineItem(MachineViewModel machine)
    {
        var item = new ToolStripMenuItem(machine.Name) { Image = LoadMachineImage(machine) };

        var muteItem = new ToolStripMenuItem("Mute") { CheckOnClick = true, Checked = !machine.IsEnabled };
        muteItem.Click += (_, _) => MachineMuteToggled?.Invoke(this, machine.Id);
        muteItem.Name = "mute";

        var soloItem = new ToolStripMenuItem("Solo (mute all others)");
        soloItem.Click += (_, _) => MachineSoloRequested?.Invoke(this, machine.Id);

        var showItem = new ToolStripMenuItem("Show cards");
        showItem.Click += (_, _) => MachineShowCardsRequested?.Invoke(this, machine.Id);

        // MACHINE-13: Close machine. Uses an inline confirm sub-item rather than a modal —
        // the parent says "Close machine"; hovering reveals "Confirm: Close '<name>'" which
        // actually fires the event. Works for degraded entries (no icon, missing root path)
        // because no per-machine state beyond Id and Name is read here.
        var closeItem = new ToolStripMenuItem("Close machine") { Name = "close" };
        var confirmItem = new ToolStripMenuItem($"Confirm: Close '{machine.Name}'") { Name = "confirmClose" };
        confirmItem.Click += (_, _) => MachineCloseRequested?.Invoke(this, machine.Id);
        closeItem.DropDownItems.Add(confirmItem);

        item.DropDownItems.Add(muteItem);
        item.DropDownItems.Add(soloItem);
        item.DropDownItems.Add(showItem);
        item.DropDownItems.Add(new ToolStripSeparator());
        item.DropDownItems.Add(closeItem);
        return item;
    }

    private void OnMachinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MachineViewModel machine) return;
        if (!_machineItems.TryGetValue(machine, out var item)) return;

        switch (e.PropertyName)
        {
            case nameof(MachineViewModel.Name):
                item.Text = machine.Name;
                // MACHINE-13: keep the Close confirm label in sync with the displayed name.
                if (item.DropDownItems["close"] is ToolStripMenuItem closeMenuItem &&
                    closeMenuItem.DropDownItems["confirmClose"] is ToolStripMenuItem confirmCloseItem)
                {
                    confirmCloseItem.Text = $"Confirm: Close '{machine.Name}'";
                }
                break;
            case nameof(MachineViewModel.IconPath):
            case nameof(MachineViewModel.RootPath):
                item.Image?.Dispose();
                item.Image = LoadMachineImage(machine);
                break;
            case nameof(MachineViewModel.IsEnabled):
                if (item.DropDownItems["mute"] is ToolStripMenuItem muteItem)
                    muteItem.Checked = !machine.IsEnabled;
                break;
        }
    }

    private static Image? LoadMachineImage(MachineViewModel machine)
    {
        try
        {
            var path = machine.ResolvedIconPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                using var ico = new Icon(path, 16, 16);
                return ico.ToBitmap();
            }
            using var src = Image.FromFile(path);
            return new Bitmap(src, new Size(16, 16));
        }
        catch { return null; }
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
        if (_machines != null)
            _machines.CollectionChanged -= OnMachinesChanged;
        foreach (var (machine, _) in _machineItems)
            machine.PropertyChanged -= OnMachinePropertyChanged;
        _machineItems.Clear();

        if (_icon != null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}
