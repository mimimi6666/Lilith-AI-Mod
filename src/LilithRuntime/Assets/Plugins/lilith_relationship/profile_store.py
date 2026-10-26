"""Independent SQLite sidecar for durable, correctable profile memory.

This module never opens or migrates ``relationship.sqlite3``.  All automatic
review writes, operator mutations, evidence, revisions, idempotency receipts,
and preferences live in ``profile-memory.sqlite3``.
"""

from __future__ import annotations

from contextlib import contextmanager
from datetime import datetime, timedelta, timezone
import hashlib
import heapq
import json
import os
from pathlib import Path
import sqlite3
import threading
from typing import Any, Sequence

from .domain import (
    FrozenTurn,
    RelationshipContractError,
    RelationshipScope,
    build_source_hash,
    canonical_json,
    canonical_utc,
    parse_utc,
    require_bounded_text,
    require_event_identity,
    require_turn_identity,
    sha256_hex,
    utc_now,
)
from .profile_domain import (
    MANUAL_ADD_CATEGORIES,
    MEMORY_CATEGORIES,
    MEMORY_SOURCES,
    MEMORY_STATES,
    PROFILE_DATABASE_FILE,
    PROFILE_MEMORY_KINDS,
    PROFILE_STORE_SCHEMA_VERSION,
    ProfileMemoryAssertion,
    ProfileMemoryEntry,
    ProfileTemporaryResolution,
    bind_temporary_resolutions,
    contains_secret,
    has_temporary_resolution_language,
    require_memory_content,
    temporary_resolution_candidates,
)


_MAX_CATALOG_LIMIT = 512
_CLAIM_LEASE_SECONDS = 180
_REVIEW_RETRY_DELAY_SECONDS = 300
_MAX_REVIEW_ATTEMPTS = 3
_MAX_DEAD_LETTERS_PER_SCOPE = 128
_MANUAL_TEMPORARY_DAYS = 30
_SQLITE_BUSY_TIMEOUT_SECONDS = 5.0
_SQLITE_BUSY_TIMEOUT_MILLISECONDS = int(_SQLITE_BUSY_TIMEOUT_SECONDS * 1000)
_STORAGE_MEMORY_KINDS = frozenset({*PROFILE_MEMORY_KINDS, "general"})


def _sqlite_wal_reset_vulnerable(version_info: tuple[int, ...]) -> bool:
    """Mirror Hermes' import-safe WAL-reset safety gate."""

    info = tuple(int(part) for part in (*version_info, 0, 0, 0)[:3])
    if info < (3, 7, 0) or info >= (3, 51, 3):
        return False
    if (3, 50, 7) <= info < (3, 51, 0):
        return False
    if (3, 44, 6) <= info < (3, 45, 0):
        return False
    return True


class ProfileMemoryManagementError(RelationshipContractError):
    """Safe, closed error vocabulary consumed by the Runtime envelope."""

    _ALLOWED = frozenset(
        {
            "memory.invalid_request",
            "memory.revision_conflict",
            "memory.entry_not_found",
            "memory.secret_rejected",
            "memory.export_failed",
            "memory.operation_failed",
        }
    )

    def __init__(self, code: str, message: str) -> None:
        if code not in self._ALLOWED:
            code = "memory.operation_failed"
        super().__init__(message)
        self.code = code
        self.safe_message = message


PROFILE_SCHEMA_V1_STATEMENTS = (
    """
    CREATE TABLE profile_schema_migrations (
        schema_version INTEGER PRIMARY KEY,
        applied_at_utc TEXT NOT NULL
    )
    """,
    """
    CREATE TABLE profile_meta (
        singleton INTEGER PRIMARY KEY CHECK(singleton = 1),
        catalog_revision INTEGER NOT NULL CHECK(catalog_revision >= 0)
    )
    """,
    """
    CREATE TABLE profile_preferences (
        singleton INTEGER PRIMARY KEY CHECK(singleton = 1),
        preferences_revision INTEGER NOT NULL CHECK(preferences_revision >= 0),
        auto_memory_enabled INTEGER NOT NULL CHECK(auto_memory_enabled IN (0,1)),
        updated_at_utc TEXT NOT NULL
    )
    """,
    """
    CREATE TABLE profile_entries (
        entry_id TEXT PRIMARY KEY,
        entry_version INTEGER NOT NULL CHECK(entry_version > 0),
        owner_id TEXT NOT NULL,
        character_key TEXT NOT NULL,
        canon_fingerprint TEXT NOT NULL,
        category TEXT NOT NULL CHECK(category IN (
            'relationship_core','long_term','candidate','temporary'
        )),
        state TEXT NOT NULL CHECK(state IN (
            'active','hidden','rejected','superseded'
        )),
        content_text TEXT NOT NULL,
        content_normalized TEXT NOT NULL,
        source TEXT NOT NULL CHECK(source IN (
            'explicit_user','inferred','operator','relationship_review'
        )),
        observed_at_utc TEXT NOT NULL,
        updated_at_utc TEXT NOT NULL,
        expires_at_utc TEXT,
        pinned INTEGER NOT NULL CHECK(pinned IN (0,1)),
        evidence_excerpt TEXT,
        memory_kind TEXT NOT NULL CHECK(memory_kind IN (
            'preference','identity','routine','communication',
            'relationship_core','boundary','goal','general'
        )),
        canonical_key TEXT NOT NULL,
        source_hash TEXT NOT NULL,
        current_revision_id TEXT NOT NULL,
        CHECK(pinned = 0 OR category = 'relationship_core'),
        UNIQUE(owner_id, character_key, canon_fingerprint, entry_id)
    )
    """,
    """
    CREATE TABLE profile_evidence (
        evidence_id TEXT PRIMARY KEY,
        entry_id TEXT NOT NULL,
        owner_id TEXT NOT NULL,
        character_key TEXT NOT NULL,
        canon_fingerprint TEXT NOT NULL,
        assertion_id TEXT NOT NULL,
        source_hash TEXT NOT NULL,
        session_id TEXT NOT NULL,
        turn_id TEXT NOT NULL,
        user_message_id TEXT NOT NULL,
        assistant_message_id TEXT NOT NULL,
        observed_at_utc TEXT NOT NULL,
        evidence_excerpt TEXT NOT NULL,
        assertion_kind TEXT NOT NULL CHECK(assertion_kind IN ('explicit','inferred','temporary')),
        FOREIGN KEY(entry_id) REFERENCES profile_entries(entry_id) ON DELETE CASCADE,
        UNIQUE(owner_id, character_key, canon_fingerprint, assertion_id)
    )
    """,
    """
    CREATE TABLE profile_revisions (
        revision_id TEXT PRIMARY KEY,
        entry_id TEXT NOT NULL,
        owner_id TEXT NOT NULL,
        character_key TEXT NOT NULL,
        canon_fingerprint TEXT NOT NULL,
        previous_revision_id TEXT,
        entry_version_after INTEGER NOT NULL CHECK(entry_version_after > 0),
        mutation_kind TEXT NOT NULL,
        category_after TEXT NOT NULL,
        state_after TEXT NOT NULL,
        content_after TEXT NOT NULL,
        source_after TEXT NOT NULL,
        expires_at_utc_after TEXT,
        pinned_after INTEGER NOT NULL CHECK(pinned_after IN (0,1)),
        evidence_excerpt_after TEXT,
        source_hash TEXT,
        superseded_by_entry_id TEXT,
        operation_id TEXT,
        created_at_utc TEXT NOT NULL,
        FOREIGN KEY(entry_id) REFERENCES profile_entries(entry_id) ON DELETE CASCADE
            DEFERRABLE INITIALLY DEFERRED
    )
    """,
    """
    CREATE TABLE profile_review_outbox (
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
        claim_token TEXT,
        claim_expires_at_utc TEXT,
        attempt_count INTEGER NOT NULL DEFAULT 0,
        UNIQUE(owner_id, character_key, canon_fingerprint, session_id, turn_id),
        UNIQUE(owner_id, character_key, canon_fingerprint, user_message_id),
        UNIQUE(owner_id, character_key, canon_fingerprint, assistant_message_id)
    )
    """,
    """
    CREATE TABLE profile_processed_reviews (
        source_hash TEXT PRIMARY KEY,
        owner_id TEXT NOT NULL,
        character_key TEXT NOT NULL,
        canon_fingerprint TEXT NOT NULL,
        session_id TEXT NOT NULL,
        turn_id TEXT NOT NULL,
        user_message_id TEXT NOT NULL,
        assistant_message_id TEXT NOT NULL,
        processed_at_utc TEXT NOT NULL,
        UNIQUE(owner_id, character_key, canon_fingerprint, session_id, turn_id),
        UNIQUE(owner_id, character_key, canon_fingerprint, user_message_id),
        UNIQUE(owner_id, character_key, canon_fingerprint, assistant_message_id)
    )
    """,
    """
    CREATE TABLE profile_operations (
        operation_id TEXT PRIMARY KEY,
        action TEXT NOT NULL,
        request_hash TEXT NOT NULL,
        response_json TEXT NOT NULL,
        created_at_utc TEXT NOT NULL
    )
    """,
    """
    CREATE INDEX profile_entries_scope_key
    ON profile_entries(owner_id, character_key, canon_fingerprint, canonical_key, state)
    """,
    """
    CREATE INDEX profile_entries_scope_recall
    ON profile_entries(owner_id, character_key, canon_fingerprint, state, category, pinned, updated_at_utc DESC)
    """,
    """
    CREATE INDEX profile_evidence_entry_time
    ON profile_evidence(owner_id, character_key, canon_fingerprint, entry_id, observed_at_utc DESC)
    """,
    """
    CREATE INDEX profile_revisions_entry_time
    ON profile_revisions(owner_id, character_key, canon_fingerprint, entry_id, created_at_utc DESC)
    """,
    """
    CREATE INDEX profile_outbox_queue
    ON profile_review_outbox(owner_id, character_key, canon_fingerprint, review_queued_at_utc)
    """,
)


PROFILE_SCHEMA_V2_STATEMENTS = (
    "ALTER TABLE profile_meta ADD COLUMN scope_owner_id TEXT",
    "ALTER TABLE profile_meta ADD COLUMN scope_character_key TEXT",
    "ALTER TABLE profile_meta ADD COLUMN scope_canon_fingerprint TEXT",
    "ALTER TABLE profile_meta ADD COLUMN telegram_subject_id TEXT",
    "ALTER TABLE profile_operations ADD COLUMN entry_id TEXT",
    "CREATE INDEX profile_operations_entry ON profile_operations(entry_id)",
    "ALTER TABLE profile_review_outbox ADD COLUMN next_attempt_at_utc TEXT",
    """
    CREATE TABLE profile_review_dead_letter (
        source_hash TEXT PRIMARY KEY,
        owner_id TEXT NOT NULL,
        character_key TEXT NOT NULL,
        canon_fingerprint TEXT NOT NULL,
        session_id TEXT NOT NULL,
        turn_id TEXT NOT NULL,
        user_message_id TEXT NOT NULL,
        assistant_message_id TEXT NOT NULL,
        review_queued_at_utc TEXT NOT NULL,
        dead_lettered_at_utc TEXT NOT NULL,
        attempt_count INTEGER NOT NULL CHECK(attempt_count > 0),
        reason_code TEXT NOT NULL
    )
    """,
    """
    CREATE INDEX profile_dead_letter_scope_time
    ON profile_review_dead_letter(
        owner_id, character_key, canon_fingerprint, dead_lettered_at_utc DESC, source_hash
    )
    """,
)


