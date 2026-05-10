"""Deterministic token accounting helpers for STS2 MCP run logs."""

from __future__ import annotations

import json
import re
from dataclasses import asdict, dataclass
from typing import Any


TOKEN_SCHEMA_VERSION = "2026-05-10"

TOKEN_FIELDS = (
    "input_tokens",
    "output_tokens",
    "tool_response_tokens",
    "hidden_poll_tokens",
    "total_tokens",
)

_TOKEN_PATTERN = re.compile(r"\w+|[^\w\s]", re.UNICODE)


@dataclass(frozen=True)
class TokenProfile:
    """Describes how local token counts were produced."""

    profile_name: str = "generic_regex_v1"
    model_family: str = "generic"
    tokenizer_name: str = "sts2_regex"
    tokenizer_version: str = TOKEN_SCHEMA_VERSION
    estimation_method: str = "regex_words_and_punctuation_v1"
    token_source: str = "estimated"

    @classmethod
    def from_profile_name(cls, profile_name: str) -> "TokenProfile":
        normalized = profile_name.strip().lower()
        profiles = {
            "generic": cls(profile_name="generic_regex_v1"),
            "generic_regex_v1": cls(profile_name="generic_regex_v1"),
            "openai": cls(
                profile_name="openai_cl100k_proxy",
                model_family="openai",
                tokenizer_name="cl100k_proxy_regex",
            ),
            "openai_cl100k_proxy": cls(
                profile_name="openai_cl100k_proxy",
                model_family="openai",
                tokenizer_name="cl100k_proxy_regex",
            ),
            "anthropic": cls(
                profile_name="anthropic_claude_proxy",
                model_family="anthropic",
                tokenizer_name="claude_proxy_regex",
            ),
            "anthropic_claude_proxy": cls(
                profile_name="anthropic_claude_proxy",
                model_family="anthropic",
                tokenizer_name="claude_proxy_regex",
            ),
        }
        return profiles.get(normalized, cls(profile_name=profile_name))

    def with_overrides(
        self,
        *,
        model_family: str | None = None,
        tokenizer_name: str | None = None,
        tokenizer_version: str | None = None,
        estimation_method: str | None = None,
    ) -> "TokenProfile":
        return TokenProfile(
            profile_name=self.profile_name,
            model_family=model_family or self.model_family,
            tokenizer_name=tokenizer_name or self.tokenizer_name,
            tokenizer_version=tokenizer_version or self.tokenizer_version,
            estimation_method=estimation_method or self.estimation_method,
            token_source=self.token_source,
        )

    def to_dict(self) -> dict[str, str]:
        return asdict(self)


class TokenEstimator:
    """Small deterministic estimator used when exact model usage is unavailable."""

    def __init__(self, profile: TokenProfile | None = None) -> None:
        self.profile = profile or TokenProfile()

    def estimate_text(self, text: str | bytes | None) -> int:
        if text is None:
            return 0
        if isinstance(text, bytes):
            text = text.decode("utf-8", errors="replace")
        if not text:
            return 0
        return len(_TOKEN_PATTERN.findall(text))

    def estimate_jsonable(self, value: Any) -> int:
        try:
            text = json.dumps(value, ensure_ascii=False, sort_keys=True, default=str)
        except TypeError:
            text = json.dumps(str(value), ensure_ascii=False)
        return self.estimate_text(text)

    def empty_usage(self, *, source: str | None = None) -> dict[str, Any]:
        usage: dict[str, Any] = {field: 0 for field in TOKEN_FIELDS}
        usage.update(
            {
                "token_source": source or self.profile.token_source,
                "tokenizer_name": self.profile.tokenizer_name,
                "tokenizer_version": self.profile.tokenizer_version,
                "model_family": self.profile.model_family,
                "estimation_method": self.profile.estimation_method,
            }
        )
        return usage

    def usage(
        self,
        *,
        input_tokens: int = 0,
        output_tokens: int = 0,
        tool_response_tokens: int = 0,
        hidden_poll_tokens: int = 0,
        source: str | None = None,
        overrides: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        usage = self.empty_usage(source=source)
        usage["input_tokens"] = max(0, int(input_tokens or 0))
        usage["output_tokens"] = max(0, int(output_tokens or 0))
        usage["tool_response_tokens"] = max(0, int(tool_response_tokens or 0))
        usage["hidden_poll_tokens"] = max(0, int(hidden_poll_tokens or 0))
        usage["total_tokens"] = (
            usage["input_tokens"]
            + usage["output_tokens"]
            + usage["tool_response_tokens"]
            + usage["hidden_poll_tokens"]
        )
        if overrides:
            for key, value in overrides.items():
                if value is not None:
                    usage[key] = value
        return usage

    @staticmethod
    def normalize_external_usage(
        payload: dict[str, Any],
        fallback_profile: TokenProfile | None = None,
    ) -> dict[str, Any]:
        profile = fallback_profile or TokenProfile()
        usage = {
            "input_tokens": int(payload.get("input_tokens") or payload.get("prompt_tokens") or 0),
            "output_tokens": int(payload.get("output_tokens") or payload.get("completion_tokens") or 0),
            "tool_response_tokens": int(payload.get("tool_response_tokens") or 0),
            "hidden_poll_tokens": int(payload.get("hidden_poll_tokens") or 0),
            "token_source": payload.get("token_source") or "external",
            "tokenizer_name": payload.get("tokenizer_name") or payload.get("model") or profile.tokenizer_name,
            "tokenizer_version": payload.get("tokenizer_version") or profile.tokenizer_version,
            "model_family": payload.get("model_family") or profile.model_family,
            "estimation_method": payload.get("estimation_method") or "externally_supplied",
        }
        supplied_total = payload.get("total_tokens")
        usage["total_tokens"] = (
            int(supplied_total)
            if supplied_total is not None
            else usage["input_tokens"]
            + usage["output_tokens"]
            + usage["tool_response_tokens"]
            + usage["hidden_poll_tokens"]
        )
        return usage
