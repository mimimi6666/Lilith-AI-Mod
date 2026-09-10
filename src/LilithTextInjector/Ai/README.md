# AI providers

This folder contains everything related to sending a chat turn to an AI provider and
turning its reply into something the rest of the mod can speak/display.

## Files

| File | Responsibility |
|---|---|
| `DialogueManagerUpdatePatch.Ai.Gemini.cs` | Builds the shared system prompt (persona, character lore, emotion guidance, time, weather, tool policy, language rules) and **dispatches** to the active provider. Also contains Gemini's own request/response handling and its desktop-tool (function-calling) loop. |
| `DialogueManagerUpdatePatch.Ai.Qwen.cs` | Qwen (Alibaba Model Studio / DashScope) request/response handling and its own desktop-tool loop. |
| `DialogueManagerUpdatePatch.Ai.OpenAiCompatible.cs` | One shared implementation for every provider that speaks the OpenAI `/chat/completions` wire format: OpenAI, DeepSeek, and **LocalAI** (any self-hosted server: Ollama, LM Studio, llama.cpp server, vLLM, text-generation-webui, ...). |
| `DialogueManagerUpdatePatch.Ai.Shared.cs` | Provider-agnostic helpers: `NormalizeAiProvider`, `GetActiveChatApiKey`, reply cleanup, bilingual (text/speech) reply parsing, emotion selection, native reaction lookup. |

Despite its name, `RequestGeminiAsync` in `Ai.Gemini.cs` is the actual entry point for
**every** provider — it builds the one shared system prompt, then near the bottom does:

```csharp
if (activeProvider == "Qwen") { await RequestQwenResponsesAsync(...); return; }
if (activeProvider != "Gemini") { await RequestOpenAiCompatibleAsync(activeProvider, ...); return; }
// ...otherwise continues with Gemini's own request.
```

This keeps prompt-building logic (persona, weather, time-of-day, tool policy, language
rules) in exactly one place instead of duplicated per provider.

## Already have a local AI model? Use it today

If your local server exposes an OpenAI-compatible `/chat/completions` endpoint — this is
true for **Ollama, LM Studio, llama.cpp server, vLLM, and text-generation-webui** out of
the box — you don't need to write any code. In
`BepInEx/config/community.lilith.textinjector.cfg`:

```ini
[AI]
Provider = LocalAI

[LocalAI]
ChatCompletionsUrl = http://127.0.0.1:11434/v1/chat/completions
Model = llama3.1
ApiKey =
```

`ApiKey` may be left empty; the request simply omits the `Authorization` header in that
case. Restart the game (or reload BepInEx config) and Lilith will talk to your local
model. Note that push-to-talk speech recognition (`F6`) and Gemini/Qwen-style desktop
tool calling are not wired up for `LocalAI` yet — text chat only, same level of support
OpenAI/DeepSeek currently have.

## Adding a provider with its own request/response format

If your target doesn't speak the OpenAI format (for example a provider with its own
streaming protocol, or a local inference server with a bespoke JSON schema), add a new
provider instead of stretching `RequestOpenAiCompatibleAsync`:

1. **Add config entries** in `Config/PluginConfig.cs` (endpoint, model, key/path, ...),
   following the `LocalAi*` entries as a template.
2. **Create `DialogueManagerUpdatePatch.Ai.<YourProvider>.cs`** in this folder with a
   `partial class DialogueManagerUpdatePatch` containing a
   `RequestYourProviderAsync(string systemInstruction, string userText, PoseContext poseContext, bool japaneseVoiceMode)`
   method. Use `Ai.Qwen.cs` as a template if you need multi-turn tool calling, or the much
   shorter `Ai.OpenAiCompatible.cs` if you just need a single request/response turn. Call
   `CompleteAiReply(rawReply, userText, poseContext, japaneseVoiceMode)` (in `Ai.Shared.cs`)
   once you have the model's raw text — that shared method handles bilingual parsing,
   memory, emotion selection, and speech synthesis for every provider.
3. **Register the name** in `NormalizeAiProvider` (`Ai.Shared.cs`) and add a case to
   `GetActiveChatApiKey` there too.
4. **Add one dispatch branch** in `RequestGeminiAsync` (`Ai.Gemini.cs`), next to the
   existing `Qwen`/OpenAI-compatible branches, calling your new method.

That's the entire surface area — nothing outside `Ai/` and `Config/PluginConfig.cs` needs
to change for a new text-chat provider.

## What is intentionally *not* provider-specific here

`Core/DialogueManagerUpdatePatch.Core.cs` (`SubmitAiInput`) only ever calls
`RequestGeminiAsync`; it does not know or care which provider ends up handling the
request. Voice synthesis (`Voice/DialogueManagerUpdatePatch.Tts.cs`), conversation memory
(`Memory/DialogueManagerUpdatePatch.Memory.cs`), and AI-generated notes
(`Memory/DialogueManagerUpdatePatch.Notes.cs`) are likewise provider-agnostic — they only
consume the final reply text, not anything provider-specific.
