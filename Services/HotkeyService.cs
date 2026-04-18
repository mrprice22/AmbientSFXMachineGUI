using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using System.Windows.Input;

namespace AmbientSFXMachineGUI.Services;

public enum HotkeyScope
{
    Global,
    Machine
}

public sealed class HotkeyAction
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public HotkeyScope Scope { get; init; } = HotkeyScope.Global;
    public string? DefaultCombo { get; init; }
}

public sealed class HotkeyBinding : INotifyPropertyChanged
{
    private string? _combo;
    public string ActionId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public HotkeyScope Scope { get; init; } = HotkeyScope.Global;

    public string? Combo
    {
        get => _combo;
        set
        {
            if (_combo == value) return;
            _combo = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Combo)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [Flags]
    private enum ModKey : uint
    {
        None = 0,
        Alt = 0x1,
        Control = 0x2,
        Shift = 0x4,
        Win = 0x8,
        NoRepeat = 0x4000
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private sealed class MessageWindow : NativeWindow
    {
        public event Action<int>? HotkeyPressed;

        public MessageWindow()
        {
            // HWND_MESSAGE = -3 creates a message-only window.
            var cp = new CreateParams { Parent = new IntPtr(-3) };
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                HotkeyPressed?.Invoke(m.WParam.ToInt32());
                return;
            }
            base.WndProc(ref m);
        }
    }

    private readonly Dictionary<string, HotkeyAction> _actions = new();
    private readonly Dictionary<string, Action> _handlers = new();
    private readonly Dictionary<string, string?> _globalBindings = new();
    private readonly Dictionary<string, string?> _machineBindings = new();
    private readonly Dictionary<int, string> _idToAction = new();
    private MessageWindow? _window;
    private int _nextId = 1;
    private Guid? _activeMachine;

    public ObservableCollection<HotkeyBinding> Bindings { get; } = new();

    public event EventHandler<string>? HotkeyTriggered;

    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AmbientAgents", "hotkeys.json");

    public HotkeyService()
    {
        _window = new MessageWindow();
        _window.HotkeyPressed += OnHotkeyPressed;
    }

    public void LoadDefaults()
    {
        RegisterAction(new HotkeyAction { Id = "app.muteAll",      DisplayName = "Mute all",           Scope = HotkeyScope.Global,  DefaultCombo = "Ctrl+Shift+M" });
        RegisterAction(new HotkeyAction { Id = "app.toggleWindow", DisplayName = "Show / hide window", Scope = HotkeyScope.Global,  DefaultCombo = "Ctrl+Shift+W" });
        RegisterAction(new HotkeyAction { Id = "app.nextProfile",  DisplayName = "Next profile",       Scope = HotkeyScope.Global,  DefaultCombo = null });
        RegisterAction(new HotkeyAction { Id = "machine.toggle",   DisplayName = "Toggle active machine", Scope = HotkeyScope.Machine, DefaultCombo = null });
        RegisterAction(new HotkeyAction { Id = "machine.solo",     DisplayName = "Solo active machine",   Scope = HotkeyScope.Machine, DefaultCombo = null });
        RegisterAction(new HotkeyAction { Id = "agent.forcePlay",  DisplayName = "Force-play focused agent", Scope = HotkeyScope.Machine, DefaultCombo = null });
        RegisterAction(new HotkeyAction { Id = "agent.toggle",     DisplayName = "Toggle focused agent",     Scope = HotkeyScope.Machine, DefaultCombo = null });

        LoadFromDisk();
        ApplyAll();
    }

    public void RegisterAction(HotkeyAction action)
    {
        _actions[action.Id] = action;
        if (!_globalBindings.ContainsKey(action.Id))
            _globalBindings[action.Id] = action.DefaultCombo;

        if (Bindings.All(b => b.ActionId != action.Id))
        {
            Bindings.Add(new HotkeyBinding
            {
                ActionId = action.Id,
                DisplayName = action.DisplayName,
                Scope = action.Scope,
                Combo = _globalBindings[action.Id]
            });
        }
    }

    public void Register(string actionId, Action handler) => _handlers[actionId] = handler;

    public IReadOnlyCollection<HotkeyAction> Actions => _actions.Values;

    public string? GetBinding(string actionId)
    {
        if (_activeMachine is not null && _machineBindings.TryGetValue(actionId, out var m) && m is not null)
            return m;
        return _globalBindings.TryGetValue(actionId, out var g) ? g : null;
    }

    public void Rebind(string actionId, string? keyCombo)
    {
        _globalBindings[actionId] = string.IsNullOrWhiteSpace(keyCombo) ? null : keyCombo;

        var binding = Bindings.FirstOrDefault(b => b.ActionId == actionId);
        if (binding != null) binding.Combo = _globalBindings[actionId];

        ApplyAll();
        SaveToDisk();
    }

    public void SetActiveMachineBindings(Guid? machineId, IReadOnlyDictionary<string, string?>? bindings)
    {
        _activeMachine = machineId;
        _machineBindings.Clear();
        if (bindings != null)
        {
            foreach (var kv in bindings) _machineBindings[kv.Key] = kv.Value;
        }
        ApplyAll();
    }

    private void ApplyAll()
    {
        if (_window == null) return;

        foreach (var id in _idToAction.Keys.ToList())
            UnregisterHotKey(_window.Handle, id);
        _idToAction.Clear();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in _actions.Values)
        {
            var combo = GetBinding(action.Id);
            if (string.IsNullOrWhiteSpace(combo)) continue;
            if (!seen.Add(combo)) continue; // duplicate combo — first wins
            if (!TryParseCombo(combo!, out var mods, out var vk)) continue;

            var id = _nextId++;
            if (RegisterHotKey(_window.Handle, id, mods | (uint)ModKey.NoRepeat, vk))
                _idToAction[id] = action.Id;
        }
    }

