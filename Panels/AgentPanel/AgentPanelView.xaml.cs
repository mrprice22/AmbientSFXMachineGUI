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

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
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
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
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
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        if (DataContext is not ShellViewModel vm) return;

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                vm.ImportAgentFolder(path);
        }
    }
}
