"""Offline, logical player-save adapter. Never initializes a provider or writes a live profile.

The archive vocabulary is independent of the MOD build and database schema.
Only this trusted adapter's table/column vocabulary is executable; archive data
cannot provide SQL, installation files, account bindings or tool authority.
"""
from __future__ import annotations

import ast
import hashlib
import json
import math
import os
from pathlib import Path
import sqlite3
import uuid
from datetime import datetime, timezone

FORMAT_VERSION = 1
DATA_FILES = ("chat.json", "relationship.json", "profile-memory.json", "native-memory.json", "game-session.json")
MEMORY_ROOT = "plugin-data/lilith-relationship"
PENDING_REVIEWS_PATH = f"{MEMORY_ROOT}/player-save-pending-reviews.v1.json"
REPLACEMENT_PATHS = (
    "state.db", f"{MEMORY_ROOT}/relationship.sqlite3",
    f"{MEMORY_ROOT}/profile-memory.sqlite3", f"{MEMORY_ROOT}/settings.json",
    "memories/MEMORY.md", "memories/USER.md", PENDING_REVIEWS_PATH,
)
_MAX_DOCUMENT_BYTES = 512 * 1024 * 1024
_SCOPE = ("owner_id", "character_key", "canon_fingerprint")
_SESSION_FIELDS = tuple("id source model parent_session_id started_at ended_at end_reason message_count tool_call_count input_tokens output_tokens cache_read_tokens cache_write_tokens reasoning_tokens estimated_cost_usd actual_cost_usd cost_status cost_source pricing_version title title_source api_call_count archived pinned last_read_at last_conversation_at profile_name".split())
_MESSAGE_FIELDS = tuple("id session_id role content tool_call_id tool_calls tool_name effect_disposition timestamp token_count finish_reason reasoning reasoning_content reasoning_details codex_reasoning_items codex_message_items observed active compacted display_kind display_metadata".split())
_USAGE_FIELDS = tuple("session_id model billing_provider billing_mode task api_call_count input_tokens output_tokens cache_read_tokens cache_write_tokens reasoning_tokens estimated_cost_usd actual_cost_usd cost_status cost_source first_seen last_seen".split())
# Keep the original v1 vocabulary for strict legacy-save validation. Operational
# usage is neither conversational continuity nor portable provider authority.
_SESSION_USAGE_FIELDS = frozenset("input_tokens output_tokens cache_read_tokens cache_write_tokens reasoning_tokens estimated_cost_usd actual_cost_usd cost_status cost_source pricing_version api_call_count".split())
_PORTABLE_SESSION_FIELDS = tuple(field for field in _SESSION_FIELDS if field not in _SESSION_USAGE_FIELDS)
_PORTABLE_MESSAGE_FIELDS = tuple(field for field in _MESSAGE_FIELDS if field != "token_count")
_REL_FIELDS = {
    "episodes": tuple("event_id session_id turn_id user_message_id assistant_message_id source_hash observed_at_utc recorded_at_utc what_kind why_text target_kind evidence_text theme event_time_appraisal link_relation link_target_event_id".split()),
    "operator_corrections": tuple("correction_id event_id correction_text changed_at_utc".split()),
    "operator_visibility_actions": tuple("action_id event_id hidden changed_at_utc".split()),
    "processed_reviews": tuple("source_hash session_id turn_id user_message_id assistant_message_id".split()),
    "review_outbox": tuple("source_hash session_id turn_id user_message_id assistant_message_id user_text assistant_text observed_at_utc review_queued_at_utc".split()),
}
_PROFILE_FIELDS = {
    "profile_entries": tuple("entry_id entry_version category state content_text content_normalized source observed_at_utc updated_at_utc expires_at_utc pinned evidence_excerpt memory_kind canonical_key source_hash current_revision_id".split()),
    "profile_evidence": tuple("evidence_id entry_id assertion_id source_hash session_id turn_id user_message_id assistant_message_id observed_at_utc evidence_excerpt assertion_kind".split()),
    "profile_revisions": tuple("revision_id entry_id previous_revision_id entry_version_after mutation_kind category_after state_after content_after source_after expires_at_utc_after pinned_after evidence_excerpt_after source_hash superseded_by_entry_id operation_id created_at_utc".split()),
    "profile_processed_reviews": tuple("source_hash session_id turn_id user_message_id assistant_message_id processed_at_utc".split()),
    "profile_review_dead_letter": tuple("source_hash session_id turn_id user_message_id assistant_message_id review_queued_at_utc dead_lettered_at_utc attempt_count reason_code".split()),
    "profile_review_outbox": tuple("source_hash session_id turn_id user_message_id assistant_message_id user_text assistant_text observed_at_utc review_queued_at_utc attempt_count next_attempt_at_utc".split()),
}
_LINEAGE_KEYS = ("_branched_from", "_delegate_from", "_reset_from")


