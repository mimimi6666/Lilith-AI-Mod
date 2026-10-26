"""One native-approved typed delivery request; never a provider or file writer."""

from __future__ import annotations

import json
from typing import Any

from tui_gateway.lilith_host_bridge import LilithHostBridgeError
from tui_gateway.lilith_noteinbox_bridge import normalize_note_arguments, request_note


def _failure(code: str) -> str:
    # This is a tool failure, not a forged host delivery receipt. No minted IDs.
    return json.dumps({"status": "failed", "code": code, "artifact": None}, sort_keys=True)


def handle_lilith_leave_note(arguments: Any, **_kwargs: Any) -> str:
    try:
        normalized = normalize_note_arguments(arguments)
    except LilithHostBridgeError:
        return _failure("invalid_arguments")
    if normalized["action"] == "deliver":
        try:
            from tools.approval import request_tool_approval
            decision = request_tool_approval(
                tool_name="lilith_leave_note",
                reason="Save one Lilith image note in the game's NoteInbox.",
                rule_key="lilith_leave_note:deliver",
            )
        except Exception:
            decision = None
        if not isinstance(decision, dict) or decision.get("approved") is not True:
            return _failure("approval_denied")
    try:
        result = request_note(normalized)
    except Exception:
        # Emission may already have reached the authoritative writer. Never
        # claim absence, retry, expose exception text, or invent an artifact.
        return json.dumps({"status": "unknown", "code": "host_unavailable", "artifact": None}, sort_keys=True)
    return json.dumps(result, ensure_ascii=False, sort_keys=True)
