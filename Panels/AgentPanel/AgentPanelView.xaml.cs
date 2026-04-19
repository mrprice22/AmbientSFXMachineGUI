using System.IO;
using System.Windows;
using System.Windows.Controls;
using AmbientSFXMachineGUI.Shell;

namespace AmbientSFXMachineGUI.Panels.AgentPanel;

public partial class AgentPanelView : UserControl
{
    public AgentPanelView()
    {
        InitializeComponent();
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
