# Ambient SFX Machine GUI

This file governs how Claude Code should approach work in this repository. Read it before starting any session.

---

## What This Project Is

Goal is to build a graphical audio machine with multiple sound agents described in `design-document.md`
The backlog tracking that journey lives in `project_backlog.json`.

---

## The Backlog

**File:** `project_backlog.json`

The backlog is a JSON object with this structure:

```json
{
  "project": "CANON",
  "features": {
    "F01_CONTEXT": "Context System Maturation — ...",
    "F02_SCHEMA":  "Schema System Maturation — ..."
  },
  "stories": [
    {
      "id":           "S-001",
      "title":        "Short title",
      "description":  "What to build and why. Enough detail to implement without asking.",
      "feature":      "F01_CONTEXT",
      "priority":     "high",
      "status":       "backlog",
      "dependencies": ["S-xxx", "S-yyy"]
    }
  ]
}
```

### Status Values

| Value | Meaning |
|---|---|
| `"backlog"` | Not started |
| `"in_progress"` | Actively being worked — set this when you begin a story |
| `"done"` | Complete and integrated — set this when the story is fully implemented and tested |

### Priority Values

`"high"` → `"medium"` → `"low"`. The tools use this for ordering suggestions.

---

## Starting a Session: Pick Your Next Story

Run this before touching any code:

```bash
py next_task.py
```

This reads `project_backlog.json`, finds the lowest-numbered feature (phase) that still has open stories, and suggests up to 3 unblocked stories to work on next. Stories are unblocked when all their `dependencies` are `"done"`.

**Before starting the suggested story, mark it `in_progress`:**

```json
"status": "in_progress"
```

If you need to understand what must be completed before a specific story, use:

```bash
py critical_path.py S-042
```

This shows the full dependency tree and a suggested execution order sorted by phase then priority.

---

## Completing a Story

When a story is fully implemented and working:

1. Set its `status` to `"done"` in `project_backlog.json`.
2. Run `py next_task.py` again to get the next story.
3. Commit both the implementation and the updated `project_backlog.json` together.

**Never mark a story done if:**
- The code compiles but hasn't been tested against the test data
- A dependent story revealed the implementation was incomplete

---

## Discovering a Defect

When you find a bug while implementing a story, add a new story to `project_backlog.json` in the `stories` array:

```json
{
  "id":           "S-BUG-001",
  "title":        "Brief description of the defect",
  "description":  "What is broken, when it manifests, what correct behavior looks like.",
  "feature":      "F04_ENGINE",
  "priority":     "high",
  "status":       "backlog",
  "dependencies": ["S-019"]
}
```

**Defect story conventions:**

- Use IDs in the format `S-BUG-NNN` (increment from the last used bug ID)
- Set `priority` to `"high"` if the defect blocks other stories, `"medium"` otherwise
- Set `dependencies` to include the story that produced the defect if relevant
- Assign to the `feature` that owns the broken code
- If the defect is blocking you right now, mark the current story `"blocked"` and start the bug story immediately

---

## Changing Scope or Adding New Stories

If implementation reveals missing scope (not a defect — a design gap):

- Add a new story with a regular `S-NNN` ID (next sequential number after S-050)
- Add it to `dependencies` of any stories that need it before they can complete
- Run `py critical_path.py S-XXX` on affected stories to verify the dependency graph stays clean

---

## Phase Discipline

Stories are grouped by feature. The feature number (F01, F02, ...) acts as the phase. `next_task.py` enforces a soft phase gate: it only suggests stories in the lowest-numbered feature that has open work. This prevents skipping to lower-priority features while high-priority foundational work remains.

The phase gate is a guard, not a prison. If `next_task.py` reports a phase is stuck (all stories blocked), use `critical_path.py` to diagnose which upstream story is the true blocker.

---

## Development Rules

**Read before you write.** Always read the relevant existing implementation files before starting a story. Understand what's already built.

**One story at a time.** Mark a story `in_progress`, implement it completely, mark it `done`, then move on. Do not batch or partially implement stories.

**Check the design document.** `CANON_Design_Document.md` is the authoritative spec for the matured design. When a story's description is ambiguous, the design document wins.

**Tests before done.** A story is not `done` unless it has been exercised against the test CSV data in `data/raw/` or `tests/`. Integration tests are in `tests/integration/` (once created by S-045).

**Commit atomically.** Each commit should implement exactly one story plus the `project_backlog.json` status update. Commit message format:

```
[S-019] Implement in-memory $Database object

Brief description of what changed and why.
```

---

## File Map

| File | Purpose |
|---|---|
| `project_backlog.json` | The backlog — source of truth for what's done and what's next |
| `next_task.py` | CLI tool: suggests the next story to work on |
| `critical_path.py` | CLI tool: shows dependency tree for a specific story |
| `CANON_Design_Document.md` | Target architecture spec |
| `Barebones.md` | The starting implementation that already exists |
| `run.ps1` | Entry point — keep under 25 lines |
| `engine/` | Core engine modules |
| `adapters/` | Data access (being refactored into `dal/` by S-013/S-014) |
| `schemas/entities/` | Entity schema JSON files |
| `schemas/relationships/` | Link table schema JSON files (created by S-011) |
| `rules/` | Validation and transformation rules |
| `contexts/` | Context JSON files |
| `data/raw/` | Source input files (gitignored) |
| `data/output/` | Output files (gitignored) |
| `data/cache/` | TTL snapshots (gitignored, created by S-030) |
| `tests/unit/` | Pester unit tests (created by S-044) |
| `tests/integration/` | End-to-end tests (created by S-045) |
| `tools/` | Preflight and doc-gen tools (created by S-041/S-042) |
| `docs/` | Auto-generated documentation (created by S-042) |
