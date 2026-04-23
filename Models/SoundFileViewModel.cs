using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AmbientSFXMachineGUI.Models;

public partial class SoundFileViewModel : ObservableObject
{
    public string FilePath { get; }
    public string FileName { get; }

    public SoundFileViewModel(string filePath)
    {
        FilePath = filePath;
        FileName = System.IO.Path.GetFileName(filePath);
    }

    [ObservableProperty] private bool _isEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVolumeOverridden))]
    private double _volumeOverride = 100; // 0-200

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCooldownOverridden))]
    [NotifyPropertyChangedFor(nameof(CooldownOverrideText))]
    private int? _cooldownOverrideSeconds;

    [ObservableProperty] private int _playCountThisSession;
    [ObservableProperty] private bool _isFavorite;

    public bool IsVolumeOverridden => System.Math.Abs(VolumeOverride - 100) > 0.5;
    public bool IsCooldownOverridden => CooldownOverrideSeconds is int s && s > 0;

    public string CooldownOverrideText
    {
        get => CooldownOverrideSeconds?.ToString() ?? string.Empty;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) { CooldownOverrideSeconds = null; return; }
            if (int.TryParse(value, out var n) && n >= 0) CooldownOverrideSeconds = n == 0 ? null : n;
        }
    }

    [RelayCommand]
    private void ResetVolumeOverride() => VolumeOverride = 100;

    [RelayCommand]
    private void ResetCooldownOverride() => CooldownOverrideSeconds = null;
}
