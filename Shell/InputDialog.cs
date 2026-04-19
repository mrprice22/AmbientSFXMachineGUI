using System.Windows;
using System.Windows.Controls;

namespace AmbientSFXMachineGUI.Shell;

internal sealed class InputDialog : Window
{
    private readonly TextBox _textBox;

    public string Value => _textBox.Text;

    public InputDialog(string title, string prompt, string initial = "")
    {
        Title = title;
        SizeToContent = SizeToContent.Height;
        Width = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _textBox = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 10) };

        var ok = new Button { Content = "OK", IsDefault = true, Width = 72, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 72 };
        ok.Click += (_, _) => { DialogResult = true; Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(_textBox);
        panel.Children.Add(buttons);

        Content = panel;
        Loaded += (_, _) => { _textBox.Focus(); _textBox.SelectAll(); };
    }
}
