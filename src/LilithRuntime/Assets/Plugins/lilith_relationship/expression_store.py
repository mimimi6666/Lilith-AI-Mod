"""Manual D048 expression versions, independent of every memory database.

Only the authenticated management adapter calls ``manage``. The turn hook
reads a fresh immutable value and never creates a missing sidecar. Revision
zero is the virtual empty supplement; all actual saves are retained forever
within the fixed capacity. The frozen identity package remains authoritative.
"""

from __future__ import annotations

from contextlib import contextmanager
import os
from pathlib import Path
import sqlite3
import stat
import tempfile
import threading
from typing import Any
import unicodedata

from .domain import (
    RelationshipContractError, RelationshipScope, canonical_json, canonical_utc,
    parse_utc, require_event_identity, require_turn_identity, sha256_hex,
    utc_now, validate_character_key,
)
from .profile_domain import contains_secret
from .profile_store import (
    _SQLITE_BUSY_TIMEOUT_MILLISECONDS, _SQLITE_BUSY_TIMEOUT_SECONDS,
)


# Each admitted core owns an independent expression lineage. Never open or
# migrate any predecessor's sidecar; its provider can read it on rollback.
EXPRESSION_DATABASE_FILE = "expression.identity-v9.sqlite3"
BASE_IDENTITY_FINGERPRINT = "CDC1DD4F53596F2E0F214C1C9CE4474BEC97634CF3CBD8A10A026FB62318DBCF"
MAX_CONTENT_BYTES = 32768
MAX_REASON_BYTES = 1024
MAX_REVISIONS = 256
_MAX_DATABASE_BYTES = 32 * 1024 * 1024
_MESSAGES = {
    "expression.invalid_request": "The expression request is invalid.",
    "expression.revision_conflict": "The expression changed; refresh before saving.",
    "expression.version_not_found": "The requested expression version is unavailable.",
    "expression.secret_rejected": "Credential-like content cannot be saved as expression.",
    "expression.capacity_reached": "The retained expression version limit has been reached.",
    "expression.operation_failed": "The expression operation could not be completed.",
}
_META_COLUMNS = (
    "singleton", "owner_id", "character_key", "canon_fingerprint",
    "profile_binding", "base_identity_fingerprint", "current_revision",
)
_REVISION_COLUMNS = (
    "revision", "content", "content_sha256", "base_identity_fingerprint",
    "created_at_utc", "reason", "reverted_from", "operation_id", "request_sha256",
)
_SCHEMA = (
    """CREATE TABLE expression_meta (
        singleton INTEGER PRIMARY KEY CHECK(singleton = 1),
        owner_id TEXT NOT NULL, character_key TEXT NOT NULL,
        canon_fingerprint TEXT NOT NULL, profile_binding TEXT NOT NULL,
        base_identity_fingerprint TEXT NOT NULL,
        current_revision INTEGER NOT NULL CHECK(current_revision BETWEEN 0 AND 256)
    )""",
    """CREATE TABLE expression_revisions (
        revision INTEGER PRIMARY KEY CHECK(revision BETWEEN 1 AND 256),
        content TEXT NOT NULL, content_sha256 TEXT NOT NULL,
        base_identity_fingerprint TEXT NOT NULL, created_at_utc TEXT NOT NULL,
        reason TEXT NOT NULL, reverted_from INTEGER,
        operation_id TEXT NOT NULL UNIQUE, request_sha256 TEXT NOT NULL,
        CHECK(reverted_from IS NULL OR (reverted_from >= 0 AND reverted_from < revision))
    )""",
)


class ExpressionManagementError(RelationshipContractError):
    """Fixed safe messages; neither content nor storage details can escape."""

    def __init__(self, code: str = "expression.operation_failed") -> None:
        self.code = code if code in _MESSAGES else "expression.operation_failed"
        self.safe_message = _MESSAGES[self.code]
        super().__init__(self.safe_message)


