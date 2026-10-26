"""Narrow in-process budget lineage for persisted-turn review work.

Only native opaque economic handles are retained, never an ambient permission
context. Missing old origins are deferred, not charged to the next human turn.
Legacy native runtimes without economic snapshots retain their existing path.
This does not reconstruct a spent budget after process restart.
"""
from __future__ import annotations

import threading
from typing import Any


_MISSING = object()
_LEGACY = object()


class ReviewUsageDeferred(RuntimeError):
    pass


def _snapshot_origin(context):
    getter = getattr(context, "_materialize_usage_origin_for_host", None)
    return getter() if callable(getter) else _LEGACY


def is_usage_deferral(error: BaseException) -> bool:
    try:
        from agent.usage_budget import BudgetError
        kinds = (ReviewUsageDeferred, BudgetError)
    except ImportError:
        kinds = (ReviewUsageDeferred,)
    seen = set()
    for _ in range(12):
        if isinstance(error, kinds):
            return True
        if id(error) in seen:
            break
        seen.add(id(error))
        error = error.__cause__ or error.__context__
        if error is None:
            break
    return False


class ReviewOriginRoutes:
    """One reviewer/store-bound map; a new instance cannot adopt unknown debt."""

    def __init__(self):
        self._lock = threading.RLock()
        self._origins: dict[str, Any] = {}
        self._routes: dict[str, Any] = {}

    def remember(self, witness) -> None:
        try:
            from agent.usage_budget_runtime import capture_origin
            origin = capture_origin()
        except ImportError:
            origin = _LEGACY
        with self._lock:
            if witness.source_hash not in self._origins:
                if len(self._origins) >= 32768:
                    raise ReviewUsageDeferred("review_origin_capacity")
                self._origins[witness.source_hash] = origin

    def matches(self, source_hash: str, context) -> bool:
        offered = _snapshot_origin(context)
        with self._lock:
            expected = self._origins.get(source_hash, _MISSING)
            if offered is _LEGACY:
                # Legacy test/runtime routes remain unchanged, but stripping
                # the opaque origin from already-budgeted work cannot bypass it.
                return expected in (_MISSING, _LEGACY, None)
            return expected is not _MISSING and expected is not _LEGACY and expected is offered

    def attach(self, witness, context) -> bool:
        if not self.matches(witness.source_hash, context):
            return False
        with self._lock:
            self._routes.setdefault(witness.source_hash, context)
        return True

    def route_for(self, witness, fallback):
        with self._lock:
            route = self._routes.get(witness.source_hash, _MISSING)
        if route is not _MISSING and self.matches(witness.source_hash, route):
            return route
        if self.matches(witness.source_hash, fallback):
            return fallback
        raise ReviewUsageDeferred("review_origin_unavailable")

    def eligible_source_hashes(self, fallback) -> tuple[str, ...] | None:
        if _snapshot_origin(fallback) is _LEGACY:
            return None
        with self._lock:
            return tuple(key for key in self._origins
                         if self.matches(key, self._routes.get(key, fallback)))

    def defer(self, witness) -> None:
        with self._lock:
            # Keep the originating handle/tombstone, but do not immediately
            # repump an exhausted or unavailable opaque route.
            self._routes.pop(witness.source_hash, None)

    def forget_completed(self, witness) -> None:
        with self._lock:
            self._routes.pop(witness.source_hash, None)
            self._origins.pop(witness.source_hash, None)
