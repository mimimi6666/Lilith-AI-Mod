# Lilith AI MOD Beta.4 — Installation and updates

[繁體中文](INSTALLATION.md) · [日本語](INSTALLATION_JA.md)

**The old architecture and previous closed-test memories are incompatible with Beta.4. A clean installation is required. Remove the old Lilith-specific data using the migration steps below; do not retain or import old chats, memories or settings into Beta.4.** New users can install directly.

## Downloads

- [Google Drive: Beta.4](https://drive.google.com/drive/folders/1q8GcsuKSjPZCbKw8HYrMYR1dgThBhfUe?usp=sharing)
- [Baidu Netdisk: Beta.4](https://pan.baidu.com/s/1xAt9y_MZ1DJMohHZ90bEQw?pwd=6666) — extraction code: `6666`

| File | Purpose | Approximate download |
| --- | --- | --- |
| `Lilith-AI-Mod-1.0.0-Beta.4.zip` | Main distribution: Setup.exe, Main.zip and instructions | 4.99 GB |
| `LilithDependencies-voice-cu128-prebuilt-20261008.zip` | Optional, preassembled dependencies for AI speech synthesis | 3.70 GB |

**The 4.99 GB distribution does not contain the separate dependency ZIP.** Voice models, reference clips and voice settings are in the main package; the execution dependencies come from a compatible existing installation, the optional ZIP, or the selected online download source. For a first installation with AI voice and no dependency downloads during setup, obtain both ZIPs.

Cleaning an old installation can also remove its installed dependencies. Do not retain old data just to preserve them. A previously downloaded `LilithDependencies-voice-cu128-20260917.zip` is still valid installation material, though it requires local assembly on first use; reusing a dependency ZIP is not reusing memories. Minigame artwork, sound effects and fixed Chinese/Japanese recordings are part of the main package regardless of the optional AI voice choice.

## Before installing

Use 64-bit Windows and install the base game through Steam first. Verify that the unmodified game starts. Previous old-architecture/closed-test users must complete the cleanup below first. Exit both the game and Control Center, including its system-tray instance. Leave space for extraction, installed files and temporary setup files, not only the download.

The installer includes its runtime; no developer SDK or system Python installation is required. Local voice performance depends on hardware, drivers and settings; there is no universal RAM/VRAM guarantee.

## Install or update

1. Extract `Lilith-AI-Mod-1.0.0-Beta.4.zip` before running anything.
2. Keep `Lilith-AI-Mod-1.0.0-Beta.4-Setup.exe` and `Lilith-AI-Mod-1.0.0-Beta.4-Main.zip` together. **Do not extract Main.zip.**
3. If using the optional voice dependency ZIP, leave it compressed and place it beside Setup.exe or in its immediate parent folder. You can also select the ZIP in the installer.
4. Run **`Lilith-AI-Mod-1.0.0-Beta.4-Setup.exe`**. Confirm the detected Steam game directory, or select the directory containing `Lilith.exe` manually.
5. Choose whether to install Chinese/Japanese AI voice and select the dependency source. **Offline only** is the default.
6. Select **Start installation** and wait for completion. If files are in use, exit the game and Control Center and retry.
7. Select **Open Control Center**, or use the installed **Lilith AI MOD** desktop shortcut. Configure your model connection and preferences, then start Lilith through Steam and check that the game is connected.

Previous old-architecture/closed-test installations require the following clean-install procedure before these setup steps. They are not compatible in-place upgrades.

## “Offline only” is an installation setting

It means setup uses compatible installed dependencies or a local ZIP without downloading missing dependencies. **It does not make remote AI chat models run offline.** Gemini, DeepSeek and other remote services still need network access and your own service configuration/credentials; provider fees and limits apply.

If AI voice is selected and neither compatible dependencies nor a local ZIP is available, supply the ZIP or choose an online source such as Official, Automatic or Tsinghua. You may also disable optional AI voice and install the core MOD first.

## Launching Control Center

Use the installed desktop shortcut. Control Center starts its background service automatically; old launch scripts and a manually started Runtime are unnecessary.

The executable is:

```text
<game folder>\BepInEx\data\LilithTextInjector\control-center\LilithControlCenter.exe
```

If recreating the shortcut, use the full quoted path followed by **`--provider-live`**, and set its working directory to the same `control-center` folder:

```text
"<game folder>\BepInEx\data\LilithTextInjector\control-center\LilithControlCenter.exe" --provider-live
```

Replace `<game folder>` with your actual installation directory. The official shortcut and setup's launch button already include this argument; double-clicking the executable alone is not equivalent.

## Required clean installation from old architecture / closed tests

Previous builds were closed tests. Their old memories, chats and settings must not be retained or imported into Beta.4. Setup normally preserves player data, so uninstalling the program alone is not enough for this migration.

1. Exit the game and old Control Center, including the system-tray instance.
2. If the old Lilith AI MOD appears in Windows **Settings → Apps**, uninstall it first. Skip this step if no entry exists.
3. Press `Win+R`, enter `%LOCALAPPDATA%`, and delete only the **`LilithAiMod` folder** there—not the entire `Local` directory.
4. Open the game's local files through Steam and delete only **`BepInEx\data\LilithTextInjector`**. This can contain legacy memory/chat data; clearing AppData alone is insufficient.
5. In the same game folder, delete the old configuration file **`BepInEx\config\community.lilith.textinjector.cfg`**. Skip any of the named items that do not exist.
6. Install Beta.4 fresh and configure your model connection, credentials and preferences again. **Do not restore the old folders or import an old chat/memory backup.**

This intentionally resets the old MOD's chats, memories and settings. Restrict cleanup to the named Lilith-owned locations. **Do not delete all of system AppData/Local, all of BepInEx, other MODs or the original game's save data.** If you changed storage locations yourself and cannot identify them, ask the maintainer with the actual path and diagnostic.

## Future updates within the new architecture

Only future releases explicitly marked as data-compatible may reuse new-architecture data. Do not apply that policy to previous closed-test builds. Export/import is for supported new-format saves, not a bridge for incompatible old memories.

Setup does not retain a rollback copy of the old program. If replacement is interrupted, exit the applications and rerun setup. For component verification errors, preserve the diagnostic and do not edit manifests to bypass validation.

## Troubleshooting and removal

- **Game not found:** select the folder containing `Lilith.exe`, `GameAssembly.dll` and `Lilith_Data`.
- **Offline dependency missing:** check the ZIP location or select it explicitly; an extra enclosing extraction folder can prevent discovery.
- **Unsupported game version:** check the game path and obtain a matching installer; do not replace game DLLs yourself.
- **Voice warning after installation:** follow Control Center diagnostics. A voice readiness warning does not mean the core MOD installation failed.
- **Files busy or interrupted update:** exit both applications, then rerun setup.

To uninstall, use Windows **Settings → Apps → Lilith AI MOD**. Ordinary uninstall removes program components but retains data and shared components; it does not restore an older MOD. **Migrating from the old architecture to Beta.4 additionally requires the data cleanup above.**
