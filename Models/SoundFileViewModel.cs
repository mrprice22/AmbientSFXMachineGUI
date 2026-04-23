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

    [ObservableProperty] private int? _cooldownOverrideSeconds;
    [ObservableProperty] private int _playCountThisSession;
    [ObservableProperty] private bool _isFavorite;

    public bool IsVolumeOverridden => System.Math.Abs(VolumeOverride - 100) > 0.5;

    [RelayCommand]
    private void ResetVolumeOverride() => VolumeOverride = 100;
}
