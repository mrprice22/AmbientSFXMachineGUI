#!/usr/bin/env python3
"""
next_task.py — show the next ready stories from the CANON backlog.

Usage:
    python next_task.py [path/to/project_back.json]

Defaults to project_back.json in the same directory as this script.

Phase discipline:
  - Stories are grouped by feature (F01_CONTEXT, F02_SCHEMA, ...).
    The numeric prefix (01, 02, ...) acts as the phase number.
  - The global minimum phase is the lowest feature number that still has
    open (non-done) stories.
  - Stories in higher-numbered features are not suggested until the minimum
    phase is clear — either all done, or none unblocked (error).
  - If the minimum phase has open stories but all are blocked, a dependency
    error is reported and the script exits with code 2.

Status values in project_back.json:
  "backlog"     — not yet started
  "in_progress" — actively being worked
  "done"        — complete; satisfies downstream dependencies
"""

import json
import sys
import os
from dataclasses import dataclass, field as dc_field

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# ── Config ────────────────────────────────────────────────────────────────────

DEFAULT_BACKLOG = os.path.join(os.path.dirname(__file__), "project_backlog.json")
PRIORITY_ORDER  = {"high": 1, "medium": 2, "low": 3}

# ── Helpers ───────────────────────────────────────────────────────────────────

def load_backlog(path: str) -> tuple[list[dict], dict]:
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    stories  = data["stories"]
    features = data.get("features", {})
    return stories, features


def feature_phase(feature_key: str) -> int:
    """Extract numeric phase from a feature key like 'F03_DAL' → 3."""
    try:
        return int(feature_key[1:3])
    except (ValueError, IndexError):
        return 99


def story_phase(story: dict) -> int:
    return feature_phase(story.get("feature", "F99"))


def priority_int(story: dict) -> int:
    return PRIORITY_ORDER.get(story.get("priority", "medium"), 2)


def done_ids(stories: list[dict]) -> set[str]:
    """IDs with status 'done' — these satisfy dependencies."""
    return {s["id"] for s in stories if s.get("status") == "done"}


def open_stories(stories: list[dict]) -> list[dict]:
    """Stories that are not yet done (backlog or in_progress)."""
    return [s for s in stories if s.get("status") != "done"]


def parse_deps(story: dict) -> list[str]:
    return list(story.get("dependencies") or [])


def is_unblocked(story: dict, completed: set[str]) -> bool:
    return all(dep in completed for dep in parse_deps(story))


def global_min_phase(stories: list[dict]) -> int | None:
    """Lowest phase that still has open stories. None if everything is done."""
    phases = [story_phase(s) for s in stories if s.get("status") != "done"]
    return min(phases) if phases else None


# ── Phase analysis ────────────────────────────────────────────────────────────

@dataclass
class PhaseStatus:
    phase: int
    unblocked: list = dc_field(default_factory=list)   # backlog + unblocked
    blocked:   list = dc_field(default_factory=list)   # backlog + blocked
    in_progress: list = dc_field(default_factory=list) # already active

    @property
    def open_count(self) -> int:
        return len(self.unblocked) + len(self.blocked)

    @property
    def is_clear(self) -> bool:
        """Phase has no backlog work left (may still have in_progress)."""
        return self.open_count == 0

    @property
    def is_stuck(self) -> bool:
        """Backlog stories exist but none are unblocked — deadlock."""
        return self.open_count > 0 and len(self.unblocked) == 0


def analyse_phase(stories: list[dict], phase: int,
                  completed: set[str]) -> PhaseStatus:
    in_phase = [s for s in stories if story_phase(s) == phase]

    in_progress = [s for s in in_phase if s.get("status") == "in_progress"]
    backlog     = [s for s in in_phase if s.get("status") == "backlog"]

    unblocked = [s for s in backlog if     is_unblocked(s, completed)]
    blocked   = [s for s in backlog if not is_unblocked(s, completed)]

    return PhaseStatus(phase=phase, unblocked=unblocked,
                       blocked=blocked, in_progress=in_progress)


def pick_next_n(ps: PhaseStatus, n: int = 3) -> list[dict]:
    candidates = sorted(
        ps.unblocked,
        key=lambda s: (priority_int(s), s.get("id", ""))
    )
    return candidates[:n]


# ── Display ───────────────────────────────────────────────────────────────────

RESET  = "\033[0m"
BOLD   = "\033[1m"
DIM    = "\033[2m"
CYAN   = "\033[96m"
YELLOW = "\033[93m"
GREEN  = "\033[92m"
RED    = "\033[91m"

PRIORITY_COLOR = {"high": RED, "medium": YELLOW, "low": DIM}


def _wrap(text: str, width: int = 70, indent: int = 4) -> list[str]:
    prefix = " " * indent
    lines  = []
    for paragraph in text.splitlines():
        paragraph = paragraph.strip()
        if not paragraph:
            continue
        words, current, cur_len = paragraph.split(), prefix, indent
        for word in words:
            if cur_len + len(word) + 1 > width:
                lines.append(current.rstrip())
                current, cur_len = prefix + word + " ", indent + len(word) + 1
            else:
                current += word + " "
                cur_len += len(word) + 1
        lines.append(current.rstrip())
    return lines


