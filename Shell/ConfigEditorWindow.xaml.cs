using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using AmbientSFXMachineGUI.Models;

namespace AmbientSFXMachineGUI.Shell;

public partial class ConfigEditorWindow : Window
{
    private readonly AgentViewModel _agent;
    private readonly AgentConfigModel _cfg;

    // Prevents slider ↔ textbox feedback loops during programmatic updates.
    private bool _updating;

    public ConfigEditorWindow(AgentViewModel agent)
    {
        _agent = agent;
        _cfg   = AgentConfigModel.ReadFromDisk(agent.FolderPath);
        InitializeComponent();
        TitleBlock.Text = $"Config — {agent.Name}";
        PopulateForm();
    }

    // ── Positioning & slide-in animation ─────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var main = Application.Current.MainWindow;
        if (main == null) return;

        Top    = main.Top;
        Height = main.ActualHeight;

        double targetLeft = main.Left + main.ActualWidth - ActualWidth;
        Left = targetLeft + ActualWidth; // start fully off-screen to the right

        var anim = new DoubleAnimation(targetLeft, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(LeftProperty, anim);
    }

    // ── Form population ───────────────────────────────────────────────────

    private void PopulateForm()
    {
        _updating = true;

        // Playback
        EnabledCheck.IsChecked = _cfg.Enabled;
        foreach (System.Windows.Controls.ComboBoxItem item in ModeCombo.Items)
        {
            if (item.Content?.ToString() == _cfg.Mode)
            {
                ModeCombo.SelectedItem = item;
                break;
            }
        }
        if (ModeCombo.SelectedItem == null) ModeCombo.SelectedIndex = 0;

        // Volume
        VolumeSlider.Value = _cfg.Volume;
        VolumeBox.Text     = _cfg.Volume.ToString();

        // Timing
        MinMinutesBox.Text     = _cfg.MinMinutes.ToString();
        MaxMinutesBox.Text     = _cfg.MaxMinutes.ToString();
        MinSecondsBox.Text     = _cfg.MinSeconds.ToString();
        MaxSecondsBox.Text     = _cfg.MaxSeconds.ToString();
        OverrideStartupBox.Text = _cfg.OverrideStartupSeconds.ToString();

        // Balance
        BalanceMinSlider.Value  = _cfg.BalanceMin;
        BalanceMinBox.Text      = _cfg.BalanceMin.ToString();
        BalanceMaxSlider.Value  = _cfg.BalanceMax;
        BalanceMaxBox.Text      = _cfg.BalanceMax.ToString();
        InvertChanceSlider.Value = _cfg.BalanceInvertChance;
        InvertChanceBox.Text    = _cfg.BalanceInvertChance.ToString();

        // Turbo
        TurboChanceSlider.Value = _cfg.TurboChance;
        TurboChanceBox.Text     = _cfg.TurboChance.ToString();
        TurboMinFiresBox.Text   = _cfg.TurboMinFires.ToString();
        TurboMaxFiresBox.Text   = _cfg.TurboMaxFires.ToString();

        _updating = false;
    }

    // ── Slider ↔ TextBox synchronisation ─────────────────────────────────

