"""Versioned portable player saves; standard library, offline and no retained backup.

Runtime owns the exclusive profile lease and process lifetime. This helper never
imports Hermes, starts a model, reads credentials or executes archive-supplied SQL.
Transaction preimages are temporary crash-recovery data, removed after commit.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import stat
import sys
import tempfile
import zipfile


FORMAT = "Lilith.PlayerSave"
VERSION = 1
TRANSACTION = ".player-save-transaction"
MAX_FILE = 512 * 1024 * 1024
MAX_EXPANDED = 3 * 1024 * 1024 * 1024
MAX_PENDING = 4096
GAME_HANDLE = "runtime/game-session-v1.json"
GAME_AUTHORITY = "runtime/game-session-authority-v2.json"
GAME_REJECTED = "runtime/game-session-v1.json.rejected"
GATEWAY_ROUTING = "sessions/sessions.json"
PENDING = "pending_messages"


class SaveError(Exception):
    def __init__(self, code="player_save.invalid"):
        super().__init__(code)
        self.code = code


def checked(path: Path) -> Path:
    """Reject redirected file/ancestor paths, including Windows junctions on 3.11."""
    path = Path(os.path.abspath(path))
    for current in (path, *path.parents):
        try:
            info = current.lstat()
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
            raise SaveError()
    return path


def contained(root: Path, relative: str) -> Path:
    path = checked(root / relative)
    if not path.is_relative_to(root) or path == root:
        raise SaveError()
    return path


def read_json(path: Path, maximum=MAX_FILE):
    path = checked(path)
    if path.stat().st_size > maximum:
        raise SaveError()
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise SaveError()
            result[key] = value
        return result
    try:
        return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=pairs,
                          parse_constant=lambda value: (_ for _ in ()).throw(SaveError()))
    except (ValueError, UnicodeError, RecursionError):
        raise SaveError() from None


def durable_json(path: Path, value):
    path = checked(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = checked(path.with_name(path.name + ".writing"))
    with temp.open("wb") as stream:
        stream.write(json.dumps(value, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8"))
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temp, path)


def digest(path: Path):
    path = checked(path)
    if not path.exists():
        return None
    if not path.is_file():
        raise SaveError()
    sha = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            sha.update(chunk)
    return sha.hexdigest()


def copy_durable(source: Path, target: Path):
    source, target = checked(source), checked(target)
    target.parent.mkdir(parents=True, exist_ok=True)
    with source.open("rb") as incoming, target.open("xb") as outgoing:
        shutil.copyfileobj(incoming, outgoing, 1024 * 1024)
        outgoing.flush()
        os.fsync(outgoing.fileno())


def remove_owned_tree(root: Path):
    """Only callers' freshly owned staging directories, never a profile root."""
    root = checked(root)
    if not root.exists():
        return
    # Check the whole tree before deleting anything. Never traverse reparse points.
    paths = []
    for directory, folders, files in os.walk(root, followlinks=False):
        for name in folders + files:
            paths.append(checked(Path(directory) / name))
    for path in sorted(paths, key=lambda item: len(item.parts), reverse=True):
        if path.is_dir():
            path.rmdir()
        else:
            path.unlink()
    root.rmdir()


def cleanup_transaction(stage: Path):
    # Keep the recovery record until every preimage/candidate has been removed.
    # A crash during cleanup must not turn a known commit/rollback into ambiguity.
    for child in tuple(checked(stage).iterdir()):
        checked(child)
        if child.name in ("journal.json", "owner.json"):
            continue
        if child.is_dir():
            remove_owned_tree(child)
        else:
            child.unlink()
    for name in ("owner.json", "journal.json"):
        marker = checked(stage / name)
        if marker.exists():
            marker.unlink()
    stage.rmdir()


def adapter_module():
    # -I -S intentionally omits script paths/site hooks; load only the adjacent asset.
    source = checked(Path(__file__).resolve().with_name("player_save_data.py"))
    spec = importlib.util.spec_from_file_location("lilith_trusted_player_save_data", source)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def pending_paths(profile: Path):
    directory = contained(profile, PENDING)
    if not directory.exists():
        return ()
    if not directory.is_dir():
        raise SaveError()
    result = []
    for child in directory.iterdir():
        if child.suffix.lower() != ".json":
            continue
        checked(child)
        if not child.is_file():
            raise SaveError()
        result.append(PENDING + "/" + child.name)
        if len(result) > MAX_PENDING:
            raise SaveError()
    return tuple(sorted(result))


