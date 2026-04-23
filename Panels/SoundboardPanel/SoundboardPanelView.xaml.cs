using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AmbientSFXMachineGUI.Models;
using AmbientSFXMachineGUI.Shell;

namespace AmbientSFXMachineGUI.Panels.SoundboardPanel;

public partial class SoundboardPanelView : UserControl
{
    private Point _dragStart;
    private SoundboardItem? _pendingDragItem;

    public SoundboardPanelView()
    {
        InitializeComponent();
        DragEnter += OnPanelDragOver;
        DragOver  += OnPanelDragOver;
        Drop      += OnPanelDrop;
    }

    private ShellViewModel? Vm => DataContext as ShellViewModel;

    private static void OnPanelDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(LogEntryViewModel))
            ? DragDropEffects.Copy
            : e.Data.GetDataPresent(typeof(SoundboardItem))
                ? DragDropEffects.Move
                : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPanelDrop(object sender, DragEventArgs e)
    {
        if (Vm is null) { e.Handled = true; return; }

        if (e.Data.GetData(typeof(LogEntryViewModel)) is LogEntryViewModel entry)
        {
            if (Vm.AddLogEntryToSoundboardCommand.CanExecute(entry))
                Vm.AddLogEntryToSoundboardCommand.Execute(entry);
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(typeof(SoundboardItem)) is SoundboardItem src)
        {
            Vm.MoveSoundboardItem(src, null);
            e.Handled = true;
        }
    }

    private void OnItemPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.IsEditMode != true) return;
        if (sender is not FrameworkElement fe || fe.Tag is not SoundboardItem item) return;
        _dragStart = e.GetPosition(this);
        _pendingDragItem = item;
    }

    private void OnItemPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pendingDragItem is null) return;
        if (Vm?.IsEditMode != true) { _pendingDragItem = null; return; }
        if (e.LeftButton != MouseButtonState.Pressed) { _pendingDragItem = null; return; }

        var now = e.GetPosition(this);
        if (System.Math.Abs(now.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
         && System.Math.Abs(now.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var dragging = _pendingDragItem;
        _pendingDragItem = null;
        if (sender is FrameworkElement fe)
            DragDrop.DoDragDrop(fe, new DataObject(typeof(SoundboardItem), dragging), DragDropEffects.Move);
    }

    private void OnItemDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(SoundboardItem)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnItemDrop(object sender, DragEventArgs e)
    {
        if (Vm is null) return;
        if (e.Data.GetData(typeof(SoundboardItem)) is not SoundboardItem src) return;
        if (sender is not FrameworkElement fe || fe.Tag is not SoundboardItem target) return;
        Vm.MoveSoundboardItem(src, target);
        e.Handled = true;
    }

    private void OnDividerMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        OnItemDoubleClick(sender, e);
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.IsEditMode != true) return;
        if (sender is not FrameworkElement fe) return;
        var item = fe.DataContext as SoundboardItem ?? (fe.Tag as SoundboardItem);
        if (item is null) return;
        if (Vm.RenameSoundboardItemCommand.CanExecute(item))
            Vm.RenameSoundboardItemCommand.Execute(item);
        e.Handled = true;
    }
}
