using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AmbientSFXMachineGUI.Services;

public sealed class AppSettings
{
    public string? debugLogPath { get; set; }
    public string? lastSelectedMachineId { get; set; }

    // MACHINE-11: per-machine collapse state for grouped agents view (key = machineId GUID string).
    public Dictionary<string, bool> agentGroupCollapsed { get; set; } = new();

    private static string SettingsPath => Path.Combine(MachinePaths.Root, "settings.json");

    public static string DefaultDebugLogFolder => Path.Combine(MachinePaths.Root, "logs");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s is not null) return s;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(MachinePaths.Root);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public string GetDebugLogFolderOrDefault()
        => string.IsNullOrWhiteSpace(debugLogPath) ? DefaultDebugLogFolder : debugLogPath!;
}
