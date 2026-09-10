# LilithTextInjector architecture

This project used to be a single ~6,600-line `Plugin.cs`. It has been split by
responsibility into the folders below. Nothing about the mod's behavior changed as part
of this split — it's the same two types (`Plugin` and `DialogueManagerUpdatePatch`),
just declared with `partial` across many files instead of one.

```
LilithTextInjector/
├─ Plugin.cs                 BepInEx entry point: bootstrap only (bind config, apply
│                             Harmony patches in isolated stages, run migrations).
├─ Config/
│  └─ PluginConfig.cs         Every ConfigEntry<T> field + BindConfig() + legacy
│                             mojibake-default migration. Add new settings here.
├─ Core/
│  ├─ DialogueManagerUpdatePatch.State.cs   All shared static state (fields) for the
│  │                                        DialogueManager.Update Harmony patch.
│  ├─ DialogueManagerUpdatePatch.Core.cs    The Postfix (main per-frame update loop)
│  │                                        and SubmitAiInput (chat entry point).
│  └─ DialogueManagerUpdatePatch.Models.cs  Small internal data/record classes shared
│                                            across the update patch (sessions, tool
│                                            batches, pose context, ...).
├─ Ai/                        Chat providers. See Ai/README.md — this is the folder to
│                             read before adding a new (e.g. local) AI model.
├─ Voice/                     Local GPT-SoVITS TTS, push-to-talk voice input (WASAPI
│                             capture, Qwen/Gemini transcription, DashScope realtime
│                             ASR), the local voice host process, and the in-game
│                             Chinese/Japanese voice-language UI hooks.
├─ Ui/                        Everything that creates or manipulates in-game UI: the
│                             text input bubble, rebindable key settings, the advanced-
│                             actions toggle, the API-key entry dialog, and interface-
│                             language helpers.
├─ PcControl/                 The reviewed, allowlisted computer actions: text-command
│                             parsing (screenshots, windows, media, clipboard, browser
│                             search, system status), the application launcher, Win32
│                             P/Invoke plumbing, local timers, and the Codex bridge
│                             status feature.
├─ Memory/                    Lightweight conversation memory and the AI-generated
│                             "notes" feature (scheduling, generation, persistence).
├─ Weather/                   Open-Meteo lookup and IP-based location detection.
├─ NativeVoicePack/           Recording/playing supplemental voice for native dialogue
│                             lines that ship with no official voice-over.
└─ HarmonyPatches/
   └─ OtherPatches.cs         The remaining small, self-contained Harmony patches that
                               don't warrant their own folder (tray localization, gift-
                               exchange API-key window hooks, typewriter/voice hooks).
```

## Why partial classes instead of new smaller types

`DialogueManagerUpdatePatch` is a single Harmony patch target
(`[HarmonyPatch(typeof(DialogueManager), "Update")]`) with a large amount of mutable
state (pending queues, UI element references, in-flight request flags, cached voice
settings, ...) that many features read and write every frame. Splitting it into
independent instantiable classes would require redesigning how that shared state is
owned and passed around — a much larger, riskier change with no working build/game
environment available to validate it against. Splitting by `partial class` file instead:

- gives every feature area its own file (the original ask),
- keeps 100% of the original logic and behavior identical,
- and is safe to review file-by-file, since each file is small enough to read in one
  sitting.

The one place a real interface-style extension point was worth adding is the AI provider
dispatch, because that's genuinely one clean seam (`RequestGeminiAsync`'s dispatch to
`RequestQwenResponsesAsync` / `RequestOpenAiCompatibleAsync`). See `Ai/README.md`.

## Adding a new feature area

Add a new folder and a new `DialogueManagerUpdatePatch.<Area>.cs` file (or a new
top-level type, if the feature doesn't need per-frame `Update` access) rather than
appending to an existing file. If it needs configuration, add the entries to
`Config/PluginConfig.cs` next to the most similar existing feature.
