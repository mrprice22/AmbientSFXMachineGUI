using CommunityToolkit.Mvvm.ComponentModel;

namespace AmbientSFXMachineGUI.Models;

public partial class SoundboardItem : ObservableObject
{
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string? _group;
    [ObservableProperty] private string? _hotkey;
    [ObservableProperty] private double _volume = 100;
}
