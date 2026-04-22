using System;
using System.IO;

namespace AmbientSFXMachineGUI.Services;

public static class MachinePaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AmbientAgents");

    public static string MachinesRoot => Path.Combine(Root, "machines");

    public static string MachineDir(Guid machineId) =>
        Path.Combine(MachinesRoot, machineId.ToString());

    public static string ProfilesDir(Guid machineId) =>
        Path.Combine(MachineDir(machineId), "profiles");

    public static string SoundboardPath(Guid machineId) =>
        Path.Combine(MachineDir(machineId), "soundboard.json");

    public static string MachineHotkeysPath(Guid machineId) =>
        Path.Combine(MachineDir(machineId), "hotkeys.json");

    public static string GlobalHotkeysPath => Path.Combine(Root, "hotkeys.json");
}
