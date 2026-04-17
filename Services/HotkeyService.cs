using System;
using System.Collections.Generic;

namespace AmbientSFXMachineGUI.Services;

public sealed class HotkeyService : IDisposable
{
    private readonly Dictionary<string, Action> _actions = new();
    private readonly Dictionary<string, string> _bindings = new(); // action → key combo

    public event EventHandler<string>? HotkeyTriggered;

    public void LoadDefaults()
    {
        // TODO: read %AppData%\AmbientAgents\hotkeys.json; fall back to built-in defaults.
    }

    public void Register(string action, Action handler) => _actions[action] = handler;

    public void Rebind(string action, string keyCombo)
    {
        // TODO: unregister previous RegisterHotKey, register new combo on hidden message-loop window,
        //       persist binding to hotkeys.json.
        _bindings[action] = keyCombo;
    }

    public string? GetBinding(string action) =>
        _bindings.TryGetValue(action, out var combo) ? combo : null;

    internal void Trigger(string action)
    {
        HotkeyTriggered?.Invoke(this, action);
        if (_actions.TryGetValue(action, out var handler)) handler();
    }

    public void Dispose()
    {
        // TODO: UnregisterHotKey all, tear down message-loop thread.
    }
}
