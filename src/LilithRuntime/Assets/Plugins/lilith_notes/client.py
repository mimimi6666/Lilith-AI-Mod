"""Approval and host-call client for the ``lilith_notes`` tool."""

from __future__ import annotations

import json
from typing import Any

from tui_gateway.lilith_host_bridge import (
    LilithHostBridgeError,
    normalize_notes_arguments,
    request_notes,
)


_MUTATING_ACTIONS = frozenset(
    {"create_scratch", "create_todo", "update", "delete", "promote", "complete"}
)


def _bounded_summary(text: Any, maximum: int = 80) -> str:
    if not isinstance(text, str):
        return ""
    compact = " ".join(text.split())
    if len(compact) <= maximum:
        return compact
    return compact[: maximum - 1] + "…"


def _approval_reason(arguments: dict) -> str:
    action = arguments["action"]
    memo_id = arguments.get("memo_id")
    if action in {"create_scratch", "create_todo"}:
        kind = "scratch memo" if action == "create_scratch" else "todo"
        summary = _bounded_summary(arguments.get("text"))
        return f"Create a Lilith {kind}: {summary}"
    if action == "update":
        return f"Change Lilith memo #{memo_id}."
    if action == "delete":
        return f"Delete Lilith memo #{memo_id}."
    if action == "promote":
        return f"Convert Lilith scratch memo #{memo_id} into a todo."
    if action == "complete":
        state = "completed" if arguments.get("done") is True else "pending"
        return f"Mark Lilith memo #{memo_id} as {state}."
    return "Change Lilith's official memo book."


def _failure(action: str, code: str, message: str) -> str:
    return json.dumps(
        {
            "succeeded": False,
            "action": action,
            "code": code,
            "message": message,
            "items": [],
        },
        ensure_ascii=False,
        sort_keys=True,
    )


def handle_lilith_notes(arguments: Any, **_kwargs: Any) -> str:
    """Validate, approve mutations, and call the exact current Runtime turn."""

    action = ""
    try:
        normalized = normalize_notes_arguments(arguments)
        action = normalized["action"]
    except LilithHostBridgeError as exc:
        return _failure(action, "invalid_arguments", str(exc))

    if action in _MUTATING_ACTIONS:
        try:
            from tools.approval import request_tool_approval

            decision = request_tool_approval(
                tool_name="lilith_notes",
                reason=_approval_reason(normalized),
                rule_key=f"lilith_notes:{action}",
            )
        except Exception:
            decision = None
        if not (
            isinstance(decision, dict) and decision.get("approved") is True
        ):
            message = (
                decision.get("message")
                if isinstance(decision, dict)
                and isinstance(decision.get("message"), str)
                and decision.get("message")
                else "Lilith Notes approval failed closed"
            )
            return _failure(action, "approval_denied", message)

    try:
        result = request_notes(normalized)
    except LilithHostBridgeError as exc:
        return _failure(action, "host_unavailable", str(exc))
    except Exception:
        return _failure(
            action,
            "host_unavailable",
            "Lilith Notes host request failed closed",
        )
    return json.dumps(result, ensure_ascii=False, sort_keys=True)


__all__ = ["handle_lilith_notes"]
