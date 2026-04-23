using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AmbientSFXMachineGUI.Panels.LogPanel;

public partial class LogPanelView : UserControl
{
    private INotifyCollectionChanged? _hookedSource;

    public LogPanelView()
    {
        InitializeComponent();
        LogList.DataContextChanged += (_, _) => RehookItemsSource();
        Loaded += (_, _) => RehookItemsSource();
        LogList.PreviewMouseRightButtonDown += OnListPreviewMouseRightButtonDown;
    }

    private void OnListPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(LogList, e.OriginalSource as DependencyObject) as ListViewItem;
        if (item is not null) item.IsSelected = true;
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