def print_tasks(tasks: list[dict], phase: int,
                features: dict, ps: PhaseStatus) -> None:
    header = f"{BOLD}{CYAN}{'─' * 60}{RESET}"
    # Find feature name for this phase
    feat_name = next(
        (v for k, v in features.items() if feature_phase(k) == phase),
        f"Phase {phase}"
    )
    # Trim to first part before em dash
    feat_label = feat_name.split(" —")[0]

    print(header)
    print(f"  {BOLD}{CYAN}▶  Next stories  —  {feat_label}{RESET}")
    if ps.in_progress:
        ids = ", ".join(s["id"] for s in ps.in_progress)
        print(f"  {DIM}In progress: {ids}{RESET}")
    print(header)

    for rank, story in enumerate(tasks, start=1):
        pri   = story.get("priority", "medium")
        pc    = PRIORITY_COLOR.get(pri, RESET)
        deps  = parse_deps(story)
        desc  = story.get("description") or ""
        ref   = story.get("references") or ""

        print(f"\n  {BOLD}{CYAN}#{rank}{RESET}")
        print(f"  {DIM}ID:          {RESET}{BOLD}{CYAN}{story['id']}{RESET}")
        print(f"  {DIM}Title:       {RESET}{BOLD}{story.get('title', '—')}{RESET}")
        if ref:
            print(f"  {DIM}References:    {RESET}{ref}{RESET}")

        if desc:
            print(f"  {DIM}Description:{RESET}")
            for line in _wrap(desc, width=70, indent=4):
                print(line)
        print(f"  {DIM}Priority:    {RESET}{pc}{pri}{RESET}")
        print(f"  {DIM}Feature:     {RESET}{story.get('feature', '—')}")
        if deps:
            print(f"  {DIM}Depends on:  {RESET}{', '.join(deps)}")
        
    
        if rank < len(tasks):
            print(f"\n  {DIM}{'·' * 56}{RESET}")

    print()


def print_stuck(ps: PhaseStatus, stories: list[dict],
                completed: set[str], features: dict) -> None:
    feat_name = next(
        (v for k, v in features.items() if feature_phase(k) == ps.phase),
        f"Phase {ps.phase}"
    ).split(" —")[0]

    id_index = {s["id"]: s for s in stories}

    print(f"{BOLD}{RED}{'─' * 60}{RESET}")
    print(f"  {BOLD}{RED}✗  STUCK — {feat_name}  (Phase {ps.phase}){RESET}")
    print(f"{BOLD}{RED}{'─' * 60}{RESET}")
    print(f"\n  {RED}All {ps.open_count} backlog story/ies in phase {ps.phase} are blocked.{RESET}")
    print(f"  {DIM}Possible causes: circular dependency or dep on a future-phase story.{RESET}\n")

    for story in sorted(ps.blocked, key=lambda s: (priority_int(s), s.get("id", ""))):
        undone = [d for d in parse_deps(story) if d not in completed]
        print(f"  {BOLD}{story['id']}{RESET}  {story.get('title', '')}")
        for dep_id in undone:
            dep = id_index.get(dep_id)
            if dep:
                dep_phase  = story_phase(dep)
                dep_status = dep.get("status", "?")
                future = (f"  {RED}← FUTURE PHASE{RESET}"
                          if dep_phase > ps.phase else "")
                print(f"    {DIM}↳ waiting on{RESET} {RED}{dep_id}{RESET}"
                      f"  {DIM}[phase={dep_phase}, status={dep_status}]{RESET}{future}")
            else:
                print(f"    {DIM}↳ waiting on{RESET} {RED}{dep_id}{RESET}"
                      f"  {RED}(ID not found in backlog!){RESET}")
        print()


# ── Main ──────────────────────────────────────────────────────────────────────

def main() -> None:
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_BACKLOG

    if not os.path.exists(path):
        print(f"{RED}Error: backlog file not found: {path}{RESET}")
        sys.exit(1)

    stories, features = load_backlog(path)
    completed = done_ids(stories)

    total     = len(stories)
    n_done    = len(completed)
    n_active  = sum(1 for s in stories if s.get("status") == "in_progress")
    n_backlog = total - n_done - n_active

    print(f"\n{DIM}Backlog : {path}{RESET}")
    print(f"{DIM}Total: {total}  |  Done: {GREEN}{n_done}{RESET}{DIM}"
          f"  |  In progress: {YELLOW}{n_active}{RESET}{DIM}"
          f"  |  Backlog: {n_backlog}{RESET}\n")

    target_phase = global_min_phase(stories)

    if target_phase is None:
        print(f"{GREEN}{BOLD}All stories complete!{RESET}\n")
        return

    print(f"{DIM}Global target phase: {BOLD}{target_phase}{RESET}\n")

    ps = analyse_phase(stories, target_phase, completed)

    if ps.is_clear:
        # Only in_progress items remain — phase is finishing
        ids = ", ".join(s["id"] for s in ps.in_progress)
        print(f"{GREEN}Phase {target_phase} backlog complete — "
              f"in progress: {ids}{RESET}\n")
    elif ps.is_stuck:
        print_stuck(ps, stories, completed, features)
        sys.exit(2)
    else:
        tasks = pick_next_n(ps)
        print_tasks(tasks, target_phase, features, ps)


if __name__ == "__main__":
    main()
