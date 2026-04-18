# Ambient SFX Machine — GUI

A WPF desktop app for running multiple ambient-sound "machines" side by side, each playing random SFX from folders on a schedule. It's a graphical successor to the [Ambient SFX Machine Console](https://github.com/mrprice22/AmbientSFXMachineConsole) and is designed so the two can coexist on the same folders.

- **Design document:** [`design-document.md`](./design-document.md) — architecture, panels, persistence, phased delivery.
- **Backlog:** [`project_backlog.json`](./project_backlog.json) — source of truth for what's done and what's next.
- **Console sibling:** https://github.com/mrprice22/AmbientSFXMachineConsole

---

## What it does

- Hosts any number of **Machines** in one window. A Machine is a named, icon-bearing group of agents with its own master volume and enable toggle — replacing the old "one EXE copy per directory" pattern.
- Each Machine contains **Agents** (one per SFX folder). Agents pick sounds at random on configurable intervals, with volume, balance, turbo mode, and per-sound overrides.
- An app-wide **Audio Library** tracks every audio file by SHA-256, surfaces exact and likely duplicates, shows where each file is used, and flags orphans.
- Dockable panels (AvalonDock): Machines rail, Agent cards, Live Playback log, Now Playing, Soundboard, Library.
- System tray with per-machine submenus, global hotkeys, and per-machine Profiles / Soundboard / Hotkeys.

See the [design document](./design-document.md) for the full picture.

---

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (to build) or the runtime (to run a published build)

Key NuGet dependencies: `NAudio`, `Dirkster.AvalonDock`, `CommunityToolkit.Mvvm`.

## Build & run

```powershell
cd AmbientSFXMachineGUI
dotnet build
dotnet run
```

---

## Folder layout a Machine expects

A Machine points at a root folder that looks like this (identical to what the console app uses):

```
<machine-root>/
├── appSettings.config          # appName=..., icon=...
├── <icon>.ico
└── snd/
    ├── <agent-1>/
    │   ├── <agent-1>.config    # enabled, volume, min/max_seconds, mode, balance_*, ...
    │   ├── sound1.wav
    │   ├── sound2.mp3
    │   └── ...
    └── <agent-2>/
        └── ...
```

- `appSettings.config` — key/value lines: `appName=...`, `icon=...`.
- `<agent>/<agent>.config` — key/value lines: `enabled`, `volume`, `min_seconds`, `max_seconds`, `min_minutes`, `max_minutes`, `mode` (`shuffle` / `random`), `balance_min`, `balance_max`, `balanceInvertChance`, `override_startup_seconds`, turbo settings, etc.

Audio files can be `.wav`, `.mp3`, and anything else NAudio can decode.

---

## Importing existing console-app machines

If you already use the console app, you can bring those machines straight into the GUI without moving any files:

1. **File → Import Machine** and pick the console machine folder (e.g. `C:\...\SpookyAudioMachine`).
2. The GUI reads `appSettings.config` and every `snd/<agent>/*.config` in place.
3. A new Machine card appears in the left rail, using the folder's icon and name.
4. Your audio files and `.config` files stay where they are — the GUI references them, it does not copy them.

Verified against these real-world machines:
- `SpookyAudioMachine`
- `ThursdayGameNightAudioSFX`
- `ThursdayPacmanAudioSFX`

---

## Playing nice with the console app

The GUI and the console app **share the same on-disk contract** (the `appSettings.config` + `snd/<agent>/*.config` layout above), so a single machine folder can be driven by either tool. You can use them independently, together, or mix and match.

### What they share

- The same `.config` file format and keys (GUI reads/writes the same keys the console does).
- The same `snd/<agent>/` folder convention.
- The same audio file formats (via NAudio in both projects).

### What they don't share

- Runtime state — if both apps run against the same folder at the same time, they're each independent audio sources (the OS will mix them).
- Profiles, soundboard, library metadata, and machine index — these are GUI-only and live under `%AppData%\AmbientAgents\`. The console app doesn't read them.
- The GUI's library cache (`library.json`) is informational only; deleting it just forces a re-scan.

### Recommended workflows

**Author once, run anywhere.** Edit `.config` files in either tool (GUI's config editor, or your text editor for the console app). The other side picks up the changes next time it loads.

**Keep the console as a lightweight per-machine runner.** Copy the console EXE into a machine folder for a "single-purpose, one icon in the Windows volume mixer" experience. Point the GUI at the same folder when you want the full panel layout, live log, or library tools — just don't run both against the same folder simultaneously unless you actually want both playing.

**Migrate gradually.** Start by importing existing console-machine folders into the GUI (see above). Nothing is copied or modified on import, so you can always go back to launching the console EXE directly from the same folder.

**Share a sound pool across machines.** The GUI's Audio Library lets you add the same WAV/MP3 to multiple agents and multiple machines without duplicating the file on disk. The console app still sees each folder as its own self-contained world — that's fine; the library is a GUI-side view on top of the same files.

### What's GUI-only

- Multiple machines in one window with unified master volume and per-machine master volume (chain: `appMaster × machine × agent × per-sound`).
- The Audio Library (duplicate detection, usage tracking, orphan detection).
- Live playback log, Now Playing panel, Soundboard, Profiles, global hotkeys, tray integration.
- Drag-and-drop ingest of folders and audio files.

---

## Project structure

```
AmbientSFXMachineGUI/
├── App.xaml / App.xaml.cs
├── Shell/                  # MainWindow + ShellViewModel (AvalonDock host)
├── Panels/                 # MachinePanel, AgentPanel, LogPanel, NowPlayingPanel,
│                           # SoundboardPanel, LibraryPanel
├── Services/               # MachineCoordinator, AgentCoordinator, MachineImporter,
│                           # AudioLibrary, AudioFileHasher, ProfileService,
│                           # HotkeyService, TrayService
├── Models/                 # MachineViewModel, AgentViewModel, SoundFileViewModel,
│                           # AudioFileEntry, LogEntryViewModel, Profile, SoundboardItem
├── design-document.md
├── project_backlog.json
├── next_task.py            # Suggests the next backlog story to work on
├── critical_path.py        # Shows dependency tree for a specific story
└── CLAUDE.md               # Contributor guidance for Claude Code sessions
```

---

## Persistence

| Data | Location |
|---|---|
| Machine audio + agent configs | `<machine-root>/appSettings.config`, `<machine-root>/snd/<agent>/*.config` (shared with console app) |
| Machine index | `%AppData%\AmbientAgents\machines\<id>.json` |
| Profiles (per-machine) | `%AppData%\AmbientAgents\machines\<id>\profiles\*.json` |
| Hotkeys (per-machine) | `%AppData%\AmbientAgents\machines\<id>\hotkeys.json` |
| Hotkeys (global app actions) | `%AppData%\AmbientAgents\hotkeys.json` |
| Library cache | `%AppData%\AmbientAgents\library.json` |
| Panel layout | `%AppData%\AmbientAgents\layout.xml` |

Nothing under `%AppData%\AmbientAgents\` is required by the console app. Delete any of it to reset GUI-side state; machine folders themselves are untouched.

---

## Contributing

Start every session with:

```powershell
py next_task.py
```

It reads `project_backlog.json`, finds the lowest-numbered feature with open stories, and suggests up to three unblocked stories. To see what must happen before a specific story:

```powershell
py critical_path.py MACHINE-04
```

See [`CLAUDE.md`](./CLAUDE.md) for the full workflow (status values, commit conventions, defect handling).
