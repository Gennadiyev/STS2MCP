"""Lightweight JSONL run logger for STS2 MCP sessions."""

import json
import atexit
from datetime import datetime
from pathlib import Path

_log_dir = Path(__file__).resolve().parent.parent / "logs"
_log_file = None


def _ensure_log_file():
    global _log_file
    if _log_file is None:
        _log_dir.mkdir(exist_ok=True)
        ts = datetime.now().strftime("%Y%m%d_%H%M%S")
        _log_file = open(_log_dir / f"run_{ts}.jsonl", "a", encoding="utf-8")
        atexit.register(_log_file.close)
    return _log_file


def log_tool_call(action: str, args: dict, result: str):
    """Log an MCP tool call (action extracted from POST body or GET params)."""
    entry = {
        "ts": datetime.now().isoformat(timespec="seconds"),
        "type": "tool_call",
        "action": action,
        "args": {k: v for k, v in args.items() if k != "action"},
        "result_length": len(result),
        "result_preview": result[:300],
    }
    _write(entry)


def log_decision(context: str, reasoning: str):
    """Log an AI decision with reasoning."""
    entry = {
        "ts": datetime.now().isoformat(timespec="seconds"),
        "type": "decision",
        "context": context,
        "reasoning": reasoning,
    }
    _write(entry)


def _write(entry: dict):
    f = _ensure_log_file()
    f.write(json.dumps(entry, ensure_ascii=False) + "\n")
    f.flush()
