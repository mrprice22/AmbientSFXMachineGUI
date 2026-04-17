# AmbientAgents UI — Design Document

## 1. Overview

AmbientAgents UI is a graphical frontend for the existing AmbientAgents console application. The backend C# engine (`SoundAgent`, `Program`) continues to run unchanged as the audio runtime. The UI layer wraps it, communicates with it, and provides real-time control, monitoring, and configuration.

---

## 2. Technology Decision

### Recommendation: WPF (.NET 8, C#) with a custom MVVM shell

**Rationale:**

- The existing codebase is C# (.NET). A WPF frontend shares the same process, same types, and the same NAudio instances — no IPC layer, no serialization overhead, no port management.
- WPF supports true dockable/floating panel layouts via third-party docking libraries (AvalonDock / Xceed Docking is MIT-licensed).
- System tray, global hotkeys (`RegisterHotKey` via P/Invoke), and native file drag-and-drop are all first-class in WPF/Win32.
- NAudio's `WaveOutEvent` and playback events can be observed directly from the same thread — no bridging needed for the now-playing panel or waveform progress.
- WPF's `DataTemplate` + `INotifyPropertyChanged` MVVM pattern is well-suited to the reactive, live-updating nature of agent cards, the playback log, and the now-playing panel.

**Alternatives considered and ruled out:**

| Option | Reason rejected |
|---|---|
| WinForms | No clean MVVM, poor layout flexibility, ugly by default |
| MAUI | Overkill cross-platform overhead; no benefit for a Windows-only audio tool |
| Electron / web UI | Separate process required; IPC complexity; no direct NAudio access |
| Avalonia | Viable, but smaller ecosystem and less mature docking support in 2025 |

**Key NuGet dependencies:**

- `NAudio` — already in use
- `AvalonDock` (Xceed) — dockable panel shell
- `CommunityToolkit.Mvvm` — source-generated MVVM boilerplate
- `System.Text.Json` — profile serialization
- `GlobalHotKey` or raw `RegisterHotKey` P/Invoke — global hotkeys

---

## 3. Architecture

```
AmbientAgents.sln
├── AmbientAgents.Core          (existing engine — unchanged)
│   ├── SoundAgent.cs
│   └── Program.cs (entry point replaced by App.xaml.cs)
├── AmbientAgents.UI            (new WPF project)
│   ├── App.xaml / App.xaml.cs
│   ├── Shell/
│   │   ├── MainWindow.xaml     (AvalonDock host)
│   │   └── ShellViewModel.cs
│   ├── Panels/
│   │   ├── AgentPanel/
│   │   ├── LogPanel/
│   │   ├── NowPlayingPanel/
│   │   └── SoundboardPanel/
│   ├── Services/
│   │   ├── AgentCoordinator.cs (owns agent instances, exposes ObservableCollections)
│   │   ├── ProfileService.cs
│   │   ├── HotkeyService.cs
│   │   └── TrayService.cs
│   └── Models/
│       ├── AgentViewModel.cs
│       ├── SoundFileViewModel.cs
│       ├── LogEntryViewModel.cs
│       ├── Profile.cs
│       └── SoundboardItem.cs
```

`SoundAgent` is made observable by wrapping it in `AgentViewModel`, which subscribes to events surfaced from the core and exposes `INotifyPropertyChanged` properties consumed by the UI.

---

## 4. Panel Layout System

The shell uses **AvalonDock** to provide:

- Dockable, floating, and auto-hidden panels
- Drag-to-reposition between dock zones (left, right, bottom, center tabbed)
- Collapsible via the auto-hide pinning metaphor
- Layout persisted to XML on shutdown and restored on startup

**Default layout:**

```
┌────────────────────────────────────────────────────────┐
│  Toolbar: Master Vol | Mute All | Profile Picker | ⚙   │
├──────────────────┬─────────────────────────────────────┤
│                  │                                     │
│   Agent Cards    │         Live Playback Log           │
│   (scrollable)   │         (auto-scroll)               │
│                  │                                     │
├──────────────────┴─────────────────────────────────────┤
│  Now Playing (horizontal strip, collapses to 1 line)   │
├────────────────────────────────────────────────────────┤
│  Soundboard / Favorites (collapsible, dockable)        │
└────────────────────────────────────────────────────────┘
```

