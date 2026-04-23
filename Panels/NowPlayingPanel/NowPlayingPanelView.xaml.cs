using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AmbientSFXMachineGUI.Services;
using AmbientSFXMachineGUI.Shell;

namespace AmbientSFXMachineGUI.Panels.NowPlayingPanel;

public partial class NowPlayingPanelView : UserControl
{
    private readonly DispatcherTimer _pollTimer;
    private INotifyCollectionChanged? _tracked;

    public NowPlayingPanelView()
    {
        InitializeComponent();
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _pollTimer.Tick += OnPollTick;

        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += (_, _) => AttachToActivePlaybacks();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachToActivePlaybacks();
        _pollTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _pollTimer.Stop();
        DetachFromActivePlaybacks();
    }

    private void AttachToActivePlaybacks()
    {
        DetachFromActivePlaybacks();
        if (DataContext is ShellViewModel vm)
        {
            _tracked = vm.ActivePlaybacks;
            _tracked.CollectionChanged += OnActivePlaybacksChanged;
        }
    }

    private void DetachFromActivePlaybacks()
    {
        if (_tracked is not null) _tracked.CollectionChanged -= OnActivePlaybacksChanged;
        _tracked = null;
    }

    private void OnActivePlaybacksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Force the Tick to run on the next interval; nothing else to do — the ItemsControl reacts automatically.
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        foreach (var playback in vm.ActivePlaybacks)
            playback.RefreshFromReader();
    }
}
