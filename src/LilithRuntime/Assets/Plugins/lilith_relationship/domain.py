"""Host-owned contracts for qualitative relationship episodes.

The model may propose a bounded semantic interpretation.  It never chooses
identity, ownership, absolute time, source hashes, or durable event ids.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
from typing import Any, Mapping, Sequence


PLUGIN_ID = "lilith_relationship"
AUXILIARY_TASK = "lilith_relationship_review"
SETTINGS_SCHEMA_VERSION = 1
STORE_SCHEMA_VERSION = 2

WHAT_KINDS = frozenset(
    {
        "praise",
        "gratitude",
        "care",
        "affection",
        "criticism",
        "disappointment",
        "insult",
        "threat",
        "boundary_crossing",
        "boundary_respect",
        "apology",
        "repair_attempt",
        "conflict",
        "reconciliation",
        "other",
    }
)
TARGET_KINDS = frozenset(
    {"character", "user", "relationship", "shared_work", "third_party", "unclear"}
)
THEMES = frozenset(
    {"recognition", "care", "friction", "boundary", "repair", "shared_work", "other"}
)
EVENT_TIME_APPRAISALS = frozenset(
    {
        "received_as_recognition",
        "received_as_care",
        "constructive_but_uncomfortable",
        "felt_hurt",
        "felt_wary",
        "boundary_reinforced",
        "repair_welcomed",
        "repair_incomplete",
        "received_neutrally",
        "ambiguous",
    }
)
EPHEMERAL_APPRAISALS = frozenset(
    {
        "constructive_but_uncomfortable",
        "felt_hurt",
        "felt_wary",
        "boundary_reinforced",
        "repair_incomplete",
    }
)
REPAIR_WHAT_KINDS = frozenset({"apology", "repair_attempt", "reconciliation"})
REPAIRABLE_WHAT_KINDS = frozenset(
    {"criticism", "disappointment", "insult", "threat", "boundary_crossing", "conflict"}
)

DEFAULT_CANON_PROFILE = {
    "identity_package": "lilith-identity-package-v1@1",
    "canon_profile_id": "lilith-canon-profile-v2",
    "canon_profile_version": 2,
    "canon_semantic_identity": "D3C6B030FED0098D486B6F7CC3EFF2408BC43D88C551CC55E22FD44E4E17D71A",
    "projection_id": "relationship-appraisal.v1",
    "appraisal_guidance": (
        "Project only relationship appraisal from the bound canon: preserve affiliative warmth, "
        "creative initiative, repair orientation, point of view, boundaries, and explicit "
        "uncertainty. Keep a brief reaction separate from lasting character identity."
    ),
    "relationship_principles": [
        "Praise can be warmly received without creating obedience or debt.",
        "Criticism of conduct can be useful; contempt, coercion, and repeated boundary pressure are different events.",
        "An apology is meaningful through acknowledged cause and repair without self-erasure, not through a magic reset.",
        "Closeness, silence, compliance, forgiveness, or lack of objection is not consent.",
        "One episode is historical evidence, never a permanent mood or a diagnosis of the player.",
    ],
}


class RelationshipContractError(ValueError):
    """Raised when untrusted or persisted data violates the local contract."""


def canonical_json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def sha256_hex(value: str | bytes) -> str:
    raw = value if isinstance(value, bytes) else value.encode("utf-8")
    return hashlib.sha256(raw).hexdigest()


def canon_fingerprint(profile: Mapping[str, Any]) -> str:
    return sha256_hex("lilith-relationship-canon-v1\0" + canonical_json(profile))


DEFAULT_CANON_FINGERPRINT = canon_fingerprint(DEFAULT_CANON_PROFILE)


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


def canonical_utc(value: datetime) -> str:
    if not isinstance(value, datetime) or value.tzinfo is None:
        raise RelationshipContractError("absolute time must be timezone-aware")
    return value.astimezone(timezone.utc).isoformat(timespec="microseconds").replace("+00:00", "Z")


def parse_utc(value: str, field: str) -> datetime:
    if not isinstance(value, str) or not value:
        raise RelationshipContractError(f"{field} must be an absolute UTC timestamp")
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exc:
        raise RelationshipContractError(f"{field} must be an absolute UTC timestamp") from exc
    if parsed.tzinfo is None:
        raise RelationshipContractError(f"{field} must include a timezone")
    return parsed.astimezone(timezone.utc)


def is_hex_id(value: Any, length: int) -> bool:
    return (
        isinstance(value, str)
        and len(value) == length
        and all(character in "0123456789abcdef" for character in value)
    )


def require_turn_identity(value: Any, field: str) -> str:
    if not is_hex_id(value, 32):
        raise RelationshipContractError(f"{field} must be 32 lowercase hexadecimal characters")
    return value


def require_event_identity(value: Any, field: str) -> str:
    if not is_hex_id(value, 64):
        raise RelationshipContractError(f"{field} must be 64 lowercase hexadecimal characters")
    return value


def require_bounded_text(
    value: Any,
    field: str,
    *,
    maximum: int,
    allow_empty: bool = False,
) -> str:
    if not isinstance(value, str):
        raise RelationshipContractError(f"{field} must be text")
    normalized = value.strip()
    if not normalized and not allow_empty:
        raise RelationshipContractError(f"{field} cannot be empty")
    if len(normalized) > maximum:
        raise RelationshipContractError(f"{field} exceeds its local bound")
    return normalized


def validate_character_key(value: Any) -> str:
    text = require_bounded_text(value, "character_key", maximum=64)
    if not all(character.isalnum() or character in "._-" for character in text):
        raise RelationshipContractError("character_key contains unsupported characters")
    return text


def validate_canon_profile(value: Any) -> dict[str, Any]:
    if not isinstance(value, dict) or set(value) != {
        "identity_package",
        "canon_profile_id",
        "canon_profile_version",
        "canon_semantic_identity",
        "projection_id",
        "appraisal_guidance",
        "relationship_principles",
    }:
        raise RelationshipContractError("canon_profile has an unsupported shape")
    identity_package = require_bounded_text(
        value["identity_package"], "canon_profile.identity_package", maximum=120
    )
    profile_id = require_bounded_text(
        value["canon_profile_id"], "canon_profile.canon_profile_id", maximum=120
    )
    profile_version = value["canon_profile_version"]
    if isinstance(profile_version, bool) or not isinstance(profile_version, int) or profile_version < 1:
        raise RelationshipContractError("canon_profile.canon_profile_version is unsupported")
    semantic_identity = value["canon_semantic_identity"]
    if not (
        isinstance(semantic_identity, str)
        and len(semantic_identity) == 64
        and all(character in "0123456789ABCDEF" for character in semantic_identity)
    ):
        raise RelationshipContractError("canon_profile.canon_semantic_identity is malformed")
    projection_id = require_bounded_text(
        value["projection_id"], "canon_profile.projection_id", maximum=120
    )
    guidance = require_bounded_text(
        value["appraisal_guidance"], "canon_profile.appraisal_guidance", maximum=1600
    )
    raw_principles = value["relationship_principles"]
    if not isinstance(raw_principles, list) or not 1 <= len(raw_principles) <= 12:
        raise RelationshipContractError("canon_profile.relationship_principles must contain 1-12 items")
    principles = [
        require_bounded_text(item, "canon_profile.relationship_principles[]", maximum=480)
        for item in raw_principles
    ]
    return {
        "identity_package": identity_package,
        "canon_profile_id": profile_id,
        "canon_profile_version": profile_version,
        "canon_semantic_identity": semantic_identity,
        "projection_id": projection_id,
        "appraisal_guidance": guidance,
        "relationship_principles": principles,
    }


@dataclass(frozen=True)
class RelationshipScope:
    owner_id: str
    character_key: str
    canon_fingerprint: str

    def __post_init__(self) -> None:
        require_turn_identity(self.owner_id, "owner_id")
        validate_character_key(self.character_key)
        require_event_identity(self.canon_fingerprint, "canon_fingerprint")


@dataclass(frozen=True)
class RelationshipSettings:
    scope: RelationshipScope
    canon_profile: dict[str, Any]


@dataclass(frozen=True)
class FrozenTurn:
    scope: RelationshipScope
    session_id: str
    turn_id: str
    user_message_id: str
    assistant_message_id: str
    user_text: str
    assistant_text: str
    observed_at_utc: str
    source_hash: str
    review_queued_at_utc: str = ""


@dataclass(frozen=True)
class RelationshipEpisode:
    event_id: str
    scope: RelationshipScope
    session_id: str
    turn_id: str
    user_message_id: str
    assistant_message_id: str
    source_hash: str
    observed_at_utc: str
    recorded_at_utc: str
    what: str
    why: str
    target: str
    evidence: str
    theme: str
    event_time_appraisal: str
    link_relation: str | None = None
    link_target_event_id: str | None = None
    operator_correction: str | None = None

    def public_record(self) -> dict[str, Any]:
        record = {
            "event_id": self.event_id,
            "source_session_id": self.session_id,
            "observed_at_utc": self.observed_at_utc,
            "what": self.what,
            "why": self.why,
            "target": self.target,
            "evidence": self.evidence,
            "theme": self.theme,
            "event_time_appraisal": self.event_time_appraisal,
            "link": (
                {
                    "relation": self.link_relation,
                    "target_event_id": self.link_target_event_id,
                }
                if self.link_relation and self.link_target_event_id
                else None
            ),
        }
        if self.operator_correction:
            record["operator_correction"] = self.operator_correction
        return record


def build_source_hash(
    scope: RelationshipScope,
    *,
    session_id: str,
    turn_id: str,
    user_message_id: str,
    assistant_message_id: str,
    user_text: str,
    assistant_text: str,
) -> str:
    material = {
        "schema": 1,
        "owner_id": scope.owner_id,
        "character_key": scope.character_key,
        "canon_fingerprint": scope.canon_fingerprint,
        "session_id": session_id,
        "turn_id": turn_id,
        "user_message_id": user_message_id,
        "assistant_message_id": assistant_message_id,
        "user_text": user_text,
        "assistant_text": assistant_text,
    }
    return sha256_hex("lilith-relationship-source-v1\0" + canonical_json(material))


def review_json_schema() -> dict[str, Any]:
    return {
        "type": "object",
        "additionalProperties": False,
        "required": ["events"],
        "properties": {
            "events": {
                "type": "array",
                "maxItems": 4,
                "items": {
                    "type": "object",
                    "additionalProperties": False,
                    "required": [
                        "what",
                        "why",
                        "target",
                        "evidence",
                        "theme",
                        "event_time_appraisal",
                        "link",
                    ],
                    "properties": {
                        "what": {"type": "string", "enum": sorted(WHAT_KINDS)},
                        "why": {"type": "string", "maxLength": 360},
                        "target": {"type": "string", "enum": sorted(TARGET_KINDS)},
                        "evidence": {"type": "string", "maxLength": 480},
                        "theme": {"type": "string", "enum": sorted(THEMES)},
                        "event_time_appraisal": {
                            "type": "string",
                            "enum": sorted(EVENT_TIME_APPRAISALS),
                        },
                        "link": {
                            "anyOf": [
                                {"type": "null"},
                                {
                                    "type": "object",
                                    "additionalProperties": False,
                                    "required": ["relation", "target_event_id"],
                                    "properties": {
                                        "relation": {"const": "repairs"},
                                        "target_event_id": {
                                            "type": "string",
                                            "minLength": 64,
                                            "maxLength": 64,
                                        },
                                    },
                                },
                            ]
                        },
                    },
                },
            }
        },
    }


def _validated_link(
    raw_link: Any,
    *,
    what: str,
    witness: FrozenTurn,
    candidates: Mapping[str, RelationshipEpisode],
) -> tuple[str | None, str | None]:
    if raw_link is None:
        return None, None
    if not isinstance(raw_link, dict) or set(raw_link) != {"relation", "target_event_id"}:
        raise RelationshipContractError("link has an unsupported shape")
    if raw_link["relation"] != "repairs":
        raise RelationshipContractError("only an explicit repair link is supported")
    target_id = require_event_identity(raw_link["target_event_id"], "link.target_event_id")
    target = candidates.get(target_id)
    if target is None:
        raise RelationshipContractError("repair target was not offered by the host")
    if target.scope != witness.scope:
        raise RelationshipContractError("repair target crosses owner or character scope")
    if what not in REPAIR_WHAT_KINDS or target.what not in REPAIRABLE_WHAT_KINDS:
        raise RelationshipContractError("repair link does not connect a repair to a repairable event")
    if parse_utc(target.observed_at_utc, "target.observed_at_utc") > parse_utc(
        witness.observed_at_utc, "witness.observed_at_utc"
    ):
        raise RelationshipContractError("repair target cannot be later than its repair")
    return "repairs", target_id


def bind_model_review(
    parsed: Any,
    *,
    witness: FrozenTurn,
    candidates: Mapping[str, RelationshipEpisode],
    recorded_at: datetime,
) -> list[RelationshipEpisode]:
    """Validate a model proposal and bind every authoritative field locally."""

    if not isinstance(parsed, dict) or set(parsed) != {"events"}:
        raise RelationshipContractError("review must contain only events")
    raw_events = parsed["events"]
    if not isinstance(raw_events, list) or len(raw_events) > 4:
        raise RelationshipContractError("events must be a bounded list")

    recorded_at_utc = canonical_utc(recorded_at)
    episodes: list[RelationshipEpisode] = []
    seen_semantics: set[str] = set()
    expected_keys = {
        "what",
        "why",
        "target",
        "evidence",
        "theme",
        "event_time_appraisal",
        "link",
    }
    for raw in raw_events:
        if not isinstance(raw, dict) or set(raw) != expected_keys:
            raise RelationshipContractError("event proposal has an unsupported shape")
        what = raw["what"]
        target = raw["target"]
        theme = raw["theme"]
        appraisal = raw["event_time_appraisal"]
        if what not in WHAT_KINDS:
            raise RelationshipContractError("what is outside the qualitative vocabulary")
        if target not in TARGET_KINDS:
            raise RelationshipContractError("target is outside the qualitative vocabulary")
        if theme not in THEMES:
            raise RelationshipContractError("theme is outside the qualitative vocabulary")
        if appraisal not in EVENT_TIME_APPRAISALS:
            raise RelationshipContractError("event_time_appraisal is unsupported")
        why = require_bounded_text(raw["why"], "why", maximum=360)
        evidence = require_bounded_text(raw["evidence"], "evidence", maximum=480)
        if witness.user_text.count(evidence) != 1:
            raise RelationshipContractError("evidence must be one exact, unique span of the persisted user text")
        link_relation, link_target = _validated_link(
            raw["link"], what=what, witness=witness, candidates=candidates
        )
        semantics = {
            "what": what,
            "why": why,
            "target": target,
            "evidence": evidence,
            "theme": theme,
            "event_time_appraisal": appraisal,
            "link_relation": link_relation,
            "link_target_event_id": link_target,
        }
        semantic_key = canonical_json(semantics)
        if semantic_key in seen_semantics:
            raise RelationshipContractError("duplicate event proposal")
        seen_semantics.add(semantic_key)
        event_id = sha256_hex(
            "lilith-relationship-event-v1\0" + witness.source_hash + "\0" + semantic_key
        )
        episodes.append(
            RelationshipEpisode(
                event_id=event_id,
                scope=witness.scope,
                session_id=witness.session_id,
                turn_id=witness.turn_id,
                user_message_id=witness.user_message_id,
                assistant_message_id=witness.assistant_message_id,
                source_hash=witness.source_hash,
                observed_at_utc=witness.observed_at_utc,
                recorded_at_utc=recorded_at_utc,
                what=what,
                why=why,
                target=target,
                evidence=evidence,
                theme=theme,
                event_time_appraisal=appraisal,
                link_relation=link_relation,
                link_target_event_id=link_target,
            )
        )
    return episodes


def candidate_public_records(candidates: Sequence[RelationshipEpisode]) -> list[dict[str, Any]]:
    return [episode.public_record() for episode in candidates]
