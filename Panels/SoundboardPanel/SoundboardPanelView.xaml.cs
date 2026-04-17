using System.Windows;
using System.Windows.Controls;

namespace AmbientSFXMachineGUI.Panels.SoundboardPanel;

public partial class SoundboardPanelView : UserControl
{
    public SoundboardPanelView()
    {
        InitializeComponent();
        Drop += OnDrop;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        // TODO: accept LogEntryViewModel drop → create SoundboardItem via SoundboardViewModel.
    }
}
