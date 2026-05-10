"""Validate STS2 MCP JSONL run logs and token summary artifacts."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

from token_usage import TOKEN_FIELDS, TokenEstimator, TokenProfile


def _load_jsonl(path: Path) -> list[dict[str, Any]]:
    records: list[dict[str, Any]] = []
    with path.open("r", encoding="utf-8") as handle:
        for line_number, line in enumerate(handle, start=1):
            if not line.strip():
                continue
            try:
                record = json.loads(line)
            except json.JSONDecodeError as exc:
                raise ValueError(f"{path}:{line_number}: invalid JSON: {exc}") from exc
            if not isinstance(record, dict):
                raise ValueError(f"{path}:{line_number}: record must be a JSON object")
            records.append(record)
    if not records:
        raise ValueError(f"{path}: no JSONL records found")
    return records


def _usage_from_records(records: list[dict[str, Any]]) -> dict[str, int]:
    totals = {field: 0 for field in TOKEN_FIELDS}
    seen_sequences: set[int] = set()
    seen_event_ids: set[str] = set()
    previous_sequence = 0
    previous_monotonic = -1.0

    for index, record in enumerate(records, start=1):
        sequence = record.get("sequence")
        if not isinstance(sequence, int):
            raise ValueError(f"record {index}: sequence must be an integer")
        if sequence <= previous_sequence:
            raise ValueError(f"record {index}: sequence is not strictly increasing")
        if sequence in seen_sequences:
            raise ValueError(f"record {index}: duplicate sequence {sequence}")
        seen_sequences.add(sequence)
        previous_sequence = sequence

        event_id = record.get("event_id")
        if not isinstance(event_id, str) or not event_id:
            raise ValueError(f"record {index}: missing event_id")
        if event_id in seen_event_ids:
            raise ValueError(f"record {index}: duplicate event_id {event_id}")
        seen_event_ids.add(event_id)

        monotonic_ms = record.get("monotonic_ms")
        if not isinstance(monotonic_ms, int | float):
            raise ValueError(f"record {index}: monotonic_ms must be numeric")
        if monotonic_ms < previous_monotonic:
            raise ValueError(f"record {index}: monotonic_ms decreased")
        previous_monotonic = float(monotonic_ms)

        for field in TOKEN_FIELDS:
            value = record.get(field)
            if not isinstance(value, int):
                raise ValueError(f"record {index}: {field} must be an integer")
            if value < 0:
                raise ValueError(f"record {index}: {field} must be non-negative")
            totals[field] += value

        expected_total = sum(int(record[field]) for field in TOKEN_FIELDS if field != "total_tokens")
        if int(record["total_tokens"]) != expected_total:
            raise ValueError(
                f"record {index}: total_tokens {record['total_tokens']} != component sum {expected_total}"
            )

        if not record.get("token_source"):
            raise ValueError(f"record {index}: missing token_source")
        if not record.get("tokenizer_version"):
            raise ValueError(f"record {index}: missing tokenizer_version")

    return totals


def _validate_summary(summary_path: Path, records: list[dict[str, Any]], totals: dict[str, int]) -> None:
    summary = json.loads(summary_path.read_text(encoding="utf-8"))
    if not isinstance(summary, dict):
        raise ValueError(f"{summary_path}: summary must be a JSON object")
    summary_totals = summary.get("totals")
    if not isinstance(summary_totals, dict):
        raise ValueError(f"{summary_path}: missing totals object")
    for field, expected in totals.items():
        actual = summary_totals.get(field)
        if actual != expected:
            raise ValueError(f"{summary_path}: totals.{field} {actual} != JSONL sum {expected}")
    _validate_conversation_summary(summary_path, records, summary)


def _validate_conversation_summary(summary_path: Path, records: list[dict[str, Any]], summary: dict[str, Any]) -> None:
    conversation = summary.get("conversation")
    if not isinstance(conversation, dict):
        return

    model_messages = [
        record for record in records
        if record.get("event_type") == "model_message" and isinstance(record.get("payload"), dict)
    ]
    turns: dict[str, int] = {}
    message_ids: set[str] = set()
    previous_first_seen_turn_index = 0

    for record in model_messages:
        payload = record["payload"]
        message_id = payload.get("message_id")
        turn_id = payload.get("turn_id")
        turn_index = payload.get("turn_index")
        role = payload.get("role")
        content = payload.get("content")

        if not isinstance(message_id, str) or not message_id:
            raise ValueError(f"record {record['sequence']}: model_message missing message_id")
        if message_id in message_ids:
            raise ValueError(f"record {record['sequence']}: duplicate message_id {message_id}")
        message_ids.add(message_id)

        if not isinstance(turn_id, str) or not turn_id:
            raise ValueError(f"record {record['sequence']}: model_message missing turn_id")
        if not isinstance(turn_index, int) or turn_index < 1:
            raise ValueError(f"record {record['sequence']}: model_message turn_index must be a positive integer")
        if turn_id not in turns:
            if turn_index < previous_first_seen_turn_index:
                raise ValueError(f"record {record['sequence']}: turn_index decreased on first turn observation")
            previous_first_seen_turn_index = turn_index
            turns[turn_id] = turn_index
        elif turns[turn_id] != turn_index:
            raise ValueError(f"record {record['sequence']}: inconsistent turn_index for {turn_id}")

        if role not in {"system", "user", "assistant", "tool", "developer", "external"}:
            raise ValueError(f"record {record['sequence']}: invalid model_message role {role!r}")
        if not isinstance(content, dict) or not content.get("sha256"):
            raise ValueError(f"record {record['sequence']}: model_message content summary missing sha256")

    if conversation.get("total_messages") != len(model_messages):
        raise ValueError(
            f"{summary_path}: conversation.total_messages {conversation.get('total_messages')} != {len(model_messages)}"
        )
    if conversation.get("total_turns") != len(turns):
        raise ValueError(
            f"{summary_path}: conversation.total_turns {conversation.get('total_turns')} != {len(turns)}"
        )

    external_related_messages = [
        record.get("payload", {}).get("related_message_id")
        for record in records
        if record.get("event_type") == "external_token_usage" and isinstance(record.get("payload"), dict)
    ]
    for related_message_id in external_related_messages:
        if related_message_id and related_message_id not in message_ids:
            raise ValueError(f"{summary_path}: external usage references unknown message_id {related_message_id}")


def _self_test() -> None:
    estimator = TokenEstimator(TokenProfile.from_profile_name("generic_regex_v1"))
    fixture = "Deal 6 damage."
    expected = 4
    actual = estimator.estimate_text(fixture)
    if actual != expected:
        raise ValueError(f"token fixture mismatch: {fixture!r} expected {expected}, got {actual}")

    usage = estimator.usage(input_tokens=2, output_tokens=3, tool_response_tokens=5, hidden_poll_tokens=7)
    if usage["total_tokens"] != 17:
        raise ValueError(f"usage rollup mismatch: {usage}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Validate STS2 MCP run log token accounting")
    parser.add_argument("log_path", nargs="?", help="Path to run_*.jsonl")
    parser.add_argument("--summary", help="Path to run_*.summary.json")
    parser.add_argument("--self-test", action="store_true", help="Run deterministic tokenizer fixture checks")
    args = parser.parse_args()

    if args.self_test:
        _self_test()

    if not args.log_path:
        if args.self_test:
            print("self-test ok")
            return
        raise SystemExit("log_path is required unless --self-test is used")

    log_path = Path(args.log_path)
    records = _load_jsonl(log_path)
    totals = _usage_from_records(records)
    summary_path = Path(args.summary) if args.summary else log_path.with_name(log_path.name.replace(".jsonl", ".summary.json"))
    if summary_path.exists():
        _validate_summary(summary_path, records, totals)
    print(json.dumps({"status": "ok", "records": len(records), "totals": totals}, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
