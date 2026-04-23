using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using AmbientSFXMachineGUI.Models;
using AmbientSFXMachineGUI.Shell;

namespace AmbientSFXMachineGUI.Panels.AgentPanel;

public partial class AgentPanelView : UserControl
{
    private ShellViewModel? _vm;

    public AgentPanelView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => HookViewModel(DataContext as ShellViewModel);
    private void OnUnloaded(object sender, RoutedEventArgs e) => HookViewModel(null);
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => HookViewModel(DataContext as ShellViewModel);

    private void HookViewModel(ShellViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm)) return;
        if (_vm is not null) _vm.AgentFocusRequested -= OnAgentFocusRequested;
        _vm = vm;
        if (_vm is not null) _vm.AgentFocusRequested += OnAgentFocusRequested;
    }

    private void OnAgentFocusRequested(object? sender, Guid agentId)
    {
        if (_vm is null) return;
        var agent = _vm.Agents.FirstOrDefault(a => a.Id == agentId);
        if (agent is null) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            AgentsList.UpdateLayout();
            if (AgentsList.ItemContainerGenerator.ContainerFromItem(agent) is FrameworkElement container)
            {
                container.BringIntoView();
                Pulse(container);
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void Pulse(FrameworkElement element)
    {
        var anim = new DoubleAnimation
        {
            From = 0.3,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(700),
            AutoReverse = false,
        };
        element.BeginAnimation(OpacityProperty, anim);
    }

    private static string[] GetDropPaths(DragEventArgs e)
        => e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();

    private static bool HasFolder(string[] paths) => paths.Any(Directory.Exists);
    private static bool HasAudioFile(string[] paths) => paths.Any(ShellViewModel.IsAudioFile);

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        var paths = GetDropPaths(e);
        bool hasFolder = HasFolder(paths);
        bool hasAudio = HasAudioFile(paths);

        if (hasFolder)
        {
            DropOverlayText.Text = "Drop folder to add agent";
            DropOverlay.Visibility = Visibility.Visible;
            e.Effects = DragDropEffects.Copy;
        }
        else if (hasAudio)
        {
            // Audio-file drop is handled per-card; panel-level only shows a hint.
            DropOverlayText.Text = "Drop audio files onto an agent card";
            DropOverlay.Visibility = Visibility.Visible;
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var paths = GetDropPaths(e);
        e.Effects = (HasFolder(paths) || HasAudioFile(paths))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (DataContext is not ShellViewModel vm) return;
        var paths = GetDropPaths(e);
        if (paths.Length == 0) return;

        // Panel-level: folders become agents. Raw audio file drops that miss a card are ignored
        // (the spec directs users to drop onto an agent card or the Library panel).
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                vm.ImportAgentFolder(path);
        }
    }

    private void OnAgentCardDragEnter(object sender, DragEventArgs e)
    {
        var paths = GetDropPaths(e);
        if (!HasAudioFile(paths)) return;
        if (sender is FrameworkElement fe) fe.Opacity = 0.7;
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnAgentCardDragOver(object sender, DragEventArgs e)
    {
        var paths = GetDropPaths(e);
        if (!HasAudioFile(paths)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnAgentCardDragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement fe) fe.Opacity = 1.0;
    }

    private void OnAgentCardDrop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement fe) fe.Opacity = 1.0;
        if (sender is not FrameworkElement { DataContext: AgentViewModel agent }) return;
        if (DataContext is not ShellViewModel vm) return;

        var paths = GetDropPaths(e);
        if (!HasAudioFile(paths)) return;

        vm.AddFilesToAgent(agent, paths);
        e.Handled = true;
    }
}