def valid_pending_name(relative):
    if not isinstance(relative, str) or not relative.startswith(PENDING + "/"):
        return False
    name = relative[len(PENDING) + 1:]
    return (name.lower().endswith(".json") and 5 < len(name) <= 255
            and not any(char in name for char in '/\\:')
            and not any(ord(char) < 32 for char in name)
            and name not in (".", "..") and name.strip() == name)


def replacement_paths(adapter, profile=None):
    paths = list(adapter.REPLACEMENT_PATHS)
    paths.extend((GAME_HANDLE, GAME_AUTHORITY, GAME_REJECTED, GATEWAY_ROUTING))
    for name in adapter.REPLACEMENT_PATHS:
        if name.endswith((".db", ".sqlite3")):
            paths.extend(name + suffix for suffix in ("-wal", "-shm", "-journal"))
    if len(paths) != len(set(paths)) or any(Path(name).is_absolute() or ".." in Path(name).parts for name in paths):
        raise SaveError("player_save.unavailable")
    if profile is not None:
        dynamic = pending_paths(profile)
        if any(not valid_pending_name(name) for name in dynamic):
            raise SaveError()
        paths.extend(dynamic)
    return tuple(paths)


def _checkpoint(hook, label):
    if hook is not None:
        hook(label)


def _validate_journal(profile: Path, stage: Path, paths):
    journal = read_json(stage / "journal.json", 8 * 1024 * 1024)
    if (not isinstance(journal, dict) or journal.get("format") != FORMAT
            or journal.get("version") != VERSION or journal.get("phase") not in ("applying", "committed", "rolled_back")
            or not isinstance(journal.get("entries"), list)):
        raise SaveError("player_save.import_uncertain")
    names = [item.get("path") if isinstance(item, dict) else None for item in journal["entries"]]
    extra = names[len(paths):]
    if (names[:len(paths)] != list(paths) or len(extra) > MAX_PENDING or any(not valid_pending_name(name) for name in extra)
            or extra != sorted(extra) or len({name.casefold() for name in names}) != len(names)):
        raise SaveError("player_save.import_uncertain")
    for entry in journal["entries"]:
        if set(entry) != {"path", "before", "after"}:
            raise SaveError("player_save.import_uncertain")
        for field in ("before", "after"):
            value = entry[field]
            if value is not None and (not isinstance(value, str) or len(value) != 64
                    or any(char not in "0123456789abcdef" for char in value)):
                raise SaveError("player_save.import_uncertain")
        # Validate every target before making recovery changes.
        contained(profile, entry["path"])
        contained(stage / "original", entry["path"])
    return journal


def recover(profile: Path, adapter, *, hook=None):
    profile = checked(profile)
    stage = contained(profile, TRANSACTION)
    if not stage.exists():
        return
    if not stage.is_dir():
        raise SaveError("player_save.import_uncertain")
    journal_path = stage / "journal.json"
    if not journal_path.exists():
        # No original file is ever changed before a durable journal is published.
        # An unrecognized pre-existing directory is not ours to remove.
        marker = stage / "owner.json"
        names = {path.name for path in stage.iterdir()}
        # The very first durable write can itself be interrupted. With no other
        # staged content and no journal, this one reserved scratch name cannot
        # have preceded any profile mutation.
        interrupted_marker_only = names == {"owner.json.writing"}
        if names and not interrupted_marker_only and (not marker.exists() or read_json(marker, 1024) != {"format": FORMAT, "version": VERSION}):
            raise SaveError("player_save.import_uncertain")
        cleanup_transaction(stage)
        return
    journal = _validate_journal(profile, stage, replacement_paths(adapter))
    if journal["phase"] in ("committed", "rolled_back"):
        # A durable commit remains committed even if cleanup was interrupted.
        cleanup_transaction(stage)
        return
    if set(pending_paths(profile)) - {entry["path"] for entry in journal["entries"]}:
        raise SaveError("player_save.import_uncertain")
    for entry in journal["entries"]:
        before, after = entry["before"], entry["after"]
        current = digest(contained(profile, entry["path"]))
        if current not in (before, after):
            raise SaveError("player_save.import_uncertain")
        if digest(contained(stage / "original", entry["path"])) != before:
            raise SaveError("player_save.import_uncertain")
    for index, entry in enumerate(journal["entries"]):
        target = contained(profile, entry["path"])
        if digest(target) != entry["before"]:
            if entry["before"] is None:
                target.unlink()
            else:
                scratch = checked(stage / "restore.tmp")
                if scratch.exists():
                    scratch.unlink()
                copy_durable(contained(stage / "original", entry["path"]), scratch)
                target.parent.mkdir(parents=True, exist_ok=True)
                os.replace(scratch, target)
        _checkpoint(hook, f"restored:{index}")
    journal["phase"] = "rolled_back"
    durable_json(stage / "journal.json", journal)
    _checkpoint(hook, "rolled_back")
    cleanup_transaction(stage)


