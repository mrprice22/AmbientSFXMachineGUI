using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AmbientSFXMachineGUI.Shell;

namespace AmbientSFXMachineGUI.Panels.LibraryPanel;

public partial class LibraryPanelView : UserControl
{
    private GridViewColumnHeader? _lastHeader;
    private ListSortDirection _lastDirection = ListSortDirection.Ascending;

    public LibraryPanelView()
    {
        InitializeComponent();
    }

    private static string[] GetDropPaths(DragEventArgs e)
        => e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        var paths = GetDropPaths(e);
        if (paths.Any(ShellViewModel.IsAudioFile))
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
        var paths = GetDropPaths(e);
        e.Effects = paths.Any(ShellViewModel.IsAudioFile) ? DragDropEffects.Copy : DragDropEffects.None;
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
        vm.RegisterLibraryFiles(paths);
        e.Handled = true;
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
