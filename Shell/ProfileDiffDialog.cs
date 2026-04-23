using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AmbientSFXMachineGUI.Services;

namespace AmbientSFXMachineGUI.Shell;

internal sealed class ProfileDiffDialog : Window
{
    public ProfileDiffDialog(string profileName, ProfileDiff diff)
    {
        Title = $"Apply Profile: {profileName}";
        Width = 480;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };

        var header = new TextBlock
        {
            Text = diff.HasChanges
                ? "The following changes will be applied. Confirm to proceed."
                : "No differences detected between the current state and this profile.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var ok = new Button { Content = "Confirm", IsDefault = true, Width = 84, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 84 };
        ok.Click += (_, _) => { DialogResult = true; Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var sections = new StackPanel();
        scroll.Content = sections;
        root.Children.Add(scroll);

        if (diff.MasterVolumeFrom.HasValue)
        {
            sections.Children.Add(BuildSection(
                $"Master volume ({diff.MasterVolumeFrom:0} → {diff.MasterVolumeTo:0})",
                new[] { $"{diff.MasterVolumeFrom:0}%  →  {diff.MasterVolumeTo:0}%" },
                expanded: true));
        }

        if (diff.AgentsToggled.Count > 0)
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (var a in diff.AgentsToggled)
                lines.Add($"{a.AgentName}: {(a.From ? "On" : "Off")}  →  {(a.To ? "On" : "Off")}");
            sections.Children.Add(BuildSection($"Agents toggled ({diff.AgentsToggled.Count})", lines));
        }

        if (diff.AgentVolumeChanges.Count > 0)
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (var a in diff.AgentVolumeChanges)
                lines.Add($"{a.AgentName}: {a.From:0}%  →  {a.To:0}%");
            sections.Children.Add(BuildSection($"Agent volumes shifted ({diff.AgentVolumeChanges.Count})", lines));
        }

        if (diff.SoundsToggled.Count > 0)
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (var s in diff.SoundsToggled)
                lines.Add($"{s.AgentName} ▸ {s.FileName}: {(s.From ? "On" : "Off")}  →  {(s.To ? "On" : "Off")}");
            sections.Children.Add(BuildSection($"Sounds enabled/disabled ({diff.SoundsToggled.Count})", lines));
        }

        if (diff.SoundboardAdded > 0 || diff.SoundboardRemoved > 0)
        {
            var lines = new System.Collections.Generic.List<string>();
            if (diff.SoundboardAdded > 0)   lines.Add($"+ {diff.SoundboardAdded} added");
            if (diff.SoundboardRemoved > 0) lines.Add($"- {diff.SoundboardRemoved} removed");
            sections.Children.Add(BuildSection("Soundboard", lines));
        }

        Content = root;
    }

    private static Expander BuildSection(string header, System.Collections.Generic.IEnumerable<string> lines, bool expanded = false)
    {
        var body = new StackPanel { Margin = new Thickness(18, 4, 0, 6) };
        foreach (var line in lines)
        {
            body.Children.Add(new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 1),
                Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            });
        }
        return new Expander
        {
            Header = header,
            IsExpanded = expanded,
            Content = body,
            Margin = new Thickness(0, 2, 0, 2),
            FontWeight = FontWeights.SemiBold,
        };
    }
}
