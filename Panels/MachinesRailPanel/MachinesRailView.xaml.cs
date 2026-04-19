using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AmbientSFXMachineGUI.Models;
using AmbientSFXMachineGUI.Shell;

namespace AmbientSFXMachineGUI.Panels.MachinesRailPanel;

public partial class MachinesRailView : UserControl
{
    private Point _dragStart;
    private MachineViewModel? _dragged;

    public MachinesRailView() => InitializeComponent();

    private void OnListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragged   = null;
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(null) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var lb   = (ListBox)sender;
        var item = HitTestItem(lb, e.GetPosition(lb));
        if (item?.DataContext is not MachineViewModel machine) return;

        _dragged = machine;
        DragDrop.DoDragDrop(lb, machine, DragDropEffects.Move);
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        if (_dragged is null) return;
        var lb     = (ListBox)sender;
        var item   = HitTestItem(lb, e.GetPosition(lb));
        var target = item?.DataContext as MachineViewModel;
        if (target is null || target == _dragged) return;
        if (lb.DataContext is not ShellViewModel vm) return;

        var fromIdx = vm.Machines.IndexOf(_dragged);
        var toIdx   = vm.Machines.IndexOf(target);
        if (fromIdx >= 0 && toIdx >= 0)
            vm.MoveMachine(fromIdx, toIdx);

        _dragged = null;
    }

    private static ListBoxItem? HitTestItem(ListBox lb, Point point)
    {
        var hit = VisualTreeHelper.HitTest(lb, point)?.VisualHit as DependencyObject;
        while (hit is not null)
        {
            if (hit is ListBoxItem lbi) return lbi;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }
}