All panels are independently resizable, detachable to floating windows, and can be tabbed together.

---

## 5. Epics and Features

### 5.1 Core Shell

| Story | Notes |
|---|---|
| SHELL-01: Dockable panel layout | AvalonDock host in MainWindow |
| SHELL-02: System tray | `NotifyIcon` via WPF tray helper; context menu for mute/show/exit |
| SHELL-03: Mini-mode strip | Separate minimal `Window` that docks to screen edge using `AppBar` Win32 API |
| SHELL-04: Configurable hotkeys | `HotkeyService` wraps `RegisterHotKey`; bindings UI in Settings flyout |

### 5.2 Agent Management

Each agent is represented by an **AgentCard** control bound to `AgentViewModel`.

**AgentViewModel exposes:**
- `Name`, `Mode`, `FileCount`, `IsEnabled`, `Volume`
- `LastPlayedFile`, `NextPlayIn` (countdown, updated every second via `DispatcherTimer`)
- `IsInTurboMode`, `RemainingTurboPlays`
- `LastPanValue` (for balance indicator)
- `Files` — `ObservableCollection<SoundFileViewModel>`

**AgentCard UI elements:**
- Toggle switch → `IsEnabled`
- Volume slider → `Volume` (two-way binding, updates `SoundAgent` live)
- L/R balance arc indicator → `LastPanValue`
- Countdown label → `NextPlayIn`
- Turbo badge (animated pulse ring) → visible when `IsInTurboMode`
- Force-play button → calls `AgentCoordinator.ForcePlay(agent)`
- Expand/collapse chevron → reveals file list (`SFX-01`, `SFX-02`, `SFX-03`)
- Config edit button → opens config editor sheet

**Config Editor:**
A slide-over sheet (not a modal) with form fields for all `.config` values. Dual-handle `RangeSlider` for min/max intervals. On save, writes back to the `.config` file and hot-applies values to the running `SoundAgent`.

### 5.3 Sound File Management

Each `SoundFileViewModel` exposes:
- `FileName`, `IsEnabled`, `VolumeOverride` (0–200%), `CooldownOverride`, `PlayCountThisSession`

The expanded file list inside an AgentCard shows each file as a row with:
- Enable/disable checkbox
- Volume override slider (defaults hidden, appears on hover)
- Cooldown override field
- Session play count heatmap dot (color intensity scales with count)
- Star button → adds to soundboard

A global **search bar** in the toolbar filters all agents' file lists simultaneously by fuzzy name match.

### 5.4 Live Playback Log

`LogPanel` contains a virtualised `ListView` bound to `ObservableCollection<LogEntryViewModel>` (capped at 500 entries, oldest pruned).

Each `LogEntryViewModel`:
- `Timestamp`, `AgentName`, `FileName`, `FilePath`

**Right-click context menu** on any row:
- Add to Soundboard
- Disable this file
- Set volume override (opens inline numeric input)
- Open containing folder

**Drag from log to soundboard:** Log rows are drag sources. Soundboard panel is a drop target. Drop creates a `SoundboardItem`.

**Export:** Toolbar button serialises the current log to a `.csv` with timestamp, agent, and file columns.

### 5.5 Now Playing Panel

A collapsible horizontal strip docked at the bottom. When expanded:

Each active sound shows:
- Agent name + file name
- Playback progress bar (driven by polling `AudioFileReader.Position` on a 100 ms timer)
- Inline volume knob (rotary `Slider` style)
- Solo button (mutes all other agents; auto-unmutes on playback end)

When collapsed: shows just a single line "N sounds playing" with master volume.

### 5.6 Soundboard / Favorites

Grid of `SoundboardButton` controls. Clicking triggers immediate `NAudio` playback independent of agent scheduling.