class PlayerSaveDataError(ValueError):
    """Safe error category; never embeds player text or database errors."""


class PlayerSaveConversionError(PlayerSaveDataError):
    """Trusted installed schema could not reconstruct otherwise parsed data."""


def _fail(message="The player-save data is invalid."):
    raise PlayerSaveDataError(message)


def _conversion_fail(message="The installed player-data adapter is incompatible."):
    raise PlayerSaveConversionError(message)


def _path(root: Path, relative: str) -> Path:
    root = Path(root).absolute()
    result = root / relative
    current = result
    while True:
        try:
            attributes = getattr(os.lstat(current), "st_file_attributes", 0)
        except FileNotFoundError:
            attributes = 0
        if current.is_symlink() or attributes & 0x400:
            _fail("A player-save path is redirected.")
        if current == current.parent:
            break
        current = current.parent
    return result


def _loads(raw: bytes):
    if len(raw) > _MAX_DOCUMENT_BYTES:
        _fail("The player-save data is too large.")
    def pairs(values):
        result = {}
        for key, value in values:
            if key in result:
                _fail()
            result[key] = value
        return result
    try:
        return json.loads(raw.decode("utf-8"), object_pairs_hook=pairs,
                          parse_constant=lambda value: _fail())
    except (UnicodeError, json.JSONDecodeError, RecursionError):
        _fail()


def _document(staging: Path, name: str) -> dict:
    try:
        data = _loads(_path(staging, name).read_bytes())
    except OSError:
        _fail("A player-save data file is unavailable.")
    if not isinstance(data, dict) or type(data.get("version")) is not int or data["version"] != FORMAT_VERSION:
        _fail("The player-save data version is unsupported.")
    return data


def _write_document(staging: Path, name: str, value: dict):
    target = _path(staging, name)
    target.parent.mkdir(parents=True, exist_ok=True)
    with target.open("xb") as stream:
        stream.write(json.dumps({"version": FORMAT_VERSION, **value}, ensure_ascii=False,
                                separators=(",", ":"), allow_nan=False).encode("utf-8"))