def commit(profile: Path, stage: Path, adapter, *, hook=None):
    entries = []
    for relative in replacement_paths(adapter, profile):
        target = contained(profile, relative)
        candidate = contained(stage / "incoming", relative)
        before, after = digest(target), digest(candidate)
        if before is not None:
            preimage = contained(stage / "original", relative)
            copy_durable(target, preimage)
            if digest(preimage) != before or digest(target) != before:
                raise SaveError("player_save.busy")
        entries.append({"path": relative, "before": before, "after": after})
    journal = {"format": FORMAT, "version": VERSION, "phase": "applying", "entries": entries}
    if tuple(entry["path"] for entry in entries) != replacement_paths(adapter, profile):
        raise SaveError("player_save.busy")
    durable_json(stage / "journal.json", journal)
    _checkpoint(hook, "prepared")
    # Complete validation before the first change, then check again at each swap.
    if any(digest(contained(profile, entry["path"])) != entry["before"] for entry in entries):
        raise SaveError("player_save.import_uncertain")
    for index, entry in enumerate(entries):
        target = contained(profile, entry["path"])
        if digest(target) != entry["before"]:
            raise SaveError("player_save.import_uncertain")
        if entry["before"] != entry["after"]:
            if entry["after"] is None:
                target.unlink()
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                os.replace(contained(stage / "incoming", entry["path"]), target)
        _checkpoint(hook, f"replaced:{index}")
    if any(digest(contained(profile, entry["path"])) != entry["after"] for entry in entries):
        raise SaveError("player_save.import_uncertain")
    if pending_paths(profile):
        raise SaveError("player_save.import_uncertain")
    journal["phase"] = "committed"
    durable_json(stage / "journal.json", journal)
    _checkpoint(hook, "committed")
    cleanup_transaction(stage)


def selected_game_session(profile: Path):
    path = contained(profile, GAME_HANDLE)
    if not path.exists():
        return None
    try:
        value = read_json(path, 16 * 1024)
        session = value.get("storedSessionId") if isinstance(value, dict) and value.get("schemaVersion") == 1 else None
        return session if isinstance(session, str) and session and session.strip() == session and not any(ord(char) < 32 for char in session) else None
    except SaveError:
        # A stale routing handle must not prevent rescue of the actual databases.
        return None


def _external_archive(profile, archive):
    archive = checked(archive)
    if archive.is_relative_to(profile) or archive == profile:
        raise SaveError()
    if archive.exists() and not archive.is_file():
        raise SaveError()
    return archive


def export_save(profile: Path, archive: Path, adapter):
    profile = checked(profile)
    archive = _external_archive(profile, archive)
    recover(profile, adapter)
    archive.parent.mkdir(parents=True, exist_ok=True)
    workspace = Path(tempfile.mkdtemp(prefix=".lilith-save-", dir=archive.parent))
    try:
        data = workspace / "data"
        data.mkdir()
        counts = adapter.export_data(profile, data, game_session_id=selected_game_session(profile))
        durable_json(data / "manifest.json", {"format": FORMAT, "version": VERSION,
                     "createdUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")})
        output = workspace / "save.tmp"
        with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as package:
            for name in ("manifest.json", *adapter.DATA_FILES):
                path = contained(data, name)
                if path.stat().st_size > MAX_FILE:
                    raise SaveError()
                package.write(path, name)
        with output.open("r+b") as stream:
            os.fsync(stream.fileno())
        checked(archive)
        os.replace(output, archive)
        return counts
    finally:
        remove_owned_tree(workspace)


