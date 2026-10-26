"""Profile-local settings and SQLite storage for relationship episodes."""

from __future__ import annotations

from collections import OrderedDict
from contextlib import contextmanager
from dataclasses import dataclass, replace
from datetime import datetime
import heapq
import json
import os
from pathlib import Path
import sqlite3
import threading
import uuid
from typing import Any, Callable, Iterable, Sequence

from .domain import (
    DEFAULT_CANON_FINGERPRINT,
    DEFAULT_CANON_PROFILE,
    EVENT_TIME_APPRAISALS,
    REPAIRABLE_WHAT_KINDS,
    REPAIR_WHAT_KINDS,
    SETTINGS_SCHEMA_VERSION,
    STORE_SCHEMA_VERSION,
    TARGET_KINDS,
    THEMES,
    WHAT_KINDS,
    FrozenTurn,
    RelationshipContractError,
    RelationshipEpisode,
    RelationshipScope,
    RelationshipSettings,
    canonical_json,
    canonical_utc,
    build_source_hash,
    canon_fingerprint,
    parse_utc,
    require_bounded_text,
    require_event_identity,
    require_turn_identity,
    sha256_hex,
    utc_now,
    validate_canon_profile,
    validate_character_key,
)


SETTINGS_FILE = "settings.json"
DATABASE_FILE = "relationship.sqlite3"


def _harden_profile_storage(data_dir: Path) -> None:
    """Apply least-privilege Unix modes without disturbing Windows ACLs."""

    if os.name == "nt":
        return
    try:
        if data_dir.exists():
            data_dir.chmod(0o700)
        for name in (
            SETTINGS_FILE,
            DATABASE_FILE,
            DATABASE_FILE + "-journal",
            DATABASE_FILE + "-wal",
            DATABASE_FILE + "-shm",
        ):
            path = data_dir / name
            if path.exists():
                path.chmod(0o600)
    except OSError as exc:
        raise RelationshipContractError(
            "relationship profile storage permissions could not be secured"
        ) from exc


@dataclass(frozen=True)
class RecallSelection:
    events: tuple[RelationshipEpisode, ...]
    repair_pairs: tuple[tuple[RelationshipEpisode, RelationshipEpisode], ...]


def _default_settings_payload(owner_id: str) -> dict[str, Any]:
    return {
        "schema_version": SETTINGS_SCHEMA_VERSION,
        "owner_id": owner_id,
        "character_key": "lilith",
        "canon_profile": json.loads(canonical_json(DEFAULT_CANON_PROFILE)),
        "canon_fingerprint": DEFAULT_CANON_FINGERPRINT,
    }


def _parse_settings_payload(payload: Any) -> RelationshipSettings:
    expected = {
        "schema_version",
        "owner_id",
        "character_key",
        "canon_profile",
        "canon_fingerprint",
    }
    if not isinstance(payload, dict) or set(payload) != expected:
        raise RelationshipContractError("settings.json has an unsupported shape")
    if payload["schema_version"] != SETTINGS_SCHEMA_VERSION:
        raise RelationshipContractError("settings.json schema version is unsupported")
    owner_id = require_turn_identity(payload["owner_id"], "owner_id")
    character_key = validate_character_key(payload["character_key"])
    profile = validate_canon_profile(payload["canon_profile"])
    fingerprint = require_event_identity(payload["canon_fingerprint"], "canon_fingerprint")
    if fingerprint != canon_fingerprint(profile):
        raise RelationshipContractError("canon_fingerprint does not match canon_profile")
    return RelationshipSettings(
        scope=RelationshipScope(
            owner_id=owner_id,
            character_key=character_key,
            canon_fingerprint=fingerprint,
        ),
        canon_profile=profile,
    )


def load_or_create_settings(
    data_dir: Path,
    *,
    owner_factory: Callable[[], str] | None = None,
) -> RelationshipSettings:
    """Create one profile-local owner id, then treat the file as immutable input.

    Exclusive creation prevents two starting processes from silently choosing
    different owners.  A malformed or partial file is never regenerated: that
    is a fail-closed identity problem requiring operator repair.
    """

    data_dir = Path(data_dir)
    data_dir.mkdir(parents=True, exist_ok=True)
    _harden_profile_storage(data_dir)
    path = data_dir / SETTINGS_FILE
    if not path.exists():
        database_artifacts = tuple(data_dir.glob(DATABASE_FILE + "*"))
        if database_artifacts:
            raise RelationshipContractError(
                "settings.json is missing beside an existing relationship database"
            )
        owner_id = (owner_factory or (lambda: uuid.uuid4().hex))()
        require_turn_identity(owner_id, "owner_id")
        encoded = (json.dumps(_default_settings_payload(owner_id), ensure_ascii=False, indent=2) + "\n").encode(
            "utf-8"
        )
        try:
            descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        except FileExistsError:
            descriptor = None
        if descriptor is not None:
            try:
                with os.fdopen(descriptor, "wb") as stream:
                    stream.write(encoded)
                    stream.flush()
                    os.fsync(stream.fileno())
            except Exception:
                raise

    _harden_profile_storage(data_dir)

    try:
        raw = path.read_bytes()
    except OSError as exc:
        raise RelationshipContractError("settings.json is unavailable") from exc
    if len(raw) > 64 * 1024:
        raise RelationshipContractError("settings.json exceeds its local bound")
    try:
        payload = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise RelationshipContractError("settings.json is malformed") from exc
    return _parse_settings_payload(payload)


