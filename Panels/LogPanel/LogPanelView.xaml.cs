using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AmbientSFXMachineGUI.Models;

namespace AmbientSFXMachineGUI.Panels.LogPanel;

public partial class LogPanelView : UserControl
{
    private INotifyCollectionChanged? _hookedSource;
    private Point _dragStart;
    private LogEntryViewModel? _dragCandidate;

    public LogPanelView()
    {
        InitializeComponent();
        LogList.DataContextChanged += (_, _) => RehookItemsSource();
        Loaded += (_, _) => RehookItemsSource();
        LogList.PreviewMouseRightButtonDown += OnListPreviewMouseRightButtonDown;
        LogList.PreviewMouseLeftButtonDown += OnListPreviewMouseLeftButtonDown;
        LogList.PreviewMouseMove += OnListPreviewMouseMove;
    }

    private void OnListPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(LogList, e.OriginalSource as DependencyObject) as ListViewItem;
        if (item is not null) item.IsSelected = true;
    }

    private void OnListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(LogList, e.OriginalSource as DependencyObject) as ListViewItem;
        _dragCandidate = item?.DataContext as LogEntryViewModel;
        _dragStart = _dragCandidate is null ? default : e.GetPosition(null);
    }

    private void OnListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(null);
        if (System.Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var data = new DataObject(typeof(LogEntryViewModel), _dragCandidate);
        var entry = _dragCandidate;
        _dragCandidate = null;
        DragDrop.DoDragDrop(LogList, data, DragDropEffects.Copy);
    }

    private void RehookItemsSource()
    {
        if (_hookedSource is not null)
            _hookedSource.CollectionChanged -= OnLogChanged;
        _hookedSource = LogList.ItemsSource as INotifyCollectionChanged;
        if (_hookedSource is not null)
            _hookedSource.CollectionChanged += OnLogChanged;
        ScrollToEnd();
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (LogList.ItemsSource is not IList list || list.Count == 0) return;
        var last = list[list.Count - 1];
        if (last is not null) LogList.ScrollIntoView(last);
    }
}
