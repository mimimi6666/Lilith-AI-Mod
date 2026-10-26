# Building this public source snapshot

This directory is the reviewed public source snapshot for **Lilith AI MOD 1.0.0 Beta.4**, prepared on 2026-10-27. It contains current first-party application source, configuration examples, dependency identities, the Hermes patch series, and third-party notices. It is not the private development workspace or a ready-to-install game MOD.

## What is included

- `src/LilithControlCenter`: the independent Windows Control Center.
- `src/LilithRuntime`, its contracts, provider bridge and shared configuration: local service, game conversation bridge, model routing and profile integration.
- `src/LilithTextInjector`: game integration, Old Maid and chess, dialogue, display languages and character interactions.
- `src/LilithCursorGuard`: the local fullscreen cursor guard derived from SmallYuanSY's MIT-licensed project. Its original license is retained and embedded during compilation.
- `src/LilithLocalSceneCapture` and `src/LilithVoiceHost`: local scene capture and the voice host.
- `packaging/hermes/patches`: all **164** ordered changes pinned by the accompanying upstream lock. Hermes upstream is commit `3c5fd918e3e2537cd74f4f88c990c5de5cbd9f63`, package version `0.20.1`.
- Three small CC0 Kenney sound-effect samples needed to compile the game plugin. Their provenance, conversion details and license are in `src/LilithTextInjector/Assets/Audio`.

`src/LilithRuntime/Assets/PlayerSave` contains the implementation of save handling, not a player's save data. The identity packages are shipped application defaults. Runtime authority manifests contain dependency names, lengths and hashes, not executable payloads or credentials.

## Requirements

Use Windows and the .NET SDK specified by `global.json` (8.0.423, with compatible patch roll-forward). Projects target .NET 6 and .NET 8; package restore may need network access and the corresponding reference packs. PowerShell 7 is suitable for the public build script.

The two game plugins additionally need a legitimate local game installation and its **matching** BepInEx 6 IL2CPP core and generated game interop assemblies. Those game binaries are not included here. Copy `config/examples/local.paths.props.example` to `local.paths.props` beside this document, then set `LILITH_BEPINEX_CORE_DIR` and `LILITH_INTEROP_DIR` for your own installation. Keep that machine-specific file untracked. Alternatively, set `LILITH_LOCAL_PATHS_FILE` to your own MSBuild properties file.

The public snapshot uses repository-relative resource paths. It does not require the author's `local` directory. The two reference policy JSON resources originally kept with internal development candidates are now under `src/LilithRuntimeContracts/Assets`; their contents and embedded resource names are unchanged.

## Compile selected projects

From this directory:

```powershell
pwsh -NoProfile -File ./scripts/build-public.ps1 -SkipGamePlugins
```

After supplying the game references:

```powershell
pwsh -NoProfile -File ./scripts/build-public.ps1
```

These commands invoke ordinary `dotnet build`; they do not install, deploy, start the game, copy files into Steam, modify player data, or assemble a complete release. Individual projects may also be built directly. The private workspace solution and its deployment/evidence projects are deliberately not copied into this public snapshot.

## Hermes source and release limitations

The Hermes lock records the upstream commit, the exact order and SHA-256 of all patches, and expected patched-file identities. A developer may obtain that upstream revision separately and inspect or apply the patches in listed order. The upstream MIT license is included as `packaging/legal/licenses/Hermes-MIT.txt`.

Only the original developer-machine `sourcePath` and `venvPath` fields were removed from the public copy of `HermesUpstream.lock.json`. Patch bytes and their hashes were not changed. Fictional Windows paths in upstream patch test cases are test fixtures, not the author's paths or player files.

This snapshot **does not yet provide a verified one-command rebuild of the complete Beta.4 installer or sealed runtime**. The internal portable-component builder still targets an older schema (105), whereas this release's source lock is schema 164. That old builder is not included or presented as the current release procedure. Rebuilding the sealed runtime requires reconstructing and checking the pinned Python dependencies, generated manifests and component authority; compiling a C# project alone does not perform this work. Original native/game binaries, packaged tools, model weights, generated voice recordings, and game art are separate distribution inputs, not embedded in this source snapshot.

The checked-in runtime authority manifest matches the reviewed distribution components. A differently rebuilt Hermes component cannot simply replace those components without the corresponding authority generation and verification. Do not weaken the runtime checks to make an unrelated build start.

Source publication validation covers the allowlist, resource references, JSON syntax, hashes and prohibited output/data paths. It is not a new full clean-machine build, installer test, or promise of bit-for-bit reproduction. Use the published Beta.4 installer for the supported player installation path.

## Licensing and attribution

See `packaging/legal/THIRD_PARTY_NOTICES.md`, the original notices alongside resources, and the upstream license texts. In particular, the CursorGuard integration credits [SmallYuanSY/LilithCursorGuard](https://github.com/SmallYuanSY/LilithCursorGuard). The separately distributed chess engine is Stockfish 16 (GPL-3.0); its matching source, network file and licenses accompany the game distribution as described in `packaging/legal/CHESS-NOTICES.txt`.

This snapshot does not assign a new license to existing first-party code or to game/voice artwork. Software dependency licenses do not automatically license character art, recordings, model weights or other content. Existing project licensing and individual third-party terms continue to apply.

## Files intentionally omitted

Player saves, conversation histories, API keys, runtime settings, private logs, internal audit/handoff reports, local machine configuration, installation backups, historical candidate trees, compiler outputs, Python bytecode, third-party runtime binaries, training materials, and release archives are excluded. The full private test/evidence tree is also omitted rather than publishing machine-bound or historical operational records as a public test suite.
