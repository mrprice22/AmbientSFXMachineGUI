using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AmbientSFXMachineGUI.Models;

public class AgentConfigModel
{
    public bool   Enabled                { get; set; } = true;
    public int    Volume                 { get; set; } = 100;
    public string Mode                   { get; set; } = "random";
    public int    MinMinutes             { get; set; } = 3;
    public int    MaxMinutes             { get; set; } = 6;
    public int    MinSeconds             { get; set; } = 0;
    public int    MaxSeconds             { get; set; } = 0;
    public int    OverrideStartupSeconds { get; set; } = 0;
    public int    BalanceMin             { get; set; } = 50;
    public int    BalanceMax             { get; set; } = 50;
    public int    BalanceInvertChance    { get; set; } = 0;
    public int    TurboChance            { get; set; } = 0;
    public int    TurboMinFires          { get; set; } = 2;
    public int    TurboMaxFires          { get; set; } = 5;

    public static AgentConfigModel ReadFromDisk(string folderPath)
    {
        var configPath = Directory.GetFiles(folderPath, "*.config").FirstOrDefault();
        if (configPath == null) return new AgentConfigModel();

        var dict = File.ReadAllLines(configPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#"))
            .Select(line => line.Split('=', 2))
            .Where(kv => kv.Length == 2)
            .ToDictionary(kv => kv[0].Trim().ToLower(), kv => kv[1].Trim(),
                          StringComparer.OrdinalIgnoreCase);

        return new AgentConfigModel
        {
            Enabled                = GetBool(dict,   "enabled",                 true),
            Volume                 = Math.Clamp(GetInt(dict, "volume",          100), 0, 100),
            Mode                   = dict.TryGetValue("mode", out var m) ? m.ToLower() : "random",
            MinMinutes             = GetInt(dict,     "min_minutes",             3),
            MaxMinutes             = GetInt(dict,     "max_minutes",             6),
            MinSeconds             = GetInt(dict,     "min_seconds",             0),
            MaxSeconds             = GetInt(dict,     "max_seconds",             0),
            OverrideStartupSeconds = GetInt(dict,     "override_startup_seconds",0),
            BalanceMin             = GetInt(dict,     "balance_min",             50),
            BalanceMax             = GetInt(dict,     "balance_max",             50),
            BalanceInvertChance    = GetInt(dict,     "balance_invert_chance",   0),
            TurboChance            = GetInt(dict,     "turbo_chance",            0),
            TurboMinFires          = GetInt(dict,     "turbo_min_fires",         2),
            TurboMaxFires          = GetInt(dict,     "turbo_max_fires",         5),
        };
    }

    public void WriteToDisk(string folderPath)
    {
        var configPath = Directory.GetFiles(folderPath, "*.config").FirstOrDefault()
                      ?? Path.Combine(folderPath, Path.GetFileName(folderPath) + ".config");

        File.WriteAllLines(configPath, new[]
        {
            $"enabled={Enabled.ToString().ToLower()}",
            $"volume={Volume}",
            $"mode={Mode}",
            $"min_minutes={MinMinutes}",
            $"max_minutes={MaxMinutes}",
            $"min_seconds={MinSeconds}",
            $"max_seconds={MaxSeconds}",
            $"override_startup_seconds={OverrideStartupSeconds}",
            $"balance_min={BalanceMin}",
            $"balance_max={BalanceMax}",
            $"balance_invert_chance={BalanceInvertChance}",
            $"turbo_chance={TurboChance}",
            $"turbo_min_fires={TurboMinFires}",
            $"turbo_max_fires={TurboMaxFires}",
        });
    }

    private static int GetInt(Dictionary<string, string> d, string key, int def)
        => d.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : def;

    private static bool GetBool(Dictionary<string, string> d, string key, bool def)
    {
        if (!d.TryGetValue(key, out var v)) return def;
        return bool.TryParse(v, out var b) ? b : def;
    }
}
