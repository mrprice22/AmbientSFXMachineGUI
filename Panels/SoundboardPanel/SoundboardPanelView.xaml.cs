using System.Windows;
using System.Windows.Controls;
using AmbientSFXMachineGUI.Models;
using AmbientSFXMachineGUI.Shell;

namespace AmbientSFXMachineGUI.Panels.SoundboardPanel;

public partial class SoundboardPanelView : UserControl
{
    public SoundboardPanelView()
    {
        InitializeComponent();
        DragEnter += OnDragOver;
        DragOver  += OnDragOver;
        Drop      += OnDrop;
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(LogEntryViewModel))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(LogEntryViewModel)) is not LogEntryViewModel entry) return;
        if (DataContext is not ShellViewModel vm) return;
        if (vm.AddLogEntryToSoundboardCommand.CanExecute(entry))
            vm.AddLogEntryToSoundboardCommand.Execute(entry);
        e.Handled = true;
    }
}