def _read_tables(path: Path, vocabulary: dict[str, tuple[str, ...]], *, required_tables: tuple[str, ...] = ()) -> dict:
    if not path.exists():
        return {table: [] for table in vocabulary}
    connection = sqlite3.connect(path.as_uri() + "?mode=ro", uri=True, timeout=5)
    try:
        connection.execute("PRAGMA query_only=ON")
        connection.execute("PRAGMA trusted_schema=OFF")
        connection.row_factory = sqlite3.Row
        connection.execute("BEGIN")
        tables = {row[0] for row in connection.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        # An absent store is a legitimate fresh install; an existing store with
        # no primary tables is not an empty profile. Never turn an unreadable or
        # unrelated database into an apparently successful destructive save.
        if not set(required_tables).issubset(tables):
            _fail("A player database is missing required data tables.")
        result = {}
        for table, fields in vocabulary.items():
            if table not in tables:
                result[table] = []
                continue
            columns = {row[1] for row in connection.execute(f'PRAGMA table_info("{table}")')}
            selected = tuple(field for field in fields if field in columns)
            if not selected:
                _fail("A player database has an unsupported table.")
            query = ",".join('"' + field + '"' for field in selected)
            result[table] = [dict(row) for row in connection.execute(f'SELECT {query} FROM "{table}" ORDER BY rowid')]
        return result
    except sqlite3.Error:
        _fail("A player database could not be read.")
    finally:
        connection.close()


def _lineage_config(raw):
    if isinstance(raw, str):
        try:
            raw = _loads(raw.encode("utf-8"))
        except PlayerSaveDataError:
            return {}
    if not isinstance(raw, dict):
        return {}
    return {key: raw[key] for key in _LINEAGE_KEYS if isinstance(raw.get(key), str)}


def export_data(profile: Path, staging: Path, *, game_session_id: str | None = None) -> dict:
    """Export local rows without Runtime, model requests, identity checks or live writes.

    The caller holds the offline maintenance boundary for a coherent multi-store
    snapshot. Account/configuration databases and expression files are not read.
    """
    chat = _read_tables(_path(profile, "state.db"), {
        "sessions": (*_PORTABLE_SESSION_FIELDS, "model_config"), "messages": _PORTABLE_MESSAGE_FIELDS,
    }, required_tables=("sessions", "messages"))
    for row in chat["sessions"]:
        row["lineage"] = _lineage_config(row.pop("model_config", None))
    relationship = _read_tables(_path(profile, f"{MEMORY_ROOT}/relationship.sqlite3"), _REL_FIELDS,
                                required_tables=("episodes",))
    memory = _read_tables(_path(profile, f"{MEMORY_ROOT}/profile-memory.sqlite3"), _PROFILE_FIELDS,
                          required_tables=("profile_entries",))
    inert_path = _path(profile, PENDING_REVIEWS_PATH)
    if inert_path.exists():
        inert = _loads(inert_path.read_bytes())
        if not isinstance(inert, dict) or inert.get("version") != FORMAT_VERSION:
            _fail("Saved pending memory records are malformed.")
        for target, table in ((relationship, "review_outbox"), (memory, "profile_review_outbox")):
            rows = inert.get(table, [])
            if not isinstance(rows, list) or any(not isinstance(row, dict) for row in rows):
                _fail()
            # New authoritative pending rows win over an earlier inert copy.
            pending = {row.get("source_hash"): row for row in rows}
            pending.update({row.get("source_hash"): row for row in target[table]})
            target[table] = list(pending.values())
    native = {}
    for name in ("MEMORY.md", "USER.md"):
        path = _path(profile, "memories/" + name)
        native[name] = path.read_text(encoding="utf-8") if path.exists() else None
    if game_session_id is not None and not any(row.get("id") == game_session_id and row.get("source") == "lilith-game" and row.get("archived") == 1 for row in chat["sessions"]):
        # Export is also the offline recovery path. A stale F10 selection must
        # not prevent rescuing the underlying chats and memories. Only the
        # unusable selection is omitted; every conversation row is preserved.
        game_session_id = None
    _write_document(staging, "chat.json", {"tables": chat})
    _write_document(staging, "relationship.json", {"tables": relationship})
    _write_document(staging, "profile-memory.json", {"tables": memory})
    _write_document(staging, "native-memory.json", {"files": native})
    _write_document(staging, "game-session.json", {"session_id": game_session_id})
    return _counts(chat, relationship, memory, game_session_id)


def _counts(chat, relationship, memory, game_session_id):
    return {"sessions": len(chat["sessions"]), "messages": len(chat["messages"]),
            "memories": len(memory["profile_entries"]), "episodes": len(relationship["episodes"]),
            "game_session_id": game_session_id}


def _constants(path: Path) -> dict:
    """Read literals in trusted shipped source; no module execution/import."""
    try:
        module = ast.parse(path.read_text(encoding="utf-8-sig"))
    except (OSError, SyntaxError, UnicodeError):
        _conversion_fail("The installed player-data adapter is unavailable.")
    result = {}
    for node in module.body:
        if isinstance(node, ast.Assign) and len(node.targets) == 1 and isinstance(node.targets[0], ast.Name):
            try:
                result[node.targets[0].id] = ast.literal_eval(node.value)
            except (ValueError, TypeError):
                pass
    return result


def _relationship_schema(plugin_source: Path) -> tuple[str, ...]:
    try:
        module = ast.parse((plugin_source / "store.py").read_text(encoding="utf-8-sig"))
        store = next(node for node in module.body if isinstance(node, ast.ClassDef) and node.name == "RelationshipStore")
        init = next(node for node in store.body if isinstance(node, ast.FunctionDef) and node.name == "_initialize_database")
        result = tuple(node.args[0].value for node in ast.walk(init)
                       if isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute)
                       and node.func.attr == "execute" and node.args
                       and isinstance(node.args[0], ast.Constant) and isinstance(node.args[0].value, str)
                       and node.args[0].value.lstrip().startswith("CREATE "))
        if not result:
            _conversion_fail()
        return result
    except (OSError, SyntaxError, StopIteration, UnicodeError):
        _conversion_fail("The installed memory adapter is unavailable.")


