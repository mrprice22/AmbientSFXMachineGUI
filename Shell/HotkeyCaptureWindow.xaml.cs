using System.Windows;
using System.Windows.Input;
using AmbientSFXMachineGUI.Services;

namespace AmbientSFXMachineGUI.Shell;

public partial class HotkeyCaptureWindow : Window
{
    public string? CapturedCombo { get; private set; }

    public HotkeyCaptureWindow()
    {
        InitializeComponent();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore pure modifier presses — wait for a real key.
        if (key is Key.LeftCtrl or Key.RightCtrl or
                   Key.LeftShift or Key.RightShift or
                   Key.LeftAlt or Key.RightAlt or
                   Key.LWin or Key.RWin)
        {
            return;
        }

        var combo = HotkeyService.FormatCombo(Keyboard.Modifiers, key);
        PreviewText.Text = combo;

        if (!HotkeyService.TryParseCombo(combo, out _, out _))
        {
            PreviewText.Text = combo + "  (invalid)";
            return;
        }

        CapturedCombo = combo;
        DialogResult = true;
        Close();
    }
}
