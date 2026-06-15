#!/usr/bin/env python3
"""Lightweight history explorer for OpenClaw creator workflows.

This script intentionally uses only stdlib so it can run in constrained
skill_exec environments.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path


def _collect_co_occurrences(log_dir: Path, top_k: int) -> list[dict]:
    """Best-effort aggregate from JSONL logs with optional skills_invoked arrays."""
    counts: dict[tuple[str, str], int] = {}
    if not log_dir.is_dir():
        return []

    for file in log_dir.glob("*.jsonl"):
        try:
            with file.open("r", encoding="utf-8") as handle:
                for line in handle:
                    line = line.strip()
                    if not line:
                        continue
                    try:
                        payload = json.loads(line)
                    except json.JSONDecodeError:
                        continue
                    invoked = payload.get("skills_invoked")
                    if not isinstance(invoked, list):
                        continue
                    names = [str(item).strip() for item in invoked if str(item).strip()]
                    for i in range(len(names)):
                        for j in range(i + 1, len(names)):
                            a, b = sorted((names[i], names[j]))
                            key = (a, b)
                            counts[key] = counts.get(key, 0) + 1
        except OSError:
            continue

    pairs = sorted(counts.items(), key=lambda item: item[1], reverse=True)[:top_k]
    return [{"skills": [a, b], "count": count} for (a, b), count in pairs]


def _collect_meta_usage(co_occurrences: list[dict]) -> list[dict]:
    usage: dict[str, int] = {}
    for entry in co_occurrences:
        skills = entry.get("skills")
        count = int(entry.get("count", 0))
        if not isinstance(skills, list):
            continue
        for name in skills:
            text = str(name)
            if text.startswith("meta-"):
                usage[text] = usage.get(text, 0) + count

    ranked = sorted(usage.items(), key=lambda item: item[1], reverse=True)
    return [{"name": name, "count": count} for name, count in ranked]


def _collect_router_fixtures() -> list[dict]:
    # Repository-local fixture discovery is intentionally conservative.
    return []


def _resolve_log_dir(raw: str | None) -> Path:
    if raw:
        return Path(raw).expanduser().resolve()
    return (Path.home() / ".openclaw" / "logs").resolve()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--log-dir", required=False, default=None)
    parser.add_argument("--query", required=True)
    parser.add_argument("--window-days", type=int, default=30)
    parser.add_argument("--include", default="co_occurrences,meta_usage,router_fixtures")
    parser.add_argument("--top-k", type=int, default=10)
    args = parser.parse_args(argv)

    include = {part.strip() for part in str(args.include).split(",") if part.strip()}
    log_dir = _resolve_log_dir(args.log_dir)

    result: dict = {"query": args.query, "window_days": args.window_days}

    co_occurrences: list[dict] = []
    if "co_occurrences" in include:
        co_occurrences = _collect_co_occurrences(log_dir, args.top_k)
        result["co_occurrences"] = co_occurrences

    if "meta_usage" in include:
        result["meta_usage"] = _collect_meta_usage(co_occurrences)

    if "router_fixtures" in include:
        result["router_fixtures"] = _collect_router_fixtures()

    if not result.get("co_occurrences") and not result.get("meta_usage"):
        result["placeholder"] = "no history available; downstream should rely on user intent only"

    print(json.dumps(result, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