    private void OnVolumeSliderChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        _updating = true;
        VolumeBox.Text = ((int)VolumeSlider.Value).ToString();
        _updating = false;
    }

    private void OnVolumeBoxChanged(object s, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updating) return;
        if (int.TryParse(VolumeBox.Text, out int v) && v >= 0 && v <= 100)
        {
            _updating = true;
            VolumeSlider.Value = v;
            _updating = false;
        }
    }

    private void OnBalanceMinSliderChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        _updating = true;
        BalanceMinBox.Text = ((int)BalanceMinSlider.Value).ToString();
        _updating = false;
    }

    private void OnBalanceMinBoxChanged(object s, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updating) return;
        if (int.TryParse(BalanceMinBox.Text, out int v) && v >= 0 && v <= 100)
        {
            _updating = true;
            BalanceMinSlider.Value = v;
            _updating = false;
        }
    }

    private void OnBalanceMaxSliderChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        _updating = true;
        BalanceMaxBox.Text = ((int)BalanceMaxSlider.Value).ToString();
        _updating = false;
    }

    private void OnBalanceMaxBoxChanged(object s, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updating) return;
        if (int.TryParse(BalanceMaxBox.Text, out int v) && v >= 0 && v <= 100)
        {
            _updating = true;
            BalanceMaxSlider.Value = v;
            _updating = false;
        }
    }

    private void OnInvertChanceSliderChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        _updating = true;
        InvertChanceBox.Text = ((int)InvertChanceSlider.Value).ToString();
        _updating = false;
    }

    private void OnInvertChanceBoxChanged(object s, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updating) return;
        if (int.TryParse(InvertChanceBox.Text, out int v) && v >= 0 && v <= 100)
        {
            _updating = true;
            InvertChanceSlider.Value = v;
            _updating = false;
        }
    }

    private void OnTurboChanceSliderChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        _updating = true;
        TurboChanceBox.Text = ((int)TurboChanceSlider.Value).ToString();
        _updating = false;
    }

    private void OnTurboChanceBoxChanged(object s, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updating) return;
        if (int.TryParse(TurboChanceBox.Text, out int v) && v >= 0 && v <= 100)
        {
            _updating = true;
            TurboChanceSlider.Value = v;
            _updating = false;
        }
    }

    // ── Validation ────────────────────────────────────────────────────────

    private bool TryCollectValues(out string? error)
    {
        error = null;

        if (!int.TryParse(VolumeBox.Text, out int vol) || vol < 0 || vol > 100)
            { error = "Volume must be 0 – 100."; return false; }

        var mode = (ModeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString()?.ToLower();
        if (mode is not ("random" or "sequential" or "shuffle"))
            { error = "Mode must be random, sequential, or shuffle."; return false; }

        if (!int.TryParse(MinMinutesBox.Text, out int minMin) || minMin < 0)
            { error = "Min Minutes must be a non-negative integer."; return false; }
        if (!int.TryParse(MaxMinutesBox.Text, out int maxMin) || maxMin < 0)
            { error = "Max Minutes must be a non-negative integer."; return false; }
        if (minMin > maxMin)
            { error = "Min Minutes must be ≤ Max Minutes."; return false; }

        if (!int.TryParse(MinSecondsBox.Text, out int minSec) || minSec < 0)
            { error = "Min Seconds must be a non-negative integer."; return false; }
        if (!int.TryParse(MaxSecondsBox.Text, out int maxSec) || maxSec < 0)
            { error = "Max Seconds must be a non-negative integer."; return false; }
        if (minSec > maxSec && minMin == maxMin)
            { error = "Min Seconds must be ≤ Max Seconds when minutes are equal."; return false; }

        if (!int.TryParse(OverrideStartupBox.Text, out int startup) || startup < 0)
            { error = "Override Startup Seconds must be a non-negative integer."; return false; }

        int balMin = (int)BalanceMinSlider.Value;
        int balMax = (int)BalanceMaxSlider.Value;
        if (balMin > balMax)
            { error = "Balance Min must be ≤ Balance Max."; return false; }

        if (!int.TryParse(TurboMinFiresBox.Text, out int turboMin) || turboMin < 1)
            { error = "Turbo Min Fires must be ≥ 1."; return false; }
        if (!int.TryParse(TurboMaxFiresBox.Text, out int turboMax) || turboMax < 1)
            { error = "Turbo Max Fires must be ≥ 1."; return false; }
        if (turboMin > turboMax)
            { error = "Turbo Min Fires must be ≤ Turbo Max Fires."; return false; }

        // All valid — write into model
        _cfg.Enabled                = EnabledCheck.IsChecked == true;
        _cfg.Volume                 = vol;
        _cfg.Mode                   = mode!;
        _cfg.MinMinutes             = minMin;
        _cfg.MaxMinutes             = maxMin;
        _cfg.MinSeconds             = minSec;
        _cfg.MaxSeconds             = maxSec;
        _cfg.OverrideStartupSeconds = startup;
        _cfg.BalanceMin             = balMin;
        _cfg.BalanceMax             = balMax;
        _cfg.BalanceInvertChance    = (int)InvertChanceSlider.Value;
        _cfg.TurboChance            = (int)TurboChanceSlider.Value;
        _cfg.TurboMinFires          = turboMin;
        _cfg.TurboMaxFires          = turboMax;
        return true;
    }

    // ── Button handlers ───────────────────────────────────────────────────

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryCollectValues(out string? error))
        {
            ErrorText.Text      = error;
            ErrorBar.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            _cfg.WriteToDisk(_agent.FolderPath);
        }
        catch (Exception ex)
        {
            ErrorText.Text      = $"Could not write config: {ex.Message}";
            ErrorBar.Visibility = Visibility.Visible;
            return;
        }

        // Hot-apply the fields AgentViewModel exposes at runtime.
        // AgentCoordinator reacts to PropertyChanged for IsEnabled and Volume automatically.
        _agent.IsEnabled = _cfg.Enabled;
        _agent.Volume    = _cfg.Volume;
        _agent.Mode      = _cfg.Mode;

        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