def _tables(document: dict, vocabulary: dict) -> dict:
    raw = document.get("tables")
    if not isinstance(raw, dict) or set(raw) - set(vocabulary):
        _fail()
    result = {}
    for table, columns in vocabulary.items():
        rows = raw.get(table, [])
        if not isinstance(rows, list):
            _fail()
        clean = []
        for row in rows:
            if not isinstance(row, dict) or set(row) - set(columns):
                _fail()
            for key, value in row.items():
                if key == "lineage":
                    if not isinstance(value, dict) or set(value) - set(_LINEAGE_KEYS) or any(not isinstance(v, str) for v in value.values()):
                        _fail()
                elif value is not None and type(value) not in (str, int, float):
                    _fail()
                elif isinstance(value, float) and not math.isfinite(value):
                    _fail()
            clean.append(dict(row))
        result[table] = clean
    return result


def _destination_settings(profile: Path, domain: dict) -> tuple[bytes, dict]:
    path = _path(profile, f"{MEMORY_ROOT}/settings.json")
    if path.exists():
        raw = path.read_bytes()
        settings = _loads(raw)
        if not isinstance(settings, dict) or any(not isinstance(settings.get(key), str) or not settings[key] for key in _SCOPE):
            _fail("The destination memory identity could not be read.")
        return raw, settings
    canon = domain.get("DEFAULT_CANON_PROFILE")
    if not isinstance(canon, dict) or type(domain.get("SETTINGS_SCHEMA_VERSION")) is not int:
        _conversion_fail("The installed character data is unavailable.")
    material = json.dumps(canon, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    settings = {"schema_version": domain["SETTINGS_SCHEMA_VERSION"],
                "owner_id": uuid.uuid5(uuid.NAMESPACE_URL, "lilith-player-save:" + str(Path(profile).absolute())).hex,
                "character_key": "lilith", "canon_profile": canon,
                "canon_fingerprint": hashlib.sha256(("lilith-relationship-canon-v1\0" + material).encode()).hexdigest()}
    return (json.dumps(settings, ensure_ascii=False, indent=2) + "\n").encode(), settings


def _insert(connection, table, rows, *, scope=None):
    columns = {row[1] for row in connection.execute(f'PRAGMA table_info("{table}")')}
    for original in rows:
        row = dict(original)
        if scope is not None:
            row.update({key: scope[key] for key in _SCOPE if key in columns})
        if set(row) - columns:
            _conversion_fail("The installed database needs a newer data adapter.")
        fields = tuple(row)
        query = ",".join('"' + field + '"' for field in fields)
        connection.execute(f'INSERT INTO "{table}" ({query}) VALUES ({",".join("?" for _ in fields)})', tuple(row.values()))


def _new_database(path: Path, schema: tuple[str, ...], version: int | None = None):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        _fail("The import workspace is not empty.")
    connection = sqlite3.connect(path)
    try:
        for sql in schema:
            connection.executescript(sql)
        if version is not None:
            connection.execute(f"PRAGMA user_version={int(version)}")
        connection.commit()
        connection.execute("PRAGMA foreign_keys=ON")
        connection.execute("BEGIN")
        connection.execute("PRAGMA defer_foreign_keys=ON")
        return connection
    except Exception:
        connection.close()
        raise


def _finish(connection):
    try:
        if connection.execute("PRAGMA foreign_key_check").fetchone() is not None:
            _fail("The save contains an incomplete data relationship.")
        connection.commit()
        if connection.execute("PRAGMA quick_check").fetchone()[0] != "ok":
            _fail("The imported database is damaged.")
    finally:
        connection.close()


def _destination_preferences(profile: Path):
    result = _read_tables(_path(profile, f"{MEMORY_ROOT}/profile-memory.sqlite3"), {
        "profile_preferences": ("preferences_revision", "auto_memory_enabled", "updated_at_utc"),
        "profile_meta": ("telegram_subject_id",),
    })
    prefs = result["profile_preferences"]
    meta = result["profile_meta"]
    return (prefs[0] if prefs else None), (meta[0].get("telegram_subject_id") if meta else None)


def import_data(staging: Path, profile: Path, output: Path, *, native_source: Path,
                plugin_source: Path | None = None) -> dict:
    """Build replacement files under output only. Caller owns stopped atomic install.

    Expression/config/account state never comes from an archive. Existing local
    memory owner and preferences survive; foreign historical scopes are rebound
    into this destination. No LLM is used to judge/transform conversation content.
    """
    plugin_source = Path(plugin_source) if plugin_source else Path(__file__).resolve().parents[1] / "Plugins" / "lilith_relationship"
    native = _constants(Path(native_source) / "hermes_state_common.py")
    domain = _constants(plugin_source / "domain.py")
    profile_schema = _constants(plugin_source / "profile_store.py")
    profile_domain = _constants(plugin_source / "profile_domain.py")
    chat = _tables(_document(staging, "chat.json"), {
        "sessions": (*_SESSION_FIELDS, "lineage"), "messages": _MESSAGE_FIELDS,
        "session_model_usage": _USAGE_FIELDS,
    })
    # Legacy v1 saves may contain usage keys collapsed by omitted provider URLs.
    # Validate the closed vocabulary above, then discard usage before any SQL.
    # Omitted columns receive the trusted destination schema's current defaults.
    del chat["session_model_usage"]
    for session in chat["sessions"]:
        for field in _SESSION_USAGE_FIELDS:
            session.pop(field, None)
    for message in chat["messages"]:
        message.pop("token_count", None)
    relationship = _tables(_document(staging, "relationship.json"), _REL_FIELDS)
    memory = _tables(_document(staging, "profile-memory.json"), _PROFILE_FIELDS)
    inert = {"version": FORMAT_VERSION, "review_outbox": relationship["review_outbox"],
             "profile_review_outbox": memory["profile_review_outbox"]}
    relationship["review_outbox"] = []
    memory["profile_review_outbox"] = []
    native_memory = _document(staging, "native-memory.json").get("files")
    if not isinstance(native_memory, dict) or set(native_memory) != {"MEMORY.md", "USER.md"} or any(value is not None and not isinstance(value, str) for value in native_memory.values()):
        _fail()
    game_session_id = _document(staging, "game-session.json").get("session_id")
    if game_session_id is not None and (not isinstance(game_session_id, str) or not any(row.get("id") == game_session_id and row.get("source") == "lilith-game" and row.get("archived") == 1 for row in chat["sessions"])):
        _fail("The saved game conversation is unavailable.")
    ids = {row.get("id") for row in chat["sessions"]}
    if None in ids or len(ids) != len(chat["sessions"]) or any(not isinstance(value, str) or not value for value in ids):
        _fail()
    for session in chat["sessions"]:
        if session.get("parent_session_id") is not None and session["parent_session_id"] not in ids:
            _fail("The save contains an incomplete conversation chain.")
        lineage = session.pop("lineage", {})
        if any(value not in ids for value in lineage.values()):
            _fail("The save contains an incomplete conversation chain.")
        session["model_config"] = json.dumps(lineage, separators=(",", ":"))
        # A saved model label is historical display data, not a request to route
        # the next turn to an old/missing provider. The native resume adapter
        # reconstructs identity and routing from this installation's settings.
        session["model"] = None
    settings_raw, settings = _destination_settings(profile, domain)
    preferences, telegram = _destination_preferences(profile)
    now = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    # All files are prepared in a disposable private directory before any live
    # replacement. sqlite errors are intentionally stripped of private values.
    try:
        schema = tuple(native[name] for name in ("SCHEMA_SQL", "DEFERRED_INDEX_SQL", "FTS_SQL") if name in native)
        if "SCHEMA_SQL" not in native or not schema:
            _conversion_fail("The installed chat adapter is unavailable.")
        connection = _new_database(_path(output, "state.db"), schema)
        try:
            connection.execute("INSERT INTO schema_version(version) VALUES (?)", (native["SCHEMA_VERSION"],))
            connection.execute("INSERT INTO state_meta(key,value) VALUES ('fts_storage_version',?)", (str(native.get("FTS_STORAGE_VERSION", 1)),))
            for table, rows in chat.items():
                _insert(connection, table, rows)
            _finish(connection)
        finally:
            connection.close()
        connection = _new_database(_path(output, f"{MEMORY_ROOT}/relationship.sqlite3"), _relationship_schema(plugin_source), domain["STORE_SCHEMA_VERSION"])
        try:
            for table, rows in relationship.items():
                _insert(connection, table, rows, scope=settings)
            _finish(connection)
        finally:
            connection.close()
        connection = _new_database(_path(output, f"{MEMORY_ROOT}/profile-memory.sqlite3"), tuple(profile_schema["PROFILE_SCHEMA_V1_STATEMENTS"]) + tuple(profile_schema["PROFILE_SCHEMA_V2_STATEMENTS"]), profile_domain["PROFILE_STORE_SCHEMA_VERSION"])
        try:
            connection.execute("INSERT INTO profile_meta(singleton,catalog_revision,scope_owner_id,scope_character_key,scope_canon_fingerprint,telegram_subject_id) VALUES (1,0,?,?,?,?)", (*[settings[key] for key in _SCOPE], telegram))
            for version in range(1, profile_domain["PROFILE_STORE_SCHEMA_VERSION"] + 1):
                connection.execute("INSERT INTO profile_schema_migrations VALUES (?,?)", (version, now))
            prefs = preferences or {"preferences_revision": 0, "auto_memory_enabled": 1, "updated_at_utc": now}
            _insert(connection, "profile_preferences", [{"singleton": 1, **prefs}])
            for table, rows in memory.items():
                _insert(connection, table, rows, scope=settings)
            _finish(connection)
        finally:
            connection.close()
        target = _path(output, f"{MEMORY_ROOT}/settings.json")
        with target.open("xb") as stream:
            stream.write(settings_raw)
        with _path(output, PENDING_REVIEWS_PATH).open("xb") as stream:
            stream.write(json.dumps(inert, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
        for name, contents in native_memory.items():
            # Empty files overwrite obsolete destination notes; null in the
            # logical save means the source had no file, not keep old notes.
            target = _path(output, "memories/" + name)
            target.parent.mkdir(parents=True, exist_ok=True)
            with target.open("xb") as stream:
                stream.write((contents or "").encode("utf-8"))
    except sqlite3.IntegrityError:
        _fail("The save contains invalid or incomplete database records.")
    except (sqlite3.Error, KeyError, TypeError):
        _conversion_fail()
    return _counts(chat, relationship, memory, game_session_id)
