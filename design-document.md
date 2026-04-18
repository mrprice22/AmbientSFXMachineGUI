# AmbientAgents UI — Design Document

## 1. Overview

AmbientAgents UI is a graphical frontend for the existing AmbientAgents console application. The backend C# engine (`SoundAgent`, `Program`) continues to run unchanged as the audio runtime. The UI layer wraps it, communicates with it, and provides real-time control, monitoring, and configuration.

The app hosts **multiple Machines**, each containing multiple **Agents**. A Machine is the unit the user toggles and volume-balances as a whole; an Agent is one scheduled sound source (a folder of related SFX plus its `.config`). Machines replace the old "one EXE per directory" pattern in which the user copied the console EXE into folders like `SpookyAudioMachine`, `ThursdayGameNightAudioSFX`, and `ThursdayPacmanAudioSFX` and balanced them via the Windows volume mixer — that balancing now happens inside the GUI via per-machine master volume.

A process-wide **Audio Library** tracks every audio file referenced by any machine, keyed by content hash, so files can be reused across agents and machines, duplicates can be surfaced and merged, and orphan files can be found.

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
│   │   ├── MachinePanel/
│   │   ├── AgentPanel/
│   │   ├── LogPanel/
│   │   ├── NowPlayingPanel/
│   │   ├── SoundboardPanel/
│   │   └── LibraryPanel/
│   ├── Services/
│   │   ├── MachineCoordinator.cs (owns ObservableCollection<MachineViewModel>, log, active playbacks)
│   │   ├── AgentCoordinator.cs   (per-machine scheduler/playback engine, owned by a machine)
│   │   ├── MachineImporter.cs    (reads appSettings.config + snd/ into a MachineViewModel)
│   │   ├── AudioLibrary.cs       (path → AudioFileEntry registry with hash + usages index)
│   │   ├── AudioFileHasher.cs    (SHA-256 streaming + NAudio duration/size probe)
│   │   ├── ProfileService.cs
│   │   ├── HotkeyService.cs
│   │   └── TrayService.cs
│   └── Models/
│       ├── MachineViewModel.cs
│       ├── AgentViewModel.cs
│       ├── SoundFileViewModel.cs
│       ├── AudioFileEntry.cs
│       ├── LogEntryViewModel.cs
│       ├── Profile.cs
│       └── SoundboardItem.cs
```

`SoundAgent` is made observable by wrapping it in `AgentViewModel`, which subscribes to events surfaced from the core and exposes `INotifyPropertyChanged` properties consumed by the UI. Each `AgentViewModel` is owned by a `MachineViewModel`; the coordinator role that held all agents in v1 is now split between `MachineCoordinator` (app-global state: machines list, log, active playbacks) and per-machine `AgentCoordinator` instances (scheduling and playback within one machine).

### 3.1 Machine concept

A Machine is a top-level grouping of agents modeling what used to be one copy of the console EXE.

- Fields: `Id (Guid), Name, IconPath, IsEnabled, MasterVolume (0–100%), RootPath`.
- Owns: `ObservableCollection<AgentViewModel> Agents`, plus per-machine Profiles / Soundboard / Hotkeys.
- Persisted as `%AppData%\AmbientAgents\machines\<id>.json`. Agent `.config` files stay at `RootPath\snd\<agent>\*.config` and are not duplicated into the GUI's AppData.
- Disabling a machine pauses all its agents as a group without tearing them down.
- Volume chain (multiplier): `final = appMaster * machine.MasterVolume * agent.Volume * sound.VolumeOverride` (last term can go to 200%; the others are 0–100%).
- The machine's icon appears on its machine card and in a per-machine tray submenu.

### 3.2 Audio Library

`AudioLibrary` is a process-wide singleton that tracks every audio file referenced by any machine.

- **Keying:** primary key is SHA-256 content hash; secondary index by absolute path.
- **Entry:** `AudioFileEntry { AbsolutePath, Sha256, Duration, ByteSize, FileName, Usages: IReadOnlyList<UsageRef> }`, where `UsageRef = { MachineId, AgentId }`.
- **Hashing:** lazy, streamed SHA-256 on a background thread. Cached in `%AppData%\AmbientAgents\library.json` keyed by path with file size + mtime invalidation. Duration/size probed via NAudio.
- **Usages:** maintained by subscribing to mutations on every `MachineViewModel.Agents[*].Files` collection; re-computed on machine import/remove.
- **Decentralized ownership:** files stay wherever the user has them on disk; the library never copies or moves files. Adding a file creates/updates a library entry pointing at its current absolute path.
- **Duplicate detection (tiered):**
  1. **Exact duplicates** — identical SHA-256.
  2. **Likely duplicates** — different hash but same filename (case/extension-insensitive) AND duration within ±1%.
  Results group in the Duplicates tab; a merge action rewrites all agent references in a group to a chosen canonical library entry.
- **Unused detection:** entries whose `Usages` is empty.
- **Reuse:** a single library entry can belong to many agents and many machines.

---

## 4. Panel Layout System

The shell uses **AvalonDock** to provide:

- Dockable, floating, and auto-hidden panels
- Drag-to-reposition between dock zones (left, right, bottom, center tabbed)
- Collapsible via the auto-hide pinning metaphor
- Layout persisted to XML on shutdown and restored on startup

**Default layout:**

```
┌─────────────────────────────────────────────────────────────────┐
│  Toolbar: App Master Vol | Mute All | Profile Picker | ⚙        │
├──────────┬──────────────────────┬───────────────────────────────┤
│          │                      │                               │
│ Machines │   Agent Cards        │     Live Playback Log         │
│  rail    │   (scoped to active  │     (auto-scroll)             │
│ (cards)  │    machine)          │                               │
│          │                      │                               │
├──────────┴──────────────────────┴───────────────────────────────┤
│  Now Playing  |  Audio Library  (tabbed bottom dock)            │
├─────────────────────────────────────────────────────────────────┤
│  Soundboard / Favorites (collapsible, dockable, per-machine)    │
└─────────────────────────────────────────────────────────────────┘
```

The Machines rail lives at the left edge. Each machine card shows icon, name, enable toggle, and master volume slider. Selecting a machine scopes the Agent Cards area to that machine (or shows all machines' agents grouped by machine header).

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

Each agent is owned by a **Machine**; the agent's volume and enable state compose through its parent machine (see §3.1). Each agent is represented by an **AgentCard** control bound to `AgentViewModel`.

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

### 5.8 Machine Management

A dockable **Machines rail** lists every machine as a card with icon, name, enable toggle, and master volume slider.

**Actions:**
- **Create machine** — prompts for name, icon file, root folder; creates metadata and an empty `snd/` subfolder.
- **Import machine** — point at an existing console-app directory. `MachineImporter` reads `appSettings.config` (`appName` → Machine.Name, `icon` → Machine.IconPath) and each `snd/<agent>/*.config` to produce `AgentViewModel`s. The folder is referenced in place, not copied. Validated against the three example machines in `C:\Users\homel\OneDrive\Projects\`.
- **Remove machine** — unlinks from the index and rebuilds library usages. Audio files and `.config` files on disk are left untouched; the action clearly says so.
- **Rename / change icon / reorder** — via context menu and drag-reorder.
- **Per-machine tray submenu** — each machine gets a submenu in the tray with its own icon and entries for toggle mute, solo (mute all other machines), and show cards.

### 5.9 Audio Library

A dockable **LibraryPanel** with three tabs:

- **All Files** — virtualized list (filename, duration, size, usage count, full path); sortable, text-filterable.
- **Duplicates** — two grouped sections:
  - Exact duplicates (same SHA-256).
  - Likely duplicates (same filename, case/extension-insensitive, AND duration ±1%).
  Each group lists candidate paths with their usage counts.
- **Unused** — library entries with zero usages; multi-select + bulk "remove from library" (does not delete from disk).

**Row actions (context menu):**
- Reveal in Explorer
- Copy full path
- Show usages — opens a side drawer listing every `(machine, agent)` pair, each with a "navigate to" action that focuses the relevant agent card.
- Add to additional agents/machines — multi-select picker that appends the file to every chosen agent's file list.
- Merge duplicates (Duplicates tab only) — choose a canonical entry; every agent reference to any other group member is rewritten to the canonical path.

**Ingest:**
- Drop audio files from Explorer onto the Library panel to register them.
- Drop onto an agent card to register and attach in one step.

A progress indicator in the tab header shows hashing progress during initial scans; hashing is streamed on a background thread and cached in `library.json` with size + mtime invalidation.

---

## 6. State Management

App-global state lives in `MachineCoordinator` (singleton):
- `ObservableCollection<MachineViewModel> Machines`
- `LogEntryViewModel` events published via a simple event bus (log is app-global, not per-machine)
- Active playback tracked via a `ConcurrentDictionary<Guid, ActivePlayback>` (entries carry both machine and agent identity)

Per-machine runtime state lives on `MachineViewModel`:
- `ObservableCollection<AgentViewModel> Agents`
- Owned `AgentCoordinator` instance driving scheduling within the machine
- Per-machine Profiles / Soundboard / Hotkeys collections

`AudioLibrary` is a separate singleton service (see §3.2) whose `Usages` index is derived from observing mutations across every `MachineViewModel.Agents[*].Files`.

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
| Agent configs | `<machineRoot>\snd\<agent>\*.config` | Existing key=value (unchanged) |
| Machine appSettings | `<machineRoot>\appSettings.config` | Existing key=value (read on import) |
| Machine index | `%AppData%\AmbientAgents\machines\<id>.json` | JSON (id, name, iconPath, isEnabled, masterVolume, rootPath, order) |
| Library cache | `%AppData%\AmbientAgents\library.json` | JSON (path → hash, duration, size, mtime) |
| Profiles (per-machine) | `%AppData%\AmbientAgents\machines\<id>\profiles\*.json` | JSON |
| Hotkeys (per-machine) | `%AppData%\AmbientAgents\machines\<id>\hotkeys.json` | JSON |
| Hotkeys (global app actions) | `%AppData%\AmbientAgents\hotkeys.json` | JSON |
| Panel layout | `%AppData%\AmbientAgents\layout.xml` | AvalonDock XML |
| Sound overrides | Inside profile JSON | — |
| Soundboard (per-machine) | Inside machine's profile JSON | — |

---

## 9. Phased Delivery

| Phase | Stories | Goal |
|---|---|---|
| **1 — Foundation** | SHELL-01, SHELL-02, AGENT-01–04, LOG-01 | Usable replacement for console; agent cards, log, tray |
| **1.5 — Machines & Library** | MACHINE-01–04, MACHINE-08, MACHINE-10, LIB-01–05 | First-class machines; import console-app folders; library with dup detection |
| **2 — Control** | AGENT-05–09, SFX-01–02, LOG-02, NOW-01–02, PROF-01–03, MACHINE-05–07 | Per-sound control, now-playing, basic profiles, machine CRUD |
| **3 — Soundboard** | SB-01–04, LOG-03, SHELL-04, SFX-04, LIB-06–09 | Favorites, hotkeys, drag-drop, library usages + ingest |
| **4 — Polish** | SHELL-03, SFX-03–05, NOW-03, PROF-04–05, LOG-04, AGENT-10–11, MACHINE-09, LIB-10–11 | Mini-mode, diffs, audition, heatmap, cooldowns, tray submenus, dup merge |

---

## 10. Out of Scope (v1)

- macOS / Linux support
- Cloud sync of profiles or library
- Audio DSP / effects (reverb, EQ)
- Multiple simultaneous audio output devices
- Plugin system for custom agent types
- Cross-machine profile templates (profiles that apply to more than one machine at once)
- Perceptual audio fingerprinting (Chromaprint-style near-duplicate detection beyond hash + filename + duration)