    private void OnHotkeyPressed(int id)
    {
        if (!_idToAction.TryGetValue(id, out var actionId)) return;
        HotkeyTriggered?.Invoke(this, actionId);
        if (_handlers.TryGetValue(actionId, out var handler))
        {
            try { handler(); } catch { /* swallow handler errors */ }
        }
    }

    public static bool TryParseCombo(string combo, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(combo)) return false;

        ModKey mods = ModKey.None;
        Key? mainKey = null;

        foreach (var raw in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    mods |= ModKey.Control; break;
                case "shift":
                    mods |= ModKey.Shift; break;
                case "alt":
                    mods |= ModKey.Alt; break;
                case "win":
                case "windows":
                    mods |= ModKey.Win; break;
                default:
                    if (!Enum.TryParse<Key>(raw, true, out var k)) return false;
                    mainKey = k;
                    break;
            }
        }

        if (mainKey is null) return false;
        modifiers = (uint)mods;
        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(mainKey.Value);
        return virtualKey != 0;
    }

    public static string FormatCombo(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift))   parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt))     parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private sealed class PersistedBindings
    {
        public Dictionary<string, string?> Global { get; set; } = new();
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var json = File.ReadAllText(ConfigPath);
            var data = JsonSerializer.Deserialize<PersistedBindings>(json);
            if (data?.Global == null) return;

            foreach (var kv in data.Global)
            {
                if (!_actions.ContainsKey(kv.Key)) continue;
                _globalBindings[kv.Key] = string.IsNullOrWhiteSpace(kv.Value) ? null : kv.Value;
                var binding = Bindings.FirstOrDefault(b => b.ActionId == kv.Key);
                if (binding != null) binding.Combo = _globalBindings[kv.Key];
            }
        }
        catch
        {
            // Corrupt file — fall back to defaults.
        }
    }

    private void SaveToDisk()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var data = new PersistedBindings { Global = new Dictionary<string, string?>(_globalBindings) };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Best-effort persistence.
        }
    }

    public void Dispose()
    {
        if (_window != null)
        {
            foreach (var id in _idToAction.Keys.ToList())
                UnregisterHotKey(_window.Handle, id);
            _idToAction.Clear();
            _window.DestroyHandle();
            _window = null;
        }
    }
}
