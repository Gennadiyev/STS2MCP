"""Structured JSONL run logging for the STS2 MCP bridge."""

from __future__ import annotations

import asyncio
import hashlib
import json
import os
import time
import uuid
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from token_usage import TOKEN_FIELDS, TOKEN_SCHEMA_VERSION, TokenEstimator, TokenProfile


SCHEMA_VERSION = "2026-05-10"

DEFAULT_REDACT_KEY_PARTS = (
    "authorization",
    "cookie",
    "password",
    "secret",
    "api_key",
    "apikey",
)

DEFAULT_REDACT_TOKEN_KEYS = (
    "token",
    "access_token",
    "auth_token",
    "bearer_token",
    "refresh_token",
    "session_token",
)


class RunLogger:
    """Append-only JSONL logger with stable event envelopes."""

    def __init__(
        self,
        *,
        enabled: bool = False,
        log_dir: str | os.PathLike[str] = "logs",
        preview_chars: int = 4000,
        include_full_text: bool = False,
        redact_key_parts: tuple[str, ...] = DEFAULT_REDACT_KEY_PARTS,
        token_profile: TokenProfile | None = None,
    ) -> None:
        self.enabled = enabled
        self.log_dir = Path(log_dir)
        self.preview_chars = max(0, preview_chars)
        self.include_full_text = include_full_text
        self.redact_key_parts = tuple(part.lower() for part in redact_key_parts)
        self.token_estimator = TokenEstimator(token_profile)
        self.token_profile = self.token_estimator.profile
        self.run_id = datetime.now(UTC).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8]
        self.path = self.log_dir / f"run_{self.run_id}.jsonl"
        self.summary_path = self.log_dir / f"run_{self.run_id}.summary.json"
        self._sequence = 0
        self._started_at = time.monotonic()
        self._lock = asyncio.Lock()
        self._turn_indices: dict[str, int] = {}
        self._summary = self._new_summary()

    def start(self, metadata: dict[str, Any] | None = None) -> None:
        if not self.enabled:
            return

        self.log_dir.mkdir(parents=True, exist_ok=True)
        payload = {
            "log_path": str(self.path),
            "summary_path": str(self.summary_path),
            "metadata": self.redact(metadata or {}),
            "token_profile": self.token_profile.to_dict(),
        }
        self._summary["session"] = payload
        record = self._envelope(
            "session_start",
            payload,
            self._infer_token_usage("session_start", payload, None),
        )
        self._write_sync(record)
        self._update_summary(record)
        self.write_summary()

    def _new_summary(self) -> dict[str, Any]:
        totals = {field: 0 for field in TOKEN_FIELDS}
        return {
            "schema_version": SCHEMA_VERSION,
            "token_schema_version": TOKEN_SCHEMA_VERSION,
            "run_id": self.run_id,
            "log_path": str(self.path),
            "summary_path": str(self.summary_path),
            "token_profile": self.token_profile.to_dict(),
            "totals": totals.copy(),
            "by_tool_name": {},
            "by_event_type": {},
            "by_state_type": {},
            "by_game_mode": {},
            "by_action_category": {},
            "by_run_phase": {},
            "by_floor": {},
            "by_token_source": {},
            "polling": {
                "poll_events": 0,
                "hidden_poll_events": 0,
                "hidden_poll_tokens": 0,
            },
            "largest_payloads": [],
            "external_usage": {
                "records": 0,
                "total_tokens": 0,
                "reconciled_tool_call_ids": [],
                "reconciled_event_ids": [],
            },
            "invalid_actions": {
                "events": 0,
                "tokens": 0,
            },
            "repeated_states": {
                "events": 0,
                "tokens": 0,
            },
            "replay_artifacts": {
                "log_path": str(self.path),
                "summary_path": str(self.summary_path),
                "log_bytes": 0,
                "summary_bytes": 0,
                "summary_estimated_tokens": 0,
            },
            "conversation": {
                "total_prompts": 0,
                "total_turns": 0,
                "total_messages": 0,
                "model_messages": 0,
                "tool_result_messages": 0,
                "agent_decision_messages": 0,
                "average_tokens_per_turn": 0.0,
                "tool_result_token_share": 0.0,
                "hidden_polling_token_share": 0.0,
                "by_turn": {},
                "by_role": {},
                "by_source": {},
                "largest_prompts": [],
            },
        }

    def write_summary(self) -> None:
        if not self.enabled:
            return
        self._summary["replay_artifacts"]["log_bytes"] = self.path.stat().st_size if self.path.exists() else 0
        summary_preview = dict(self._summary)
        summary_text = json.dumps(summary_preview, ensure_ascii=False, sort_keys=True, default=str)
        self._summary["replay_artifacts"]["summary_estimated_tokens"] = self.token_estimator.estimate_text(summary_text)
        self._write_summary_once()
        summary_bytes = self.summary_path.stat().st_size
        if self._summary["replay_artifacts"]["summary_bytes"] != summary_bytes:
            self._summary["replay_artifacts"]["summary_bytes"] = summary_bytes
            self._write_summary_once()

    def _write_summary_once(self) -> None:
        with self.summary_path.open("w", encoding="utf-8") as handle:
            json.dump(
                self._summary,
                handle,
                ensure_ascii=False,
                indent=2,
                sort_keys=True,
                default=str,
            )
            handle.write("\n")

    async def log(
        self,
        event_type: str,
        payload: dict[str, Any] | None = None,
        *,
        tool_call_id: str | None = None,
        tool_name: str | None = None,
        token_usage: dict[str, Any] | None = None,
    ) -> None:
        if not self.enabled:
            return

        async with self._lock:
            redacted_payload = self.redact(payload or {})
            record = self._envelope(
                event_type,
                redacted_payload,
                self._infer_token_usage(event_type, redacted_payload, token_usage),
                tool_call_id=tool_call_id,
                tool_name=tool_name,
            )
            self._write_sync(record)
            self._update_summary(record)
            self.write_summary()

    def resolve_turn(self, turn_id: str | None = None, turn_index: int | None = None) -> tuple[str, int]:
        if turn_id is None or not str(turn_id).strip():
            if turn_index is not None:
                turn_id = f"turn-{turn_index}"
            else:
                turn_id = f"turn-{len(self._turn_indices) + 1}"
        turn_id = str(turn_id)
        if turn_id not in self._turn_indices:
            self._turn_indices[turn_id] = int(turn_index) if turn_index is not None else len(self._turn_indices) + 1
        return turn_id, self._turn_indices[turn_id]

    def redact(self, value: Any) -> Any:
        if isinstance(value, dict):
            redacted: dict[str, Any] = {}
            for key, item in value.items():
                key_text = str(key)
                if self._is_sensitive_key(key_text):
                    redacted[key_text] = "[REDACTED]"
                else:
                    redacted[key_text] = self.redact(item)
            return redacted
        if isinstance(value, list):
            return [self.redact(item) for item in value]
        if isinstance(value, tuple):
            return [self.redact(item) for item in value]
        return value

    def summarize_text(self, text: str | bytes | None) -> dict[str, Any]:
        if text is None:
            return {"length": 0, "sha256": None, "preview": ""}
        if isinstance(text, bytes):
            raw = text
            display = text.decode("utf-8", errors="replace")
        else:
            display = text
            raw = text.encode("utf-8", errors="replace")

        summary: dict[str, Any] = {
            "length": len(display),
            "bytes": len(raw),
            "sha256": hashlib.sha256(raw).hexdigest(),
            "preview": display[: self.preview_chars],
            "truncated": len(display) > self.preview_chars,
            "estimated_tokens": self.token_estimator.estimate_text(display),
        }
        if self.include_full_text:
            summary["text"] = display
        return summary

    def summarize_jsonable(self, value: Any) -> dict[str, Any]:
        redacted = self.redact(value)
        try:
            serialized = json.dumps(redacted, ensure_ascii=False, sort_keys=True, default=str)
        except TypeError:
            serialized = json.dumps(str(redacted), ensure_ascii=False)

        summary = self.summarize_text(serialized)
        summary["json_type"] = type(value).__name__
        return summary

    def _envelope(
        self,
        event_type: str,
        payload: dict[str, Any],
        token_usage: dict[str, Any],
        *,
        tool_call_id: str | None = None,
        tool_name: str | None = None,
    ) -> dict[str, Any]:
        self._sequence += 1
        event_id = f"{self.run_id}:{self._sequence}"
        envelope: dict[str, Any] = {
            "schema_version": SCHEMA_VERSION,
            "token_schema_version": TOKEN_SCHEMA_VERSION,
            "run_id": self.run_id,
            "event_id": event_id,
            "sequence": self._sequence,
            "timestamp": datetime.now(UTC).isoformat(timespec="milliseconds").replace("+00:00", "Z"),
            "monotonic_ms": round((time.monotonic() - self._started_at) * 1000, 3),
            "event_type": event_type,
            "payload": payload,
        }
        envelope.update(token_usage)
        if tool_call_id is not None:
            envelope["tool_call_id"] = tool_call_id
        if tool_name is not None:
            envelope["tool_name"] = tool_name
        return envelope

    def _infer_token_usage(
        self,
        event_type: str,
        payload: dict[str, Any],
        supplied: dict[str, Any] | None,
    ) -> dict[str, Any]:
        if supplied is not None:
            usage = self.token_estimator.empty_usage(source=supplied.get("token_source"))
            usage.update(supplied)
            usage["total_tokens"] = int(
                supplied.get("total_tokens")
                if supplied.get("total_tokens") is not None
                else sum(int(usage.get(field) or 0) for field in TOKEN_FIELDS if field != "total_tokens")
            )
            return usage

        if event_type == "tool_call_start":
            return self.token_estimator.usage(
                input_tokens=self.token_estimator.estimate_jsonable(payload.get("args", {}))
            )
        if event_type == "tool_call_result":
            return self.token_estimator.usage(
                tool_response_tokens=self._summary_estimated_tokens(payload.get("result"))
            )
        if event_type == "tool_call_error":
            return self.token_estimator.usage(output_tokens=self.token_estimator.estimate_jsonable(payload))
        if event_type == "http_request":
            return self.token_estimator.usage(input_tokens=self.token_estimator.estimate_jsonable(payload))
        if event_type == "http_response":
            return self.token_estimator.usage(output_tokens=self._summary_estimated_tokens(payload.get("response")))
        if event_type == "http_error":
            return self.token_estimator.usage(output_tokens=self.token_estimator.estimate_jsonable(payload))
        if event_type == "agent_decision":
            return self.token_estimator.usage(input_tokens=self.token_estimator.estimate_jsonable(payload))
        if event_type == "state_poll":
            return self.token_estimator.usage(hidden_poll_tokens=int(payload.get("hidden_poll_tokens") or 0))
        if event_type == "external_token_usage":
            return TokenEstimator.normalize_external_usage(payload, self.token_profile)
        if event_type == "model_message":
            return self._model_message_usage(payload)

        return self.token_estimator.empty_usage()

    def _model_message_usage(self, payload: dict[str, Any]) -> dict[str, Any]:
        supplied = payload.get("exact_token_usage")
        if isinstance(supplied, dict):
            return TokenEstimator.normalize_external_usage(supplied, self.token_profile)

        content_tokens = self._summary_estimated_tokens(payload.get("content"))
        role = str(payload.get("role") or "external").lower()
        if role == "assistant":
            return self.token_estimator.usage(output_tokens=content_tokens)
        if role == "tool":
            return self.token_estimator.usage(tool_response_tokens=content_tokens)
        return self.token_estimator.usage(input_tokens=content_tokens)

    @staticmethod
    def _summary_estimated_tokens(summary: Any) -> int:
        return int(summary.get("estimated_tokens") or 0) if isinstance(summary, dict) else 0

    def _update_summary(self, record: dict[str, Any]) -> None:
        usage = {field: int(record.get(field) or 0) for field in TOKEN_FIELDS}
        for field, value in usage.items():
            self._summary["totals"][field] += value

        context = self._record_context(record)
        self._add_group_tokens(self._summary["by_event_type"], record["event_type"], usage)
        if tool_name := record.get("tool_name"):
            self._add_group_tokens(self._summary["by_tool_name"], str(tool_name), usage)
        for key, group_name in (
            ("state_type", "by_state_type"),
            ("game_mode", "by_game_mode"),
            ("action_category", "by_action_category"),
            ("run_phase", "by_run_phase"),
            ("floor", "by_floor"),
        ):
            if value := context.get(key):
                self._add_group_tokens(self._summary[group_name], str(value), usage)
        self._add_group_tokens(self._summary["by_token_source"], str(record.get("token_source") or "unknown"), usage)

        if record["event_type"] == "state_poll":
            self._summary["polling"]["poll_events"] += 1
            if usage["hidden_poll_tokens"] > 0:
                self._summary["polling"]["hidden_poll_events"] += 1
                self._summary["polling"]["hidden_poll_tokens"] += usage["hidden_poll_tokens"]
            if context.get("state_repeated"):
                self._summary["repeated_states"]["events"] += 1
                self._summary["repeated_states"]["tokens"] += usage["total_tokens"]

        if record["event_type"] == "external_token_usage":
            self._summary["external_usage"]["records"] += 1
            self._summary["external_usage"]["total_tokens"] += usage["total_tokens"]
            payload = record.get("payload", {})
            if isinstance(payload, dict):
                if payload.get("related_tool_call_id"):
                    self._summary["external_usage"]["reconciled_tool_call_ids"].append(payload["related_tool_call_id"])
                if payload.get("related_event_id"):
                    self._summary["external_usage"]["reconciled_event_ids"].append(payload["related_event_id"])
                if payload.get("related_message_id"):
                    self._summary["external_usage"].setdefault("reconciled_message_ids", []).append(payload["related_message_id"])

        if context.get("invalid_action"):
            self._summary["invalid_actions"]["events"] += 1
            self._summary["invalid_actions"]["tokens"] += usage["total_tokens"]

        if record["event_type"] == "agent_decision":
            self._summary["conversation"]["agent_decision_messages"] += 1
        if record["event_type"] == "model_message":
            self._update_conversation_summary(record, usage)

        self._update_conversation_derived()
        self._track_largest_payload(record, context)

    @staticmethod
    def _add_group_tokens(group: dict[str, Any], key: str, usage: dict[str, int]) -> None:
        bucket = group.setdefault(key, {field: 0 for field in TOKEN_FIELDS} | {"events": 0})
        bucket["events"] += 1
        for field, value in usage.items():
            bucket[field] += value

    def _record_context(self, record: dict[str, Any]) -> dict[str, Any]:
        payload = record.get("payload", {})
        context: dict[str, Any] = {
            "action_category": self._action_category(record.get("tool_name")),
        }
        if isinstance(payload, dict):
            for key in ("state_type", "game_mode", "run_phase"):
                if payload.get(key) is not None:
                    context[key] = payload[key]
            if payload.get("state_repeated") is True or payload.get("reason") == "state_repeated":
                context["state_repeated"] = True
            if str(payload.get("status") or "").lower() == "error":
                context["invalid_action"] = True

            for summary_key in ("result", "response"):
                parsed = self._parse_summary_json(payload.get(summary_key))
                if isinstance(parsed, dict):
                    for key in ("state_type", "game_mode"):
                        if parsed.get(key) is not None:
                            context.setdefault(key, parsed[key])
                    floor = self._extract_floor(parsed)
                    if floor is not None:
                        context.setdefault("floor", floor)
                    if parsed.get("status") == "error":
                        context["invalid_action"] = True

        if "state_type" in context:
            context.setdefault("run_phase", self._run_phase_from_state(str(context["state_type"])))
        return context

    @staticmethod
    def _extract_floor(state: dict[str, Any]) -> int | None:
        run = state.get("run")
        if not isinstance(run, dict):
            return None
        for key in ("floor", "current_floor", "floor_num"):
            value = run.get(key)
            if isinstance(value, int):
                return value
        return None

    @staticmethod
    def _parse_summary_json(summary: Any) -> Any:
        if not isinstance(summary, dict) or summary.get("truncated"):
            return None
        text = summary.get("text") or summary.get("preview")
        if not isinstance(text, str) or not text:
            return None
        try:
            return json.loads(text)
        except json.JSONDecodeError:
            return None

    @staticmethod
    def _action_category(tool_name: Any) -> str | None:
        if not isinstance(tool_name, str):
            return None
        normalized = tool_name.removeprefix("mp_")
        for prefix, category in (
            ("combat_", "combat"),
            ("rewards_", "reward"),
            ("map_", "map"),
            ("event_", "event"),
            ("rest_", "rest_site"),
            ("shop_", "shop"),
            ("deck_", "card_select"),
            ("bundle_", "bundle_select"),
            ("relic_", "relic_select"),
            ("treasure_", "treasure"),
            ("crystal_sphere_", "crystal_sphere"),
            ("menu_", "menu"),
        ):
            if normalized.startswith(prefix):
                return category
        if normalized in {"get_game_state", "log_agent_decision", "log_external_token_usage"}:
            return "general"
        return normalized

    @staticmethod
    def _run_phase_from_state(state_type: str) -> str:
        if state_type in {"monster", "elite", "boss", "hand_select"}:
            return "combat"
        if state_type in {"rewards", "card_reward"}:
            return "reward"
        if state_type in {"menu", "game_over"}:
            return "menu"
        return state_type

    def _track_largest_payload(self, record: dict[str, Any], context: dict[str, Any]) -> None:
        candidates: list[tuple[str, dict[str, Any]]] = []
        payload = record.get("payload", {})
        if isinstance(payload, dict):
            for key in ("result", "response"):
                value = payload.get(key)
                if isinstance(value, dict) and value.get("estimated_tokens"):
                    candidates.append((key, value))
        for payload_kind, summary in candidates:
            entry = {
                "event_id": record["event_id"],
                "event_type": record["event_type"],
                "tool_name": record.get("tool_name"),
                "payload_kind": payload_kind,
                "estimated_tokens": int(summary.get("estimated_tokens") or 0),
                "length": int(summary.get("length") or 0),
                "sha256": summary.get("sha256"),
                "state_type": context.get("state_type"),
                "game_mode": context.get("game_mode"),
            }
            largest = self._summary["largest_payloads"]
            largest.append(entry)
            largest.sort(key=lambda item: item["estimated_tokens"], reverse=True)
            del largest[10:]

    def _update_conversation_summary(self, record: dict[str, Any], usage: dict[str, int]) -> None:
        payload = record.get("payload", {})
        if not isinstance(payload, dict):
            return

        conversation = self._summary["conversation"]
        role = str(payload.get("role") or "external").lower()
        source = str(payload.get("source") or "unknown")
        turn_id = str(payload.get("turn_id") or "turn-unknown")
        turn_index = int(payload.get("turn_index") or 0)

        conversation["total_messages"] += 1
        if role in {"system", "user", "developer", "external"}:
            conversation["total_prompts"] += 1
        if role == "assistant":
            conversation["model_messages"] += 1
        if role == "tool":
            conversation["tool_result_messages"] += 1

        self._add_group_tokens(conversation["by_role"], role, usage)
        self._add_group_tokens(conversation["by_source"], source, usage)

        turn = conversation["by_turn"].setdefault(
            turn_id,
            {
                "turn_id": turn_id,
                "turn_index": turn_index,
                "start_timestamp": record["timestamp"],
                "end_timestamp": record["timestamp"],
                "start_monotonic_ms": record["monotonic_ms"],
                "end_monotonic_ms": record["monotonic_ms"],
                "elapsed_ms": 0,
                "event_ids": [],
                "message_ids": [],
                "related_tool_call_ids": [],
                "state_hashes": [],
                "roles": {},
                **{field: 0 for field in TOKEN_FIELDS},
            },
        )
        turn["start_timestamp"] = min(turn["start_timestamp"], record["timestamp"])
        turn["end_timestamp"] = max(turn["end_timestamp"], record["timestamp"])
        turn["start_monotonic_ms"] = min(float(turn["start_monotonic_ms"]), float(record["monotonic_ms"]))
        turn["end_monotonic_ms"] = max(float(turn["end_monotonic_ms"]), float(record["monotonic_ms"]))
        turn["elapsed_ms"] = round(float(turn["end_monotonic_ms"]) - float(turn["start_monotonic_ms"]), 3)
        turn["event_ids"].append(record["event_id"])
        if payload.get("message_id"):
            turn["message_ids"].append(payload["message_id"])
        if payload.get("related_tool_call_id"):
            turn["related_tool_call_ids"].append(payload["related_tool_call_id"])
        if payload.get("state_sha256"):
            turn["state_hashes"].append(payload["state_sha256"])
        turn["roles"][role] = turn["roles"].get(role, 0) + 1
        for field, value in usage.items():
            turn[field] += value

        content = payload.get("content")
        content_tokens = self._summary_estimated_tokens(content)
        if role in {"system", "user", "developer", "external"} and content_tokens:
            prompt_entry = {
                "event_id": record["event_id"],
                "message_id": payload.get("message_id"),
                "turn_id": turn_id,
                "turn_index": turn_index,
                "role": role,
                "source": source,
                "estimated_tokens": content_tokens,
                "length": content.get("length") if isinstance(content, dict) else None,
                "sha256": content.get("sha256") if isinstance(content, dict) else None,
                "privacy": payload.get("privacy", {}),
            }
            largest_prompts = conversation["largest_prompts"]
            largest_prompts.append(prompt_entry)
            largest_prompts.sort(key=lambda item: item["estimated_tokens"], reverse=True)
            del largest_prompts[10:]

    def _update_conversation_derived(self) -> None:
        conversation = self._summary["conversation"]
        conversation["total_turns"] = len(conversation["by_turn"])
        total_tokens = int(self._summary["totals"]["total_tokens"] or 0)
        turn_count = int(conversation["total_turns"] or 0)
        conversation["average_tokens_per_turn"] = round(total_tokens / turn_count, 3) if turn_count else 0.0
        conversation["tool_result_token_share"] = (
            round(self._summary["totals"]["tool_response_tokens"] / total_tokens, 6) if total_tokens else 0.0
        )
        conversation["hidden_polling_token_share"] = (
            round(self._summary["totals"]["hidden_poll_tokens"] / total_tokens, 6) if total_tokens else 0.0
        )

    def _write_sync(self, record: dict[str, Any]) -> None:
        with self.path.open("a", encoding="utf-8") as handle:
            json.dump(record, handle, ensure_ascii=False, separators=(",", ":"), default=str)
            handle.write("\n")

    def _is_sensitive_key(self, key: str) -> bool:
        key_lower = key.lower()
        return key_lower in DEFAULT_REDACT_TOKEN_KEYS or any(part in key_lower for part in self.redact_key_parts)
