"""Player-owned correction and recoverable deletion for relationship memory."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

from hermes_constants import get_hermes_home

from .domain import RelationshipContractError
from .store import RelationshipStore


def register_cli(subparser: argparse.ArgumentParser) -> None:
    commands = subparser.add_subparsers(dest="lilith_relationship_action")

    list_parser = commands.add_parser("list", help="List relationship episodes and visibility")
    list_parser.add_argument("--limit", type=int, default=50)

    correct_parser = commands.add_parser("correct", help="Append a player correction to an episode")
    correct_parser.add_argument("event_id")
    correct_parser.add_argument("correction")

    delete_parser = commands.add_parser(
        "delete",
        help="Hide an episode from recall without erasing its audit history",
    )
    delete_parser.add_argument("event_id")

    restore_parser = commands.add_parser("restore", help="Restore a hidden episode to recall")
    restore_parser.add_argument("event_id")

    subparser.set_defaults(func=lilith_relationship_command)


def _store() -> RelationshipStore:
    data_dir = Path(get_hermes_home()).resolve() / "plugin-data" / "lilith-relationship"
    return RelationshipStore(data_dir)


def _emit(value: dict[str, Any]) -> None:
    print(json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2))


def lilith_relationship_command(args: argparse.Namespace) -> int:
    action = getattr(args, "lilith_relationship_action", None)
    if action not in {"list", "correct", "delete", "restore"}:
        _emit({"ok": False, "error": "choose list, correct, delete, or restore"})
        return 2
    try:
        store = _store()
        if action == "list":
            records = store.management_records(limit=args.limit)
            _emit({"ok": True, "events": records})
        elif action == "correct":
            correction_id = store.add_operator_correction(args.event_id, args.correction)
            _emit({"ok": True, "event_id": args.event_id, "correction_id": correction_id})
        elif action == "delete":
            action_id = store.set_event_hidden(args.event_id, True)
            _emit(
                {
                    "ok": True,
                    "event_id": args.event_id,
                    "hidden_from_recall": True,
                    "recoverable": True,
                    "action_id": action_id,
                }
            )
        else:
            action_id = store.set_event_hidden(args.event_id, False)
            _emit(
                {
                    "ok": True,
                    "event_id": args.event_id,
                    "hidden_from_recall": False,
                    "action_id": action_id,
                }
            )
        return 0
    except RelationshipContractError as exc:
        _emit({"ok": False, "error": str(exc)})
        return 1


__all__ = ["lilith_relationship_command", "register_cli"]
