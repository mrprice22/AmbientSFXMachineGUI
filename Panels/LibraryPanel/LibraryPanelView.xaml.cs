using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AmbientSFXMachineGUI.Panels.LibraryPanel;

public partial class LibraryPanelView : UserControl
{
    private GridViewColumnHeader? _lastHeader;
    private ListSortDirection _lastDirection = ListSortDirection.Ascending;

    public LibraryPanelView()
    {
        InitializeComponent();
    }

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header) return;
        if (header.Role == GridViewColumnHeaderRole.Padding) return;
        if (header.Tag is not string sortPath || string.IsNullOrEmpty(sortPath)) return;

        var direction = header == _lastHeader && _lastDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        var view = CollectionViewSource.GetDefaultView(FilesList.ItemsSource);
        if (view is null) return;
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(sortPath, direction));

        _lastHeader = header;
        _lastDirection = direction;
    }
}
