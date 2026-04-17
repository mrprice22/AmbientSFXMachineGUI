using CommunityToolkit.Mvvm.ComponentModel;

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
    [ObservableProperty] private double _volumeOverride = 100; // 0-200
    [ObservableProperty] private int? _cooldownOverrideSeconds;
    [ObservableProperty] private int _playCountThisSession;
    [ObservableProperty] private bool _isFavorite;
}