def unpack(archive: Path, target: Path, adapter):
    allowed = {"manifest.json", *adapter.DATA_FILES}
    try:
        with zipfile.ZipFile(checked(archive), "r") as package:
            members = package.infolist()
            if (len(members) != len(allowed) or {item.filename for item in members} != allowed
                    or sum(item.file_size for item in members) > MAX_EXPANDED):
                raise SaveError()
            for item in members:
                mode = item.external_attr >> 16
                if (item.is_dir() or item.flag_bits & 1 or stat.S_ISLNK(mode)
                        or item.file_size > MAX_FILE or item.compress_type not in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED)):
                    raise SaveError()
                dest = contained(target, item.filename)
                with package.open(item) as source, dest.open("xb") as outgoing:
                    remaining = item.file_size
                    while chunk := source.read(min(1024 * 1024, remaining + 1)):
                        remaining -= len(chunk)
                        if remaining < 0:
                            raise SaveError()
                        outgoing.write(chunk)
                    if remaining != 0:
                        raise SaveError()
        manifest = read_json(target / "manifest.json", 16 * 1024)
        if not isinstance(manifest, dict) or manifest.get("format") != FORMAT or type(manifest.get("version")) is not int:
            raise SaveError()
        if manifest["version"] > VERSION:
            raise SaveError("player_save.newer_format")
        if manifest["version"] != VERSION:
            raise SaveError()
    except (zipfile.BadZipFile, NotImplementedError, RuntimeError):
        raise SaveError() from None


def import_save(profile: Path, archive: Path, adapter, native_source: Path, *, hook=None):
    profile = checked(profile)
    archive = _external_archive(profile, archive)
    recover(profile, adapter)
    profile.mkdir(parents=True, exist_ok=True)
    stage = contained(profile, TRANSACTION)
    stage.mkdir()
    try:
        durable_json(stage / "owner.json", {"format": FORMAT, "version": VERSION})
        data, incoming = stage / "archive", stage / "incoming"
        data.mkdir()
        incoming.mkdir()
        unpack(archive, data, adapter)
        try:
            counts = adapter.import_data(data, profile, incoming, native_source=checked(native_source))
        except Exception as error:
            if isinstance(error, getattr(adapter, "PlayerSaveConversionError", ())):
                raise SaveError("player_save.conversion_failed") from None
            raise
        # Never transport generation-bound game authority. Rebuild only selection.
        session = counts.get("game_session_id")
        if session is not None:
            durable_json(contained(incoming, GAME_HANDLE), {"schemaVersion": 1, "storedSessionId": session})
        _checkpoint(hook, "converted")
        commit(profile, stage, adapter, hook=hook)
        return counts
    except Exception:
        try:
            recover(profile, adapter)
        except Exception:
            raise SaveError("player_save.import_uncertain") from None
        raise


def result(succeeded, code, counts=None):
    counts = counts or {}
    return {"succeeded": succeeded, "code": code,
            "conversationCount": counts.get("sessions", 0), "messageCount": counts.get("messages", 0),
            "memoryCount": counts.get("memories", 0), "episodeCount": counts.get("episodes", 0)}


def main(argv=None):
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("operation", choices=("export", "import", "recover"))
    parser.add_argument("--profile", required=True)
    parser.add_argument("--archive")
    parser.add_argument("--hermes-source", required=True)
    try:
        args = parser.parse_args(argv)
        if not Path(args.profile).is_absolute() or not Path(args.hermes_source).is_absolute():
            raise SaveError()
        adapter = adapter_module()
        profile = checked(Path(args.profile))
        counts = {}
        if args.operation == "recover":
            recover(profile, adapter)
            code = "player_save.recovered"
        else:
            if not args.archive or not Path(args.archive).is_absolute():
                raise SaveError()
            if args.operation == "export":
                counts = export_save(profile, Path(args.archive), adapter)
                code = "player_save.exported"
            else:
                counts = import_save(profile, Path(args.archive), adapter, Path(args.hermes_source))
                code = "player_save.imported"
        output = result(True, code, counts)
    except SaveError as error:
        output = result(False, error.code)
    except (OSError, shutil.Error):
        output = result(False, "player_save.io_failed")
    except Exception:
        # No raw error text: SQLite/zip/provider errors may contain private data.
        output = result(False, "player_save.invalid")
    print(json.dumps(output, separators=(",", ":")))
    return 0 if output["succeeded"] else 1


if __name__ == "__main__":
    sys.exit(main())