class RelationshipStore:
    """Small, append-only qualitative event graph scoped to one profile."""

    def __init__(
        self,
        data_dir: Path,
        *,
        owner_factory: Callable[[], str] | None = None,
    ) -> None:
        self.data_dir = Path(data_dir)
        self.settings = load_or_create_settings(self.data_dir, owner_factory=owner_factory)
        self.path = self.data_dir / DATABASE_FILE
        self._write_lock = threading.RLock()
        self._initialize_database()
        _harden_profile_storage(self.data_dir)

    @property
    def scope(self) -> RelationshipScope:
        return self.settings.scope

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(str(self.path), timeout=15.0)
        try:
            _harden_profile_storage(self.data_dir)
            connection.row_factory = sqlite3.Row
            connection.execute("PRAGMA foreign_keys = ON")
            connection.execute("PRAGMA busy_timeout = 15000")
            _harden_profile_storage(self.data_dir)
        except Exception:
            connection.close()
            raise
        return connection

    @contextmanager
    def _connection(self):
        connection = self._connect()
        try:
            with connection:
                yield connection
        finally:
            connection.close()
            _harden_profile_storage(self.data_dir)

    def _initialize_database(self) -> None:
        self.data_dir.mkdir(parents=True, exist_ok=True)
        with self._write_lock, self._connection() as connection:
            version = int(connection.execute("PRAGMA user_version").fetchone()[0])
            if version not in {0, 1, STORE_SCHEMA_VERSION}:
                raise RelationshipContractError("relationship database schema is unsupported")
            connection.execute(
                """
                CREATE TABLE IF NOT EXISTS episodes (
                    event_id TEXT PRIMARY KEY,
                    owner_id TEXT NOT NULL,
                    character_key TEXT NOT NULL,
                    canon_fingerprint TEXT NOT NULL,
                    session_id TEXT NOT NULL,
                    turn_id TEXT NOT NULL,
                    user_message_id TEXT NOT NULL,
                    assistant_message_id TEXT NOT NULL,
                    source_hash TEXT NOT NULL,
                    observed_at_utc TEXT NOT NULL,
                    recorded_at_utc TEXT NOT NULL,
                    what_kind TEXT NOT NULL,
                    why_text TEXT NOT NULL,
                    target_kind TEXT NOT NULL,
                    evidence_text TEXT NOT NULL,
                    theme TEXT NOT NULL,
                    event_time_appraisal TEXT NOT NULL,
                    link_relation TEXT,
                    link_target_event_id TEXT,
                    FOREIGN KEY(link_target_event_id) REFERENCES episodes(event_id),
                    UNIQUE(owner_id, character_key, source_hash, event_id)
                )
                """
            )
            connection.execute(
                """
                CREATE TABLE IF NOT EXISTS review_outbox (
                    source_hash TEXT PRIMARY KEY,
                    owner_id TEXT NOT NULL,
                    character_key TEXT NOT NULL,
                    canon_fingerprint TEXT NOT NULL,
                    session_id TEXT NOT NULL,
                    turn_id TEXT NOT NULL,
                    user_message_id TEXT NOT NULL,
                    assistant_message_id TEXT NOT NULL,
                    user_text TEXT NOT NULL,
                    assistant_text TEXT NOT NULL,
                    observed_at_utc TEXT NOT NULL,
                    review_queued_at_utc TEXT NOT NULL,
                    UNIQUE(owner_id, character_key, canon_fingerprint, session_id, turn_id)
                )
                """
            )
            connection.execute(
                """
                CREATE TABLE IF NOT EXISTS processed_reviews (
                    source_hash TEXT PRIMARY KEY,
                    owner_id TEXT NOT NULL,
                    character_key TEXT NOT NULL,
                    canon_fingerprint TEXT NOT NULL,
                    session_id TEXT NOT NULL,
                    turn_id TEXT NOT NULL,
                    user_message_id TEXT NOT NULL,
                    assistant_message_id TEXT NOT NULL,
                    UNIQUE(owner_id, character_key, canon_fingerprint, session_id, turn_id),
                    UNIQUE(owner_id, character_key, canon_fingerprint, user_message_id),
                    UNIQUE(owner_id, character_key, canon_fingerprint, assistant_message_id)
                )
                """
            )
            connection.execute(
                """
                CREATE TABLE IF NOT EXISTS operator_corrections (
                    correction_id TEXT PRIMARY KEY,
                    event_id TEXT NOT NULL,
                    owner_id TEXT NOT NULL,
                    character_key TEXT NOT NULL,
                    canon_fingerprint TEXT NOT NULL,
                    correction_text TEXT NOT NULL,
                    changed_at_utc TEXT NOT NULL,
                    FOREIGN KEY(event_id) REFERENCES episodes(event_id)
                )
                """
            )
            connection.execute(
                """
                CREATE INDEX IF NOT EXISTS operator_corrections_event_time
                ON operator_corrections(
                    owner_id, character_key, canon_fingerprint,
                    event_id, changed_at_utc DESC, correction_id DESC
                )
                """
            )
            connection.execute(
                """
                CREATE TABLE IF NOT EXISTS operator_visibility_actions (
                    action_id TEXT PRIMARY KEY,
                    event_id TEXT NOT NULL,
                    owner_id TEXT NOT NULL,
                    character_key TEXT NOT NULL,
                    canon_fingerprint TEXT NOT NULL,
                    hidden INTEGER NOT NULL CHECK(hidden IN (0, 1)),
                    changed_at_utc TEXT NOT NULL,
                    FOREIGN KEY(event_id) REFERENCES episodes(event_id)
                )
                """
            )
            connection.execute(
                """
                CREATE INDEX IF NOT EXISTS operator_visibility_event_time
                ON operator_visibility_actions(
                    owner_id, character_key, canon_fingerprint,
                    event_id, changed_at_utc DESC, action_id DESC
                )
                """
            )
            connection.execute(
                """
                CREATE INDEX IF NOT EXISTS episodes_scope_time
                ON episodes(
                    owner_id, character_key, canon_fingerprint,
                    observed_at_utc DESC, event_id ASC
                )
                """
            )
            connection.execute(
                """
                CREATE INDEX IF NOT EXISTS episodes_repair_target
                ON episodes(owner_id, character_key, canon_fingerprint, link_target_event_id)
                """
            )
            connection.execute(f"PRAGMA user_version = {STORE_SCHEMA_VERSION}")

    @staticmethod
    def _row_to_episode(row: sqlite3.Row) -> RelationshipEpisode:
        return RelationshipEpisode(
            event_id=row["event_id"],
            scope=RelationshipScope(
                owner_id=row["owner_id"],
                character_key=row["character_key"],
                canon_fingerprint=row["canon_fingerprint"],
            ),
            session_id=row["session_id"],
            turn_id=row["turn_id"],
            user_message_id=row["user_message_id"],
            assistant_message_id=row["assistant_message_id"],
            source_hash=row["source_hash"],
            observed_at_utc=row["observed_at_utc"],
            recorded_at_utc=row["recorded_at_utc"],
            what=row["what_kind"],
            why=row["why_text"],
            target=row["target_kind"],
            evidence=row["evidence_text"],
            theme=row["theme"],
            event_time_appraisal=row["event_time_appraisal"],
            link_relation=row["link_relation"],
            link_target_event_id=row["link_target_event_id"],
        )

    @staticmethod
    def _visible_sql(alias: str = "episodes") -> str:
        return f"""
            COALESCE((
                SELECT visibility.hidden
                FROM operator_visibility_actions AS visibility
                WHERE visibility.event_id = {alias}.event_id
                  AND visibility.owner_id = {alias}.owner_id
                  AND visibility.character_key = {alias}.character_key
                  AND visibility.canon_fingerprint = {alias}.canon_fingerprint
                ORDER BY visibility.changed_at_utc DESC, visibility.action_id DESC
                LIMIT 1
            ), 0) = 0
        """

    def _with_latest_correction(
        self,
        connection: sqlite3.Connection,
        episode: RelationshipEpisode,
    ) -> RelationshipEpisode:
        row = connection.execute(
            """
            SELECT correction_text FROM operator_corrections
            WHERE event_id = ? AND owner_id = ? AND character_key = ?
              AND canon_fingerprint = ?
            ORDER BY changed_at_utc DESC, correction_id DESC
            LIMIT 1
            """,
            (
                episode.event_id,
                self.scope.owner_id,
                self.scope.character_key,
                self.scope.canon_fingerprint,
            ),
        ).fetchone()
        return replace(
            episode,
            operator_correction=row["correction_text"] if row is not None else None,
        )

    def _is_hidden(self, connection: sqlite3.Connection, event_id: str) -> bool:
        row = connection.execute(
            """
            SELECT hidden FROM operator_visibility_actions
            WHERE event_id = ? AND owner_id = ? AND character_key = ?
              AND canon_fingerprint = ?
            ORDER BY changed_at_utc DESC, action_id DESC
            LIMIT 1
            """,
            (
                event_id,
                self.scope.owner_id,
                self.scope.character_key,
                self.scope.canon_fingerprint,
            ),
        ).fetchone()
        return row is not None and int(row["hidden"]) == 1

    def _validate_episode(self, episode: RelationshipEpisode) -> None:
        if not isinstance(episode, RelationshipEpisode):
            raise RelationshipContractError("store accepts RelationshipEpisode only")
        if episode.scope != self.scope:
            raise RelationshipContractError("episode crosses the active owner or character scope")
        require_event_identity(episode.event_id, "event_id")
        require_event_identity(episode.source_hash, "source_hash")
        require_turn_identity(episode.turn_id, "turn_id")
        require_turn_identity(episode.user_message_id, "user_message_id")
        require_turn_identity(episode.assistant_message_id, "assistant_message_id")
        if len({episode.turn_id, episode.user_message_id, episode.assistant_message_id}) != 3:
            raise RelationshipContractError("turn and message identities must be distinct")
        require_bounded_text(episode.session_id, "session_id", maximum=240)
        parse_utc(episode.observed_at_utc, "observed_at_utc")
        parse_utc(episode.recorded_at_utc, "recorded_at_utc")
        if episode.what not in WHAT_KINDS:
            raise RelationshipContractError("unsupported what kind")
        if episode.target not in TARGET_KINDS:
            raise RelationshipContractError("unsupported target kind")
        if episode.theme not in THEMES:
            raise RelationshipContractError("unsupported theme")
        if episode.event_time_appraisal not in EVENT_TIME_APPRAISALS:
            raise RelationshipContractError("unsupported event-time appraisal")
        require_bounded_text(episode.why, "why", maximum=360)
        require_bounded_text(episode.evidence, "evidence", maximum=480)
        if (episode.link_relation is None) != (episode.link_target_event_id is None):
            raise RelationshipContractError("repair link must be complete or absent")
        if episode.link_relation is not None:
            if episode.link_relation != "repairs" or episode.what not in REPAIR_WHAT_KINDS:
                raise RelationshipContractError("unsupported repair source")
            require_event_identity(episode.link_target_event_id, "link_target_event_id")

    @staticmethod
    def _row_signature(row: sqlite3.Row) -> tuple[Any, ...]:
        return tuple(row[key] for key in row.keys())

    @staticmethod
    def _episode_signature(episode: RelationshipEpisode) -> tuple[Any, ...]:
        return (
            episode.event_id,
            episode.scope.owner_id,
            episode.scope.character_key,
            episode.scope.canon_fingerprint,
            episode.session_id,
            episode.turn_id,
            episode.user_message_id,
            episode.assistant_message_id,
            episode.source_hash,
            episode.observed_at_utc,
            episode.recorded_at_utc,
            episode.what,
            episode.why,
            episode.target,
            episode.evidence,
            episode.theme,
            episode.event_time_appraisal,
            episode.link_relation,
            episode.link_target_event_id,
        )

    def _insert_validated(
        self,
        connection: sqlite3.Connection,
        episodes: Sequence[RelationshipEpisode],
    ) -> int:
        inserted = 0
        for episode in episodes:
            if episode.link_target_event_id:
                target_row = connection.execute(
                    f"""
                    SELECT * FROM episodes
                    WHERE event_id = ? AND owner_id = ? AND character_key = ?
                      AND canon_fingerprint = ?
                      AND {self._visible_sql('episodes')}
                    """,
                    (
                        episode.link_target_event_id,
                        self.scope.owner_id,
                        self.scope.character_key,
                        self.scope.canon_fingerprint,
                    ),
                ).fetchone()
                if target_row is None:
                    raise RelationshipContractError("repair target is absent from this scope")
                target = self._row_to_episode(target_row)
                if target.what not in REPAIRABLE_WHAT_KINDS:
                    raise RelationshipContractError("repair target is not a repairable event")
                if parse_utc(target.observed_at_utc, "target.observed_at_utc") > parse_utc(
                    episode.observed_at_utc, "episode.observed_at_utc"
                ):
                    raise RelationshipContractError("repair target occurs after its repair")

            existing = connection.execute(
                """
                SELECT * FROM episodes
                WHERE event_id = ? AND owner_id = ? AND character_key = ?
                  AND canon_fingerprint = ?
                """,
                (
                    episode.event_id,
                    self.scope.owner_id,
                    self.scope.character_key,
                    self.scope.canon_fingerprint,
                ),
            ).fetchone()
            if existing is not None:
                if self._row_signature(existing) != self._episode_signature(episode):
                    raise RelationshipContractError("event id conflicts with different persisted content")
                continue
            connection.execute(
                """
                INSERT INTO episodes (
                    event_id, owner_id, character_key, canon_fingerprint,
                    session_id, turn_id, user_message_id, assistant_message_id,
                    source_hash, observed_at_utc, recorded_at_utc,
                    what_kind, why_text, target_kind, evidence_text, theme,
                    event_time_appraisal, link_relation, link_target_event_id
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                self._episode_signature(episode),
            )
            inserted += 1
        return inserted

    def record_episodes(self, episodes: Sequence[RelationshipEpisode]) -> int:
        if not isinstance(episodes, Sequence) or isinstance(episodes, (str, bytes)):
            raise RelationshipContractError("episodes must be a sequence")
        for episode in episodes:
            self._validate_episode(episode)
        with self._write_lock, self._connection() as connection:
            return self._insert_validated(connection, episodes)

    def _validate_frozen_turn(self, witness: FrozenTurn) -> None:
        if not isinstance(witness, FrozenTurn) or witness.scope != self.scope:
            raise RelationshipContractError("review witness crosses the active scope")
        require_bounded_text(witness.session_id, "session_id", maximum=240)
        require_turn_identity(witness.turn_id, "turn_id")
        require_turn_identity(witness.user_message_id, "user_message_id")
        require_turn_identity(witness.assistant_message_id, "assistant_message_id")
        if len({witness.turn_id, witness.user_message_id, witness.assistant_message_id}) != 3:
            raise RelationshipContractError("review witness identities are not distinct")
        require_bounded_text(witness.user_text, "user_text", maximum=32768)
        require_bounded_text(witness.assistant_text, "assistant_text", maximum=32768)
        parse_utc(witness.observed_at_utc, "observed_at_utc")
        parse_utc(witness.review_queued_at_utc, "review_queued_at_utc")
        expected_hash = build_source_hash(
            witness.scope,
            session_id=witness.session_id,
            turn_id=witness.turn_id,
            user_message_id=witness.user_message_id,
            assistant_message_id=witness.assistant_message_id,
            user_text=witness.user_text,
            assistant_text=witness.assistant_text,
        )
        if witness.source_hash != expected_hash:
            raise RelationshipContractError("review witness source hash does not match its exact payload")

    @staticmethod
    def _outbox_signature(witness: FrozenTurn) -> tuple[Any, ...]:
        return (
            witness.source_hash,
            witness.scope.owner_id,
            witness.scope.character_key,
            witness.scope.canon_fingerprint,
            witness.session_id,
            witness.turn_id,
            witness.user_message_id,
            witness.assistant_message_id,
            witness.user_text,
            witness.assistant_text,
            witness.observed_at_utc,
            witness.review_queued_at_utc,
        )

    @staticmethod
    def _outbox_source_signature(witness: FrozenTurn) -> tuple[Any, ...]:
        """Identity-bearing content, excluding delivery wall-clock fields."""

        return (
            witness.source_hash,
            witness.scope.owner_id,
            witness.scope.character_key,
            witness.scope.canon_fingerprint,
            witness.session_id,
            witness.turn_id,
            witness.user_message_id,
            witness.assistant_message_id,
            witness.user_text,
            witness.assistant_text,
        )

    @staticmethod
    def _row_source_signature(row: sqlite3.Row) -> tuple[Any, ...]:
        return tuple(
            row[key]
            for key in (
                "source_hash",
                "owner_id",
                "character_key",
                "canon_fingerprint",
                "session_id",
                "turn_id",
                "user_message_id",
                "assistant_message_id",
                "user_text",
                "assistant_text",
            )
        )

    @staticmethod
    def _processed_signature(witness: FrozenTurn) -> tuple[Any, ...]:
        """Secret-free receipt proving one exact source was already reviewed."""

        return (
            witness.source_hash,
            witness.scope.owner_id,
            witness.scope.character_key,
            witness.scope.canon_fingerprint,
            witness.session_id,
            witness.turn_id,
            witness.user_message_id,
            witness.assistant_message_id,
        )

    @staticmethod
    def _row_processed_signature(row: sqlite3.Row) -> tuple[Any, ...]:
        return tuple(
            row[key]
            for key in (
                "source_hash",
                "owner_id",
                "character_key",
                "canon_fingerprint",
                "session_id",
                "turn_id",
                "user_message_id",
                "assistant_message_id",
            )
        )

    def _identity_matches(
        self,
        connection: sqlite3.Connection,
        table: str,
        witness: FrozenTurn,
    ) -> tuple[sqlite3.Row, ...]:
        """Find source or durable-identity collisions inside the active scope."""

        if table not in {"review_outbox", "processed_reviews"}:
            raise RelationshipContractError("unsupported review identity table")
        rows = connection.execute(
            f"""
            SELECT * FROM {table}
            WHERE owner_id = ? AND character_key = ? AND canon_fingerprint = ?
              AND (
                    source_hash = ?
                 OR (session_id = ? AND turn_id = ?)
                 OR user_message_id IN (?, ?)
                 OR assistant_message_id IN (?, ?)
              )
            """,
            (
                self.scope.owner_id,
                self.scope.character_key,
                self.scope.canon_fingerprint,
                witness.source_hash,
                witness.session_id,
                witness.turn_id,
                witness.user_message_id,
                witness.assistant_message_id,
                witness.user_message_id,
                witness.assistant_message_id,
            ),
        ).fetchall()
        return tuple(rows)

    @staticmethod
    def _row_to_witness(row: sqlite3.Row) -> FrozenTurn:
        return FrozenTurn(
            scope=RelationshipScope(
                owner_id=row["owner_id"],
                character_key=row["character_key"],
                canon_fingerprint=row["canon_fingerprint"],
            ),
            session_id=row["session_id"],
            turn_id=row["turn_id"],
            user_message_id=row["user_message_id"],
            assistant_message_id=row["assistant_message_id"],
            user_text=row["user_text"],
            assistant_text=row["assistant_text"],
            observed_at_utc=row["observed_at_utc"],
            source_hash=row["source_hash"],
            review_queued_at_utc=row["review_queued_at_utc"],
        )

    def enqueue_review_status(self, witness: FrozenTurn) -> str:
        """Return inserted/pending_duplicate/processed_duplicate after validation."""

        self._validate_frozen_turn(witness)
        with self._write_lock, self._connection() as connection:
            processed = self._identity_matches(connection, "processed_reviews", witness)
            existing = self._identity_matches(connection, "review_outbox", witness)
            if processed:
                expected = self._processed_signature(witness)
                if len(processed) != 1 or self._row_processed_signature(processed[0]) != expected:
                    raise RelationshipContractError(
                        "processed review identity conflicts with another payload"
                    )
                if existing:
                    raise RelationshipContractError(
                        "processed review still has a pending outbox payload"
                    )
                return "processed_duplicate"
            if existing:
                # Hook delivery is at-least-once. The same exact durable turn
                # can be observed again with a later hook/queue wall clock;
                # preserve the first authoritative timestamps rather than
                # turning a harmless retry into a conflict. Any identity or
                # content difference still fails closed.
                expected = self._outbox_source_signature(witness)
                if len(existing) != 1 or self._row_source_signature(existing[0]) != expected:
                    raise RelationshipContractError("outbox source hash conflicts with another payload")
                return "pending_duplicate"
            try:
                connection.execute(
                    """
                    INSERT INTO review_outbox (
                        source_hash, owner_id, character_key, canon_fingerprint,
                        session_id, turn_id, user_message_id, assistant_message_id,
                        user_text, assistant_text, observed_at_utc, review_queued_at_utc
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    self._outbox_signature(witness),
                )
            except sqlite3.IntegrityError as exc:
                raise RelationshipContractError(
                    "review outbox identity conflicts with another payload"
                ) from exc
            return "inserted"

    def enqueue_review(self, witness: FrozenTurn) -> bool:
        """Compatibility bool: true only when a new outbox row was inserted."""

        return self.enqueue_review_status(witness) == "inserted"

    def pending_reviews(
        self,
        *,
        session_id: str | None = None,
        limit: int = 128,
        eligible_source_hashes: tuple[str, ...] | None = None,
        excluded_source_hashes: tuple[str, ...] | None = None,
    ) -> tuple[FrozenTurn, ...]:
        bounded = max(1, min(int(limit), 512))
        if session_id is not None:
            session_id = require_bounded_text(session_id, "session_id", maximum=240)
        if excluded_source_hashes is not None:
            if (eligible_source_hashes is not None or type(excluded_source_hashes) is not tuple
                    or len(excluded_source_hashes) > 32768):
                raise RelationshipContractError("review exclusion is invalid")
            excluded = frozenset(require_event_identity(value, "source_hash")
                                 for value in excluded_source_hashes)
            # One bounded metadata query avoids repeatedly sorting the same
            # session. Each examined row either advances the selected limit
            # or consumes one excluded key. Payloads are read only after the
            # eligible set is known; no catch-up work is dispatched here.
            selected = []
            with self._connection() as connection:
                base = [self.scope.owner_id, self.scope.character_key, self.scope.canon_fingerprint]
                session_sql = ""
                if session_id is not None:
                    session_sql = " AND session_id = ?"
                    base.append(session_id)
                metadata = connection.execute(
                    "SELECT review_queued_at_utc, source_hash FROM review_outbox "
                    "WHERE owner_id = ? AND character_key = ? AND canon_fingerprint = ?" +
                    session_sql + " ORDER BY review_queued_at_utc, source_hash LIMIT ?",
                    (*base, len(excluded) + bounded))
                for row in metadata:
                    if row["source_hash"] not in excluded:
                        selected.append(row["source_hash"])
                        if len(selected) == bounded:
                            break
            eligible_source_hashes = tuple(selected)
        if eligible_source_hashes is not None:
            if type(eligible_source_hashes) is not tuple or len(eligible_source_hashes) > 32768:
                raise RelationshipContractError("review eligibility is invalid")
            eligible_source_hashes = tuple(dict.fromkeys(
                require_event_identity(value, "source_hash") for value in eligible_source_hashes))
            if not eligible_source_hashes:
                return ()
        with self._connection() as connection:
            params: list[Any] = [
                self.scope.owner_id,
                self.scope.character_key,
                self.scope.canon_fingerprint,
            ]
            session_clause = ""
            if session_id is not None:
                session_clause = " AND session_id = ?"
                params.append(session_id)
            if eligible_source_hashes is None:
                params.append(bounded)
                rows = connection.execute(
                f"""
                SELECT * FROM review_outbox
                WHERE owner_id = ? AND character_key = ? AND canon_fingerprint = ?
                {session_clause}
                ORDER BY review_queued_at_utc ASC, source_hash ASC
                LIMIT ?
                """,
                    params,
                ).fetchall()
            else:
                # Local ready-source selection is a read-only restriction,
                # applied before LIMIT. The reviewer supplies either original-
                # origin eligibility or its authorized same-session batch set.
                # Bound SQLite parameters and retained rows independently.
                rows = []
                for start in range(0, len(eligible_source_hashes), 900):
                    batch = eligible_source_hashes[start:start + 900]
                    placeholders = ",".join("?" for _ in batch)
                    candidates = connection.execute(
                        "SELECT * FROM review_outbox WHERE owner_id = ? AND character_key = ? "
                        "AND canon_fingerprint = ?" + session_clause +
                        " AND source_hash IN (" + placeholders + ") "
                        "ORDER BY review_queued_at_utc ASC, source_hash ASC LIMIT ?",
                        (*params, *batch, bounded),
                    ).fetchall()
                    rows = heapq.nsmallest(bounded, (*rows, *candidates), key=lambda row: (
                        row["review_queued_at_utc"], row["source_hash"]))
        witnesses = tuple(self._row_to_witness(row) for row in rows)
        for witness in witnesses:
            self._validate_frozen_turn(witness)
        return witnesses

    def outbox_count(self) -> int:
        with self._connection() as connection:
            row = connection.execute(
                """
                SELECT COUNT(*) FROM review_outbox
                WHERE owner_id = ? AND character_key = ? AND canon_fingerprint = ?
                """,
                (
                    self.scope.owner_id,
                    self.scope.character_key,
                    self.scope.canon_fingerprint,
                ),
            ).fetchone()
            return int(row[0])

    def has_pending_review(self, source_hash: str) -> bool:
        require_event_identity(source_hash, "source_hash")
        with self._connection() as connection:
            row = connection.execute(
                """
                SELECT 1 FROM review_outbox
                WHERE source_hash = ? AND owner_id = ? AND character_key = ?
                  AND canon_fingerprint = ?
                """,
                (
                    source_hash,
                    self.scope.owner_id,
                    self.scope.character_key,
                    self.scope.canon_fingerprint,
                ),
            ).fetchone()
            return row is not None

    def processed_review_count(self) -> int:
        with self._connection() as connection:
            row = connection.execute(
                """
                SELECT COUNT(*) FROM processed_reviews
                WHERE owner_id = ? AND character_key = ? AND canon_fingerprint = ?
                """,
                (
                    self.scope.owner_id,
                    self.scope.character_key,
                    self.scope.canon_fingerprint,
                ),
            ).fetchone()
            return int(row[0])

    def commit_review(
        self,
        witness: FrozenTurn,
        episodes: Sequence[RelationshipEpisode],
    ) -> int:
        """Atomically commit a reviewed result and acknowledge its durable outbox row."""

        self._validate_frozen_turn(witness)
        if not isinstance(episodes, Sequence) or isinstance(episodes, (str, bytes)):
            raise RelationshipContractError("episodes must be a sequence")
        for episode in episodes:
            self._validate_episode(episode)
            if episode.source_hash != witness.source_hash:
                raise RelationshipContractError("review event does not belong to its source witness")
        with self._write_lock, self._connection() as connection:
            return self._commit_review_in_connection(connection, witness, episodes)

    def commit_review_batch(self, reviews) -> int:
        """One transaction for validated same-session witnesses; no new schema."""
        if not isinstance(reviews, tuple) or not 2 <= len(reviews) <= 8:
            raise RelationshipContractError("invalid review batch")
        session = reviews[0][0].session_id
        seen = set()
        for witness, episodes in reviews:
            self._validate_frozen_turn(witness)
            if witness.session_id != session or witness.source_hash in seen:
                raise RelationshipContractError("batch source scope conflict")
            seen.add(witness.source_hash)
            for episode in episodes:
                self._validate_episode(episode)
                if episode.source_hash != witness.source_hash:
                    raise RelationshipContractError("batch evidence source conflict")
        with self._write_lock, self._connection() as connection:
            return sum(self._commit_review_in_connection(connection, w, events)
                       for w, events in reviews)

    def _commit_review_in_connection(self, connection, witness, episodes):
        pending = connection.execute(
            """
            SELECT * FROM review_outbox
            WHERE source_hash = ? AND owner_id = ? AND character_key = ?
              AND canon_fingerprint = ?
            """,
            (
                witness.source_hash,
                self.scope.owner_id,
                self.scope.character_key,
                self.scope.canon_fingerprint,
            ),
        ).fetchone()
        if pending is None:
            raise RelationshipContractError("review outbox row is absent")
        if tuple(pending[key] for key in pending.keys()) != self._outbox_signature(witness):
            raise RelationshipContractError("review witness differs from its durable outbox payload")
        inserted = self._insert_validated(connection, episodes)
        existing_receipts = self._identity_matches(
            connection,
            "processed_reviews",
            witness,
        )
        if existing_receipts:
            raise RelationshipContractError("review source was already acknowledged")
        try:
            connection.execute(
                """
                INSERT INTO processed_reviews (
                    source_hash, owner_id, character_key, canon_fingerprint,
                    session_id, turn_id, user_message_id, assistant_message_id
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                self._processed_signature(witness),
            )
        except sqlite3.IntegrityError as exc:
            raise RelationshipContractError(
                "processed review identity conflicts with another payload"
            ) from exc
        connection.execute(
            """
            DELETE FROM review_outbox
            WHERE source_hash = ? AND owner_id = ? AND character_key = ?
              AND canon_fingerprint = ?
            """,
            (
                witness.source_hash,
                self.scope.owner_id,
                self.scope.character_key,
                self.scope.canon_fingerprint,
            ),
        )
        return inserted

    def add_operator_correction(
        self,
        event_id: str,
        correction_text: str,
        *,
        changed_at: datetime | None = None,
    ) -> str:
        """Append a local-player correction without rewriting the source event."""

        event_id = require_event_identity(event_id, "event_id")
        correction = require_bounded_text(correction_text, "correction_text", maximum=1200)
        changed_at_utc = canonical_utc(changed_at or utc_now())
        correction_id = sha256_hex(
            "lilith-relationship-operator-correction-v1\0"
            + canonical_json(
                {
                    "owner_id": self.scope.owner_id,
                    "character_key": self.scope.character_key,
                    "canon_fingerprint": self.scope.canon_fingerprint,
                    "event_id": event_id,
                    "correction_text": correction,
                    "changed_at_utc": changed_at_utc,
                }
            )
        )
        with self._write_lock, self._connection() as connection:
            event = connection.execute(
                """
                SELECT event_id FROM episodes
                WHERE event_id = ? AND owner_id = ? AND character_key = ?
                  AND canon_fingerprint = ?
                """,
                (
                    event_id,
                    self.scope.owner_id,
                    self.scope.character_key,
                    self.scope.canon_fingerprint,
                ),
            ).fetchone()
            if event is None:
                raise RelationshipContractError("event is absent from this scope")
            connection.execute(
                """
                INSERT OR IGNORE INTO operator_corrections (
                    correction_id, event_id, owner_id, character_key,
                    canon_fingerprint, correction_text, changed_at_utc
                ) VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    correction_id,
                    event_id,
                    self.scope.owner_id,
                    self.scope.character_key,
                    self.scope.canon_fingerprint,
                    correction,
                    changed_at_utc,
                ),
            )
        return correction_id

    def set_event_hidden(
        self,
        event_id: str,
        hidden: bool,
        *,
        changed_at: datetime | None = None,
    ) -> str:
        """Append a recoverable local visibility action for player-owned deletion."""

        event_id = require_event_identity(event_id, "event_id")
        if not isinstance(hidden, bool):
            raise RelationshipContractError("hidden must be boolean")
        changed_at_utc = canonical_utc(changed_at or utc_now())
        action_id = sha256_hex(
            "lilith-relationship-operator-visibility-v1\0"
            + canonical_json(
                {
                    "owner_id": self.scope.owner_id,
                    "character_key": self.scope.character_key,
                    "canon_fingerprint": self.scope.canon_fingerprint,
                    "event_id": event_id,
                    "hidden": hidden,
                    "changed_at_utc": changed_at_utc,
                }
            )
        )
        with self._write_lock, self._connection() as connection:
            event = connection.execute(
                """
                SELECT event_id FROM episodes
                WHERE event_id = ? AND owner_id = ? AND character_key = ?
                  AND canon_fingerprint = ?
                """,
                (
                    event_id,
                    self.scope.owner_id,
                    self.scope.character_key,
                    self.scope.canon_fingerprint,
                ),
            ).fetchone()
            if event is None:
                raise RelationshipContractError("event is absent from this scope")
            connection.execute(
                """
                INSERT OR IGNORE INTO operator_visibility_actions (
                    action_id, event_id, owner_id, character_key,
                    canon_fingerprint, hidden, changed_at_utc
                ) VALUES (?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    action_id,
                    event_id,
                    self.scope.owner_id,
                    self.scope.character_key,
                    self.scope.canon_fingerprint,
                    1 if hidden else 0,
                    changed_at_utc,
                ),
            )
        return action_id

    def management_records(self, *, limit: int = 50) -> list[dict[str, Any]]:
        if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= 200:
            raise RelationshipContractError("management limit must be between 1 and 200")
        return self.management_records_page(limit=limit, offset=0)

    def management_records_page(
        self,
        *,
        limit: int = 200,
        offset: int = 0,
    ) -> list[dict[str, Any]]:
        """Read a bounded management page for a frozen higher-level catalog."""

        if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= 512:
            raise RelationshipContractError("management page limit must be between 1 and 512")
        if isinstance(offset, bool) or not isinstance(offset, int) or not 0 <= offset <= 1_000_000:
            raise RelationshipContractError("management page offset is invalid")
        with self._connection() as connection:
            rows = connection.execute(
                """
                SELECT * FROM episodes
                WHERE owner_id = ? AND character_key = ? AND canon_fingerprint = ?
                ORDER BY observed_at_utc DESC, event_id ASC
                LIMIT ? OFFSET ?
                """,
                (
                    self.scope.owner_id,
                    self.scope.character_key,
                    self.scope.canon_fingerprint,
                    limit,
                    offset,
                ),
            ).fetchall()
            records: list[dict[str, Any]] = []
            for row in rows:
                episode = self._with_latest_correction(connection, self._row_to_episode(row))
                record = episode.public_record()
                record["hidden_from_recall"] = self._is_hidden(connection, episode.event_id)
                records.append(record)
            return records

    def management_records_snapshot(
        self,
        *,
        maximum: int = 1_000_000,
    ) -> list[dict[str, Any]]:
        """Read one coherent, bounded snapshot for the combined D057 catalog."""

        if (
            isinstance(maximum, bool)
            or not isinstance(maximum, int)
            or not 1 <= maximum <= 1_000_000
        ):
            raise RelationshipContractError("management snapshot bound is invalid")
        with self._connection() as connection:
            connection.execute("BEGIN")
            try:
                rows = connection.execute(
                    """
                    SELECT * FROM episodes
                    WHERE owner_id = ? AND character_key = ? AND canon_fingerprint = ?
                    ORDER BY observed_at_utc DESC, event_id ASC
                    LIMIT ?
                    """,
                    (
                        self.scope.owner_id,
                        self.scope.character_key,
                        self.scope.canon_fingerprint,
                        maximum + 1,
                    ),
                ).fetchall()
                if len(rows) > maximum:
                    raise RelationshipContractError("relationship management catalog is too large")
                records: list[dict[str, Any]] = []
                for row in rows:
                    episode = self._with_latest_correction(
                        connection,
                        self._row_to_episode(row),
                    )
                    record = episode.public_record()
                    record["hidden_from_recall"] = self._is_hidden(
                        connection,
                        episode.event_id,
                    )
                    records.append(record)
                connection.execute("COMMIT")
                return records
            except Exception:
                connection.execute("ROLLBACK")
                raise

    def count(self) -> int:
        with self._connection() as connection:
            row = connection.execute(
                """
                SELECT COUNT(*) FROM episodes
                WHERE owner_id = ? AND character_key = ? AND canon_fingerprint = ?
                """,
                (
                    self.scope.owner_id,
                    self.scope.character_key,
                    self.scope.canon_fingerprint,
                ),
            ).fetchone()
            return int(row[0])

    def get(self, event_id: str) -> RelationshipEpisode | None:
        require_event_identity(event_id, "event_id")
        with self._connection() as connection:
            row = connection.execute(
                """
                SELECT * FROM episodes
                WHERE event_id = ? AND owner_id = ? AND character_key = ?
                  AND canon_fingerprint = ?
                """,
                (
                    event_id,
                    self.scope.owner_id,
                    self.scope.character_key,
                    self.scope.canon_fingerprint,
                ),
            ).fetchone()
            if row is None:
                return None
            return self._with_latest_correction(connection, self._row_to_episode(row))

    def recent_link_candidates(
        self,
        *,
        before_utc: str | None = None,
        limit: int = 24,
    ) -> tuple[RelationshipEpisode, ...]:
        bounded_limit = max(1, min(int(limit), 48))
        params: list[Any] = [
            self.scope.owner_id,
            self.scope.character_key,
            self.scope.canon_fingerprint,
        ]
        where = "owner_id = ? AND character_key = ? AND canon_fingerprint = ?"
        if before_utc is not None:
            normalized = canonical_utc(parse_utc(before_utc, "before_utc"))
            where += " AND observed_at_utc <= ?"
            params.append(normalized)
        params.append(bounded_limit)
        with self._connection() as connection:
            rows = connection.execute(
                f"""
                SELECT * FROM episodes
                WHERE {where} AND {self._visible_sql('episodes')}
                ORDER BY observed_at_utc DESC, event_id ASC LIMIT ?
                """,
                params,
            ).fetchall()
            return tuple(
                self._with_latest_correction(connection, self._row_to_episode(row))
                for row in rows
            )

    def candidate_pool(
        self,
        *,
        themes: Iterable[str] = (),
        from_utc: str | None = None,
        to_utc: str | None = None,
        limit: int = 96,
    ) -> tuple[RelationshipEpisode, ...]:
        """Return recent plus time-by-theme strata for bounded semantic selection.

        A recency-only window eventually makes older episodes unreachable.  The
        host therefore reserves recent context and deterministically samples
        every present theme across its full filtered timeline, including the
        oldest stratum, without ever returning more than ``limit`` rows.
        """

        if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= 96:
            raise RelationshipContractError("candidate limit must be between 1 and 96")
        requested_themes = tuple(dict.fromkeys(themes))
        if any(theme not in THEMES for theme in requested_themes):
            raise RelationshipContractError("themes contain an unsupported value")
        params: list[Any] = [
            self.scope.owner_id,
            self.scope.character_key,
            self.scope.canon_fingerprint,
        ]
        clauses = ["owner_id = ?", "character_key = ?", "canon_fingerprint = ?"]
        if from_utc:
            clauses.append("observed_at_utc >= ?")
            params.append(canonical_utc(parse_utc(from_utc, "from_utc")))
        if to_utc:
            clauses.append("observed_at_utc <= ?")
            params.append(canonical_utc(parse_utc(to_utc, "to_utc")))
        if from_utc and to_utc and parse_utc(from_utc, "from_utc") > parse_utc(
            to_utc, "to_utc"
        ):
            raise RelationshipContractError("from_utc cannot be later than to_utc")
        if requested_themes:
            placeholders = ",".join("?" for _ in requested_themes)
            clauses.append(f"theme IN ({placeholders})")
            params.extend(requested_themes)
        where = " AND ".join(clauses)
        recent_quota = min(limit, max(1, limit // 3))

        with self._connection() as connection:
            recent_rows = connection.execute(
                f"""
                SELECT * FROM episodes
                WHERE {where} AND {self._visible_sql('episodes')}
                ORDER BY observed_at_utc DESC, event_id ASC
                LIMIT ?
                """,
                [*params, recent_quota],
            ).fetchall()
            count_rows = connection.execute(
                f"""
                SELECT theme, COUNT(*) AS event_count FROM episodes
                WHERE {where} AND {self._visible_sql('episodes')}
                GROUP BY theme ORDER BY theme ASC
                """,
                params,
            ).fetchall()
            counts = {row["theme"]: int(row["event_count"]) for row in count_rows}
            quotas = {theme: 0 for theme in counts}
            remaining = min(max(0, limit - len(recent_rows)), sum(counts.values()))
            while remaining:
                progressed = False
                for theme in sorted(counts):
                    if quotas[theme] < counts[theme] and remaining:
                        quotas[theme] += 1
                        remaining -= 1
                        progressed = True
                if not progressed:
                    break

            rank_conditions: list[str] = []
            rank_params: list[Any] = []
            for theme in sorted(quotas):
                quota = quotas[theme]
                count = counts[theme]
                if quota <= 0:
                    continue
                if quota == 1:
                    positions = [count - 1]
                else:
                    divisor = quota - 1
                    positions = sorted(
                        {
                            (index * (count - 1) + divisor // 2) // divisor
                            for index in range(quota)
                        }
                    )
                placeholders = ",".join("?" for _ in positions)
                rank_conditions.append(f"(theme = ? AND theme_rank IN ({placeholders}))")
                rank_params.extend([theme, *positions])

            stratified_rows: list[sqlite3.Row] = []
            if rank_conditions:
                stratified_rows = connection.execute(
                    f"""
                    WITH ranked AS (
                        SELECT episodes.*,
                               ROW_NUMBER() OVER (
                                   PARTITION BY theme
                                   ORDER BY observed_at_utc DESC, event_id ASC
                               ) - 1 AS theme_rank
                        FROM episodes
                        WHERE {where} AND {self._visible_sql('episodes')}
                    )
                    SELECT * FROM ranked
                    WHERE {' OR '.join(rank_conditions)}
                    ORDER BY observed_at_utc DESC, event_id ASC
                    """,
                    [*params, *rank_params],
                ).fetchall()

            selected: list[RelationshipEpisode] = []
            seen: set[str] = set()
            for row in [*recent_rows, *stratified_rows]:
                episode = self._with_latest_correction(connection, self._row_to_episode(row))
                if episode.event_id in seen:
                    continue
                selected.append(episode)
                seen.add(episode.event_id)
                if len(selected) == limit:
                    break
            return tuple(selected)

    def refresh_visible(
        self,
        episodes: Sequence[RelationshipEpisode],
    ) -> tuple[RelationshipEpisode, ...]:
        """Revalidate selector results against current visibility and scope."""

        if len(episodes) > 96:
            raise RelationshipContractError("visible refresh exceeds its host bound")
        event_ids: list[str] = []
        for episode in episodes:
            if episode.scope != self.scope:
                raise RelationshipContractError("visible refresh crosses scope")
            event_ids.append(require_event_identity(episode.event_id, "event_id"))
        if len(set(event_ids)) != len(event_ids):
            raise RelationshipContractError("visible refresh ids must be unique")
        if not event_ids:
            return ()
        placeholders = ",".join("?" for _ in event_ids)
        with self._connection() as connection:
            rows = connection.execute(
                f"""
                SELECT * FROM episodes
                WHERE owner_id = ? AND character_key = ? AND canon_fingerprint = ?
                  AND event_id IN ({placeholders})
                  AND {self._visible_sql('episodes')}
                """,
                [
                    self.scope.owner_id,
                    self.scope.character_key,
                    self.scope.canon_fingerprint,
                    *event_ids,
                ],
            ).fetchall()
            by_id = {
                row["event_id"]: self._with_latest_correction(
                    connection, self._row_to_episode(row)
                )
                for row in rows
            }
            return tuple(by_id[event_id] for event_id in event_ids if event_id in by_id)

    @staticmethod
    def _diverse(events: Sequence[RelationshipEpisode], limit: int) -> list[RelationshipEpisode]:
        buckets: OrderedDict[str, list[RelationshipEpisode]] = OrderedDict()
        for episode in events:
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
        return selected

    def recall(
        self,
        *,
        themes: Iterable[str] = (),
        from_utc: str | None = None,
        to_utc: str | None = None,
        limit: int = 8,
        include_repair_pairs: bool = True,
    ) -> RecallSelection:
        if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= 12:
            raise RelationshipContractError("limit must be between 1 and 12")
        events = self.candidate_pool(
            themes=themes,
            from_utc=from_utc,
            to_utc=to_utc,
            limit=96,
        )
        selected = self._diverse(events, limit)
        pairs = self.repair_pairs_for(selected) if include_repair_pairs else ()
        return RecallSelection(events=tuple(selected), repair_pairs=tuple(pairs))

    def repair_pairs_for(
        self,
        selected: Sequence[RelationshipEpisode],
    ) -> tuple[tuple[RelationshipEpisode, RelationshipEpisode], ...]:
        pairs: list[tuple[RelationshipEpisode, RelationshipEpisode]] = []
        seen_pairs: set[tuple[str, str]] = set()
        if selected:
            for episode in selected:
                if episode.scope != self.scope:
                    raise RelationshipContractError("repair pair selection crosses scope")
            with self._connection() as connection:
                for episode in selected:
                    if episode.link_relation == "repairs" and episode.link_target_event_id:
                        target_row = connection.execute(
                            f"""
                            SELECT * FROM episodes
                            WHERE event_id = ? AND owner_id = ? AND character_key = ?
                              AND canon_fingerprint = ?
                              AND {self._visible_sql('episodes')}
                            """,
                            (
                                episode.link_target_event_id,
                                self.scope.owner_id,
                                self.scope.character_key,
                                self.scope.canon_fingerprint,
                            ),
                        ).fetchone()
                        if target_row is not None:
                            pair_key = (episode.event_id, episode.link_target_event_id)
                            if pair_key not in seen_pairs:
                                target = self._with_latest_correction(
                                    connection, self._row_to_episode(target_row)
                                )
                                pairs.append((episode, target))
                                seen_pairs.add(pair_key)
                    repair_rows = connection.execute(
                        f"""
                        SELECT * FROM episodes
                        WHERE link_relation = 'repairs' AND link_target_event_id = ?
                          AND owner_id = ? AND character_key = ?
                          AND canon_fingerprint = ?
                          AND {self._visible_sql('episodes')}
                        ORDER BY observed_at_utc DESC, event_id ASC
                        """,
                        (
                            episode.event_id,
                            self.scope.owner_id,
                            self.scope.character_key,
                            self.scope.canon_fingerprint,
                        ),
                    ).fetchall()
                    for repair_row in repair_rows:
                        repair = self._with_latest_correction(
                            connection, self._row_to_episode(repair_row)
                        )
                        pair_key = (repair.event_id, episode.event_id)
                        if pair_key not in seen_pairs:
                            pairs.append((repair, episode))
                            seen_pairs.add(pair_key)
            # The primary selection remains bounded.  Pair context is returned
            # separately so a repair is never shown without the event it repairs.
            pairs.sort(key=lambda pair: (pair[0].observed_at_utc, pair[0].event_id), reverse=True)
        return tuple(pairs)