def _text(value: Any, maximum: int) -> str:
    if not isinstance(value, str):
        raise ExpressionManagementError("expression.invalid_request")
    try:
        encoded = value.encode("utf-8", errors="strict")
    except UnicodeError:
        raise ExpressionManagementError("expression.invalid_request") from None
    if len(encoded) > maximum or any(
        unicodedata.category(character) in {"Cc", "Cf", "Cs"}
        and character not in "\t\r\n" for character in value
    ):
        raise ExpressionManagementError("expression.invalid_request")
    if contains_secret(value):
        raise ExpressionManagementError("expression.secret_rejected")
    return value


def _revision(value: Any) -> int:
    if type(value) is not int or not 0 <= value <= MAX_REVISIONS:
        raise ExpressionManagementError("expression.invalid_request")
    return value


def _operation_id(value: Any) -> str:
    if (
        not isinstance(value, str) or not 1 <= len(value) <= 128
        or not value[0].isascii() or not value[0].isalnum()
        or any(not character.isascii() or (not character.isalnum() and character not in "._:-") for character in value[1:])
    ):
        raise ExpressionManagementError("expression.invalid_request")
    if contains_secret(value):
        raise ExpressionManagementError("expression.secret_rejected")
    return value


class ExpressionStore:
    """Bounded, scope-bound append-only host expression authority."""

    def __init__(self, data_dir: Path, *, scope: RelationshipScope) -> None:
        try:
            if not isinstance(scope, RelationshipScope):
                raise ValueError("scope")
            require_turn_identity(scope.owner_id, "owner_id")
            require_event_identity(scope.canon_fingerprint, "canon_fingerprint")
            if validate_character_key(scope.character_key) != scope.character_key:
                raise ValueError("scope")
            # Preserve the lexical path until its ancestors have been checked.
            self._data_dir = Path(os.path.abspath(data_dir))
            self._path = self._data_dir / EXPRESSION_DATABASE_FILE
            self._scope = scope
            self._lock = threading.RLock()
            self._check_paths()
            self._profile_binding = sha256_hex(os.path.normcase(str(self._data_dir.resolve())))
        except Exception:
            raise ExpressionManagementError() from None

    @property
    def path(self) -> Path:
        return self._path

    @property
    def scope(self) -> RelationshipScope:
        return self._scope

    def _check_paths(self, path: Path | None = None) -> bool:
        storage_path = self._path if path is None else path
        for ancestor in (*reversed(self._data_dir.parents), self._data_dir):
            info = ancestor.lstat()
            if (
                not stat.S_ISDIR(info.st_mode)
                or getattr(info, "st_file_attributes", 0) & 0x400
            ):
                raise ExpressionManagementError()
        exists = False
        for suffix in ("", "-journal", "-wal", "-shm"):
            candidate = Path(str(storage_path) + suffix)
            try:
                info = candidate.lstat()
            except FileNotFoundError:
                continue
            if (
                not stat.S_ISREG(info.st_mode)
                or getattr(info, "st_file_attributes", 0) & 0x400
                or info.st_nlink != 1 or info.st_size > _MAX_DATABASE_BYTES
            ):
                raise ExpressionManagementError()
            if not suffix:
                exists = True
        if not exists and any(Path(str(storage_path) + suffix).exists() for suffix in ("-journal", "-wal", "-shm")):
            raise ExpressionManagementError()
        return exists

    @contextmanager
    def _connection(self, *, writable: bool = False, path: Path | None = None):
        storage_path = self._path if path is None else path
        self._check_paths(storage_path)
        before = storage_path.lstat()
        connection = sqlite3.connect(
            storage_path.as_uri() + ("?mode=rw" if writable else "?mode=ro"),
            uri=True, timeout=_SQLITE_BUSY_TIMEOUT_SECONDS, isolation_level=None,
        )
        try:
            connection.row_factory = sqlite3.Row
            connection.execute(f"PRAGMA busy_timeout = {_SQLITE_BUSY_TIMEOUT_MILLISECONDS}")
            connection.execute("PRAGMA trusted_schema = OFF")
            if not writable:
                connection.execute("PRAGMA query_only = ON")
            self._check_paths(storage_path)
            after = storage_path.lstat()
            if (before.st_dev, before.st_ino) != (after.st_dev, after.st_ino):
                raise ExpressionManagementError()
            yield connection
        finally:
            connection.close()

    def _scope_record(self) -> tuple[Any, ...]:
        return (
            1, self._scope.owner_id, self._scope.character_key,
            self._scope.canon_fingerprint, self._profile_binding, BASE_IDENTITY_FINGERPRINT,
        )

    def _request_hash(self, action: str, values: dict[str, Any]) -> str:
        return sha256_hex("lilith-expression-operation-v1\0" + canonical_json({
            "scope": self._scope_record(), "action": action, "params": values,
        }))

    def _verify(self, connection: sqlite3.Connection) -> list[dict[str, Any]]:
        """Validate the whole bounded lineage before projecting or mutating it."""
        try:
            if connection.execute("PRAGMA user_version").fetchone()[0] != 1:
                raise ValueError("schema")
            schema = [tuple(row) for row in connection.execute(
                "SELECT type, name, sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY name"
            )]
            expected_schema = [
                ("table", "expression_meta", _SCHEMA[0]),
                ("table", "expression_revisions", _SCHEMA[1]),
            ]
            if schema != expected_schema:
                raise ValueError("schema")
            if connection.execute("PRAGMA quick_check(1)").fetchone()[0] != "ok":
                raise ValueError("integrity")
            for name, columns in (("expression_meta", _META_COLUMNS), ("expression_revisions", _REVISION_COLUMNS)):
                if tuple(row[1] for row in connection.execute("PRAGMA table_info(" + name + ")")) != columns:
                    raise ValueError("schema")
            meta = connection.execute("SELECT * FROM expression_meta LIMIT 2").fetchall()
            if len(meta) != 1 or tuple(meta[0])[:6] != self._scope_record():
                raise ValueError("scope")
            rows = [dict(row) for row in connection.execute(
                "SELECT * FROM expression_revisions ORDER BY revision LIMIT ?", (MAX_REVISIONS + 1,)
            )]
            if not 1 <= len(rows) <= MAX_REVISIONS or meta[0]["current_revision"] != len(rows):
                raise ValueError("history")
            operation_ids = set()
            for number, row in enumerate(rows, 1):
                if type(row["revision"]) is not int or row["revision"] != number:
                    raise ValueError("revision")
                content = _text(row["content"], MAX_CONTENT_BYTES)
                reason = _text(row["reason"], MAX_REASON_BYTES)
                if row["content_sha256"] != sha256_hex(content) or row["base_identity_fingerprint"] != BASE_IDENTITY_FINGERPRINT:
                    raise ValueError("identity")
                if canonical_utc(parse_utc(row["created_at_utc"], "created_at_utc")) != row["created_at_utc"]:
                    raise ValueError("timestamp")
                operation_id = _operation_id(row["operation_id"])
                if operation_id in operation_ids:
                    raise ValueError("operation")
                operation_ids.add(operation_id)
                values = {"operation_id": operation_id, "expected_revision": number - 1, "reason": reason}
                target = row["reverted_from"]
                if target is None:
                    action = "expression.save"
                    values["content"] = content
                else:
                    if type(target) is not int or not 0 <= target < number:
                        raise ValueError("revert")
                    if content != (rows[target - 1]["content"] if target else ""):
                        raise ValueError("revert")
                    action = "expression.revert"
                    values["target_revision"] = target
                if row["request_sha256"] != self._request_hash(action, values):
                    raise ValueError("receipt")
            return rows
        except Exception:
            raise ExpressionManagementError() from None

    @staticmethod
    def _version(rows: list[dict[str, Any]], revision: int) -> dict[str, Any]:
        if revision > len(rows):
            raise ExpressionManagementError("expression.version_not_found")
        content = rows[revision - 1]["content"] if revision else ""
        return {
            "schema_version": 1, "revision": revision, "content": content,
            "content_sha256": sha256_hex(content),
            "base_identity_fingerprint": BASE_IDENTITY_FINGERPRINT,
        }

    @classmethod
    def _catalog(cls, rows: list[dict[str, Any]], revision: int) -> dict[str, Any]:
        return {
            **cls._version(rows, revision),
            "history": [{key: row[key] for key in (
                "revision", "created_at_utc", "reason", "content_sha256", "reverted_from",
            )} for row in rows[:revision]],
            "automatic_updates_enabled": False,
        }

    def _read(self) -> list[dict[str, Any]]:
        if not self._check_paths():
            return []
        with self._connection() as connection:
            connection.execute("BEGIN")
            try:
                return self._verify(connection)
            finally:
                connection.execute("ROLLBACK")

    def snapshot(self) -> dict[str, Any]:
        try:
            with self._lock:
                rows = self._read()
                return self._version(rows, len(rows))
        except Exception:
            raise ExpressionManagementError() from None

    @staticmethod
    def _request(action: Any, params: Any) -> dict[str, Any]:
        shapes = {
            "expression.get": set(),
            "expression.version.get": {"revision"},
            "expression.save": {"operation_id", "expected_revision", "content", "reason"},
            "expression.revert": {"operation_id", "expected_revision", "target_revision", "reason"},
        }
        if not isinstance(action, str) or action not in shapes or not isinstance(params, dict) or set(params) != shapes[action]:
            raise ExpressionManagementError("expression.invalid_request")
        values = dict(params)
        for name in ("revision", "expected_revision", "target_revision"):
            if name in values:
                _revision(values[name])
        if "operation_id" in values:
            _operation_id(values["operation_id"])
            _text(values["reason"], MAX_REASON_BYTES)
        if "content" in values:
            _text(values["content"], MAX_CONTENT_BYTES)
        return values

    def manage(self, action: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
        values = self._request(action, params)
        try:
            with self._lock:
                if action in {"expression.get", "expression.version.get"}:
                    rows = self._read()
                    return self._catalog(rows, len(rows)) if action == "expression.get" else self._version(rows, values["revision"])
                return self._mutate(action, values)
        except ExpressionManagementError:
            raise
        except Exception:
            raise ExpressionManagementError() from None

    def _mutate(self, action: str, values: dict[str, Any], *, allow_initial: bool = True) -> dict[str, Any]:
        # Read-only preflight rejects corrupt or foreign storage before SQLite
        # may recover journals or write. Recheck after obtaining the writer lock.
        rows = self._read()
        replay = self._check_new_request(rows, action, values)
        if replay is not None:
            return replay
        if not self._check_paths():
            if not allow_initial:
                raise ExpressionManagementError()
            return self._publish_first(action, values)
        with self._connection(writable=True) as connection:
            return self._transaction(connection, action, values)

    def _publish_first(self, action: str, values: dict[str, Any]) -> dict[str, Any]:
        # Never expose a partial canonical database. A unique sibling receives
        # the complete first transaction before an atomic, no-overwrite publish.
        descriptor, name = tempfile.mkstemp(prefix=".expression-", suffix=".sqlite3", dir=self._data_dir)
        temporary_path = Path(name)
        os.close(descriptor)
        try:
            with self._connection(writable=True, path=temporary_path) as connection:
                # Rollback journaling keeps the committed file self-contained
                # at publication and avoids the bundled SQLite WAL-reset issue.
                if str(connection.execute("PRAGMA journal_mode = DELETE").fetchone()[0]).upper() != "DELETE":
                    raise ExpressionManagementError()
                result = self._transaction(connection, action, values, initial_path=temporary_path)
            self._check_paths(temporary_path)
            # Windows _commit requires a writable handle even after SQLite
            # closed its own successfully committed transaction.
            descriptor = os.open(temporary_path, os.O_RDWR)
            try:
                os.fsync(descriptor)
            finally:
                os.close(descriptor)
            self._check_paths()
            try:
                self._publish_file(temporary_path)
            except FileExistsError:
                # A concurrent first saver won. Its accepted lineage decides
                # whether this request is a replay or a revision conflict.
                return self._mutate(action, values, allow_initial=False)
            return result
        finally:
            # Only this attempt's exact generated sibling names are owned.
            # Abandoned files from a killed process are never adopted or swept.
            for suffix in ("-journal", "-wal", "-shm", ""):
                Path(str(temporary_path) + suffix).unlink(missing_ok=True)

    def _publish_file(self, temporary_path: Path) -> None:
        if os.name == "nt":
            # Windows rename is atomic on this volume and refuses an existing
            # destination. os.replace must never be used here.
            os.rename(temporary_path, self._path)
        else:
            # POSIX rename overwrites, so publish with an exclusive hardlink.
            os.link(temporary_path, self._path)
            temporary_path.unlink()
            descriptor = os.open(self._data_dir, os.O_RDONLY)
            try:
                os.fsync(descriptor)
            finally:
                os.close(descriptor)

    def _transaction(self, connection: sqlite3.Connection, action: str, values: dict[str, Any], *, initial_path: Path | None = None) -> dict[str, Any]:
        connection.execute("PRAGMA synchronous = FULL")
        connection.execute("BEGIN IMMEDIATE")
        try:
            if initial_path is not None:
                for statement in _SCHEMA:
                    connection.execute(statement)
                connection.execute("PRAGMA user_version = 1")
                connection.execute("INSERT INTO expression_meta VALUES (?, ?, ?, ?, ?, ?, ?)", (*self._scope_record(), 0))
                rows = []
            else:
                rows = self._verify(connection)
            replay = self._check_new_request(rows, action, values)
            if replay is not None:
                connection.execute("ROLLBACK")
                return replay
            revision = len(rows) + 1
            target = values.get("target_revision")
            content = values["content"] if action == "expression.save" else self._version(rows, target)["content"]
            row = {
                "revision": revision, "content": content,
                "content_sha256": sha256_hex(content),
                "base_identity_fingerprint": BASE_IDENTITY_FINGERPRINT,
                "created_at_utc": canonical_utc(utc_now()), "reason": values["reason"],
                "reverted_from": target, "operation_id": values["operation_id"],
                "request_sha256": self._request_hash(action, values),
            }
            connection.execute(
                "INSERT INTO expression_revisions VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                tuple(row[key] for key in _REVISION_COLUMNS),
            )
            connection.execute("UPDATE expression_meta SET current_revision=? WHERE singleton=1", (revision,))
            rows = self._verify(connection)
            self._check_paths(initial_path)
            result = self._catalog(rows, revision)
            connection.execute("COMMIT")
            return result
        except Exception:
            if connection.in_transaction:
                connection.execute("ROLLBACK")
            raise

    def _check_new_request(self, rows: list[dict[str, Any]], action: str, values: dict[str, Any]) -> dict[str, Any] | None:
        request_hash = self._request_hash(action, values)
        for row in rows:
            if row["operation_id"] == values["operation_id"]:
                if row["request_sha256"] != request_hash:
                    raise ExpressionManagementError("expression.invalid_request")
                return self._catalog(rows, row["revision"])
        if values["expected_revision"] != len(rows):
            raise ExpressionManagementError("expression.revision_conflict")
        if action == "expression.revert" and values["target_revision"] > len(rows):
            raise ExpressionManagementError("expression.version_not_found")
        if len(rows) >= MAX_REVISIONS:
            raise ExpressionManagementError("expression.capacity_reached")
        return None