def _harden_profile_sidecar(data_dir: Path) -> None:
    if os.name == "nt":
        return
    try:
        if data_dir.exists():
            data_dir.chmod(0o700)
        for suffix in ("", "-journal", "-wal", "-shm"):
            path = data_dir / (PROFILE_DATABASE_FILE + suffix)
            if path.exists():
                path.chmod(0o600)
    except OSError as exc:
        raise RelationshipContractError(
            "profile-memory storage permissions could not be secured"
        ) from exc


def _normalize_content(value: str) -> str:
    return " ".join(value.casefold().split())


def _is_cjk(character: str) -> bool:
    code = ord(character)
    return 0x3400 <= code <= 0x9FFF or 0x3040 <= code <= 0x30FF or 0xAC00 <= code <= 0xD7AF


def _search_units(text: str) -> frozenset[str]:
    units: set[str] = set()
    word = ""
    cjk_run = ""

    def flush_word() -> None:
        nonlocal word
        if word:
            units.add(word)
            word = ""

    def flush_cjk() -> None:
        nonlocal cjk_run
        if cjk_run:
            if len(cjk_run) == 1:
                units.add(cjk_run)
            else:
                units.update(
                    cjk_run[index : index + 2]
                    for index in range(len(cjk_run) - 1)
                )
            cjk_run = ""

    for character in text.casefold():
        if _is_cjk(character):
            flush_word()
            cjk_run += character
        elif character.isalnum():
            flush_cjk()
            word += character
        else:
            flush_word()
            flush_cjk()
    flush_word()
    flush_cjk()
    return frozenset(item for item in units if item)


def _kind_hints(query: str) -> frozenset[str]:
    lowered = query.casefold()
    ascii_words = " " + " ".join(
        "".join(character if character.isascii() and character.isalnum() else " " for character in lowered).split()
    ) + " "
    vocabularies = {
        "preference": ("prefer", "favorite", "like", "dislike", "eat", "drink", "recommend", "喜歡", "喜欢", "討厭", "讨厌", "吃", "喝", "推薦", "推荐", "好き", "嫌い"),
        "identity": ("name", "call you", "who am i", "名字", "稱呼", "称呼", "名前"),
        "routine": ("routine", "usually", "schedule", "習慣", "习惯", "通常", "日課"),
        "communication": ("reply", "tone", "language", "回覆", "回复", "語氣", "语气", "言語"),
        "boundary": ("boundary", "don't", "do not", "不要", "不准", "禁止", "やめて"),
        "goal": ("goal", "plan", "want to", "目標", "目标", "計畫", "计划"),
    }
    def present(word: str) -> bool:
        if word.isascii():
            needle = " ".join(
                "".join(
                    character if character.isalnum() else " "
                    for character in word.casefold()
                ).split()
            )
            return " " + needle + " " in ascii_words
        return word in lowered

    return frozenset(
        kind for kind, words in vocabularies.items() if any(present(word) for word in words)
    )


_PROFILE_GENERIC_SEARCH_UNITS = _search_units(
    "player user the player's user's likes like loves love prefers prefer dislikes "
    "dislike hates hate name birthday routine habit goal pronoun language "
    "玩家 使用者 用户 喜歡 喜欢 愛 爱 討厭 讨厌 偏好 過敏 过敏 名字 生日 "
    "習慣 习惯 目標 目标 稱呼 称呼 語言 语言 プレイヤー ユーザー 好き 嫌い "
    "名前 誕生日 習慣 目標 플레이어 사용자 좋아 싫어 알레르기 이름 생일 습관 목표 언어"
)


def _management_error(code: str, message: str) -> ProfileMemoryManagementError:
    return ProfileMemoryManagementError(code, message)


