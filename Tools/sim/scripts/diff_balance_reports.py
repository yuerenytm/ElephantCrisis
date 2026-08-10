#!/usr/bin/env python3
"""Compare two balance_report.json (before/after tuning)."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("before", type=Path)
    p.add_argument("after", type=Path)
    args = p.parse_args()

    b = json.loads(args.before.read_text(encoding="utf-8"))
    a = json.loads(args.after.read_text(encoding="utf-8"))

    roles = sorted(set(b.get("by_role", {})) | set(a.get("by_role", {})))
    print(f"{'role':<10} {'win_b':>8} {'win_a':>8} {'d_win':>8} {'death_b':>8} {'death_a':>8}")
    for r in roles:
        rb = b.get("by_role", {}).get(r, {})
        ra = a.get("by_role", {}).get(r, {})
        wb = float(rb.get("win_rate", 0))
        wa = float(ra.get("win_rate", 0))
        db = float(rb.get("death_rate", 0))
        da = float(ra.get("death_rate", 0))
        print(f"{r:<10} {wb:8.3f} {wa:8.3f} {wa-wb:+8.3f} {db:8.3f} {da:8.3f}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
