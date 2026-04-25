using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AmbientSFXMachineGUI.Models;

namespace AmbientSFXMachineGUI.Services;

public sealed record MachineImportResult(
    bool Success,
    MachineViewModel? Machine,
    string? ErrorTitle,
    string? ErrorMessage,
    bool Reused);

public static class MachineImporter
{
    public static MachineImportResult TryImport(string machineFolder, MachineCoordinator coordinator)
    {
        if (string.IsNullOrWhiteSpace(machineFolder))
            return Reject("No folder selected",
                "No folder was selected for import.",
                coordinator, machineFolder);

        if (!Directory.Exists(machineFolder))
            return Reject("Folder not found",
                $"The folder '{machineFolder}' does not exist.",
                coordinator, machineFolder);

        var fullPath  = Path.GetFullPath(machineFolder);
        var leafName  = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var appCfg    = Path.Combine(fullPath, "appSettings.config");
        var sndDir    = Path.Combine(fullPath, "snd");
        var hasAppCfg = File.Exists(appCfg);
        var hasSnd    = Directory.Exists(sndDir);

        // Common mistake: user picked the snd/ folder itself.
        if (string.Equals(leafName, "snd", StringComparison.OrdinalIgnoreCase) ||
            (!hasAppCfg && LooksLikeSndFolder(fullPath)))
        {
            var parent = Path.GetDirectoryName(fullPath);
            var hint   = string.IsNullOrEmpty(parent)
                ? "Select the parent machine folder instead."
                : $"Select the parent folder '{parent}' instead.";
            return Reject("Not a machine folder",
                $"'{fullPath}' looks like the 'snd' subfolder of a machine. {hint}",
                coordinator, machineFolder);
        }

        if (!hasAppCfg)
            return Reject("Not a machine folder",
                $"'{fullPath}' is missing 'appSettings.config'. A machine folder must contain appSettings.config and a 'snd' subfolder.",
                coordinator, machineFolder);

        if (!hasSnd)
            return Reject("Not a machine folder",
                $"'{fullPath}' is missing the 'snd' subfolder. A machine folder must contain agent subfolders under snd/.",
                coordinator, machineFolder);

        if (Directory.GetDirectories(sndDir).Length == 0)
            return Reject("Empty machine",
                $"'{sndDir}' contains no agent subfolders. Add at least one agent folder before importing.",
                coordinator, machineFolder);

        Dictionary<string, string> dict;
        try { dict = ReadKeyValue(appCfg); }
        catch (Exception ex)
        {
            return Reject("Cannot read appSettings.config",
                $"Failed to read '{appCfg}': {ex.Message}",
                coordinator, machineFolder);
        }

        // Idempotent re-import: match on RootPath (case-insensitive full path).
        var existing = coordinator.Machines.FirstOrDefault(m =>
            !string.IsNullOrEmpty(m.RootPath) &&
            string.Equals(Path.GetFullPath(m.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                          fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                          StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            coordinator.LogDebug(DebugLogCategory.User, "MachineImporter",
                $"Re-import matched existing machine '{existing.Name}' at {fullPath} — no duplicate created.");
            return new MachineImportResult(true, existing, null, null, Reused: true);
        }

        var name     = leafName;
        var iconPath = string.Empty;
        if (dict.TryGetValue("appname", out var n) && !string.IsNullOrWhiteSpace(n))
            name = n;
        if (dict.TryGetValue("icon", out var ico) && !string.IsNullOrWhiteSpace(ico))
        {
            var full = Path.Combine(fullPath, ico.Trim());
            if (File.Exists(full)) iconPath = full;
        }

        var machine = coordinator.CreateMachine(name, fullPath);
        machine.IconPath = iconPath;
        coordinator.ScanAgentsFromDisk(machine);
        coordinator.LogDebug(DebugLogCategory.User, "MachineImporter",
            $"Imported machine '{machine.Name}' from {fullPath} ({machine.Agents.Count} agents)");
        return new MachineImportResult(true, machine, null, null, Reused: false);
    }

    /// <summary>Back-compat shim for callers that still expect the throwing API.</summary>
    public static MachineViewModel Import(string machineFolder, MachineCoordinator coordinator)
    {
        var result = TryImport(machineFolder, coordinator);
        if (!result.Success || result.Machine is null)
            throw new InvalidOperationException(result.ErrorMessage ?? "Machine import failed.");
        return result.Machine;
    }

    private static bool LooksLikeSndFolder(string folder)
    {
        // Heuristic: folder lacks appSettings.config but contains subfolders that themselves contain .config files.
        try
        {
            var subs = Directory.GetDirectories(folder);
            if (subs.Length == 0) return false;
            return subs.Any(d => Directory.EnumerateFiles(d, "*.config").Any());
        }
        catch { return false; }
    }

    private static MachineImportResult Reject(string title, string message, MachineCoordinator coordinator, string folder)
    {
        coordinator.LogDebug(DebugLogCategory.Error, "MachineImporter",
            $"Rejected '{folder}': {message}");
        return new MachineImportResult(false, null, title, message, Reused: false);
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