class ProfileMemoryStore:
    """Versioned profile/evidence/revision/outbox store and D057 backend."""

    def __init__(self, data_dir: Path, *, scope: RelationshipScope) -> None:
        self._data_dir = Path(data_dir).resolve()
        self._path = self._data_dir / PROFILE_DATABASE_FILE
        self._scope = scope
        self._lock = threading.RLock()
        self._data_dir.mkdir(parents=True, exist_ok=True)
        _harden_profile_sidecar(self._data_dir)
        self._initialize_database()

    @property
    def path(self) -> Path:
        return self._path

    @property
    def scope(self) -> RelationshipScope:
        return self._scope

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(
            self._path,
            timeout=_SQLITE_BUSY_TIMEOUT_SECONDS,
            isolation_level=None,
        )
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON")
        connection.execute("PRAGMA secure_delete = ON")
        connection.execute(f"PRAGMA busy_timeout = {_SQLITE_BUSY_TIMEOUT_MILLISECONDS}")
        return connection

    @contextmanager
    def _connection(self):
        connection = self._connect()
        try:
            yield connection
        finally:
            connection.close()

    def _initialize_database(self) -> None:
        with self._lock, self._connection() as connection:
            # The bundled Python currently links SQLite 3.50.4, which Hermes
            # classifies as vulnerable to the WAL-reset corruption bug. Use
            # rollback journal there; automatically enable WAL only after the
            # linked SQLite reaches a fixed build.
            requested_journal = (
                "DELETE"
                if _sqlite_wal_reset_vulnerable(sqlite3.sqlite_version_info)
                else "WAL"
            )
            journal_mode = connection.execute(
                "PRAGMA journal_mode = " + requested_journal
            ).fetchone()[0]
            if str(journal_mode).casefold() != requested_journal.casefold():
                raise RelationshipContractError("profile-memory journal mode is unavailable")
            if requested_journal == "WAL":
                connection.execute("PRAGMA wal_autocheckpoint = 1000")
            version = int(connection.execute("PRAGMA user_version").fetchone()[0])
            if version > PROFILE_STORE_SCHEMA_VERSION:
                raise RelationshipContractError("profile-memory schema is newer than this plugin")
            if version == 0:
                now = canonical_utc(utc_now())
                connection.execute("BEGIN EXCLUSIVE")
                try:
                    for statement in PROFILE_SCHEMA_V1_STATEMENTS:
                        connection.execute(statement)
                    connection.execute(
                        "INSERT INTO profile_schema_migrations(schema_version, applied_at_utc) VALUES (?, ?)",
                        (1, now),
                    )
                    connection.execute(
                        "INSERT INTO profile_meta(singleton, catalog_revision) VALUES (1, 0)"
                    )
                    connection.execute(
                        "INSERT INTO profile_preferences(singleton, preferences_revision, auto_memory_enabled, updated_at_utc) VALUES (1, 0, 1, ?)",
                        (now,),
                    )
                    connection.execute("PRAGMA user_version = 1")
                    self._migrate_v1_to_v2(connection, now)
                    connection.execute("COMMIT")
                except Exception:
                    connection.execute("ROLLBACK")
                    raise
            elif version == 1:
                now = canonical_utc(utc_now())
                connection.execute("BEGIN EXCLUSIVE")
                try:
                    self._migrate_v1_to_v2(connection, now)
                    connection.execute("COMMIT")
                except Exception:
                    connection.execute("ROLLBACK")
                    raise
            self._verify_schema(connection)
        _harden_profile_sidecar(self._data_dir)

    @staticmethod
    def _entry_id_from_operation_response(response_json: str) -> str | None:
        try:
            response = json.loads(response_json)
        except Exception:
            return None
        if not isinstance(response, dict):
            return None
        entry = response.get("entry")
        candidate = entry.get("entry_id") if isinstance(entry, dict) else response.get("entry_id")
        return candidate if isinstance(candidate, str) and candidate else None

    def _migrate_v1_to_v2(self, connection: sqlite3.Connection, applied_at_utc: str) -> None:
        scope_rows = connection.execute(
            """
            SELECT owner_id, character_key, canon_fingerprint FROM profile_entries
            UNION SELECT owner_id, character_key, canon_fingerprint FROM profile_evidence
            UNION SELECT owner_id, character_key, canon_fingerprint FROM profile_revisions
            UNION SELECT owner_id, character_key, canon_fingerprint FROM profile_review_outbox
            UNION SELECT owner_id, character_key, canon_fingerprint FROM profile_processed_reviews
            """
        ).fetchall()
        discovered = {
            (row["owner_id"], row["character_key"], row["canon_fingerprint"])
            for row in scope_rows
        }
        if len(discovered) > 1 or (discovered and discovered != {self._scope_values()}):
            raise RelationshipContractError("profile-memory scope binding is invalid")
        for statement in PROFILE_SCHEMA_V2_STATEMENTS:
            connection.execute(statement)
        connection.execute(
            """
            UPDATE profile_meta
            SET scope_owner_id=?, scope_character_key=?, scope_canon_fingerprint=?
            WHERE singleton=1
            """,
            self._scope_values(),
        )
        rows = connection.execute(
            "SELECT operation_id, response_json FROM profile_operations"
        ).fetchall()
        for row in rows:
            entry_id = self._entry_id_from_operation_response(row["response_json"])
            if entry_id is not None:
                connection.execute(
                    "UPDATE profile_operations SET entry_id=? WHERE operation_id=?",
                    (entry_id, row["operation_id"]),
                )
        connection.execute(
            "INSERT INTO profile_schema_migrations(schema_version, applied_at_utc) VALUES (2, ?)",
            (applied_at_utc,),
        )
        connection.execute("PRAGMA user_version = 2")

    def _verify_schema(self, connection: sqlite3.Connection) -> None:
        expected = {
            "profile_schema_migrations", "profile_meta", "profile_preferences",
            "profile_entries", "profile_evidence", "profile_revisions",
            "profile_review_outbox", "profile_review_dead_letter",
            "profile_processed_reviews", "profile_operations",
        }
        tables = {
            row[0]
            for row in connection.execute(
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"
            )
        }
        if tables != expected:
            raise RelationshipContractError("profile-memory schema object set is invalid")
        migration = connection.execute(
            "SELECT schema_version FROM profile_schema_migrations ORDER BY schema_version"
        ).fetchall()
        if [int(row[0]) for row in migration] != list(
            range(1, PROFILE_STORE_SCHEMA_VERSION + 1)
        ):
            raise RelationshipContractError("profile-memory migration ledger is invalid")
        operation_columns = {
            row[1] for row in connection.execute("PRAGMA table_info(profile_operations)")
        }
        meta_columns = {
            row[1] for row in connection.execute("PRAGMA table_info(profile_meta)")
        }
        outbox_columns = {
            row[1] for row in connection.execute("PRAGMA table_info(profile_review_outbox)")
        }
        if (
            "entry_id" not in operation_columns
            or "next_attempt_at_utc" not in outbox_columns
            or "telegram_subject_id" not in meta_columns
        ):
            raise RelationshipContractError("profile-memory schema columns are invalid")
        bound_scope = connection.execute(
            """
            SELECT scope_owner_id, scope_character_key, scope_canon_fingerprint
            FROM profile_meta WHERE singleton=1
            """
        ).fetchone()
        if bound_scope is None or tuple(bound_scope) != self._scope_values():
            raise RelationshipContractError("profile-memory scope binding is invalid")

    def _scope_values(self) -> tuple[str, str, str]:
        return (self._scope.owner_id, self._scope.character_key, self._scope.canon_fingerprint)

    @staticmethod
    def _require_telegram_subject_id(subject_id: Any) -> str:
        if (
            not isinstance(subject_id, str)
            or not 1 <= len(subject_id) <= 128
            or any(not 0x21 <= ord(character) <= 0x7E for character in subject_id)
        ):
            raise RelationshipContractError("telegram profile owner is invalid")
        return subject_id

    def bind_or_validate_telegram_subject(self, subject_id: Any) -> str:
        """Atomically bind this personal profile to its first paired Telegram user."""

        checked = self._require_telegram_subject_id(subject_id)
        with self._lock, self._connection() as connection:
            connection.execute("BEGIN IMMEDIATE")
            try:
                row = connection.execute(
                    "SELECT telegram_subject_id FROM profile_meta WHERE singleton=1"
                ).fetchone()
                if row is None:
                    raise RelationshipContractError("profile-memory metadata is absent")
                bound = row["telegram_subject_id"]
                if bound is None:
                    connection.execute(
                        "UPDATE profile_meta SET telegram_subject_id=? WHERE singleton=1 AND telegram_subject_id IS NULL",
                        (checked,),
                    )
                    bound = checked
                if bound != checked:
                    raise RelationshipContractError("telegram profile owner does not match")
                connection.execute("COMMIT")
                return checked
            except Exception:
                connection.execute("ROLLBACK")
                raise

    @staticmethod
    def _row_to_entry(row: sqlite3.Row) -> ProfileMemoryEntry:
        scope = RelationshipScope(
            owner_id=row["owner_id"],
            character_key=row["character_key"],
            canon_fingerprint=row["canon_fingerprint"],
        )
        return ProfileMemoryEntry(
            entry_id=row["entry_id"],
            entry_version=int(row["entry_version"]),
            scope=scope,
            category=row["category"],
            state=row["state"],
            content=row["content_text"],
            source=row["source"],
            observed_at_utc=row["observed_at_utc"],
            updated_at_utc=row["updated_at_utc"],
            expires_at_utc=row["expires_at_utc"],
            pinned=bool(row["pinned"]),
            evidence_excerpt=row["evidence_excerpt"],
            memory_kind=row["memory_kind"],
            canonical_key=row["canonical_key"],
            source_hash=row["source_hash"],
            current_revision_id=row["current_revision_id"],
        )

    def auto_memory_enabled(self) -> bool:
        with self._connection() as connection:
            row = connection.execute(
                "SELECT auto_memory_enabled FROM profile_preferences WHERE singleton = 1"
            ).fetchone()
        return bool(row[0])

    def _validate_witness(self, witness: FrozenTurn) -> None:
        if not isinstance(witness, FrozenTurn) or witness.scope != self._scope:
            raise RelationshipContractError("profile review scope mismatch")
        require_bounded_text(witness.session_id, "session_id", maximum=240)
        require_turn_identity(witness.turn_id, "turn_id")
        require_turn_identity(witness.user_message_id, "user_message_id")
        require_turn_identity(witness.assistant_message_id, "assistant_message_id")
        require_bounded_text(witness.user_text, "user_text", maximum=65536)
        require_bounded_text(witness.assistant_text, "assistant_text", maximum=65536)
        parse_utc(witness.observed_at_utc, "observed_at_utc")
        parse_utc(witness.review_queued_at_utc, "review_queued_at_utc")
        require_event_identity(witness.source_hash, "source_hash")
        expected = build_source_hash(
            witness.scope,
            session_id=witness.session_id,
            turn_id=witness.turn_id,
            user_message_id=witness.user_message_id,
            assistant_message_id=witness.assistant_message_id,
            user_text=witness.user_text,
            assistant_text=witness.assistant_text,
        )
        if expected != witness.source_hash:
            raise RelationshipContractError("profile review source hash mismatch")

    def enqueue_review_status(self, witness: FrozenTurn) -> str:
        self._validate_witness(witness)
        if contains_secret(witness.user_text) or contains_secret(witness.assistant_text):
            raise RelationshipContractError("credential-like turns cannot enter profile memory")
        if not self.auto_memory_enabled():
            return "disabled"
        values = self._scope_values()
        with self._lock, self._connection() as connection:
            connection.execute("BEGIN IMMEDIATE")
            try:
                if connection.execute(
                    "SELECT 1 FROM profile_processed_reviews WHERE source_hash = ?",
                    (witness.source_hash,),
                ).fetchone():
                    connection.execute("COMMIT")
                    return "processed_duplicate"
                inserted = connection.execute(
                    """
                    INSERT OR IGNORE INTO profile_review_outbox(
                        source_hash, owner_id, character_key, canon_fingerprint,
                        session_id, turn_id, user_message_id, assistant_message_id,
                        user_text, assistant_text, observed_at_utc, review_queued_at_utc
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        witness.source_hash, *values, witness.session_id, witness.turn_id,
                        witness.user_message_id, witness.assistant_message_id,
                        witness.user_text, witness.assistant_text, witness.observed_at_utc,
                        witness.review_queued_at_utc,
                    ),
                ).rowcount
                connection.execute("COMMIT")
                return "inserted" if inserted else "pending_duplicate"
            except Exception:
                connection.execute("ROLLBACK")
                raise

    def enqueue_review(self, witness: FrozenTurn) -> bool:
        return self.enqueue_review_status(witness) == "inserted"

    @staticmethod
    def _row_to_witness(row: sqlite3.Row) -> FrozenTurn:
        return FrozenTurn(
            scope=RelationshipScope(row["owner_id"], row["character_key"], row["canon_fingerprint"]),
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

    def pending_review_count(self) -> int:
        with self._connection() as connection:
            return int(connection.execute(
                "SELECT COUNT(*) FROM profile_review_outbox WHERE owner_id=? AND character_key=? AND canon_fingerprint=?",
                self._scope_values(),
            ).fetchone()[0])

    def processed_review_count(self) -> int:
        with self._connection() as connection:
            return int(connection.execute(
                "SELECT COUNT(*) FROM profile_processed_reviews WHERE owner_id=? AND character_key=? AND canon_fingerprint=?",
                self._scope_values(),
            ).fetchone()[0])

    def dead_letter_count(self) -> int:
        with self._connection() as connection:
            return int(connection.execute(
                "SELECT COUNT(*) FROM profile_review_dead_letter WHERE owner_id=? AND character_key=? AND canon_fingerprint=?",
                self._scope_values(),
            ).fetchone()[0])

    def _has_earlier_pending_review(
        self, connection: sqlite3.Connection, source_hash: str, observed_at_utc: str,
    ) -> bool:
        return connection.execute(
            """SELECT 1 FROM profile_review_outbox
               WHERE owner_id=? AND character_key=? AND canon_fingerprint=?
                 AND source_hash<>? AND observed_at_utc<? LIMIT 1""",
            (*self._scope_values(), source_hash, observed_at_utc),
        ).fetchone() is not None

    def claim_pending_reviews(self, *, claim_token: str, now: datetime, limit: int = 1,
                              eligible_source_hashes: tuple[str, ...] | None = None) -> tuple[FrozenTurn, ...]:
        if not self.auto_memory_enabled():
            return ()
        token = require_bounded_text(claim_token, "claim_token", maximum=128)
        if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= 32:
            raise RelationshipContractError("profile review claim limit is invalid")
        eligible_parameters = ()
        if eligible_source_hashes is not None:
            if type(eligible_source_hashes) is not tuple or len(eligible_source_hashes) > 32768:
                raise RelationshipContractError("profile review eligibility is invalid")
            if not eligible_source_hashes:
                return ()
            eligible_parameters = tuple(dict.fromkeys(
                require_event_identity(value, "source_hash") for value in eligible_source_hashes))
        now_utc = canonical_utc(now)
        expires = canonical_utc(now.astimezone(timezone.utc) + timedelta(seconds=_CLAIM_LEASE_SECONDS))
        with self._lock, self._connection() as connection:
            connection.execute("BEGIN IMMEDIATE")
            try:
                # Eligibility precedes LIMIT and claim-attempt increment. An
                # older row without its economic origin cannot starve current
                # work. Fixed-size batches also stay below SQLite's variable
                # limit even when all 32,768 retained origins are eligible.
                batches = (((),) if eligible_source_hashes is None else
                           tuple(eligible_parameters[start:start + 900]
                                 for start in range(0, len(eligible_parameters), 900)))
                rows = []
                for batch in batches:
                    eligibility_sql = ("" if eligible_source_hashes is None else
                                       " AND source_hash IN (" + ",".join("?" for _ in batch) + ")")
                    batch_rows = connection.execute(
                        """
                        SELECT * FROM profile_review_outbox
                        WHERE owner_id=? AND character_key=? AND canon_fingerprint=?
                          AND (claim_token IS NULL OR claim_expires_at_utc <= ?)
                          AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= ?)
                        """ + eligibility_sql + " ORDER BY observed_at_utc, review_queued_at_utc, source_hash LIMIT 32",
                        (*self._scope_values(), now_utc, now_utc, *batch),
                    ).fetchall()
                    # Same deterministic order as SQLite; keep only the global
                    # oldest 32 without growing a full eligible-row collection.
                    rows = heapq.nsmallest(32, (*rows, *batch_rows), key=lambda row: (
                        row["observed_at_utc"], row["review_queued_at_utc"], row["source_hash"],
                    ))
                # A cancellation must not be consumed as "no target" while an
                # earlier arrangement is deferred or leased elsewhere. Leave
                # it queued without spending attempts until that work settles.
                rows = [row for row in rows if not (
                    has_temporary_resolution_language(row["user_text"])
                    and self._has_earlier_pending_review(
                        connection, row["source_hash"], row["observed_at_utc"],
                    )
                )][:limit]
                for row in rows:
                    connection.execute(
                        "UPDATE profile_review_outbox SET claim_token=?, claim_expires_at_utc=?, attempt_count=attempt_count+1 WHERE source_hash=?",
                        (token, expires, row["source_hash"]),
                    )
                connection.execute("COMMIT")
            except Exception:
                connection.execute("ROLLBACK")
                raise
        return tuple(self._row_to_witness(row) for row in rows)

    def release_claim(self, source_hash: str, claim_token: str) -> None:
        require_event_identity(source_hash, "source_hash")
        token = require_bounded_text(claim_token, "claim_token", maximum=128)
        with self._lock, self._connection() as connection:
            connection.execute(
                "UPDATE profile_review_outbox SET claim_token=NULL, claim_expires_at_utc=NULL WHERE source_hash=? AND claim_token=?",
                (source_hash, token),
            )

    def defer_claim(self, source_hash: str, claim_token: str) -> None:
        """Release a budget-deferred scheduling claim without failure/quarantine.

        This reverses only this outbox claim's attempt increment, never provider
        usage. Matching lease identity makes a duplicate deferral harmless.
        """
        require_event_identity(source_hash, "source_hash")
        token = require_bounded_text(claim_token, "claim_token", maximum=128)
        with self._lock, self._connection() as connection:
            connection.execute(
                "UPDATE profile_review_outbox SET claim_token=NULL, claim_expires_at_utc=NULL, "
                "attempt_count=MAX(0,attempt_count-1) WHERE source_hash=? AND claim_token=?",
                (source_hash, token),
            )

    def fail_claim(
        self,
        source_hash: str,
        claim_token: str,
        *,
        now: datetime,
        reason_code: str = "review_failed",
    ) -> bool:
        """Defer one failed review or quarantine it after a bounded retry count.

        The dead-letter row intentionally contains only identities and timing,
        never the player or assistant text that made up the review payload.
        Returns ``True`` when this failure moved the row to dead-letter.
        """

        require_event_identity(source_hash, "source_hash")
        token = require_bounded_text(claim_token, "claim_token", maximum=128)
        reason = require_bounded_text(reason_code, "reason_code", maximum=64)
        if any(not character.isascii() or (not character.isalnum() and character not in "._-") for character in reason):
            raise RelationshipContractError("profile review failure reason is invalid")
        now_utc = canonical_utc(now)
        retry_at = canonical_utc(
            now.astimezone(timezone.utc) + timedelta(seconds=_REVIEW_RETRY_DELAY_SECONDS)
        )
        with self._lock, self._connection() as connection:
            connection.execute("BEGIN IMMEDIATE")
            try:
                row = connection.execute(
                    "SELECT * FROM profile_review_outbox WHERE source_hash=? AND claim_token=?",
                    (source_hash, token),
                ).fetchone()
                if row is None:
                    connection.execute("COMMIT")
                    return False
                attempts = int(row["attempt_count"])
                if attempts < _MAX_REVIEW_ATTEMPTS:
                    connection.execute(
                        """
                        UPDATE profile_review_outbox
                        SET claim_token=NULL, claim_expires_at_utc=NULL, next_attempt_at_utc=?
                        WHERE source_hash=? AND claim_token=?
                        """,
                        (retry_at, source_hash, token),
                    )
                    connection.execute("COMMIT")
                    return False
                connection.execute(
                    """
                    INSERT OR REPLACE INTO profile_review_dead_letter(
                        source_hash, owner_id, character_key, canon_fingerprint,
                        session_id, turn_id, user_message_id, assistant_message_id,
                        review_queued_at_utc, dead_lettered_at_utc, attempt_count, reason_code
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        row["source_hash"], row["owner_id"], row["character_key"],
                        row["canon_fingerprint"], row["session_id"], row["turn_id"],
                        row["user_message_id"], row["assistant_message_id"],
                        row["review_queued_at_utc"], now_utc, attempts, reason,
                    ),
                )
                connection.execute(
                    "DELETE FROM profile_review_outbox WHERE source_hash=? AND claim_token=?",
                    (source_hash, token),
                )
                connection.execute(
                    """
                    DELETE FROM profile_review_dead_letter
                    WHERE source_hash IN (
                        SELECT source_hash FROM profile_review_dead_letter
                        WHERE owner_id=? AND character_key=? AND canon_fingerprint=?
                        ORDER BY dead_lettered_at_utc DESC, source_hash DESC
                        LIMIT -1 OFFSET ?
                    )
                    """,
                    (*self._scope_values(), _MAX_DEAD_LETTERS_PER_SCOPE),
                )
                connection.execute("COMMIT")
                return True
            except Exception:
                connection.execute("ROLLBACK")
                raise

    def _entry_rows_for_key(self, connection: sqlite3.Connection, canonical_key: str) -> list[sqlite3.Row]:
        return connection.execute(
            """
            SELECT * FROM profile_entries
            WHERE owner_id=? AND character_key=? AND canon_fingerprint=? AND canonical_key=?
            ORDER BY updated_at_utc DESC, entry_id
            """,
            (*self._scope_values(), canonical_key),
        ).fetchall()

    @staticmethod
    def _new_entry_id(material: str) -> str:
        return "pmem_" + sha256_hex("lilith-profile-entry-v1\0" + material)[:40]

    @staticmethod
    def _new_revision_id(entry_id: str, version: int, material: str) -> str:
        return sha256_hex(
            "lilith-profile-revision-v1\0" + entry_id + "\0" + str(version) + "\0" + material
        )

    def _insert_revision(
        self,
        connection: sqlite3.Connection,
        *,
        row: dict[str, Any],
        previous_revision_id: str | None,
        mutation_kind: str,
        created_at_utc: str,
        operation_id: str | None = None,
        superseded_by_entry_id: str | None = None,
    ) -> str:
        material = canonical_json(
            {
                "entry_version": row["entry_version"], "category": row["category"],
                "state": row["state"], "content": row["content_text"],
                "source": row["source"], "expires": row["expires_at_utc"],
                "pinned": bool(row["pinned"]), "kind": mutation_kind,
                "operation_id": operation_id, "superseded_by": superseded_by_entry_id,
            }
        )
        revision_id = self._new_revision_id(row["entry_id"], int(row["entry_version"]), material)
        connection.execute(
            """
            INSERT INTO profile_revisions(
                revision_id, entry_id, owner_id, character_key, canon_fingerprint,
                previous_revision_id, entry_version_after, mutation_kind,
                category_after, state_after, content_after, source_after,
                expires_at_utc_after, pinned_after, evidence_excerpt_after,
                source_hash, superseded_by_entry_id, operation_id, created_at_utc
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                revision_id, row["entry_id"], *self._scope_values(), previous_revision_id,
                row["entry_version"], mutation_kind, row["category"], row["state"],
                row["content_text"], row["source"], row["expires_at_utc"], row["pinned"],
                row.get("evidence_excerpt"), row.get("source_hash"), superseded_by_entry_id,
                operation_id, created_at_utc,
            ),
        )
        return revision_id

    def _insert_evidence(self, connection: sqlite3.Connection, entry_id: str, assertion: ProfileMemoryAssertion) -> None:
        evidence_id = sha256_hex(
            "lilith-profile-evidence-v1\0" + entry_id + "\0" + assertion.assertion_id
        )
        connection.execute(
            """
            INSERT OR IGNORE INTO profile_evidence(
                evidence_id, entry_id, owner_id, character_key, canon_fingerprint,
                assertion_id, source_hash, session_id, turn_id, user_message_id,
                assistant_message_id, observed_at_utc, evidence_excerpt, assertion_kind
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                evidence_id, entry_id, *self._scope_values(), assertion.assertion_id,
                assertion.source_hash, assertion.session_id, assertion.turn_id,
                assertion.user_message_id, assertion.assistant_message_id,
                assertion.observed_at_utc, assertion.evidence_excerpt, assertion.assertion_kind,
            ),
        )

    def _create_assertion_entry(self, connection: sqlite3.Connection, assertion: ProfileMemoryAssertion) -> str:
        entry_id = self._new_entry_id(assertion.assertion_id)
        row = {
            "entry_id": entry_id,
            "entry_version": 1,
            "category": assertion.category,
            "state": "active",
            "content_text": assertion.content,
            "source": assertion.source,
            "expires_at_utc": assertion.expires_at_utc,
            "pinned": int(assertion.pinned),
            "evidence_excerpt": assertion.evidence_excerpt,
            "source_hash": assertion.source_hash,
        }
        revision_id = self._insert_revision(
            connection, row=row, previous_revision_id=None, mutation_kind="created",
            created_at_utc=assertion.recorded_at_utc,
        )
        connection.execute(
            """
            INSERT INTO profile_entries(
                entry_id, entry_version, owner_id, character_key, canon_fingerprint,
                category, state, content_text, content_normalized, source,
                observed_at_utc, updated_at_utc, expires_at_utc, pinned,
                evidence_excerpt, memory_kind, canonical_key, source_hash, current_revision_id
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                entry_id, 1, *self._scope_values(), assertion.category, "active",
                assertion.content, _normalize_content(assertion.content), assertion.source,
                assertion.observed_at_utc, assertion.recorded_at_utc, assertion.expires_at_utc,
                int(assertion.pinned), assertion.evidence_excerpt, assertion.memory_kind,
                assertion.canonical_key, assertion.source_hash, revision_id,
            ),
        )
        self._insert_evidence(connection, entry_id, assertion)
        return entry_id

    def _supersede(self, connection: sqlite3.Connection, row: sqlite3.Row, *, by_entry_id: str, at_utc: str) -> None:
        next_version = int(row["entry_version"]) + 1
        snapshot = dict(row)
        snapshot.update(entry_version=next_version, state="superseded", pinned=0)
        revision_id = self._insert_revision(
            connection, row=snapshot, previous_revision_id=row["current_revision_id"],
            mutation_kind="superseded", created_at_utc=at_utc,
            superseded_by_entry_id=by_entry_id,
        )
        connection.execute(
            "UPDATE profile_entries SET entry_version=?, state='superseded', pinned=0, updated_at_utc=?, current_revision_id=? WHERE entry_id=?",
            (next_version, at_utc, revision_id, row["entry_id"]),
        )

    def _apply_assertion(self, connection: sqlite3.Connection, assertion: ProfileMemoryAssertion) -> bool:
        if self._blocked_by_later_cancellation(connection, assertion):
            # An older queued/retried statement cannot revive a cancelled plan,
            # including when a classifier changes its canonical key.
            return False
        rows = self._entry_rows_for_key(connection, assertion.canonical_key)
        normalized = _normalize_content(assertion.content)
        pinned_rows = [
            row for row in rows
            if row["state"] in {"active", "hidden"} and bool(row["pinned"])
        ]
        if pinned_rows and not assertion.pinned:
            # A background classifier cannot replace a player-authorized core
            # fact.  Exact repeats may still enrich its audit evidence.
            pinned_same = next(
                (row for row in pinned_rows if row["content_normalized"] == normalized),
                None,
            )
            if pinned_same is not None:
                self._insert_evidence(connection, pinned_same["entry_id"], assertion)
            return False
        same = next(
            (
                row for row in rows
                if row["state"] in {"active", "hidden"}
                and row["content_normalized"] == normalized
                and row["category"] == assertion.category
            ),
            None,
        )
        if same is not None:
            self._insert_evidence(connection, same["entry_id"], assertion)
            # Repeated inference cannot rewrite public state.  Explicit
            # confirmation can refresh time/evidence and increment its version.
            if assertion.source == "inferred" or same["source"] != "inferred":
                return False
            next_version = int(same["entry_version"]) + 1
            snapshot = dict(same)
            snapshot.update(
                entry_version=next_version, source=assertion.source,
                evidence_excerpt=assertion.evidence_excerpt,
                source_hash=assertion.source_hash,
            )
            revision_id = self._insert_revision(
                connection, row=snapshot, previous_revision_id=same["current_revision_id"],
                mutation_kind="confirmed", created_at_utc=assertion.recorded_at_utc,
            )
            connection.execute(
                """
                UPDATE profile_entries SET entry_version=?, source=?, evidence_excerpt=?,
                    source_hash=?, updated_at_utc=?, current_revision_id=? WHERE entry_id=?
                """,
                (
                    next_version, assertion.source, assertion.evidence_excerpt,
                    assertion.source_hash, assertion.recorded_at_utc, revision_id,
                    same["entry_id"],
                ),
            )
            return True

        entry_id = self._create_assertion_entry(connection, assertion)
        if assertion.category != "candidate":
            new_subject_units = _search_units(
                assertion.content + " " + assertion.evidence_excerpt
            )
            for row in rows:
                if row["state"] not in {"active", "hidden"}:
                    continue
                if bool(row["pinned"]) and not assertion.pinned:
                    continue
                # A temporary fact overlays but never destroys the durable one.
                if assertion.category == "temporary" and row["category"] != "temporary":
                    continue
                old_subject_units = _search_units(
                    str(row["content_text"])
                    + " "
                    + str(row["evidence_excerpt"] or "")
                )
                if not (new_subject_units & old_subject_units):
                    # A model-selected canonical key cannot, by itself,
                    # authorize destruction of an unrelated player fact.
                    continue
                self._supersede(connection, row, by_entry_id=entry_id, at_utc=assertion.recorded_at_utc)
        return True

    def _resolution_candidates_in_connection(
        self, connection: sqlite3.Connection, witness: FrozenTurn,
    ) -> tuple[ProfileMemoryEntry, ...]:
        # Do not silently choose one target from a truncated/ambiguous catalog.
        rows = connection.execute(
            """SELECT * FROM profile_entries
               WHERE owner_id=? AND character_key=? AND canon_fingerprint=?
                 AND category='temporary' AND state='active' AND pinned=0
                 AND expires_at_utc>?
               ORDER BY observed_at_utc DESC, entry_id LIMIT 33""",
            (*self._scope_values(), witness.observed_at_utc),
        ).fetchall()
        if len(rows) > 32:
            return ()
        return temporary_resolution_candidates(
            witness.user_text, tuple(self._row_to_entry(row) for row in rows),
            observed_at=parse_utc(witness.observed_at_utc, "observed_at_utc"),
        )

    def resolution_candidates(self, witness: FrozenTurn) -> tuple[ProfileMemoryEntry, ...]:
        self._validate_witness(witness)
        if not has_temporary_resolution_language(witness.user_text):
            return ()
        with self._lock, self._connection() as connection:
            return self._resolution_candidates_in_connection(connection, witness)

    def _blocked_by_later_cancellation(
        self, connection: sqlite3.Connection, assertion: ProfileMemoryAssertion,
    ) -> bool:
        if assertion.category != "temporary":
            return False
        rows = connection.execute(
            """SELECT r.evidence_excerpt_after, e.observed_at_utc
               FROM profile_revisions r JOIN profile_evidence e
                 ON e.entry_id=r.entry_id AND e.source_hash=r.source_hash
               WHERE r.owner_id=? AND r.character_key=? AND r.canon_fingerprint=?
                 AND r.mutation_kind='cancelled' AND e.observed_at_utc>=?
               ORDER BY e.observed_at_utc DESC LIMIT 65""",
            (*self._scope_values(), assertion.observed_at_utc),
        ).fetchall()
        if len(rows) > 64:
            return True  # An unbounded old backlog cannot reintroduce plans.
        proposed = ProfileMemoryEntry(
            entry_id="pending-assertion", entry_version=1, scope=assertion.scope,
            category="temporary", state="active", content=assertion.content,
            source=assertion.source, observed_at_utc=assertion.observed_at_utc,
            updated_at_utc=assertion.observed_at_utc,
            expires_at_utc=assertion.expires_at_utc, pinned=False,
            evidence_excerpt=assertion.evidence_excerpt, memory_kind=assertion.memory_kind,
            canonical_key=assertion.canonical_key, source_hash=assertion.source_hash,
            current_revision_id="pending-assertion",
        )
        return any(temporary_resolution_candidates(
            str(row["evidence_excerpt_after"]), (proposed,),
            observed_at=parse_utc(row["observed_at_utc"], "observed_at_utc"),
        ) for row in rows)

    def _apply_temporary_resolution(
        self, connection: sqlite3.Connection, resolution: ProfileTemporaryResolution,
        witness: FrozenTurn,
    ) -> bool:
        # Re-read the whole bounded candidate set under the write transaction.
        # New competing plans, manual edits, hiding, pinning, and expiry must
        # never be overwritten using an obsolete model-visible snapshot.
        fresh = self._resolution_candidates_in_connection(connection, witness)
        if resolution.target not in fresh:
            raise RelationshipContractError("temporary resolution target changed or is ambiguous")
        rebound = bind_temporary_resolutions(
            [{"target_ref": "temporary_0", "evidence": resolution.evidence_excerpt,
              "disposition": "cancelled"}],
            witness=witness, candidates=(resolution.target,),
            recorded_at=parse_utc(resolution.recorded_at_utc, "recorded_at_utc"),
        )
        if rebound != [resolution]:
            raise RelationshipContractError("temporary resolution provenance mismatch")
        row = connection.execute(
            "SELECT * FROM profile_entries WHERE entry_id=?", (resolution.target.entry_id,),
        ).fetchone()
        snapshot = dict(row)
        next_version = int(row["entry_version"]) + 1
        snapshot.update(
            entry_version=next_version, state="superseded", pinned=0,
            evidence_excerpt=resolution.evidence_excerpt, source_hash=witness.source_hash,
        )
        revision = self._insert_revision(
            connection, row=snapshot, previous_revision_id=row["current_revision_id"],
            mutation_kind="cancelled", created_at_utc=resolution.recorded_at_utc,
        )
        # Keep the original text and its earlier revision. This is a terminal
        # lifecycle revision, not a permanent delete or a new durable fact.
        connection.execute(
            """UPDATE profile_entries SET entry_version=?, state='superseded', pinned=0,
               evidence_excerpt=?, source_hash=?, updated_at_utc=?, current_revision_id=?
               WHERE entry_id=? AND entry_version=?""",
            (next_version, resolution.evidence_excerpt, witness.source_hash,
             resolution.recorded_at_utc, revision, row["entry_id"], row["entry_version"]),
        )
        assertion_id = sha256_hex("temporary-cancellation-v1\0" + witness.source_hash + "\0" + row["entry_id"])
        evidence_id = sha256_hex("lilith-profile-evidence-v1\0" + row["entry_id"] + "\0" + assertion_id)
        connection.execute(
            """INSERT INTO profile_evidence(
               evidence_id, entry_id, owner_id, character_key, canon_fingerprint,
               assertion_id, source_hash, session_id, turn_id, user_message_id,
               assistant_message_id, observed_at_utc, evidence_excerpt, assertion_kind
               ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'explicit')""",
            (evidence_id, row["entry_id"], *self._scope_values(), assertion_id,
             witness.source_hash, witness.session_id, witness.turn_id, witness.user_message_id,
             witness.assistant_message_id, witness.observed_at_utc, resolution.evidence_excerpt),
        )
        return True

    def commit_review(
        self,
        witness: FrozenTurn,
        assertions: Sequence[ProfileMemoryAssertion],
        *,
        claim_token: str | None = None,
        resolutions: Sequence[ProfileTemporaryResolution] = (),
    ) -> int:
        self._validate_witness(witness)
        if not isinstance(assertions, Sequence) or len(assertions) > 4:
            raise RelationshipContractError("profile assertion batch is invalid")
        for assertion in assertions:
            if not isinstance(assertion, ProfileMemoryAssertion):
                raise RelationshipContractError("profile assertion type is invalid")
            if assertion.scope != self._scope or assertion.source_hash != witness.source_hash:
                raise RelationshipContractError("profile assertion provenance mismatch")
            if assertion.category not in MEMORY_CATEGORIES or assertion.source not in MEMORY_SOURCES:
                raise RelationshipContractError("profile assertion vocabulary is invalid")
            if assertion.memory_kind not in PROFILE_MEMORY_KINDS:
                raise RelationshipContractError("profile assertion memory kind is invalid")
        if not isinstance(resolutions, Sequence) or len(resolutions) + len(assertions) > 4:
            raise RelationshipContractError("profile resolution batch is invalid")
        targets = set()
        for resolution in resolutions:
            if not isinstance(resolution, ProfileTemporaryResolution) or resolution.witness != witness:
                raise RelationshipContractError("profile resolution provenance mismatch")
            if resolution.target.scope != self._scope or resolution.target.entry_id in targets:
                raise RelationshipContractError("profile resolution target is invalid")
            targets.add(resolution.target.entry_id)
        token = None
        if claim_token is not None:
            token = require_bounded_text(claim_token, "claim_token", maximum=128)

        changed = 0
        with self._lock, self._connection() as connection:
            connection.execute("BEGIN IMMEDIATE")
            try:
                if connection.execute(
                    "SELECT 1 FROM profile_processed_reviews WHERE source_hash=?",
                    (witness.source_hash,),
                ).fetchone():
                    connection.execute(
                        "DELETE FROM profile_review_outbox WHERE source_hash=?",
                        (witness.source_hash,),
                    )
                    connection.execute("COMMIT")
                    return 0
                if not bool(self._preferences_row(connection)["auto_memory_enabled"]):
                    # The setting may have changed while the model was reviewing.
                    # Recheck under the same write transaction that would commit
                    # assertions so an in-flight review can never cross the off
                    # boundary.
                    connection.execute(
                        "DELETE FROM profile_review_outbox WHERE source_hash=?",
                        (witness.source_hash,),
                    )
                    connection.execute("COMMIT")
                    return 0
                outbox = connection.execute(
                    "SELECT claim_token FROM profile_review_outbox WHERE source_hash=?",
                    (witness.source_hash,),
                ).fetchone()
                if outbox is None or (token is not None and outbox["claim_token"] != token):
                    raise RelationshipContractError("profile review claim is absent or stale")
                if has_temporary_resolution_language(witness.user_text) and self._has_earlier_pending_review(
                    connection, witness.source_hash, witness.observed_at_utc,
                ):
                    raise RelationshipContractError("temporary resolution is waiting for an earlier review")
                for resolution in resolutions:
                    changed += int(self._apply_temporary_resolution(connection, resolution, witness))
                for assertion in assertions:
                    changed += int(self._apply_assertion(connection, assertion))
                if changed:
                    connection.execute(
                        "UPDATE profile_meta SET catalog_revision=catalog_revision+1 WHERE singleton=1"
                    )
                connection.execute(
                    """
                    INSERT INTO profile_processed_reviews(
                        source_hash, owner_id, character_key, canon_fingerprint,
                        session_id, turn_id, user_message_id, assistant_message_id, processed_at_utc
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        witness.source_hash, *self._scope_values(), witness.session_id,
                        witness.turn_id, witness.user_message_id, witness.assistant_message_id,
                        canonical_utc(utc_now()),
                    ),
                )
                connection.execute(
                    "DELETE FROM profile_review_outbox WHERE source_hash=?",
                    (witness.source_hash,),
                )
                connection.execute("COMMIT")
            except Exception:
                connection.execute("ROLLBACK")
                raise
        return changed

    @staticmethod
    def _not_expired(entry: ProfileMemoryEntry, now: datetime) -> bool:
        return entry.expires_at_utc is None or parse_utc(entry.expires_at_utc, "expires_at_utc") > now

    def core_entries(self, *, now: datetime | None = None, limit: int = 8) -> tuple[ProfileMemoryEntry, ...]:
        reference = (now or utc_now()).astimezone(timezone.utc)
        if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= 16:
            raise RelationshipContractError("core memory limit is invalid")
        with self._connection() as connection:
            rows = connection.execute(
                """
                SELECT * FROM profile_entries
                WHERE owner_id=? AND character_key=? AND canon_fingerprint=?
                  AND state='active' AND category='relationship_core'
                ORDER BY pinned DESC, updated_at_utc DESC, entry_id LIMIT ?
                """,
                (*self._scope_values(), limit * 2),
            ).fetchall()
        entries = [self._row_to_entry(row) for row in rows]
        return tuple(entry for entry in entries if self._not_expired(entry, reference))[:limit]

    def recall_entries(self, query: str, *, now: datetime | None = None, limit: int = 8) -> tuple[ProfileMemoryEntry, ...]:
        query_text = require_bounded_text(query, "query", maximum=4096)
        if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= 16:
            raise RelationshipContractError("profile recall limit is invalid")
        reference = (now or utc_now()).astimezone(timezone.utc)
        with self._connection() as connection:
            rows = connection.execute(
                """
                SELECT * FROM profile_entries
                WHERE owner_id=? AND character_key=? AND canon_fingerprint=?
                  AND state='active' AND category IN ('long_term','temporary') AND pinned=0
                ORDER BY updated_at_utc DESC, entry_id
                """,
                self._scope_values(),
            )
            query_units = _search_units(query_text)
            hints = _kind_hints(query_text)
            # Score the complete active profile library while keeping only the
            # requested best rows in memory.  An old but relevant fact cannot
            # disappear merely because 256 newer unrelated rows exist.
            scored: list[tuple[int, int, str, ProfileMemoryEntry]] = []
            for order, row in enumerate(rows):
                entry = self._row_to_entry(row)
                if not self._not_expired(entry, reference):
                    continue
                entry_units = _search_units(
                    entry.content + " " + entry.canonical_key.replace(".", " ")
                )
                overlap = len(query_units & entry_units)
                kind_bonus = 4 if entry.memory_kind in hints else 0
                temporary_bonus = 1 if entry.category == "temporary" else 0
                score = overlap * 8 + kind_bonus + temporary_bonus
                if score <= 0:
                    continue
                item = (score, -order, entry.entry_id, entry)
                if len(scored) < limit:
                    heapq.heappush(scored, item)
                elif item[:3] > scored[0][:3]:
                    heapq.heapreplace(scored, item)
        scored.sort(reverse=True)
        return tuple(item[3] for item in scored)

    def _non_recallable_signatures(
        self,
        *,
        now: datetime | None = None,
    ) -> tuple[tuple[frozenset[str], str], ...]:
        """Build value signatures that current profile recall must not revive."""

        reference = (now or utc_now()).astimezone(timezone.utc)
        signatures: set[tuple[frozenset[str], str]] = set()

        def add_signature(value: str, memory_kind: str) -> None:
            units = _search_units(value) - _PROFILE_GENERIC_SEARCH_UNITS
            if units:
                signatures.add((frozenset(units), memory_kind))

        with self._connection() as connection:
            rows = connection.execute(
                """
                SELECT entry_id, current_revision_id, state, category,
                       content_text, evidence_excerpt, canonical_key,
                       memory_kind, expires_at_utc
                FROM profile_entries
                WHERE owner_id=? AND character_key=? AND canon_fingerprint=?
                ORDER BY updated_at_utc DESC, entry_id
                """,
                self._scope_values(),
            ).fetchall()
            current_by_id = {str(row["entry_id"]): row for row in rows}
            for row in rows:
                expired = (
                    row["expires_at_utc"] is not None
                    and parse_utc(row["expires_at_utc"], "expires_at_utc") <= reference
                )
                if (
                    row["state"] != "active"
                    or row["category"] == "candidate"
                    or expired
                ):
                    add_signature(
                        " ".join(
                            value
                            for value in (
                                str(row["content_text"] or ""),
                                str(row["evidence_excerpt"] or ""),
                                str(row["canonical_key"] or "").replace(".", " "),
                            )
                            if value
                        ),
                        str(row["memory_kind"]),
                    )

            revisions = connection.execute(
                """
                SELECT entry_id, revision_id, content_after,
                       evidence_excerpt_after
                FROM profile_revisions
                WHERE owner_id=? AND character_key=? AND canon_fingerprint=?
                ORDER BY created_at_utc DESC, revision_id
                """,
                self._scope_values(),
            ).fetchall()
            for revision in revisions:
                current = current_by_id.get(str(revision["entry_id"]))
                if current is None or revision["revision_id"] == current["current_revision_id"]:
                    continue
                old_content = str(revision["content_after"] or "")
                if _normalize_content(old_content) == _normalize_content(
                    str(current["content_text"] or "")
                ):
                    continue
                add_signature(
                    " ".join(
                        value
                        for value in (
                            old_content,
                            str(revision["evidence_excerpt_after"] or ""),
                            str(current["canonical_key"] or "").replace(".", " "),
                        )
                        if value
                    ),
                    str(current["memory_kind"]),
                )
        return tuple(signatures)

    def has_forget_barrier(self, query: str) -> bool:
        """Return whether a non-recallable profile value is relevant to ``query``.

        Hiding a profile item is the recoverable player-owned "forget" action.
        Rejected, superseded, expired, and corrected-old values receive the
        same current-profile boundary. The original conversation remains an
        auditable transcript, but it must not silently become a second profile
        authority through transcript search. This method exposes only a
        boolean barrier: callers never receive the protected value itself.
        """

        query_text = require_bounded_text(query, "query", maximum=4096)
        query_units = _search_units(query_text)
        hints = _kind_hints(query_text)
        for signature, memory_kind in self._non_recallable_signatures():
            if query_units & signature:
                return True
            if memory_kind in hints:
                return True
        return False

    def contains_non_recallable_profile_value(self, text: str) -> bool:
        """Return whether serialized history exposes a protected profile value."""

        if not isinstance(text, str) or not text:
            return False
        haystack = _search_units(text)
        for signature, _memory_kind in self._non_recallable_signatures():
            overlap = haystack & signature
            if not overlap:
                continue
            if len(signature) == 1 or len(overlap) >= 2:
                return True
            token = next(iter(overlap))
            if any(_is_cjk(character) for character in token) or len(token) >= 6:
                return True
        return False

    def get_entry(self, entry_id: str) -> ProfileMemoryEntry | None:
        checked = self._require_entry_id(entry_id)
        with self._connection() as connection:
            row = connection.execute(
                "SELECT * FROM profile_entries WHERE entry_id=? AND owner_id=? AND character_key=? AND canon_fingerprint=?",
                (checked, *self._scope_values()),
            ).fetchone()
        return self._row_to_entry(row) if row is not None else None

    def list_entries(
        self,
        *,
        include_candidates: bool = False,
        include_inactive: bool = False,
        limit: int = 200,
    ) -> tuple[ProfileMemoryEntry, ...]:
        if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= _MAX_CATALOG_LIMIT:
            raise RelationshipContractError("profile list limit is invalid")
        clauses = []
        if not include_inactive:
            clauses.append("state='active'")
        if not include_candidates:
            clauses.append("category!='candidate'")
        suffix = " AND " + " AND ".join(clauses) if clauses else ""
        with self._connection() as connection:
            rows = connection.execute(
                "SELECT * FROM profile_entries WHERE owner_id=? AND character_key=? AND canon_fingerprint=?"
                + suffix + " ORDER BY updated_at_utc DESC, entry_id LIMIT ?",
                (*self._scope_values(), limit),
            ).fetchall()
        return tuple(self._row_to_entry(row) for row in rows)

    def evidence_count(self) -> int:
        with self._connection() as connection:
            return int(connection.execute(
                "SELECT COUNT(*) FROM profile_evidence WHERE owner_id=? AND character_key=? AND canon_fingerprint=?",
                self._scope_values(),
            ).fetchone()[0])

    def revision_count(self) -> int:
        with self._connection() as connection:
            return int(connection.execute(
                "SELECT COUNT(*) FROM profile_revisions WHERE owner_id=? AND character_key=? AND canon_fingerprint=?",
                self._scope_values(),
            ).fetchone()[0])

    @staticmethod
    def _require_exact(values: Any, names: set[str], action: str) -> dict[str, Any]:
        if not isinstance(values, dict) or set(values) != names:
            raise _management_error("memory.invalid_request", "The memory request shape is invalid.")
        return values

    @staticmethod
    def _require_nonnegative_int(value: Any) -> int:
        if isinstance(value, bool) or not isinstance(value, int) or value < 0:
            raise _management_error("memory.invalid_request", "The memory revision is invalid.")
        return value

    @staticmethod
    def _require_positive_int(value: Any) -> int:
        if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
            raise _management_error("memory.invalid_request", "The memory entry version is invalid.")
        return value

    @staticmethod
    def _require_bool(value: Any) -> bool:
        if not isinstance(value, bool):
            raise _management_error("memory.invalid_request", "The memory flag is invalid.")
        return value

    @staticmethod
    def _require_operation_id(value: Any) -> str:
        if (
            not isinstance(value, str) or not value or len(value) > 128
            or not value[0].isascii() or not value[0].isalnum()
            or any(not character.isascii() or (not character.isalnum() and character not in "._:-") for character in value[1:])
        ):
            raise _management_error("memory.invalid_request", "The memory operation id is invalid.")
        return value

    @staticmethod
    def _require_entry_id(value: Any) -> str:
        if (
            not isinstance(value, str) or not value.strip() or value != value.strip()
            or len(value) > 256 or any(ord(character) < 32 for character in value)
        ):
            raise _management_error("memory.invalid_request", "The memory entry id is invalid.")
        return value

    @staticmethod
    def _request_hash(action: str, params: dict[str, Any]) -> str:
        return sha256_hex("lilith-profile-operation-v1\0" + canonical_json({"action": action, "params": params}))

    def _operation_replay(
        self, connection: sqlite3.Connection, *, operation_id: str, action: str, request_hash: str
    ) -> dict[str, Any] | None:
        row = connection.execute(
            "SELECT action, request_hash, response_json FROM profile_operations WHERE operation_id=?",
            (operation_id,),
        ).fetchone()
        if row is None:
            return None
        if row["action"] != action or row["request_hash"] != request_hash:
            raise _management_error("memory.invalid_request", "The memory operation id was already used.")
        try:
            response = json.loads(row["response_json"])
        except Exception as exc:
            raise _management_error("memory.operation_failed", "The memory operation could not be completed.") from exc
        if not isinstance(response, dict):
            raise _management_error("memory.operation_failed", "The memory operation could not be completed.")
        return response

    @staticmethod
    def _record_operation(
        connection: sqlite3.Connection, *, operation_id: str, action: str,
        request_hash: str, response: dict[str, Any], created_at_utc: str,
    ) -> None:
        entry_id = ProfileMemoryStore._entry_id_from_operation_response(
            canonical_json(response)
        )
        connection.execute(
            "INSERT INTO profile_operations(operation_id, action, request_hash, response_json, created_at_utc, entry_id) VALUES (?, ?, ?, ?, ?, ?)",
            (
                operation_id, action, request_hash, canonical_json(response),
                created_at_utc, entry_id,
            ),
        )

    @staticmethod
    def _catalog_revision(connection: sqlite3.Connection) -> int:
        return int(connection.execute(
            "SELECT catalog_revision FROM profile_meta WHERE singleton=1"
        ).fetchone()[0])

    @staticmethod
    def _preferences_row(connection: sqlite3.Connection) -> sqlite3.Row:
        return connection.execute(
            "SELECT preferences_revision, auto_memory_enabled FROM profile_preferences WHERE singleton=1"
        ).fetchone()

    @staticmethod
    def _check_catalog_revision(connection: sqlite3.Connection, expected: int) -> int:
        actual = ProfileMemoryStore._catalog_revision(connection)
        if actual != expected:
            raise _management_error("memory.revision_conflict", "The memory catalog changed; refresh and try again.")
        return actual

    def _require_entry_row(self, connection: sqlite3.Connection, entry_id: str) -> sqlite3.Row:
        row = connection.execute(
            "SELECT * FROM profile_entries WHERE entry_id=? AND owner_id=? AND character_key=? AND canon_fingerprint=?",
            (entry_id, *self._scope_values()),
        ).fetchone()
        if row is None:
            raise _management_error("memory.entry_not_found", "The memory entry no longer exists.")
        return row

    @staticmethod
    def _check_entry_version(row: sqlite3.Row, expected: int) -> None:
        if int(row["entry_version"]) != expected:
            raise _management_error("memory.revision_conflict", "The memory entry changed; refresh and try again.")

    def catalog(
        self,
        *,
        include_hidden: bool,
        limit: int,
        offset: int = 0,
        now: datetime | None = None,
    ) -> dict[str, Any]:
        reference = (now or utc_now()).astimezone(timezone.utc)
        hidden = self._require_bool(include_hidden)
        if isinstance(limit, bool) or not isinstance(limit, int) or not 1 <= limit <= _MAX_CATALOG_LIMIT:
            raise _management_error("memory.invalid_request", "The memory catalog limit is invalid.")
        checked_offset = self._require_nonnegative_int(offset)
        if checked_offset > 1_000_000:
            raise _management_error("memory.invalid_request", "The memory catalog offset is invalid.")
        hidden_clause = "" if hidden else " AND state!='hidden'"
        with self._connection() as connection:
            connection.execute("BEGIN")
            try:
                revision = self._catalog_revision(connection)
                rows = connection.execute(
                    "SELECT * FROM profile_entries WHERE owner_id=? AND character_key=? AND canon_fingerprint=?"
                    + hidden_clause + " ORDER BY updated_at_utc DESC, entry_id LIMIT ? OFFSET ?",
                    (*self._scope_values(), limit + 1, checked_offset),
                ).fetchall()
                connection.execute("COMMIT")
            except Exception:
                connection.execute("ROLLBACK")
                raise
        has_more = len(rows) > limit
        page = rows[:limit]
        entries = [self._row_to_entry(row).public_record(now=reference) for row in page]
        return {
            "schema_version": 1,
            "catalog_revision": revision,
            "export_supported": True,
            "next_offset": checked_offset + len(page) if has_more else None,
            "entries": entries,
        }

    def preferences(self) -> dict[str, Any]:
        with self._connection() as connection:
            row = self._preferences_row(connection)
        return {
            "schema_version": 1,
            "preferences_revision": int(row["preferences_revision"]),
            "auto_memory_enabled": bool(row["auto_memory_enabled"]),
        }

    def _create_manual_entry(
        self, connection: sqlite3.Connection, *, operation_id: str, category: str,
        content: str, pinned: bool, now_utc: str,
    ) -> ProfileMemoryEntry:
        entry_id = self._new_entry_id("operator\0" + operation_id)
        source_hash = sha256_hex("lilith-profile-operator-source-v1\0" + operation_id)
        expires = None
        if category == "temporary":
            expires = canonical_utc(parse_utc(now_utc, "now_utc") + timedelta(days=_MANUAL_TEMPORARY_DAYS))
        memory_kind = "relationship_core" if category == "relationship_core" else "general"
        canonical_key = "operator." + sha256_hex(operation_id)[:24]
        row = {
            "entry_id": entry_id, "entry_version": 1, "category": category,
            "state": "active", "content_text": content, "source": "operator",
            "expires_at_utc": expires, "pinned": int(pinned),
            "evidence_excerpt": None, "source_hash": source_hash,
        }
        revision_id = self._insert_revision(
            connection, row=row, previous_revision_id=None, mutation_kind="operator_add",
            created_at_utc=now_utc, operation_id=operation_id,
        )
        connection.execute(
            """
            INSERT INTO profile_entries(
                entry_id, entry_version, owner_id, character_key, canon_fingerprint,
                category, state, content_text, content_normalized, source,
                observed_at_utc, updated_at_utc, expires_at_utc, pinned,
                evidence_excerpt, memory_kind, canonical_key, source_hash, current_revision_id
            ) VALUES (?, 1, ?, ?, ?, ?, 'active', ?, ?, 'operator', ?, ?, ?, ?, NULL, ?, ?, ?, ?)
            """,
            (
                entry_id, *self._scope_values(), category, content, _normalize_content(content),
                now_utc, now_utc, expires, int(pinned), memory_kind, canonical_key,
                source_hash, revision_id,
            ),
        )
        return self._row_to_entry(self._require_entry_row(connection, entry_id))

    def _mutate_entry(
        self,
        connection: sqlite3.Connection,
        *,
        row: sqlite3.Row,
        changes: dict[str, Any],
        mutation_kind: str,
        operation_id: str,
        now_utc: str,
    ) -> ProfileMemoryEntry:
        snapshot = dict(row)
        snapshot.update(changes)
        snapshot["entry_version"] = int(row["entry_version"]) + 1
        revision_id = self._insert_revision(
            connection, row=snapshot, previous_revision_id=row["current_revision_id"],
            mutation_kind=mutation_kind, created_at_utc=now_utc, operation_id=operation_id,
        )
        assignments = ["entry_version=?", "updated_at_utc=?", "current_revision_id=?"]
        parameters: list[Any] = [snapshot["entry_version"], now_utc, revision_id]
        mapping = {
            "category": "category", "state": "state", "content_text": "content_text",
            "content_normalized": "content_normalized", "expires_at_utc": "expires_at_utc",
            "pinned": "pinned", "evidence_excerpt": "evidence_excerpt",
            "source": "source", "source_hash": "source_hash",
            "observed_at_utc": "observed_at_utc",
        }
        for key, column in mapping.items():
            if key in changes:
                assignments.append(column + "=?")
                parameters.append(changes[key])
        parameters.extend([row["entry_id"], row["entry_version"]])
        changed = connection.execute(
            "UPDATE profile_entries SET " + ", ".join(assignments)
            + " WHERE entry_id=? AND entry_version=?",
            tuple(parameters),
        ).rowcount
        if changed != 1:
            raise _management_error("memory.revision_conflict", "The memory entry changed; refresh and try again.")
        return self._row_to_entry(self._require_entry_row(connection, row["entry_id"]))

    def _entry_mutation_request(self, values: dict[str, Any], extra: set[str]) -> tuple[str, int, str, int]:
        expected = {"operation_id", "expected_catalog_revision", "entry_id", "expected_entry_version", *extra}
        self._require_exact(values, expected, "entry mutation")
        return (
            self._require_operation_id(values["operation_id"]),
            self._require_nonnegative_int(values["expected_catalog_revision"]),
            self._require_entry_id(values["entry_id"]),
            self._require_positive_int(values["expected_entry_version"]),
        )

    def manage(self, action: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
        if not isinstance(action, str) or not isinstance(params, dict):
            raise _management_error("memory.invalid_request", "The memory management request is invalid.")
        if action == "catalog":
            if set(params) not in (
                {"include_hidden", "limit"},
                {"include_hidden", "limit", "offset"},
            ):
                raise _management_error("memory.invalid_request", "The memory request shape is invalid.")
            return self.catalog(
                include_hidden=params["include_hidden"],
                limit=params["limit"],
                offset=params.get("offset", 0),
            )
        if action == "preferences.get":
            self._require_exact(params, set(), action)
            return self.preferences()
        if action == "entry.add":
            return self._manage_add(params)
        if action == "entry.pin.set":
            return self._manage_pin(params)
        if action == "entry.promote":
            return self._manage_promote(params)
        if action == "entry.correct":
            return self._manage_correct(params)
        if action == "entry.visibility.set":
            return self._manage_visibility(params)
        if action == "entry.reject":
            return self._manage_reject(params)
        if action == "entry.delete":
            return self._manage_delete(params)
        if action == "preferences.set":
            return self._manage_preferences(params)
        if action == "export":
            return self._manage_export(params)
        raise _management_error("memory.invalid_request", "The memory management action is unsupported.")

    def _transactional_operation(self, action: str, params: dict[str, Any], callback: Any) -> dict[str, Any]:
        operation_id = self._require_operation_id(params.get("operation_id"))
        request_hash = self._request_hash(action, params)
        with self._lock, self._connection() as connection:
            connection.execute("BEGIN IMMEDIATE")
            try:
                replay = self._operation_replay(
                    connection, operation_id=operation_id, action=action, request_hash=request_hash
                )
                if replay is not None:
                    connection.execute("COMMIT")
                    return replay
                now_utc = canonical_utc(utc_now())
                response = callback(connection, operation_id, now_utc)
                self._record_operation(
                    connection, operation_id=operation_id, action=action,
                    request_hash=request_hash, response=response, created_at_utc=now_utc,
                )
                connection.execute("COMMIT")
                return response
            except Exception:
                connection.execute("ROLLBACK")
                raise

    def _manage_add(self, values: dict[str, Any]) -> dict[str, Any]:
        self._require_exact(
            values, {"operation_id", "expected_catalog_revision", "category", "content", "pinned"},
            "entry.add",
        )
        expected = self._require_nonnegative_int(values["expected_catalog_revision"])
        category = values["category"]
        if category not in MANUAL_ADD_CATEGORIES:
            raise _management_error("memory.invalid_request", "The memory category cannot be added manually.")
        try:
            content = require_memory_content(values["content"])
        except RelationshipContractError as exc:
            raise _management_error("memory.invalid_request", "The memory content is invalid.") from exc
        if contains_secret(content):
            raise _management_error("memory.secret_rejected", "Credential-like content cannot be stored in memory.")
        pinned = self._require_bool(values["pinned"])
        if pinned and category != "relationship_core":
            raise _management_error("memory.invalid_request", "Only relationship-core memory can be pinned.")

        def apply(connection: sqlite3.Connection, operation_id: str, now_utc: str) -> dict[str, Any]:
            self._check_catalog_revision(connection, expected)
            entry = self._create_manual_entry(
                connection, operation_id=operation_id, category=category,
                content=content, pinned=pinned, now_utc=now_utc,
            )
            connection.execute("UPDATE profile_meta SET catalog_revision=catalog_revision+1 WHERE singleton=1")
            revision = self._catalog_revision(connection)
            return {"schema_version": 1, "operation_id": operation_id, "catalog_revision": revision, "entry": entry.public_record()}

        return self._transactional_operation("entry.add", values, apply)

    def _manage_pin(self, values: dict[str, Any]) -> dict[str, Any]:
        operation_id, expected_catalog, entry_id, expected_entry = self._entry_mutation_request(values, {"pinned"})
        pinned = self._require_bool(values["pinned"])

        def apply(connection: sqlite3.Connection, _: str, now_utc: str) -> dict[str, Any]:
            self._check_catalog_revision(connection, expected_catalog)
            row = self._require_entry_row(connection, entry_id)
            self._check_entry_version(row, expected_entry)
            if pinned and (row["category"] != "relationship_core" or row["state"] not in {"active", "hidden"}):
                raise _management_error("memory.invalid_request", "Only active relationship-core memory can be pinned.")
            entry = self._mutate_entry(
                connection, row=row, changes={"pinned": int(pinned)},
                mutation_kind="pin_set", operation_id=operation_id, now_utc=now_utc,
            )
            connection.execute("UPDATE profile_meta SET catalog_revision=catalog_revision+1 WHERE singleton=1")
            return {"schema_version": 1, "operation_id": operation_id, "catalog_revision": self._catalog_revision(connection), "entry": entry.public_record()}

        return self._transactional_operation("entry.pin.set", values, apply)

    def _manage_promote(self, values: dict[str, Any]) -> dict[str, Any]:
        operation_id, expected_catalog, entry_id, expected_entry = self._entry_mutation_request(values, {"target_category"})
        target = values["target_category"]
        if target not in {"relationship_core", "long_term"}:
            raise _management_error("memory.invalid_request", "The promotion target is invalid.")

        def apply(connection: sqlite3.Connection, _: str, now_utc: str) -> dict[str, Any]:
            self._check_catalog_revision(connection, expected_catalog)
            row = self._require_entry_row(connection, entry_id)
            self._check_entry_version(row, expected_entry)
            if row["state"] not in {"active", "hidden"}:
                raise _management_error("memory.invalid_request", "This memory cannot be promoted.")
            entry = self._mutate_entry(
                connection, row=row,
                changes={"category": target, "expires_at_utc": None, "pinned": 0},
                mutation_kind="promoted", operation_id=operation_id, now_utc=now_utc,
            )
            connection.execute("UPDATE profile_meta SET catalog_revision=catalog_revision+1 WHERE singleton=1")
            return {"schema_version": 1, "operation_id": operation_id, "catalog_revision": self._catalog_revision(connection), "entry": entry.public_record()}

        return self._transactional_operation("entry.promote", values, apply)

    def _manage_correct(self, values: dict[str, Any]) -> dict[str, Any]:
        operation_id, expected_catalog, entry_id, expected_entry = self._entry_mutation_request(values, {"replacement_content"})
        try:
            replacement = require_memory_content(values["replacement_content"], "replacement_content")
        except RelationshipContractError as exc:
            raise _management_error("memory.invalid_request", "The replacement memory content is invalid.") from exc
        if contains_secret(replacement):
            raise _management_error("memory.secret_rejected", "Credential-like content cannot be stored in memory.")

        def apply(connection: sqlite3.Connection, _: str, now_utc: str) -> dict[str, Any]:
            self._check_catalog_revision(connection, expected_catalog)
            row = self._require_entry_row(connection, entry_id)
            self._check_entry_version(row, expected_entry)
            correction_source_hash = sha256_hex(
                "lilith-profile-operator-correction-v1\0" + operation_id
            )
            entry = self._mutate_entry(
                connection, row=row,
                changes={
                    "content_text": replacement,
                    "content_normalized": _normalize_content(replacement),
                    "source": "operator",
                    "source_hash": correction_source_hash,
                    "observed_at_utc": now_utc,
                    "evidence_excerpt": None,
                },
                mutation_kind="corrected", operation_id=operation_id, now_utc=now_utc,
            )
            connection.execute("UPDATE profile_meta SET catalog_revision=catalog_revision+1 WHERE singleton=1")
            return {"schema_version": 1, "operation_id": operation_id, "catalog_revision": self._catalog_revision(connection), "entry": entry.public_record()}

        return self._transactional_operation("entry.correct", values, apply)

    def _manage_visibility(self, values: dict[str, Any]) -> dict[str, Any]:
        operation_id, expected_catalog, entry_id, expected_entry = self._entry_mutation_request(values, {"hidden"})
        hidden = self._require_bool(values["hidden"])

        def apply(connection: sqlite3.Connection, _: str, now_utc: str) -> dict[str, Any]:
            self._check_catalog_revision(connection, expected_catalog)
            row = self._require_entry_row(connection, entry_id)
            self._check_entry_version(row, expected_entry)
            if row["state"] not in {"active", "hidden"}:
                raise _management_error("memory.invalid_request", "This memory visibility cannot be changed.")
            state = "hidden" if hidden else "active"
            entry = self._mutate_entry(
                connection, row=row, changes={"state": state}, mutation_kind="visibility_set",
                operation_id=operation_id, now_utc=now_utc,
            )
            connection.execute("UPDATE profile_meta SET catalog_revision=catalog_revision+1 WHERE singleton=1")
            return {"schema_version": 1, "operation_id": operation_id, "catalog_revision": self._catalog_revision(connection), "entry": entry.public_record()}

        return self._transactional_operation("entry.visibility.set", values, apply)

    def _manage_reject(self, values: dict[str, Any]) -> dict[str, Any]:
        operation_id, expected_catalog, entry_id, expected_entry = self._entry_mutation_request(values, set())

        def apply(connection: sqlite3.Connection, _: str, now_utc: str) -> dict[str, Any]:
            self._check_catalog_revision(connection, expected_catalog)
            row = self._require_entry_row(connection, entry_id)
            self._check_entry_version(row, expected_entry)
            if row["category"] != "candidate" or row["state"] not in {"active", "hidden"}:
                raise _management_error("memory.invalid_request", "Only a candidate memory can be rejected.")
            entry = self._mutate_entry(
                connection, row=row, changes={"state": "rejected", "pinned": 0},
                mutation_kind="rejected", operation_id=operation_id, now_utc=now_utc,
            )
            connection.execute("UPDATE profile_meta SET catalog_revision=catalog_revision+1 WHERE singleton=1")
            return {"schema_version": 1, "operation_id": operation_id, "catalog_revision": self._catalog_revision(connection), "entry": entry.public_record()}

        return self._transactional_operation("entry.reject", values, apply)

    def _manage_delete(self, values: dict[str, Any]) -> dict[str, Any]:
        operation_id, expected_catalog, entry_id, expected_entry = self._entry_mutation_request(values, {"permanent"})
        if self._require_bool(values["permanent"]) is not True:
            raise _management_error("memory.invalid_request", "Permanent deletion must be explicitly requested.")

        def apply(connection: sqlite3.Connection, _: str, now_utc: str) -> dict[str, Any]:
            self._check_catalog_revision(connection, expected_catalog)
            row = self._require_entry_row(connection, entry_id)
            self._check_entry_version(row, expected_entry)
            # Remove every earlier idempotency receipt that serialized this
            # entry.  The enclosing transaction records a content-free delete
            # tombstone after this callback returns, so retries remain safe.
            connection.execute(
                "DELETE FROM profile_operations WHERE entry_id=?",
                (entry_id,),
            )
            connection.execute("DELETE FROM profile_entries WHERE entry_id=?", (entry_id,))
            connection.execute("UPDATE profile_meta SET catalog_revision=catalog_revision+1 WHERE singleton=1")
            return {
                "schema_version": 1, "operation_id": operation_id,
                "catalog_revision": self._catalog_revision(connection),
                "entry_id": entry_id, "deleted": True,
            }

        return self._transactional_operation("entry.delete", values, apply)

    def _manage_preferences(self, values: dict[str, Any]) -> dict[str, Any]:
        self._require_exact(
            values, {"operation_id", "expected_preferences_revision", "auto_memory_enabled"},
            "preferences.set",
        )
        expected = self._require_nonnegative_int(values["expected_preferences_revision"])
        enabled = self._require_bool(values["auto_memory_enabled"])

        def apply(connection: sqlite3.Connection, _: str, now_utc: str) -> dict[str, Any]:
            row = self._preferences_row(connection)
            if int(row["preferences_revision"]) != expected:
                raise _management_error("memory.revision_conflict", "The memory preferences changed; refresh and try again.")
            next_revision = expected + 1
            connection.execute(
                "UPDATE profile_preferences SET preferences_revision=?, auto_memory_enabled=?, updated_at_utc=? WHERE singleton=1",
                (next_revision, int(enabled), now_utc),
            )
            if not enabled:
                # Turning automatic memory off is also a privacy boundary:
                # discard queued raw turns so they cannot be reviewed later if
                # the option is re-enabled. An already-running review is still
                # stopped by commit_review's transaction-time recheck.
                connection.execute(
                    """
                    DELETE FROM profile_review_outbox
                    WHERE owner_id=? AND character_key=? AND canon_fingerprint=?
                    """,
                    self._scope_values(),
                )
            return {"schema_version": 1, "preferences_revision": next_revision, "auto_memory_enabled": enabled}

        return self._transactional_operation("preferences.set", values, apply)

    def _manage_export(self, values: dict[str, Any]) -> dict[str, Any]:
        self._require_exact(values, {"operation_id", "expected_catalog_revision", "format"}, "export")
        operation_id = self._require_operation_id(values["operation_id"])
        expected = self._require_nonnegative_int(values["expected_catalog_revision"])
        if values["format"] != "json":
            raise _management_error("memory.invalid_request", "The memory export format is unsupported.")
        request_hash = self._request_hash("export", values)

        # Capture one coherent read snapshot without reserving SQLite's write
        # lock while the JSON file is serialized and flushed to disk.
        with self._lock, self._connection() as connection:
            connection.execute("BEGIN")
            try:
                replay = self._operation_replay(
                    connection,
                    operation_id=operation_id,
                    action="export",
                    request_hash=request_hash,
                )
                if replay is not None:
                    connection.execute("COMMIT")
                    return replay
                self._check_catalog_revision(connection, expected)
                now_utc = canonical_utc(utc_now())
                rows = connection.execute(
                    "SELECT * FROM profile_entries WHERE owner_id=? AND character_key=? AND canon_fingerprint=? ORDER BY updated_at_utc DESC, entry_id",
                    self._scope_values(),
                ).fetchall()
                entries = [
                    self._row_to_entry(row).public_record(
                        now=parse_utc(now_utc, "now_utc")
                    )
                    for row in rows
                ]
                connection.execute("COMMIT")
            except Exception:
                connection.execute("ROLLBACK")
                raise

        document = canonical_json(
            {
                "schema_version": 1,
                "catalog_revision": expected,
                "created_at_utc": now_utc,
                "entries": entries,
            }
        ).encode("utf-8")
        digest = hashlib.sha256(document).hexdigest().upper()
        exports = (self._data_dir / "exports").resolve()
        try:
            exports.mkdir(parents=True, exist_ok=True)
            stem = sha256_hex("lilith-profile-export-v1\0" + operation_id)[:24]
            destination = (exports / ("profile-memory-" + stem + ".json")).resolve()
            if exports not in destination.parents:
                raise OSError("unsafe export destination")
            temporary = destination.with_name(
                destination.name
                + "."
                + str(os.getpid())
                + "."
                + str(threading.get_ident())
                + ".tmp"
            )
            with temporary.open("wb") as stream:
                stream.write(document)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, destination)
        except Exception as exc:
            try:
                temporary.unlink(missing_ok=True)
            except Exception:
                pass
            raise _management_error(
                "memory.export_failed",
                "The memory export could not be created.",
            ) from exc

        response = {
            "schema_version": 1,
            "operation_id": operation_id,
            "path": str(destination),
            "sha256": digest,
            "entry_count": len(entries),
            "created_at_utc": now_utc,
        }
        with self._lock, self._connection() as connection:
            connection.execute("BEGIN IMMEDIATE")
            try:
                replay = self._operation_replay(
                    connection,
                    operation_id=operation_id,
                    action="export",
                    request_hash=request_hash,
                )
                if replay is not None:
                    connection.execute("COMMIT")
                    return replay
                self._record_operation(
                    connection,
                    operation_id=operation_id,
                    action="export",
                    request_hash=request_hash,
                    response=response,
                    created_at_utc=now_utc,
                )
                connection.execute("COMMIT")
                return response
            except Exception:
                connection.execute("ROLLBACK")
                raise

__all__ = [
    "PROFILE_SCHEMA_V1_STATEMENTS",
    "ProfileMemoryManagementError",
    "ProfileMemoryStore",
]
