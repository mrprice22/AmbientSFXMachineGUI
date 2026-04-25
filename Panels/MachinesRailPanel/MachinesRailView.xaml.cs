using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AmbientSFXMachineGUI.Models;
using AmbientSFXMachineGUI.Shell;

namespace AmbientSFXMachineGUI.Panels.MachinesRailPanel;

public partial class MachinesRailView : UserControl
{
    private Point _dragStart;
    private MachineViewModel? _dragged;
    private bool _suppressReorderDrag;

    public MachinesRailView() => InitializeComponent();

    private void OnListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart            = e.GetPosition(null);
        _dragged              = null;
        // Don't start a row reorder drag if the press began inside an interactive
        // control like the master-volume Slider or the enable ToggleButton —
        // DoDragDrop would steal mouse capture from the Slider's Thumb mid-drag.
        _suppressReorderDrag  = IsInsideInteractiveControl(e.OriginalSource as DependencyObject);
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (_suppressReorderDrag) return;
        var delta = e.GetPosition(null) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var lb   = (ListBox)sender;
        var item = HitTestItem(lb, e.GetPosition(lb));
        if (item?.DataContext is not MachineViewModel machine) return;

        _dragged = machine;
        DragDrop.DoDragDrop(lb, machine, DragDropEffects.Move);
    }

    private static bool IsInsideInteractiveControl(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is RangeBase or Thumb or ToggleButton) return true;
            source = source is Visual
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return false;
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