**Layout editor mode:** Toggle via toolbar. In edit mode:
- Buttons become drag-reorderable
- Double-click to rename
- Right-click to delete or move to a different group
- `+` button adds a labeled section divider

**Hotkey assignment:** Right-click any button → "Assign hotkey" → press key combo → stored in `HotkeyService` and the active profile.

### 5.7 Profiles

A `Profile` is a JSON file containing:

```json
{
  "name": "Tavern Night",
  "agents": [
    { "name": "fire", "enabled": true, "volume": 80 },
    { "name": "crowd", "enabled": true, "volume": 60 }
  ],
  "soundOverrides": [
    { "path": "snd/fire/crackling_loud.wav", "enabled": false, "volumeOverride": 100 }
  ],
  "soundboard": [ ... ],
  "hotkeys": { ... },
  "pinnedAgents": ["rain"]
}
```

**ProfileService** handles load/save/list. Profiles stored in `%AppData%\AmbientAgents\profiles\`.

**Profile picker:** Dropdown in the main toolbar. Selecting a profile triggers:
1. Diff view (collapsible summary of changes)
2. Confirm → apply; Cancel → revert

**Quick-save:** Floppy/snapshot icon next to picker → prompts for name → saves immediately.

**Pinned agents:** Agents marked as pinned are skipped during profile application. Their current state is preserved across swaps.

**Profile audition:** Hold-to-preview button applies a profile's settings temporarily. Release or timeout (configurable, default 30 s) reverts to previous state.

---

## 6. State Management

All runtime state lives in `AgentCoordinator` (singleton service):
- `ObservableCollection<AgentViewModel> Agents`
- `LogEntryViewModel` events published via a simple event bus
- Active playback tracked via a `ConcurrentDictionary<Guid, ActivePlayback>`

UI consumes state via standard WPF data binding. No additional state management library needed.

Profile application is a transaction: `ProfileService.Apply(profile)` iterates agents, applies values, then raises `ProfileChanged` event so all ViewModels refresh.

---

## 7. Hotkey Architecture

`HotkeyService` registers system-wide hotkeys via `RegisterHotKey` Win32 on a dedicated message-loop thread. On trigger:
- Dispatches to `AgentCoordinator` or `SoundboardService` on the UI thread via `Dispatcher.Invoke`
- Bindings are stored in the active profile and in `%AppData%\AmbientAgents\hotkeys.json` as a fallback default

Rebinding flow: Settings UI captures `KeyDown`, validates no conflicts, calls `HotkeyService.Rebind(action, keys)`.

---

## 8. Persistence

| Data | Location | Format |
|---|---|---|
| Agent configs | `snd/<agent>/*.config` | Existing key=value (unchanged) |
| Profiles | `%AppData%\AmbientAgents\profiles\` | JSON |
| Default hotkeys | `%AppData%\AmbientAgents\hotkeys.json` | JSON |
| Panel layout | `%AppData%\AmbientAgents\layout.xml` | AvalonDock XML |
| Sound overrides | Inside profile JSON | — |
| Soundboard | Inside profile JSON | — |

---

## 9. Phased Delivery

| Phase | Stories | Goal |
|---|---|---|
| **1 — Foundation** | SHELL-01, SHELL-02, AGENT-01–04, LOG-01 | Usable replacement for console; agent cards, log, tray |
| **2 — Control** | AGENT-05–09, SFX-01–02, LOG-02, NOW-01–02, PROF-01–03 | Per-sound control, now-playing, basic profiles |
| **3 — Soundboard** | SB-01–04, LOG-03, SHELL-04, SFX-04 | Favorites, hotkeys, drag-drop |
| **4 — Polish** | SHELL-03, SFX-03–05, NOW-03, PROF-04–05, LOG-04, AGENT-10–11 | Mini-mode, diffs, audition, heatmap, cooldowns |

---

## 10. Out of Scope (v1)

- macOS / Linux support
- Cloud sync of profiles
- Audio DSP / effects (reverb, EQ)
- Multiple simultaneous audio output devices
- Plugin system for custom agent types
