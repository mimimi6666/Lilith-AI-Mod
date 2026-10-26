# Third-Party Components and License Notice

This release candidate includes, redistributes, or is capable of interoperating with the following third-party projects:

* [Hermes Agent 0.20.1](https://github.com/NousResearch/hermes-agent)
  License: MIT. The upstream revision is `3c5fd918e3e2537cd74f4f88c990c5de5cbd9f63`.
  The public source includes 164 ordered modifications and their hashes in
  `packaging/hermes/HermesUpstream.lock.json` and `packaging/hermes/patches`.
  The original license is retained in `packaging/legal/licenses/Hermes-MIT.txt`.
  See `BUILDING.md` for the distinction between this source snapshot and a
  verified complete rebuild of the sealed runtime.

* [Gera.Chess 1.1.1](https://www.nuget.org/packages/Gera.Chess/1.1.1)
  License: MIT, Copyright (c) 2024 Geras1mleo. Chess rules library, unmodified.
  Source revision: `a59d2ca4128aba565fe6dc9e8b73eefbb9d8bbd3` in
  https://github.com/Geras1mleo/Chess. The chess payload includes the full MIT notice.

* [Ardalis.SmartEnum 2.0.1](https://www.nuget.org/packages/Ardalis.SmartEnum/2.0.1)
  License: MIT, Copyright (c) 2017 Steve Smith. Dependency of Gera.Chess,
  unmodified. Source revision: `aeb2e99c731b581667aa1b4957e16d55ac5467ba`
  in https://github.com/ardalis/SmartEnum. Full MIT notice accompanies the payload.

* [Stockfish 16](https://github.com/official-stockfish/Stockfish/releases/tag/sf_16)
  License: GPL-3.0. The chess payload includes the unmodified generic
  Windows x86-64 executable, run as a separately owned local UCI process.
  `LilithChess/source/stockfish16-official.zip` preserves the official release,
  including full source, Makefile, AUTHORS and license. The matching NNUE file
  `nn-5af11540bbfe.nnue` accompanies it for offline source builds; place it inside
  the archive's extracted `stockfish/src` directory. The separate licenses folder
  also contains GPL-3.0 and AUTHORS. No game-time downloads or automatic updates.
  Engine binaries and matching source materials are separate distribution
  inputs; they are not copied into this public source snapshot.

* [Kenney Casino Audio 1.1](https://kenney.nl/assets/casino-audio)
  License: CC0 1.0. The Old Maid draw effect is adapted from `card-slide-1.ogg`.
  Attribution is optional; the source, conversion and hash notice is embedded
  in the plugin as `LilithTextInjector.Audio.KENNEY_CASINO_LICENSE.txt`.
  This public source includes the three CC0 PCM samples needed by the plugin
  under `src/LilithTextInjector/Assets/Audio`, with conversion details and the
  original license. There are no game-time audio downloads.

* [LilithCursorGuard](https://github.com/SmallYuanSY/LilithCursorGuard/tree/9343b847667733f3c28a3eb5e224e5a67a099aed)
  License: MIT, Copyright (c) 2026 SmallYuanSY. The local automatic guard derives
  its process-local IAT approach and foreground policy from this revision.
  Lilith's variant removes the option, bounds image reads, verifies the original
  pointer and makes installation atomic with protection-restoration checks.
  The full upstream notice is in `packaging/legal/LilithCursorGuard-MIT.txt`
  and embedded in the guard assembly.

* [BepInEx 6.0.0-be.780 IL2CPP](https://github.com/BepInEx/BepInEx)
  License: LGPL-2.1-or-later
  The relevant license documents and upstream project information are included with this package.

* [Il2CppInterop 1.5.3](https://github.com/BepInEx/Il2CppInterop) / [Cpp2IL 2022.1.0-pre-release.21](https://github.com/SamboyCoding/Cpp2IL)
  For the applicable licenses, copyright notices, and version information, please refer to the corresponding files in the `licenses` folder and the upstream repositories of the respective projects.

* [NAudio 2.2.1](https://github.com/naudio/NAudio)
  License: MIT
  The applicable license text and copyright notice are included with this package.

* [GPT-SoVITS commit be6a4f1e9d8a22d41b7d42c22df9d7ef36f225d2](https://github.com/RVC-Boss/GPT-SoVITS/commit/be6a4f1e9d8a22d41b7d42c22df9d7ef36f225d2)
  License: MIT
  The applicable license text and copyright notice are included with this package.

* [uv 0.11.14](https://github.com/astral-sh/uv/releases/tag/0.11.14)
  License: MIT OR Apache License 2.0
  Both license texts are included with this package. Users may choose either license in accordance with the upstream licensing terms.

* [Cua Driver 0.21.0](https://github.com/trycua/cua/releases/tag/cua-driver-rs-v0.21.0)
  License: MIT
  Copyright (c) 2025 Cua AI, Inc. The exact upstream MIT license text is included with this package as `licenses/CuaDriver-MIT.txt`.

* [edge-tts 7.2.7](https://github.com/rany2/edge-tts)
  License: LGPL-3.0
  The exact upstream license text is preserved in the packaged Python distribution at `edge_tts-7.2.7.dist-info/licenses/LICENSE`.

* [tabulate 0.9.0](https://github.com/astanin/python-tabulate)
  License: MIT
  The exact upstream license text is preserved in the packaged Python distribution at `tabulate-0.9.0.dist-info/LICENSE`.

* [PyTorch 2.6.0](https://github.com/pytorch/pytorch/releases/tag/v2.6.0) and [torchaudio 2.6.0](https://github.com/pytorch/audio/tree/v2.6.0)
  These components are downloaded and installed directly from upstream package sources during the optional AI voice installation process and are not prepackaged with the core MOD. Their applicable license terms, copyright notices, and third-party dependency information are governed by the files included with the versions actually installed and by the corresponding upstream releases.

* [FastAPI >= 0.115.2](https://github.com/fastapi/fastapi) / [Uvicorn](https://github.com/encode/uvicorn)

* [Transformers 4.43-4.50](https://github.com/huggingface/transformers), [ONNX Runtime](https://github.com/microsoft/onnxruntime), [librosa 0.10.2](https://github.com/librosa/librosa/releases/tag/0.10.2), [pyopenjtalk-prebuilt](https://github.com/tsukumijima/pyopenjtalk-prebuilt), and their respective dependencies

Some of the components listed above may be downloaded from their official or upstream sources through package management tools only when the user chooses to install specific optional features. This MOD does not claim ownership of any copyright in these third-party components.

The `licenses` folder contains the license terms, copyright notices, and other required notices for the major third-party components directly redistributed with this package. Where a component includes a `NOTICE`, `COPYING`, `COPYRIGHT`, `AUTHORS`, or other upstream notice file, such files shall be considered part of that component’s licensing information.

All names, trademarks, and logos of third-party components remain the property of their respective rights holders. The inclusion of a third-party project in this notice is solely intended to describe a technical dependency or interoperability relationship and does not imply any endorsement, sponsorship, approval, or partnership by the project’s authors, maintainers, or affiliated organizations beyond the rights granted under the applicable license.

Unless otherwise stated in the applicable license, third-party components are provided under their upstream license terms on an “AS IS” basis, without any express or implied warranty. The author of this MOD does not make any warranty on behalf of third-party authors regarding merchantability, fitness for a particular purpose, continued maintenance, compatibility, or non-infringement of third-party rights.

Any modification, packaging, configuration, or integration performed by this MOD does not alter the original license terms applicable to the relevant third-party component. If this package contains modified third-party files, the relevant modification information, source version, or patch details will be documented in the corresponding license files, source code repository, or changelog.

Where an applicable open-source license requires the provision of corresponding source code, modifications, build scripts, relinking information, or other materials, users may obtain them from the following location:

Source code / Corresponding source: https://github.com/mimimi6666/Lilith-AI-Mod
Version or commit identifier: 1.0.0-Beta.4
Contact: **[mimimi5206666@gmail.com]**

This notice applies only to software components and source code licensing. AI voice models, model weights, training data, reference audio, generated speech, and other content materials do not automatically become subject to the license of the open-source software used to create or operate them. The rights and usage restrictions applicable to such materials must be assessed separately according to their respective sources, licenses, and applicable law.

All rights relating to the game, characters, artwork, original dialogue, original voice recordings, voice performances, and other original content belong to their respective lawful rights holders.

This package is not authorized, approved, sponsored, or endorsed by the game developer, publisher, original voice actors, or any other rights holder associated with the original work. The licenses applicable to third-party open-source components do not grant permission to use game content, character assets, original recordings, voice models, or the voices of any performers.

If this notice conflicts with the official license terms included with any individual third-party component, the original text of that component’s official license shall prevail.
