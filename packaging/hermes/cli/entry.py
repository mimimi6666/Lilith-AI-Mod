"""Relocatable entry for the sealed Lilith component, never a global install.

The native terminal/management caller owns approval. This bootstrap binds the
implementation and profile, not permissions; it is not an OS sandbox.
"""
from __future__ import annotations

import importlib.util
import os
from pathlib import Path
import runpy
import sys


class CliBindingError(RuntimeError):
    pass


def _same_path(left: Path, right: Path) -> bool:
    return os.path.normcase(str(left.resolve())) == os.path.normcase(str(right.resolve()))


def validate_binding(component: Path, environ: dict, executable: str) -> tuple[Path, list[Path]]:
    """Resolve explicit runtime binding without a HOME/working-dir fallback."""
    def required_path(key: str) -> Path:
        value = environ.get(key, "")
        path = Path(value)
        if not value or not path.is_absolute() or not path.is_dir():
            raise CliBindingError("Lilith CLI requires a valid Runtime-bound environment.")
        return path.resolve()

    bound_component = required_path("HERMES_LILITH_CLI_COMPONENT")
    bound_home = required_path("HERMES_LILITH_BOUND_HOME")
    actual_home = required_path("HERMES_HOME")
    if not _same_path(component, bound_component) or not _same_path(actual_home, bound_home):
        raise CliBindingError("Lilith CLI profile or component binding does not match.")
    if not _same_path(Path(executable), component / "python" / "python.exe"):
        raise CliBindingError("Lilith CLI must use its packaged Python interpreter.")
    source = component / "source"
    dependencies = component / "site-packages"
    roots = [source, dependencies, dependencies / "win32", dependencies / "win32" / "lib", dependencies / "Pythonwin"]
    # Voice dependencies are supplied by the same Runtime installation, never
    # inferred from a developer's Python or current working directory.
    roots.append(required_path("HERMES_LILITH_CLI_VOICE_SITE"))
    if any(not path.is_dir() for path in roots) or not (source / "hermes_cli" / "main.py").is_file():
        raise CliBindingError("Lilith CLI packaged dependencies are incomplete.")
    return bound_home, roots


def main() -> None:
    if not sys.flags.isolated or not sys.flags.no_site:
        raise CliBindingError("Lilith CLI requires isolated Python startup.")
    component = Path(__file__).resolve().parent.parent
    home, roots = validate_binding(component, os.environ, sys.executable)
    sys.path[:0] = [str(path) for path in roots]
    os.environ["HERMES_HOME"] = str(home)
    # Use upstream's managed-install policy for this CLI child only. This is
    # not approval_mode, and is never set on the normal gateway process.
    os.environ["HERMES_MANAGED"] = "Lilith"
    for key in ("PYTHONHOME", "PYTHONUSERBASE", "PYTHONSTARTUP", "PYTHONINSPECT", "VIRTUAL_ENV", "CONDA_PREFIX"):
        os.environ.pop(key, None)
    os.environ["PYTHONDONTWRITEBYTECODE"] = "1"
    os.environ["PYTHONNOUSERSITE"] = "1"
    os.environ["PYTHONSAFEPATH"] = "1"
    os.environ["PYTHONPATH"] = os.pathsep.join(str(path) for path in roots)
    for key, child in (("HERMES_PYTHON_SRC_ROOT", ""), ("HERMES_BUNDLED_SKILLS", "skills"),
                       ("HERMES_BUNDLED_PLUGINS", "plugins"), ("HERMES_BUNDLED_LOCALES", "locales"),
                       ("HERMES_OPTIONAL_SKILLS", "optional-skills"), ("HERMES_OPTIONAL_MCPS", "optional-mcps")):
        os.environ[key] = str(roots[0] / child)
    # -S prevents all ambient sitecustomize/.pth execution. Load only the
    # component's sealed bootstrap (pywin32 DLL registration).
    bootstrap = roots[0] / "sitecustomize.py"
    if not bootstrap.is_file():
        raise CliBindingError("Lilith CLI sealed bootstrap is missing.")
    spec = importlib.util.spec_from_file_location("_lilith_cli_site", bootstrap)
    if spec is None or spec.loader is None:
        raise CliBindingError("Lilith CLI sealed bootstrap could not be loaded.")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    sys.argv[0] = "hermes"
    runpy.run_module("hermes_cli.main", run_name="__main__", alter_sys=False)


if __name__ == "__main__":
    try:
        main()
    except CliBindingError as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(2) from None
