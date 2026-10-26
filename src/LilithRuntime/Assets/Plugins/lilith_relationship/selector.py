"""Bounded LLM semantic selection for explicit relationship recall."""

from __future__ import annotations

from collections import OrderedDict
from dataclasses import dataclass
import logging
from typing import Any, Sequence

from .domain import (
    RelationshipContractError,
    RelationshipEpisode,
    canonical_json,
    require_bounded_text,
    require_event_identity,
)


RECALL_AUXILIARY_TASK = "lilith_relationship_recall"
_INITIAL_MAX_TOKENS = 480
_RECOVERY_MAX_TOKENS = 2048
_RECALL_JSON_CORRECTION = (
    "The previous response was not valid structured JSON for the supplied schema. "
    "Return only one JSON object with the event_ids array. Copy ids exactly from the "
    "supplied candidates and include no prose or markdown."
)
logger = logging.getLogger(__name__)


@dataclass(frozen=True)
class SemanticRecallSelection:
    events: tuple[RelationshipEpisode, ...]
    semantic_selection: bool
    selection_mode: str
    fallback_reason: str | None = None


def recall_selection_json_schema(limit: int) -> dict[str, Any]:
    return {
        "type": "object",
        "additionalProperties": False,
        "required": ["event_ids"],
        "properties": {
            "event_ids": {
                "type": "array",
                "maxItems": limit,
                "uniqueItems": True,
                "items": {
                    "type": "string",
                    "minLength": 64,
                    "maxLength": 64,
                },
            }
        },
    }


def _recent_diverse(
    candidates: Sequence[RelationshipEpisode],
    limit: int,
) -> tuple[RelationshipEpisode, ...]:
    """Deterministic, non-semantic fallback over an already bounded pool."""

    buckets: OrderedDict[str, list[RelationshipEpisode]] = OrderedDict()
    for episode in candidates:
        buckets.setdefault(episode.theme, []).append(episode)
    selected: list[RelationshipEpisode] = []
    while buckets and len(selected) < limit:
        exhausted: list[str] = []
        for theme, bucket in buckets.items():
            if bucket and len(selected) < limit:
                selected.append(bucket.pop(0))
            if not bucket:
                exhausted.append(theme)
        for theme in exhausted:
            buckets.pop(theme, None)
    return tuple(selected)


class SemanticRecallSelector:
    """Let the host-owned auxiliary LLM rank only host-supplied event ids."""

    def __init__(self, llm: Any) -> None:
        self._llm = llm

    @staticmethod
    def _instructions() -> str:
        return (
            "Select and rank only the supplied relationship episode ids whose meaning helps answer "
            "the exact recall query. Interpret the query semantically rather than by token overlap. "
            "Prefer episodes that explain the requested relational cause, continuity, boundary, or "
            "repair context. Return only ordered event_ids copied from candidates. Do not invent an "
            "id, rewrite an event, infer a current mood from event-time appraisal, or select more "
            "than the requested limit. An empty selection is valid."
        )

    def select(
        self,
        *,
        query: str,
        candidates: Sequence[RelationshipEpisode],
        limit: int,
    ) -> SemanticRecallSelection:
        query = require_bounded_text(query, "query", maximum=600)
        if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= 12:
            raise RelationshipContractError("semantic selection limit must be between 1 and 12")
        candidate_tuple = tuple(candidates)
        if len(candidate_tuple) > 96:
            raise RelationshipContractError("semantic candidate pool exceeds its host bound")
        candidate_map: dict[str, RelationshipEpisode] = {}
        for episode in candidate_tuple:
            event_id = require_event_identity(episode.event_id, "candidate.event_id")
            if event_id in candidate_map:
                raise RelationshipContractError("semantic candidate ids must be unique")
            candidate_map[event_id] = episode
        if not candidate_tuple:
            return SemanticRecallSelection(
                events=(),
                semantic_selection=False,
                selection_mode="empty_candidate_pool",
            )

        selector_input = {
            "query": query,
            "limit": limit,
            "candidates": [episode.public_record() for episode in candidate_tuple],
        }
        completion_arguments = {
            "input": [{"type": "text", "text": canonical_json(selector_input)}],
            "json_schema": recall_selection_json_schema(limit),
            "json_mode": True,
            "schema_name": "lilith_relationship_recall_v1",
            "temperature": 0.0,
            "timeout": 30.0,
            "purpose": "semantic relationship episode recall selection",
            "task": RECALL_AUXILIARY_TASK,
        }
        try:
            for attempt in range(2):
                instructions = self._instructions()
                if attempt:
                    instructions = f"{instructions}\n\n{_RECALL_JSON_CORRECTION}"
                result = self._llm.complete_structured(
                    instructions=instructions,
                    max_tokens=(
                        _INITIAL_MAX_TOKENS if attempt == 0 else _RECOVERY_MAX_TOKENS
                    ),
                    **completion_arguments,
                )
                parsed = getattr(result, "parsed", None)
                content_type = getattr(result, "content_type", None)
                try:
                    if parsed is None or content_type != "json":
                        raise RelationshipContractError(
                            "semantic selector did not return structured JSON"
                        )
                    if not isinstance(parsed, dict) or set(parsed) != {"event_ids"}:
                        raise RelationshipContractError(
                            "semantic selector returned an unsupported shape"
                        )
                    raw_ids = parsed["event_ids"]
                    if not isinstance(raw_ids, list) or len(raw_ids) > limit:
                        raise RelationshipContractError(
                            "semantic selector returned too many event ids"
                        )
                    selected_ids: list[str] = []
                    seen: set[str] = set()
                    for raw_id in raw_ids:
                        event_id = require_event_identity(raw_id, "semantic event_id")
                        if event_id in seen:
                            raise RelationshipContractError(
                                "semantic selector returned a duplicate event id"
                            )
                        if event_id not in candidate_map:
                            raise RelationshipContractError(
                                "semantic selector returned a non-candidate event id"
                            )
                        seen.add(event_id)
                        selected_ids.append(event_id)
                    return SemanticRecallSelection(
                        events=tuple(candidate_map[event_id] for event_id in selected_ids),
                        semantic_selection=True,
                        selection_mode="llm_semantic_candidate_ranking",
                    )
                except RelationshipContractError:
                    if attempt == 0:
                        logger.info(
                            "Relationship semantic recall returned invalid structured output; "
                            "retrying once"
                        )
                        continue
                    raise
        except Exception:
            return SemanticRecallSelection(
                events=_recent_diverse(candidate_tuple, limit),
                semantic_selection=False,
                selection_mode="recent_diverse_fallback",
                fallback_reason="semantic_selector_unavailable_or_invalid",
            )


__all__ = [
    "RECALL_AUXILIARY_TASK",
    "SemanticRecallSelection",
    "SemanticRecallSelector",
    "recall_selection_json_schema",
]
