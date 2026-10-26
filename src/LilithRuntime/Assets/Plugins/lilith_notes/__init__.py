"""Hermes registration for Lilith's official memo book."""

from __future__ import annotations

from .client import handle_lilith_notes


LILITH_NOTES_SCHEMA = {
    "name": "lilith_notes",
    "description": (
        "Read and manage Lilith's official in-game memo book. This is the "
        "single source of truth used by the Detailed, Simple, and Scratch "
        "interfaces; the tool does not keep a second Notes database. Use list "
        "or get before changing an existing memo so memo_id and "
        "expected_updated_time come from current game state. Existing-memo "
        "writes fail closed when that timestamp is stale. Scratch priority "
        "inputs use normal, important, or temporary; none is result-only for "
        "todo projection. A new todo cannot enable reminder and countdown "
        "together."
    ),
    "parameters": {
        "type": "object",
        "additionalProperties": False,
        "properties": {
            "action": {
                "type": "string",
                "enum": [
                    "list",
                    "get",
                    "create_scratch",
                    "create_todo",
                    "update",
                    "delete",
                    "promote",
                    "complete",
                ],
                "description": (
                    "The exact memo operation. list/get are read-only; the "
                    "other actions require native user approval."
                ),
            },
            "memo_id": {
                "type": "integer",
                "minimum": 1,
                "description": (
                    "Current official memo id. Required by get, update, "
                    "delete, promote, and complete."
                ),
            },
            "expected_updated_time": {
                "type": "string",
                "maxLength": 128,
                "description": (
                    "Exact updated_time returned by list/get. Required before "
                    "update, delete, promote, or complete."
                ),
            },
            "text": {
                "type": "string",
                "minLength": 1,
                "maxLength": 32768,
                "description": "Memo text for create or update.",
            },
            "date": {
                "type": "string",
                "pattern": "^\\d{4}-\\d{2}-\\d{2}$",
                "description": (
                    "Todo date in yyyy-MM-dd form. It may also select the "
                    "date used by promote or the occurrence used by complete."
                ),
            },
            "query": {
                "type": "string",
                "maxLength": 512,
                "description": "Optional text search used only by list.",
            },
            "memo_kind": {
                "type": "string",
                "enum": ["all", "scratch", "todo"],
                "description": "Memo-kind filter used only by list.",
            },
            "filter": {
                "type": "string",
                "enum": ["all", "pending", "done"],
                "description": "Completion filter used only by list.",
            },
            "scratch_tier": {
                "type": "string",
                "enum": ["normal", "important", "temporary"],
                "description": (
                    "Scratch priority for create_scratch or update. The none "
                    "value appears only in projected todo results and is not "
                    "a valid input."
                ),
            },
            "remind": {
                "type": "boolean",
                "description": (
                    "Enable or disable the official todo reminder. For "
                    "create_todo, remind and countdown cannot both be true."
                ),
            },
            "remind_hour": {
                "type": "integer",
                "minimum": 0,
                "maximum": 23,
                "description": "Reminder hour; supply together with remind_minute.",
            },
            "remind_minute": {
                "type": "integer",
                "minimum": 0,
                "maximum": 59,
                "description": "Reminder minute; supply together with remind_hour.",
            },
            "repeat": {
                "type": "string",
                "enum": ["none", "daily", "weekly", "monthly", "yearly"],
                "description": "Official todo repeat rule.",
            },
            "done": {
                "type": "boolean",
                "description": "Required by complete; true completes, false reopens.",
            },
            "pinned": {
                "type": "boolean",
                "description": "Official todo pinned state.",
            },
            "countdown": {
                "type": "boolean",
                "description": (
                    "Official todo countdown display state. For create_todo, "
                    "countdown and remind cannot both be true."
                ),
            },
            "limit": {
                "type": "integer",
                "minimum": 1,
                "maximum": 50,
                "description": "Maximum list items; defaults to 25.",
            },
        },
        "required": ["action"],
    },
}


def register(ctx) -> None:
    """Register one closed Notes tool in its own opt-in toolset."""

    ctx.register_tool(
        name="lilith_notes",
        toolset="lilith_notes",
        schema=LILITH_NOTES_SCHEMA,
        handler=handle_lilith_notes,
        description=LILITH_NOTES_SCHEMA["description"],
        emoji="📝",
    )


__all__ = ["LILITH_NOTES_SCHEMA", "register"]
