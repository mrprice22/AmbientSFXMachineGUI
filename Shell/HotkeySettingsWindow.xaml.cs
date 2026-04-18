using System.Windows;
using System.Windows.Controls;
using AmbientSFXMachineGUI.Services;

namespace AmbientSFXMachineGUI.Shell;

public partial class HotkeySettingsWindow : Window
{
    private readonly HotkeyService _hotkeys;

    public HotkeySettingsWindow(HotkeyService hotkeys)
    {
        InitializeComponent();
        _hotkeys = hotkeys;
        DataContext = hotkeys.Bindings;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnRebindClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: HotkeyBinding binding }) return;

        var capture = new HotkeyCaptureWindow { Owner = this };
        if (capture.ShowDialog() == true)
        {
            _hotkeys.Rebind(binding.ActionId, capture.CapturedCombo);
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: HotkeyBinding binding }) return;
        _hotkeys.Rebind(binding.ActionId, null);
    }
}
