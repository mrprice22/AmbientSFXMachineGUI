using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AmbientSFXMachineGUI.Models;

namespace AmbientSFXMachineGUI.Services;

public static class MachineImporter
{
    public static MachineViewModel Import(string machineFolder, MachineCoordinator coordinator)
    {
        string name     = Path.GetFileName(machineFolder);
        string iconPath = string.Empty;

        var appSettingsPath = Path.Combine(machineFolder, "appSettings.config");
        if (File.Exists(appSettingsPath))
        {
            var dict = ReadKeyValue(appSettingsPath);
            if (dict.TryGetValue("appname", out var n) && !string.IsNullOrWhiteSpace(n))
                name = n;
            if (dict.TryGetValue("icon", out var ico))
            {
                var full = Path.Combine(machineFolder, ico.Trim());
                if (File.Exists(full)) iconPath = full;
            }
        }

        var machine = coordinator.CreateMachine(name, machineFolder);
        machine.IconPath = iconPath;
        coordinator.ScanAgentsFromDisk(machine);
        return machine;
    }

    private static Dictionary<string, string> ReadKeyValue(string path)
        => File.ReadAllLines(path)
               .Select(l => l.Trim())
               .Where(l => l.Length > 0 && !l.StartsWith("#"))
               .Select(l => l.Split('=', 2))
               .Where(kv => kv.Length == 2)
               .ToDictionary(kv => kv[0].Trim(), kv => kv[1].Trim(),
                             StringComparer.OrdinalIgnoreCase);
}
