using System.Windows.Controls;

namespace AmbientSFXMachineGUI.Panels.NowPlayingPanel;

public partial class NowPlayingPanelView : UserControl
{
    public NowPlayingPanelView()
    {
        InitializeComponent();
        // TODO: 100ms DispatcherTimer polls AudioFileReader.Position for each ActivePlayback.
    }
}
