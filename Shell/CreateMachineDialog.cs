using System.Windows;
using System.Windows.Controls;

namespace AmbientSFXMachineGUI.Shell;

internal sealed class CreateMachineDialog : Window
{
    private readonly TextBox _nameBox;
    private readonly TextBox _iconBox;
    private readonly TextBox _rootBox;

    public string MachineName => _nameBox.Text.Trim();
    public string IconPath    => _iconBox.Text.Trim();
    public string RootPath    => _rootBox.Text.Trim();

    public CreateMachineDialog()
    {
        Title = "Create Machine";
        SizeToContent = SizeToContent.Height;
        Width = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _nameBox = new TextBox();
        _iconBox = new TextBox();
        _rootBox = new TextBox();

        var ok     = new Button { Content = "Create", IsDefault = true, Width = 76, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel",  IsCancel  = true, Width = 76 };
        ok.Click += OnCreate;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(MakeLabel("Name:"));
        panel.Children.Add(_nameBox);
        panel.Children.Add(MakeLabel("Icon file (optional):"));
        panel.Children.Add(MakeRow(_iconBox, MakeButton("…", BrowseIcon)));
        panel.Children.Add(MakeLabel("Root folder:"));
        panel.Children.Add(MakeRow(_rootBox, MakeButton("…", BrowseRoot)));
        panel.Children.Add(buttons);

        Content = panel;
        Loaded += (_, _) => _nameBox.Focus();
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show("Name is required.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(_rootBox.Text))
        {
            MessageBox.Show("Root folder is required.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
        Close();
    }

    private void BrowseIcon(object sender, RoutedEventArgs e)
    {
        using var ofd = new System.Windows.Forms.OpenFileDialog
            { Title = "Select icon", Filter = "Images|*.ico;*.png|All files|*.*" };
        if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            _iconBox.Text = ofd.FileName;
    }

    private void BrowseRoot(object sender, RoutedEventArgs e)
    {
        using var fbd = new System.Windows.Forms.FolderBrowserDialog
            { UseDescriptionForTitle = true, Description = "Select or create the machine root folder" };
        if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            _rootBox.Text = fbd.SelectedPath;
    }

    private static TextBlock MakeLabel(string text)
        => new() { Text = text, Margin = new Thickness(0, 8, 0, 3) };

    private static Button MakeButton(string content, RoutedEventHandler click)
    {
        var btn = new Button
            { Content = content, Width = 28, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        btn.Click += click;
        return btn;
    }

    private static DockPanel MakeRow(TextBox box, Button browse)
    {
        var row = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(box);
        return row;
    }
}
