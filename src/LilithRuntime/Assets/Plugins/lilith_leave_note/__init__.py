"""Hermes-visible image-note capability, separate from the official MemoBook."""

from .client import handle_lilith_leave_note

LILITH_LEAVE_NOTE_SCHEMA = {
    "name": "lilith_leave_note",
    "description": (
        "Leave one short, personal image note for the player in Lilith's game NoteInbox. "
        "Write the final note yourself (roughly 45–110 natural words or equivalent characters; "
        "a short localized Lilith signature is welcome). The game renders and saves the PNG; "
        "this tool does not speak, send a chat message, create a MemoBook task, schedule work, "
        "or generate another model response. Requires an enabled game-owned turn or an explicit "
        "Runtime-issued daily-note lease; ordinary workbench/background requests have no write authority. "
        "deliver requests one image per authorized turn or lease; repeats only query the same delivery. "
        "status queries that scope's existing receipt without granting a new write. "
        "Only status=delivered with an artifact proves success. Unknown/deferred/not_found "
        "are not success: do not claim delivery or blindly retry in another turn."
    ),
    "parameters": {
        "type": "object",
        "properties": {
            "action": {"type": "string", "enum": ["deliver", "status"]},
            "locale": {"type": "string", "enum": ["zh-TW", "zh-CN", "ja", "en"]},
            "text": {
                "type": "string", "minLength": 1, "maxLength": 1200,
                "description": "Final note text, required only for deliver; omit for status. No paths or instructions to local tools.",
            },
        },
        "required": ["action", "locale"],
        "additionalProperties": False,
    },
}


def register(ctx):
    ctx.register_tool(
        name="lilith_leave_note", toolset="lilith_leave_note",
        schema=LILITH_LEAVE_NOTE_SCHEMA, handler=handle_lilith_leave_note,
        description="Leave a verified image note in Lilith's game NoteInbox.", emoji="💌",
    )
