using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace AmbientSFXMachineGUI.Shell;

public partial class MiniModeWindow : Window, INotifyPropertyChanged
{
    private readonly ShellViewModel _shell;
    private AppBarHelper? _appBar;
    private int _edgeIndex;

    public MiniModeWindow(ShellViewModel shell)
    {
        _shell = shell;
        DataContext = this;
        InitializeComponent();
        SourceInitialized += (_, _) => _appBar = new AppBarHelper(this);
        Closing += OnClosing;
    }

    public System.Collections.ObjectModel.ObservableCollection<Models.AgentViewModel> Agents => _shell.Agents;

    public double MasterVolume
    {
        get => _shell.MasterVolume;
        set { _shell.MasterVolume = value; OnPropertyChanged(nameof(MasterVolume)); }
    }

    public bool IsMutedAll
    {
        get => _shell.IsMutedAll;
        set { _shell.IsMutedAll = value; OnPropertyChanged(nameof(IsMutedAll)); }
    }

    public int EdgeIndex
    {
        get => _edgeIndex;
        set
        {
            if (_edgeIndex == value) return;
            _edgeIndex = value;
            OnPropertyChanged(nameof(EdgeIndex));
            _appBar?.SetEdge(IndexToEdge(value));
        }
    }

    private static AppBarEdge IndexToEdge(int i) => i switch
    {
        1 => AppBarEdge.Left,
        2 => AppBarEdge.Top,
        3 => AppBarEdge.Right,
        4 => AppBarEdge.Bottom,
        _ => AppBarEdge.Float,
    };

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (_appBar?.Edge == AppBarEdge.Float && e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _appBar?.Unregister();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
