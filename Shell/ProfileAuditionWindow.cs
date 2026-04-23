using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AmbientSFXMachineGUI.Models;
using AmbientSFXMachineGUI.Services;

namespace AmbientSFXMachineGUI.Shell;

internal sealed class ProfileAuditionWindow : Window
{
    private readonly ProfileService _profiles;
    private readonly HotkeyService _hotkeys;
    private readonly MachineViewModel _machine;

    private readonly ComboBox _picker;
    private readonly TextBox _seconds;
    private readonly Button _hold;
    private readonly TextBlock _status;

    private ProfileService.AuditionHandle? _active;

    public ProfileAuditionWindow(
        ProfileService profiles,
        HotkeyService hotkeys,
        MachineViewModel machine,
        ObservableCollection<Profile> allProfiles,
        Profile? selected)
    {
        _profiles = profiles;
        _hotkeys  = hotkeys;
        _machine  = machine;

        Title = "Profile Audition";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(12) };

        panel.Children.Add(new TextBlock
        {
            Text = "Hold the button to preview a profile. Release or wait for the timeout to revert.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        panel.Children.Add(new TextBlock { Text = "Profile:", Margin = new Thickness(0, 0, 0, 2) });
        _picker = new ComboBox
        {
            ItemsSource = allProfiles,
            DisplayMemberPath = nameof(Profile.Name),
            SelectedItem = selected,
            Margin = new Thickness(0, 0, 0, 10),
        };
        _picker.SelectionChanged += (_, _) => UpdateHoldState();
        panel.Children.Add(_picker);

        var durRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var durLabel = new TextBlock { Text = "Timeout (seconds):", VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(durLabel, Dock.Left);
        durRow.Children.Add(durLabel);
        _seconds = new TextBox { Text = "30", Width = 60, Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        durRow.Children.Add(_seconds);
        panel.Children.Add(durRow);

        _hold = new Button
        {
            Content = "Hold to Preview",
            Height  = 40,
            FontWeight = FontWeights.SemiBold,
            Margin  = new Thickness(0, 0, 0, 6),
        };
        _hold.PreviewMouseLeftButtonDown += OnHoldDown;
        _hold.PreviewMouseLeftButtonUp   += OnHoldUp;
        _hold.MouseLeave                 += OnHoldLeave;
        panel.Children.Add(_hold);

        _status = new TextBlock
        {
            Text = "Idle",
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            Margin = new Thickness(0, 0, 0, 10),
        };
        panel.Children.Add(_status);

        var close = new Button { Content = "Close", IsCancel = true, Width = 80, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(close);

        Content = panel;
        Closed += (_, _) => StopAudition("Closed");
        UpdateHoldState();
    }

    private void UpdateHoldState()
    {
        _hold.IsEnabled = _picker.SelectedItem is Profile;
    }

    private void OnHoldDown(object sender, MouseButtonEventArgs e)
    {
        if (_active is not null) return;
        if (_picker.SelectedItem is not Profile profile) return;

        if (!double.TryParse(_seconds.Text, out var secs) || secs <= 0) secs = 30;
        var duration = TimeSpan.FromSeconds(Math.Min(secs, 600));

        _active = _profiles.Audition(_machine, _hotkeys, profile, duration);
        _active.Reverted += (_, _) =>
        {
            if (_active is null) return;
            _active = null;
            _status.Text = "Auto-reverted (timeout)";
            _status.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            _hold.Content = "Hold to Preview";
        };
        _status.Text = $"Previewing '{profile.Name}' (auto-reverts in {secs:0}s)";
        _status.Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x7A, 0x2E));
        _hold.Content = "Hold to Preview (active)";
    }

    private void OnHoldUp(object sender, MouseButtonEventArgs e) => StopAudition("Reverted (released)");

    private void OnHoldLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) StopAudition("Reverted (left button)");
    }

    private void StopAudition(string message)
    {
        if (_active is null) return;
        _active.Dispose();
        _active = null;
        _status.Text = message;
        _status.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        _hold.Content = "Hold to Preview";
    }
}
