#!/usr/bin/env python3
"""
critical_path.py — show the dependency tree needed to unblock a given story.

Usage:
    python critical_path.py <STORY_ID> [path/to/project_back.json]

Defaults to project_back.json in the same directory as this script.

Output:
  - Dependency tree rooted at the given story, showing which stories block it
    recursively, with status and feature.
  - Flat ordered list of stories that must be completed first, sorted by
    (phase ASC, priority ASC).
  - Exits with code 1 if the story ID is not found.
  - Exits with code 2 if a circular dependency is detected.

Status values:
  "backlog"     — not yet started
  "in_progress" — actively being worked
  "done"        — complete (shown as ✓, not included in execution order)
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

# ── ANSI colours ──────────────────────────────────────────────────────────────

RESET   = "\033[0m"
BOLD    = "\033[1m"
DIM     = "\033[2m"
CYAN    = "\033[96m"
YELLOW  = "\033[93m"
GREEN   = "\033[92m"
RED     = "\033[91m"
MAGENTA = "\033[95m"
WHITE   = "\033[97m"

STATUS_COLOR = {
    "done":        GREEN,
    "in_progress": YELLOW,
    "blocked":     RED,
    "backlog":     RESET,
}

PRIORITY_COLOR = {"high": RED, "medium": YELLOW, "low": DIM}

def status_color(status: str) -> str:
    return STATUS_COLOR.get(status, RESET)

def priority_int(story: dict) -> int:
    return PRIORITY_ORDER.get(story.get("priority", "medium"), 2)

def feature_phase(feature_key: str) -> int:
    try:
        return int(feature_key[1:3])
    except (ValueError, IndexError):
        return 99

def story_phase(story: dict) -> int:
    return feature_phase(story.get("feature", "F99"))

# ── Data helpers ──────────────────────────────────────────────────────────────

def load_backlog(path: str) -> tuple[list[dict], dict]:
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    return data["stories"], data.get("features", {})


def build_index(stories: list[dict]) -> dict[str, dict]:
    return {s["id"]: s for s in stories}


def parse_deps(story: dict) -> list[str]:
    return list(story.get("dependencies") or [])


def is_done(story: dict) -> bool:
    return story.get("status") == "done"

# ── Critical path resolution ──────────────────────────────────────────────────

@dataclass
class PathNode:
    story_id:  str
    item:      dict
    children:  list = dc_field(default_factory=list)
    is_target: bool = False


class CircularDependencyError(Exception):
    def __init__(self, cycle: list[str]):
        self.cycle = cycle
        super().__init__("Circular dependency: " + " → ".join(cycle))


def build_dep_tree(
    story_id: str,
    index: dict[str, dict],
    visited: set[str] | None = None,
    ancestors: list[str] | None = None,
) -> PathNode:
    """
    Recursively build a tree of unresolved dependencies.
    Only follows dependencies that are NOT yet done.
    Raises CircularDependencyError on a cycle.
    """
    if visited   is None: visited   = set()
    if ancestors is None: ancestors = []

    if story_id in ancestors:
        cycle_start = ancestors.index(story_id)
        raise CircularDependencyError(ancestors[cycle_start:] + [story_id])

    item = index.get(story_id)
    if item is None:
        stub = {
            "id": story_id, "title": "(not found in backlog)",
            "status": "unknown", "feature": "F99", "priority": "low",
        }
        return PathNode(story_id=story_id, item=stub)

    node = PathNode(story_id=story_id, item=item)

    if story_id in visited:
        return node  # already expanded elsewhere

    visited.add(story_id)
    ancestors = ancestors + [story_id]

    for dep_id in parse_deps(item):
        dep_item = index.get(dep_id)
        if dep_item and is_done(dep_item):
            continue  # already complete — not on the critical path
        child = build_dep_tree(dep_id, index, visited, ancestors)
        node.children.append(child)

    return node


def collect_blocking_stories(node: PathNode,
                              seen: set[str] | None = None) -> list[dict]:
    """Post-order walk: deps before dependents, de-duplicated."""
    if seen is None:
        seen = set()
    result = []
    for child in node.children:
        result.extend(collect_blocking_stories(child, seen))
    if node.story_id not in seen and not node.is_target:
        seen.add(node.story_id)
        result.append(node.item)
    return result


def collect_all_deps(node: PathNode, seen: set[str] | None = None) -> list[dict]:
    """Same as collect_blocking_stories but includes done items (for summary)."""
    if seen is None:
        seen = set()
    result = []
    for child in node.children:
        result.extend(collect_all_deps(child, seen))
    if node.story_id not in seen and not node.is_target:
        seen.add(node.story_id)
        result.append(node.item)
    return result

# ── Display ───────────────────────────────────────────────────────────────────

def _wrap(text: str, width: int = 72, indent: int = 4) -> list[str]:
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


def _story_card(story: dict, indent: int = 2, bullet: str = "") -> None:
    """Print a single story card with title, meta, and description."""
    status = story.get("status", "?")
    phase  = story_phase(story)
    pri    = story.get("priority", "?")
    sc     = status_color(status)
    pc     = PRIORITY_COLOR.get(pri, RESET)
    title  = story.get("title", "—")
    desc   = story.get("description") or ""
    feat   = story.get("feature", "—")
    pad    = " " * indent
    prefix = f"{pad}{bullet}" if bullet else pad

    print(f"{prefix}{BOLD}{CYAN}{story['id']}{RESET}  {BOLD}{title}{RESET}")
    print(f"{pad}   {DIM}phase={phase}  feature={feat}  "
          f"priority={pc}{pri}{RESET}{DIM}  status={sc}{status}{RESET}")
    if desc:
        print(f"{pad}   {DIM}Description:{RESET}")
        for line in _wrap(desc, width=66, indent=len(pad) + 5):
            print(line)


def find_external_blocker_chains(
    cycle_ids: set[str],
    index: dict[str, dict],
) -> dict[str, list[list[str]]]:
    """Find chains of active+incomplete stories outside a cycle that block it."""
    def _walk(story_id: str, visited: set[str],
              ancestors: list[str]) -> list[list[str]]:
        if story_id in ancestors:
            return []
        item = index.get(story_id)
        if item is None or is_done(item):
            return []
        deps = [d for d in parse_deps(item)
                if d not in cycle_ids and d not in visited]
        if not deps:
            return [[story_id]]
        chains: list[list[str]] = []
        visited2 = visited | {story_id}
        for dep_id in deps:
            for sub in _walk(dep_id, visited2, ancestors + [story_id]):
                chains.append([story_id] + sub)
        return chains if chains else [[story_id]]

    result: dict[str, list[list[str]]] = {}
    for cid in cycle_ids:
        item = index.get(cid)
        if item is None:
            continue
        external_deps = [d for d in parse_deps(item) if d not in cycle_ids]
        all_chains: list[list[str]] = []
        for dep_id in external_deps:
            dep_item = index.get(dep_id)
            if dep_item and not is_done(dep_item):
                chains = _walk(dep_id, set(), [cid])
                all_chains.extend(chains if chains else [[dep_id]])
        if all_chains:
            result[cid] = all_chains
    return result


def print_cycle(cycle: list[str], index: dict[str, dict]) -> None:
    sep     = f"{BOLD}{RED}{'─' * 60}{RESET}"
    sep_dim = f"{DIM}{'─' * 60}{RESET}"

    print(f"\n{sep}")
    print(f"  {BOLD}{RED}✗  CIRCULAR DEPENDENCY DETECTED{RESET}")
    print(sep)

    arrow      = f"  {DIM}→{RESET}  "
    chain_parts = [f"{RED}{BOLD}{t}{RESET}" for t in cycle]
    print(f"\n  {DIM}Cycle:{RESET}  " + arrow.join(chain_parts) + "\n")

    cycle_ids_ordered = list(dict.fromkeys(cycle[:-1]))
    cycle_ids_set     = set(cycle_ids_ordered)

    tasks = sorted(
        [index[t] for t in cycle_ids_ordered if t in index],
        key=lambda s: (priority_int(s), s.get("id", ""))
    )

    print(f"{sep_dim}")
    print(f"  {BOLD}{WHITE}Stories in the cycle — sorted by priority{RESET}")
    print(sep_dim + "\n")

    for rank, story in enumerate(tasks, 1):
        _story_card(story, indent=2, bullet=f"{BOLD}{RED}#{rank}{RESET}  ")
        if rank < len(tasks):
            print(f"\n  {DIM}{'·' * 56}{RESET}\n")

    ext = find_external_blocker_chains(cycle_ids_set, index)
    if ext:
        print(f"\n{sep_dim}")
        print(f"  {BOLD}{YELLOW}External blockers feeding into the cycle{RESET}")
        print(sep_dim)
        for cid in cycle_ids_ordered:
            if cid not in ext:
                continue
            cycle_title = index.get(cid, {}).get("title", "")
            print(f"\n  {BOLD}{RED}{cid}{RESET}  {DIM}{cycle_title}{RESET}"
                  f"  {DIM}← is also blocked by:{RESET}\n")
            seen_chains: set[str] = set()
            for chain in ext[cid]:
                key = ",".join(chain)
                if key in seen_chains:
                    continue
                seen_chains.add(key)
                arrow_str = f"  {DIM}→{RESET}  ".join(
                    f"{BOLD}{YELLOW}{t}{RESET}" for t in chain
                )
                print(f"    {arrow_str}")
                first = index.get(chain[0])
                if first:
                    _story_card(first, indent=6)
                if len(chain) > 1:
                    for tid in chain[1:]:
                        sub = index.get(tid)
                        if sub:
                            sc = status_color(sub.get("status", "?"))
                            print(f"      {DIM}↳{RESET}  {BOLD}{YELLOW}{tid}{RESET}"
                                  f"  {sub.get('title', '—')}"
                                  f"  {DIM}[{sc}{sub.get('status','?')}{RESET}{DIM}]{RESET}")
                print()
    else:
        print(f"\n  {DIM}No external blockers found outside the cycle.{RESET}\n")

    print(sep)
    print(f"  {RED}Resolve the cycle (and any external blockers above) to proceed.{RESET}")
    print(sep + "\n")


def print_tree(node: PathNode, prefix: str = "",
               is_last: bool = True, depth: int = 0) -> None:
    item   = node.item
    status = item.get("status", "?")
    phase  = story_phase(item)
    title  = item.get("title", "")

    connector = "└── " if is_last else "├── "
    extension = "    " if is_last else "│   "

    sc        = status_color(status)
    done_mark = f" {GREEN}✓{RESET}" if status == "done" else ""
    id_str    = f"{BOLD}{CYAN}{node.story_id}{RESET}{done_mark}"
    meta      = (f"{DIM}[phase={phase}, feature={item.get('feature','?')}, "
                 f"status={sc}{status}{RESET}{DIM}]{RESET}")

    if depth == 0:
        print(f"  {BOLD}{MAGENTA}◉  {node.story_id}{RESET}  {BOLD}{title}{RESET}")
        print(f"     {meta}")
    else:
        print(f"  {DIM}{prefix}{connector}{RESET}{id_str}  {title}")
        print(f"  {DIM}{prefix}{extension}{RESET}    {meta}")

    child_prefix = prefix + extension
    for i, child in enumerate(node.children):
        print_tree(child, child_prefix, is_last=(i == len(node.children) - 1),
                   depth=depth + 1)


def print_execution_order(stories: list[dict], target_id: str) -> None:
    ordered = sorted(
        stories,
        key=lambda s: (story_phase(s), priority_int(s), s.get("id", ""))
    )
    sep = f"{DIM}{'─' * 60}{RESET}"
    print(f"\n{sep}")
    print(f"  {BOLD}{WHITE}Suggested execution order to unblock {MAGENTA}{target_id}{RESET}")
    print(sep)

    if not ordered:
        print(f"\n  {GREEN}No blockers — {target_id} is already unblocked (or done)!{RESET}\n")
        return

    for i, story in enumerate(ordered, 1):
        status = story.get("status", "?")
        phase  = story_phase(story)
        pri    = story.get("priority", "?")
        sc     = status_color(status)
        pc     = PRIORITY_COLOR.get(pri, RESET)

        print(f"\n  {BOLD}{CYAN}Step {i}{RESET}  {BOLD}{story['id']}{RESET}"
              f"  {DIM}[phase={phase}, priority={pc}{pri}{RESET}{DIM}]{RESET}")
        print(f"         {story.get('title', '—')}")
        if status != "done":
            print(f"         Status: {sc}{status}{RESET}")
        desc = story.get("description") or ""
        if desc:
            for line in _wrap(desc, width=68, indent=9):
                print(line)

    print()


def print_summary(all_deps: list[dict], target_id: str,
                  target_done: bool) -> None:
    done_count    = sum(1 for s in all_deps if s.get("status") == "done")
    active_count  = sum(1 for s in all_deps if s.get("status") == "in_progress")
    pending_count = len(all_deps) - done_count - active_count

    sep = f"{DIM}{'─' * 60}{RESET}"
    print(f"\n{sep}")
    print(f"  {BOLD}Summary{RESET}")
    print(sep)
    if target_done:
        print(f"  {GREEN}✓  {target_id} is already done.{RESET}")
    else:
        print(f"  Blocking stories total : {BOLD}{len(all_deps)}{RESET}")
        print(f"  Already done           : {GREEN}{done_count}{RESET}")
        print(f"  In progress            : {YELLOW}{active_count}{RESET}")
        print(f"  Still backlog          : {BOLD}{RED}{pending_count}{RESET}")
    print()

# ── Main ──────────────────────────────────────────────────────────────────────

def main() -> None:
    if len(sys.argv) < 2:
        print(f"{RED}Usage: python critical_path.py <STORY_ID> [backlog.json]{RESET}")
        sys.exit(1)

    target_id = sys.argv[1].upper()
    path      = sys.argv[2] if len(sys.argv) > 2 else DEFAULT_BACKLOG

    if not os.path.exists(path):
        print(f"{RED}Error: backlog file not found: {path}{RESET}")
        sys.exit(1)

    stories, features = load_backlog(path)
    index             = build_index(stories)

    if target_id not in index:
        print(f"{RED}Error: story ID '{target_id}' not found in backlog.{RESET}")
        print(f"{DIM}Available IDs: {', '.join(sorted(index.keys()))}{RESET}")
        sys.exit(1)

    target_item = index[target_id]
    target_done = is_done(target_item)

    print(f"\n{DIM}Backlog : {path}{RESET}")
    print(f"{DIM}Target  : {BOLD}{target_id}{RESET}  "
          f"{target_item.get('title', '')}\n")

    try:
        tree = build_dep_tree(target_id, index)
        tree.is_target = True
    except CircularDependencyError as exc:
        print_cycle(exc.cycle, index)
        sys.exit(2)

    sep = f"{DIM}{'─' * 60}{RESET}"
    print(sep)
    print(f"  {BOLD}{WHITE}Dependency tree{RESET}")
    print(sep + "\n")
    print_tree(tree)

    all_blocking = collect_blocking_stories(tree)
    all_deps     = collect_all_deps(tree)

    pending = [s for s in all_blocking if not is_done(s)]
    print_execution_order(pending, target_id)
    print_summary(all_deps, target_id, target_done)


if __name__ == "__main__":
    main()
