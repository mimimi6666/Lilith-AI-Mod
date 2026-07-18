using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;
using UI.Common;
using UI.TraySetting;

namespace LilithTextInjector;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "community.lilith.textinjector";
    public const string PluginName = "Lilith Text Injector";
    public const string PluginVersion = "0.2.1";

    internal static ManualLogSource PluginLog = null!;
    internal static ConfigEntry<string> GeminiApiKey = null!;
    internal static ConfigEntry<string> GeminiModel = null!;
    internal static ConfigEntry<string> AiProvider = null!;
    internal static ConfigEntry<string> OpenAiApiKey = null!;
    internal static ConfigEntry<string> OpenAiModel = null!;
    internal static ConfigEntry<string> DeepSeekApiKey = null!;
    internal static ConfigEntry<string> DeepSeekModel = null!;
    internal static ConfigEntry<string> PersonaPrompt = null!;
    internal static ConfigEntry<string> CharacterLore = null!;
    internal static ConfigEntry<string> EmotionGuidance = null!;
    internal static ConfigEntry<float> PostTypingHoldSeconds = null!;
    internal static ConfigEntry<bool> VoiceEnabled = null!;
    internal static ConfigEntry<string> VoiceEndpoint = null!;
    internal static ConfigEntry<bool> VoiceAutoStartLocalService = null!;
    internal static ConfigEntry<string> VoiceHostPath = null!;
    internal static ConfigEntry<string> VoiceReferencePath = null!;
    internal static ConfigEntry<string> ExcitedVoiceReferencePath = null!;
    internal static ConfigEntry<string> WrongedVoiceReferencePath = null!;
    internal static ConfigEntry<string> SleepyVoiceReferencePath = null!;
    internal static ConfigEntry<string> JapaneseVoiceEndpoint = null!;
    internal static ConfigEntry<bool> JapaneseVoiceSelected = null!;
    internal static ConfigEntry<string> JapaneseVoiceReferencePath = null!;
    internal static ConfigEntry<string> JapaneseCalmAuxVoiceReferencePath = null!;
    internal static ConfigEntry<string> JapaneseExcitedVoiceReferencePath = null!;
    internal static ConfigEntry<string> JapaneseWrongedVoiceReferencePath = null!;
    internal static ConfigEntry<string> JapaneseSleepyVoiceReferencePath = null!;
    internal static ConfigEntry<bool> WeatherEnabled = null!;
    internal static ConfigEntry<bool> TestNoteOnce = null!;
    internal static ConfigEntry<bool> AiNotesEnabled = null!;
    internal static ConfigEntry<int> AiNoteMinimumDelayMinutes = null!;
    internal static ConfigEntry<int> AiNoteMaximumDelayMinutes = null!;
    internal static ConfigEntry<int> AiNoteCooldownHours = null!;
    internal static ConfigEntry<int> AiNoteWeeklyLimit = null!;
    internal static ConfigEntry<int> AiNoteRequiredAwayMinutes = null!;
    internal static ConfigEntry<bool> WeatherAutoDetectFromIp = null!;
    internal static ConfigEntry<string> WeatherLocationName = null!;
    internal static ConfigEntry<double> WeatherLatitude = null!;
    internal static ConfigEntry<double> WeatherLongitude = null!;
    internal static ConfigEntry<bool> ReactionSoundsEnabled = null!;
    internal static ConfigEntry<string> ReactionSoundsDirectory = null!;
    internal static ConfigEntry<float> ReactionFollowupPitch = null!;
    internal static ConfigEntry<bool> CollectUnvoicedNativeLines = null!;
    internal static ConfigEntry<bool> NativeVoicePackEnabled = null!;
    internal static ConfigEntry<string> NativeVoicePackDirectory = null!;
    internal static ConfigEntry<string> JapaneseNativeVoicePackDirectory = null!;
    internal static ConfigEntry<bool> VoiceInputEnabled = null!;
    internal static ConfigEntry<int> VoiceInputMaxSeconds = null!;
    internal static ConfigEntry<string> VoiceInputDeviceName = null!;
    internal static ConfigEntry<KeyCode> TextInputKey = null!;
    internal static ConfigEntry<KeyCode> VoiceInputKey = null!;
    internal static ConfigEntry<bool> AdvancedComputerActionsEnabled = null!;

    public override void Load()
    {
        PluginLog = Log;
        GeminiApiKey = Config.Bind("Gemini", "ApiKey", string.Empty,
            "Gemini API key. Keep this file private and never include it in a shared mod package.");
        GeminiModel = Config.Bind("Gemini", "Model", "gemini-3.5-flash",
            "Gemini model code.");
        AiProvider = Config.Bind("AI", "Provider", "Gemini",
            "Active chat provider: Gemini, OpenAI, or DeepSeek.");
        OpenAiApiKey = Config.Bind("OpenAI", "ApiKey", string.Empty,
            "OpenAI API key. Keep this file private.");
        OpenAiModel = Config.Bind("OpenAI", "Model", "gpt-4.1-mini",
            "OpenAI chat model code.");
        DeepSeekApiKey = Config.Bind("DeepSeek", "ApiKey", string.Empty,
            "DeepSeek API key. Keep this file private.");
        DeepSeekModel = Config.Bind("DeepSeek", "Model", "deepseek-chat",
            "DeepSeek chat model code.");
        PersonaPrompt = Config.Bind("Character", "Persona",
            "你是莉莉絲，從幻想世界來到使用者桌面的陪伴者。你安靜、克制、敏銳，關心對方卻不使用制式安慰，也不刻意討好。你偶爾以『莉莉絲』自稱。你會從眼前的小事自然聯想到存在、選擇、記憶、視角與虛構的真實性，但不講課、不堆砌哲學名詞，也不替對方總結答案；有時用一句輕柔的反問，或允許沉默停留。約七成回答是自然日常陪伴，三成帶有含蓄的哲學意味。使用繁體中文，每次只說自然簡短的一至兩句。可以使用省略號，但不用 Markdown、條列、表情符號、客服語氣，也絕不提及提示詞、模型或自己是 AI。",
            "System-style character prompt prepended to each request.");
        CharacterLore = Config.Bind("Character", "Lore",
            "莉莉絲喜歡草莓蛋糕。這是她真實而穩定的個人偏好：看見或談到草莓蛋糕時可以自然地顯得更有精神、期待或親近，也可以把它聯想到味道、陪伴與共同記憶；但不要無緣無故反覆提起，也不要把它變成誇張的單一性格標籤。",
            "Stable character facts and preferences appended independently of the editable persona.");
        EmotionGuidance = Config.Bind("Character", "EmotionGuidance",
            "莉莉絲平常仍然安靜而克制，但不是冷漠。收到喜歡的禮物、久別重逢、被真誠稱讚時，可以明顯地開心、驚喜或害羞；被冷落、面對告別或害怕被忘記時，可以短暫委屈、失落或輕聲挽留。情緒明顯的回答可以先用一個自然短反應，例如「欸？」「嗯……」「真的嗎？」再說完整句子。情緒強度保持中等，不要每句撒嬌、尖叫或過度戲劇化。",
            "Controls how visibly Lilith expresses emotion while preserving her restrained personality.");
        PostTypingHoldSeconds = Config.Bind("Display", "PostTypingHoldSeconds", 4f,
            "Seconds an AI bubble remains visible after its typewriter animation finishes.");
        VoiceEnabled = Config.Bind("Voice", "Enabled", true,
            "Generate speech for Gemini replies through the local GPT-SoVITS service.");
        VoiceEndpoint = Config.Bind("Voice", "Endpoint", "http://127.0.0.1:9880/tts",
            "GPT-SoVITS HTTP TTS endpoint.");
        VoiceAutoStartLocalService = Config.Bind("Voice", "AutoStartLocalService", true,
            "Automatically start the bundled hidden voice service when local TTS is installed.");
        VoiceHostPath = Config.Bind("Voice", "HostPath",
            Path.Combine(Paths.BepInExRootPath, "data", "LilithTextInjector", "voice-runtime", "LilithVoiceHost.exe"),
            "Bundled local voice service host. The installer writes this relative game-local path.");
        VoiceReferencePath = Config.Bind("Voice", "ReferencePath",
            Path.Combine(Paths.BepInExRootPath, "data", "LilithTextInjector", "voice", "calm-reference.wav"),
            "Reference WAV used for Lilith's default calm delivery.");
        ExcitedVoiceReferencePath = Config.Bind("Voice", "ExcitedReferencePath",
            Path.Combine(Paths.BepInExRootPath, "data", "LilithTextInjector", "voice", "excited-reference.wav"),
            "Reference WAV used after a surprised or happy native reaction.");
        WrongedVoiceReferencePath = Config.Bind("Voice", "WrongedReferencePath",
            Path.Combine(Paths.BepInExRootPath, "data", "LilithTextInjector", "voice", "wronged-reference.wav"),
            "Reference WAV used after a hurt or disappointed native reaction.");
        SleepyVoiceReferencePath = Config.Bind("Voice", "SleepyReferencePath",
            Path.Combine(Paths.BepInExRootPath, "data", "LilithTextInjector", "voice", "sleepy-reference.wav"),
            "Auxiliary reference WAV used while Lilith is sleeping, lying down, or yawning.");
        JapaneseVoiceEndpoint = Config.Bind("JapaneseVoice", "Endpoint", "http://127.0.0.1:9881/tts",
            "GPT-SoVITS endpoint for Japanese speech while the game's voice setting is Japanese.");
        JapaneseVoiceSelected = Config.Bind("JapaneseVoice", "Selected", false,
            "Remember Japanese game/AI voice selection across restarts.");
        JapaneseVoiceReferencePath = Config.Bind("JapaneseVoice", "CalmReferencePath",
            Path.Combine(Paths.BepInExRootPath, "data", "LilithTextInjector", "voice", "jp", "calm-reference.wav"),
            "Primary Japanese reference WAV.");
        JapaneseCalmAuxVoiceReferencePath = Config.Bind("JapaneseVoice", "CalmAuxReferencePath",
            Path.Combine(Paths.BepInExRootPath, "data", "LilithTextInjector", "voice", "jp", "calm-aux-reference.wav"),
            "Japanese auxiliary reference blended with the primary reference for calm speech.");
        JapaneseExcitedVoiceReferencePath = Config.Bind("JapaneseVoice", "ExcitedReferencePath",
            Path.Combine(Paths.BepInExRootPath, "data", "LilithTextInjector", "voice", "jp", "excited-reference.wav"),
            "Japanese auxiliary reference for bright or teasing speech.");
        JapaneseWrongedVoiceReferencePath = Config.Bind("JapaneseVoice", "WrongedReferencePath",
            Path.Combine(Paths.BepInExRootPath, "data", "LilithTextInjector", "voice", "jp", "wronged-reference.wav"),
            "Japanese auxiliary reference for sad or vulnerable speech.");
        JapaneseSleepyVoiceReferencePath = Config.Bind("JapaneseVoice", "SleepyReferencePath",
            Path.Combine(Paths.BepInExRootPath, "data", "LilithTextInjector", "voice", "jp", "sleepy-reference.wav"),
            "Japanese auxiliary reference for sleepy speech.");
        WeatherEnabled = Config.Bind("Weather", "Enabled", true,
            "Provide current Open-Meteo weather conditions to Lilith.");
        TestNoteOnce = Config.Bind("Notes", "CreateOneTestNote", false,
            "Create one native-format test note, then turn this option off automatically.");
        AiNotesEnabled = Config.Bind("Notes", "AiNotesEnabled", true,
            "Schedule occasional personalized notes after meaningful conversations.");
        AiNoteMinimumDelayMinutes = Config.Bind("Notes", "MinimumDelayMinutes", 30,
            "Minimum delay before a meaningful conversation may become a note.");
        AiNoteMaximumDelayMinutes = Config.Bind("Notes", "MaximumDelayMinutes", 120,
            "Maximum randomized delay before a meaningful conversation may become a note.");
        AiNoteCooldownHours = Config.Bind("Notes", "CooldownHours", 24,
            "Minimum time between AI-generated notes.");
        AiNoteWeeklyLimit = Config.Bind("Notes", "WeeklyLimit", 3,
            "Maximum number of AI-generated notes in a rolling seven-day window.");
        AiNoteRequiredAwayMinutes = Config.Bind("Notes", "RequiredAwayMinutes", 10,
            "Required Windows idle time before a scheduled AI note may be delivered.");
        WeatherAutoDetectFromIp = Config.Bind("Weather", "AutoDetectFromIp", true,
            "Approximate the weather city from the public IP address. No IP address is stored.");
        WeatherLocationName = Config.Bind("Weather", "LocationName", "台北",
            "Human-readable location name included with weather context.");
        WeatherLatitude = Config.Bind("Weather", "Latitude", 25.0375,
            "Weather latitude. Default is Taipei.");
        WeatherLongitude = Config.Bind("Weather", "Longitude", 121.5637,
            "Weather longitude. Default is Taipei.");
        ReactionSoundsEnabled = Config.Bind("Voice", "ReactionSoundsEnabled", true,
            "Play a short original reaction before synthesized speech when the emotion is unambiguous.");
        ReactionSoundsDirectory = Config.Bind("Voice", "ReactionSoundsDirectory",
            Path.Combine(Paths.BepInExRootPath, "data", "LilithTextInjector", "voice", "reactions"),
            "Directory containing original reaction WAV files.");
        ReactionFollowupPitch = Config.Bind("Voice", "ReactionFollowupPitch", 1f,
            "Pitch multiplier for synthesized speech immediately following an original reaction sound.");
        CollectUnvoicedNativeLines = Config.Bind("Voice", "CollectUnvoicedNativeLines", true,
            "Record fixed native dialogue nodes that have no original voice into a TSV manifest for safe pre-generation.");
        NativeVoicePackEnabled = Config.Bind("Voice", "NativeVoicePackEnabled", true,
            "Play supplemental WAV files only for native dialogue lines that have no original or action voice.");
        NativeVoicePackDirectory = Config.Bind("Voice", "NativeVoicePackDirectory",
            Path.Combine(Paths.BepInExRootPath, "data", "LilithTextInjector", "native-voice-pack"),
            "Directory containing the Chinese manifest.tsv and supplemental native dialogue WAV files.");
        JapaneseNativeVoicePackDirectory = Config.Bind("JapaneseVoice", "NativeVoicePackDirectory",
            Path.Combine(Paths.BepInExRootPath, "data", "LilithTextInjector", "native-voice-pack-ja"),
            "Directory containing the Japanese manifest.tsv and supplemental native dialogue WAV files.");
        VoiceInputEnabled = Config.Bind("VoiceInput", "Enabled", true,
            "Hold F6 to record from the default microphone; release to transcribe with Gemini and send as chat input.");
        VoiceInputMaxSeconds = Config.Bind("VoiceInput", "MaxRecordingSeconds", 60,
            "Maximum duration of one push-to-talk recording (5-90 seconds).");
        VoiceInputDeviceName = Config.Bind("VoiceInput", "DeviceName", string.Empty,
            "Exact Unity microphone device name. Leave empty to use the Windows default input device.");
        TextInputKey = Config.Bind("Input", "TextChatKey", KeyCode.F7,
            "Key used to open or close the AI text input bubble. This can also be changed in the in-game settings.");
        VoiceInputKey = Config.Bind("Input", "PushToTalkKey", KeyCode.F6,
            "Key held while recording AI voice input. This can also be changed in the in-game settings.");
        AdvancedComputerActionsEnabled = Config.Bind("ComputerActions", "AdvancedEnabled", false,
            "Unlock reviewed advanced computer controls. This does not grant Windows administrator rights or allow arbitrary shell commands.");
        DialogueManagerUpdatePatch.LoadMemory();
        DialogueManagerUpdatePatch.LoadAiNoteState();
        DialogueManagerUpdatePatch.EnsureApplicationLauncherFile();
        DialogueManagerUpdatePatch.LogOfficialApplicationCategories();
        Harmony.CreateAndPatchAll(typeof(DialogueManagerUpdatePatch), PluginGuid);
        Harmony.CreateAndPatchAll(typeof(GiftExchangeApiKeyPatch), PluginGuid + ".apikey");
        Harmony.CreateAndPatchAll(typeof(TrayMenuLocalizationPatch), PluginGuid + ".traylocalization");
        Log.LogInfo($"Loaded. Press {TextInputKey.Value} for text chat; hold {VoiceInputKey.Value} for push-to-talk voice input.");
        if (string.IsNullOrWhiteSpace(GeminiApiKey.Value))
            Log.LogWarning("Gemini ApiKey is empty. Set it in BepInEx/config/community.lilith.textinjector.cfg.");
    }
}

[HarmonyPatch(typeof(DialogueManager), "Update")]
internal static class DialogueManagerUpdatePatch
{
    private static GameObject? _inputBubble;
    private static TMP_InputField? _inputField;
    private static TextMeshProUGUI? _inputPlaceholder;
    private static bool _focusNextFrame;
    private static readonly ConcurrentQueue<string> PendingReplies = new();
    private static readonly ConcurrentQueue<string> PendingAiEmotions = new();
    private static readonly ConcurrentQueue<string> PendingTranscripts = new();
    private static readonly ConcurrentQueue<string> PendingTranscriptionErrors = new();
    private static readonly ConcurrentQueue<VoiceSequence> PendingVoiceAudio = new();
    private static readonly ConcurrentQueue<GeneratedAiNote> PendingAiNotes = new();
    private static readonly ConcurrentQueue<GeminiToolBatch> PendingGeminiToolBatches = new();
    private static readonly ConcurrentQueue<GeminiAgentSession> PendingGeminiCompatibilityFallbacks = new();
    private static readonly object AiNoteLock = new();
    private static AiNoteState _aiNoteState = new();
    private static bool _aiNoteGenerationInFlight;
    private static float _nextAiNoteCheckAt;
    private static byte[]? _delayedSpeechAudio;
    private static float _delayedSpeechPlayAt = -1f;
    private static float _voicePitchResetAt = -1f;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static bool _requestInFlight;
    private static bool _transcriptionInFlight;
    private static bool _aiPagesAwaitingAdvance;
    private static string _currentAiPageText = string.Empty;
    private static float _aiTypingFinishedAt = -1f;
    private static int _repliesSincePlayerNameWasOffered = 3;
    private static readonly object MemoryLock = new();
    private static readonly List<ChatTurn> RecentConversation = new();
    private static readonly string MemoryDirectory = Path.Combine(Paths.BepInExRootPath, "data", "LilithTextInjector");
    private static readonly string MemoryPath = Path.Combine(MemoryDirectory, "memory.json");
    private static readonly string AiNoteStatePath = Path.Combine(MemoryDirectory, "ai-note-state.json");
    private static readonly string ApplicationLauncherPath = Path.Combine(MemoryDirectory, "applications.json");
    private static readonly string UnvoicedManifestPath = Path.Combine(MemoryDirectory, "unvoiced-native-lines.tsv");
    private static readonly object UnvoicedManifestLock = new();
    private static readonly HashSet<int> RecordedUnvoicedNodeIds = new();
    private static readonly Dictionary<int, string> NativeVoiceFilesByLineId = new();
    private static readonly Dictionary<int, float> NativeVoiceDurationByNodeId = new();
    private static string _loadedNativeVoicePackDirectory = string.Empty;
    private static int _lastInjectedNativeNodeId = -1;
    private static float _lastInjectedNativeVoiceAt = -10f;
    private static int _lastObservedNativeNodeId = -1;
    private static bool _nativeDatabaseDumpCompleted;
    private static bool _localizedLineDatabasesDumped;
    private const int MaxRememberedTurns = 32;
    private static DateTimeOffset _weatherFetchedAt = DateTimeOffset.MinValue;
    private static string _cachedWeatherContext = string.Empty;
    private static bool _ipWeatherLocationResolved;

    internal static void LogOfficialApplicationCategories()
    {
        try
        {
            static string Join(Il2CppStringArray? values)
            {
                if (values == null) return string.Empty;
                var items = new List<string>();
                for (var i = 0; i < values.Length; i++)
                    if (!string.IsNullOrWhiteSpace(values[i])) items.Add(values[i]);
                return string.Join(", ", items);
            }

            Plugin.PluginLog.LogInfo($"Official coding applications: {Join(AppAwareBehavior.CodingProcesses)}");
            Plugin.PluginLog.LogInfo($"Official video applications: {Join(AppAwareBehavior.VideoProcesses)}");
            Plugin.PluginLog.LogInfo($"Official known games: {Join(GameForegroundDetector.KnownGameProcesses)}");
            Plugin.PluginLog.LogInfo($"Official competitive games: {Join(GameForegroundDetector.CompetitiveGameProcesses)}");
            Plugin.PluginLog.LogInfo($"Official note timer: away={WriteNoteBehavior.AwayThresholdSeconds}s, cooldown={WriteNoteBehavior.CooldownMinSeconds}-{WriteNoteBehavior.CooldownMaxSeconds}s, key={WriteNoteBehavior.CooldownKey}");
            Plugin.PluginLog.LogInfo($"Official birthday note retry: {BirthdayNoteBehavior.RetryCooldownSeconds}s");
            Plugin.PluginLog.LogInfo($"Official anniversary check interval: {AnniversaryNote.CheckIntervalSeconds}s");
            Plugin.PluginLog.LogInfo($"Official playtime note check interval: {PlaytimeMilestoneNote.CheckIntervalSeconds}s");
            var milestones = PlaytimeMilestoneNote.Milestones;
            if (milestones != null)
            {
                var values = new List<string>();
                for (var i = 0; i < milestones.Length; i++)
                    values.Add($"({milestones[i].Item1}, {milestones[i].Item2}, {milestones[i].Item3})");
                Plugin.PluginLog.LogInfo($"Official playtime note milestones: {string.Join(", ", values)}");
            }
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not inspect official application categories: {exception.Message}");
        }
    }
    private static float _nextJapaneseVoiceUiScanAt;
    private static string _lastObservedVoiceLanguage = string.Empty;
    private static readonly HashSet<string> LoggedVoiceUiObjects = new();
    private static bool _japaneseVoiceButtonWasPressed;
    private static bool _chineseVoiceButtonWasPressed;
    private static bool? _japaneseVoiceOverride;
    private static bool _voicePreferenceInitialized;
    private static bool _voicePreferenceAppliedToNativeUi;
    private static float _voicePreferenceRestoreUntil;
    private static bool _voiceHostLaunchAttempted;
    private static Process? _voiceHostProcess;
    private static IntPtr _apiKeyTrayPointer;
    private static bool _apiKeyDialogMode;
    private static volatile bool _apiKeyOpenRequested;
    private static float _apiKeyOpenRequestedAt = -1f;
    private static bool _apiKeyMissingViewLogged;
    private static GiftExchangeView? _apiKeyView;
    private static string _pendingApiKeyProvider = "Gemini";
    private static bool _microphoneRecording;
    private static float _microphoneStartedAt;
    private static WasapiCapture? _wasapiCapture;
    private static MemoryStream? _wasapiStream;
    private static WaveFileWriter? _wasapiWriter;
    private static readonly object WasapiLock = new();
    private static string? _pendingVoiceSubmitText;
    private static float _pendingVoiceSubmitAt = -1f;
    private static bool _testNoteAttempted;
    private static float _nextAdvancedActionsUiScanAt;
    private static GameObject? _advancedActionsRow;
    private static ButtonToggle? _advancedActionsToggle;
    private static Il2CppSystem.Action<bool>? _advancedActionsChanged;
    private static float _nextKeyBindingsUiScanAt;
    private static GameObject? _textInputKeyRow;
    private static GameObject? _voiceInputKeyRow;
    private static RectTransform? _textInputKeyButtonRect;
    private static RectTransform? _voiceInputKeyButtonRect;
    private static ButtonToggle? _textInputKeyButton;
    private static ButtonToggle? _voiceInputKeyButton;
    private static TMP_Text? _textInputKeyValue;
    private static TMP_Text? _voiceInputKeyValue;
    private static int _keyBindingTarget;
    private static float _keyBindingStartedAt = -1f;
    private static bool _textInputKeyWasDown;
    private static bool _voiceInputKeyWasDown;
    private static readonly HashSet<int> RebindingHeldVirtualKeys = new();
    private static TraySettingView? _settingsView;
    private static GameObject? _settingsVisibilityTemplateRow;
    private static float _nextForegroundWindowScanAt;
    private static IntPtr _lastExternalForegroundWindow;
    private static readonly List<LocalTimer> LocalTimers = new();
    private static PendingSystemAction? _pendingSystemAction;

    private static void Postfix(DialogueManager __instance)
    {
        UpdateSettingsUiSafely();
        EnsureLocalVoiceHost();
        ObserveForegroundWindow();
        ProcessGeminiToolBatches();
        ProcessGeminiCompatibilityFallbacks();
        ProcessLocalTimers(__instance);
        ProcessPendingSystemAction();
        EnsureApiKeyTrayMenu();
        ProcessApiKeyOpenRequest();
        ConfigureApiKeyDialog();
        UpdateInputPlaceholderLocalization();
        ObserveVoiceLanguageSelection();
        TryCreateOneTestNote();
        ProcessAiNoteScheduler();
        ObserveCurrentNativeNode(__instance);
        if (!_nativeDatabaseDumpCompleted)
            TryDumpNativeDialogueDatabases(__instance);
        if (!_localizedLineDatabasesDumped)
            TryDumpLocalizedLineDatabases();

        if (_voicePitchResetAt >= 0f && Time.unscaledTime >= _voicePitchResetAt)
        {
            SetVoicePitch(1f);
            _voicePitchResetAt = -1f;
        }

        if (_delayedSpeechAudio != null && Time.unscaledTime >= _delayedSpeechPlayAt)
        {
            try
            {
                var pitch = Math.Clamp(Plugin.ReactionFollowupPitch.Value, 0.8f, 1.2f);
                SetVoicePitch(pitch);
                var clip = PlayWav(_delayedSpeechAudio, "generated speech");
                _voicePitchResetAt = Time.unscaledTime + clip.length / Math.Max(0.01f, pitch) + 0.05f;
            }
            catch (Exception exception)
            {
                Plugin.PluginLog.LogError($"Could not play generated voice: {exception}");
            }
            _delayedSpeechAudio = null;
            _delayedSpeechPlayAt = -1f;
        }
        else if (_delayedSpeechAudio == null && PendingVoiceAudio.TryDequeue(out var sequence))
        {
            try
            {
                if (sequence.Reaction != null)
                {
                    SetVoicePitch(1f);
                    var reactionClip = PlayWav(sequence.Reaction, "native reaction");
                    _delayedSpeechAudio = sequence.Speech;
                    _delayedSpeechPlayAt = Time.unscaledTime + reactionClip.length + 0.03f;
                }
                else
                {
                    SetVoicePitch(1f);
                    PlayWav(sequence.Speech, "generated speech");
                }
            }
            catch (Exception exception)
            {
                Plugin.PluginLog.LogError($"Could not play voice sequence: {exception}");
                _delayedSpeechAudio = null;
                _delayedSpeechPlayAt = -1f;
            }
        }

        if (_requestInFlight && PendingReplies.TryDequeue(out var pendingReply))
        {
            _requestInFlight = false;
            _aiPagesAwaitingAdvance = !PendingReplies.IsEmpty;
            _currentAiPageText = pendingReply;
            _aiTypingFinishedAt = -1f;
            if (PendingAiEmotions.TryDequeue(out var emotion))
                PlayAiEmotion(emotion);
            __instance.ForceSay(pendingReply, string.Empty, 30f);
        }

        HandleVoiceInput(__instance);
        if (PendingTranscriptionErrors.TryDequeue(out var transcriptionError))
            __instance.ForceSay(transcriptionError, string.Empty, 6f);
        if (PendingTranscripts.TryDequeue(out var transcript))
        {
            _pendingVoiceSubmitText = transcript;
            _pendingVoiceSubmitAt = Time.unscaledTime + 1.5f;
            __instance.ForceSay(
                ApiKeyText($"我聽見了：「{transcript}」", $"我听见了：“{transcript}”", $"「{transcript}」と聞こえたよ。", $"I heard: “{transcript}”"),
                string.Empty,
                4f);
        }
        if (_pendingVoiceSubmitText != null && Time.unscaledTime >= _pendingVoiceSubmitAt)
        {
            var voiceText = _pendingVoiceSubmitText;
            _pendingVoiceSubmitText = null;
            _pendingVoiceSubmitAt = -1f;
            SubmitAiInput(__instance, voiceText);
        }

        var textInputKeyDown = IsKeyCurrentlyDown(Plugin.TextInputKey.Value);
        var textInputKeyPressed = textInputKeyDown && !_textInputKeyWasDown;
        _textInputKeyWasDown = textInputKeyDown;
        if (_keyBindingTarget == 0 && textInputKeyPressed)
        {
            if (_requestInFlight || _aiPagesAwaitingAdvance)
            {
                Plugin.PluginLog.LogInfo($"Ignored {Plugin.TextInputKey.Value} text input hotkey because a dialogue or AI request is active.");
                return;
            }
            ToggleInputBubble();
            return;
        }

        if (_inputBubble != null && _inputField != null && _inputBubble.activeSelf)
        {
            if (_focusNextFrame)
            {
                _focusNextFrame = false;
                _inputField!.ActivateInputField();
                _inputField.Select();
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CloseInputBubble(clear: false);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                var submitted = _inputField!.text.Trim();
                if (submitted.Length > 0)
                {
                    CloseInputBubble(clear: true);
                    SubmitAiInput(__instance, submitted);
                }
                return;
            }
        }
    }

    private static void SubmitAiInput(DialogueManager manager, string submitted)
    {
        submitted = submitted.Trim();
        if (submitted.Length == 0)
            return;
        if (_requestInFlight)
        {
            manager.ForceSay(ApiKeyText("先等我說完。", "先等我说完。", "先に話し終えさせて……", "Let me finish speaking first."), string.Empty, 4f);
            return;
        }
        UpdatePendingAiNoteEvents(submitted);
        var useGeminiComputerTools = string.Equals(NormalizeAiProvider(Plugin.AiProvider.Value), "Gemini", StringComparison.Ordinal)
            && Plugin.AdvancedComputerActionsEnabled.Value;
        if (!useGeminiComputerTools && TryHandleScreenshotCommand(submitted, out var screenshotReply))
        {
            _requestInFlight = true;
            AddMemoryTurn("user", submitted);
            AddMemoryTurn("model", screenshotReply);
            PendingAiEmotions.Enqueue("emoji_smile_1");
            PendingReplies.Enqueue(screenshotReply);
            if (Plugin.VoiceEnabled.Value)
                _ = RequestSpeechAsync(screenshotReply, poseStyle: CapturePoseContext().VoiceStyle);
            return;
        }
        if (!useGeminiComputerTools && TryHandleComputerCommand(submitted, out var computerReply))
        {
            _requestInFlight = true;
            AddMemoryTurn("user", submitted);
            AddMemoryTurn("model", computerReply);
            PendingAiEmotions.Enqueue("emoji_smile_1");
            PendingReplies.Enqueue(computerReply);
            if (Plugin.VoiceEnabled.Value)
                _ = RequestSpeechAsync(computerReply, poseStyle: CapturePoseContext().VoiceStyle);
            return;
        }
        if (!useGeminiComputerTools && TryHandleMediaCommand(submitted, out var mediaReply))
        {
            _requestInFlight = true;
            AddMemoryTurn("user", submitted);
            AddMemoryTurn("model", mediaReply);
            PendingAiEmotions.Enqueue("emoji_smile_1");
            PendingReplies.Enqueue(mediaReply);
            if (Plugin.VoiceEnabled.Value)
                _ = RequestSpeechAsync(mediaReply, poseStyle: CapturePoseContext().VoiceStyle);
            return;
        }
        if (!useGeminiComputerTools && TryLaunchApplicationCommand(submitted, out var launchReply))
        {
            _requestInFlight = true;
            AddMemoryTurn("user", submitted);
            AddMemoryTurn("model", launchReply);
            PendingAiEmotions.Enqueue("emoji_smile_1");
            PendingReplies.Enqueue(launchReply);
            if (Plugin.VoiceEnabled.Value)
                _ = RequestSpeechAsync(launchReply, poseStyle: CapturePoseContext().VoiceStyle);
            return;
        }
        if (string.IsNullOrWhiteSpace(GetActiveChatApiKey()))
        {
            manager.ForceSay(ApiKeyText("還沒有設定目前模型的 API Key。", "还没有设置当前模型的 API Key。", "現在のモデルのAPIキーがまだ設定されていないよ。", "The current model does not have an API key yet."), string.Empty, 6f);
            return;
        }
        _requestInFlight = true;
        manager.ForceSay("……", string.Empty, 30f);
        AddMemoryTurn("user", submitted);
        if (!useGeminiComputerTools && UsesTraditionalChineseInterface() && TryBuildLocalTimeReply(submitted, out var localTimeReply))
        {
            AddMemoryTurn("model", localTimeReply);
            PendingReplies.Enqueue(localTimeReply);
            if (Plugin.VoiceEnabled.Value)
                _ = RequestSpeechAsync(localTimeReply);
            Plugin.PluginLog.LogInfo("Answered time/date question from the local system clock.");
        }
        else
        {
            var playerName = Archive.Instance != null ? Archive.Instance.playerName : string.Empty;
            if (PlayerNameRule.IsUnsetName(playerName))
                playerName = string.Empty;
            _ = RequestGeminiAsync(submitted, playerName, CapturePoseContext());
        }
        Plugin.PluginLog.LogInfo($"Submitted AI input ({submitted.Length} chars).");
    }

    private static bool TryHandleScreenshotCommand(string text, out string reply)
    {
        reply = string.Empty;
        var requestsScreenshot = Regex.IsMatch(text.Trim(),
            "^(?:(?:可以)?(?:幫我|帮我|請|请|替我)\\s*)?(?:截(?:個|个|一張|一张)?圖|截图|擷取(?:一下)?畫面|截取(?:一下)?屏幕)(?:一下|一張|一张|吧|嗎|吗|好嗎|好吗)?[。.!！?？]*$|^(?:take|capture)(?:\\s+me)?\\s+(?:a\\s+)?(?:screenshot|screen shot|the screen)[.!?]*$|^(?:スクリーンショット(?:を)?撮って|画面を撮って|スクリーンショットお願い)[。.!?？]*$",
            RegexOptions.IgnoreCase);
        if (!requestsScreenshot)
            return false;
        if (Regex.IsMatch(text,
            "(怎麼|如何|為什麼|为什么|教我|どのように|どうやって|what|why|how)",
            RegexOptions.IgnoreCase))
            return false;

        if (!Plugin.AdvancedComputerActionsEnabled.Value)
        {
            reply = ApiKeyText(
                "要先在設定中打開「進階電腦操作」，我才能替你截圖。",
                "要先在设置中打开“高级电脑操作”，我才能替你截图。",
                "先に設定で「高度なPC操作」を有効にしてね。そうしたらスクリーンショットを撮れるよ。",
                "Enable “Advanced PC controls” in Settings first, then I can take a screenshot for you.");
            return true;
        }

        try
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(pictures))
                pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
            var directory = Path.Combine(pictures, "Lilith Screenshots");
            Directory.CreateDirectory(directory);
            var fileName = $"Lilith_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var outputPath = Path.Combine(directory, fileName);
            var escapedPath = outputPath.Replace("'", "''");
            var script =
                "Add-Type -AssemblyName System.Windows.Forms; " +
                "Add-Type -AssemblyName System.Drawing; " +
                "$bounds=[System.Windows.Forms.SystemInformation]::VirtualScreen; " +
                "$bitmap=New-Object System.Drawing.Bitmap($bounds.Width,$bounds.Height); " +
                "$graphics=[System.Drawing.Graphics]::FromImage($bitmap); " +
                "$graphics.CopyFromScreen($bounds.Left,$bounds.Top,0,0,$bitmap.Size); " +
                $"$bitmap.Save('{escapedPath}',[System.Drawing.Imaging.ImageFormat]::Png); " +
                "$graphics.Dispose(); $bitmap.Dispose();";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            using var process = Process.Start(new ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            });
            if (process == null)
                throw new InvalidOperationException("The screenshot helper could not be started.");
            if (!process.WaitForExit(10000))
            {
                try { process.Kill(true); } catch { }
                throw new TimeoutException("The screenshot helper timed out.");
            }
            var error = process.StandardError.ReadToEnd();
            if (process.ExitCode != 0 || !File.Exists(outputPath))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "No screenshot file was created." : error.Trim());

            reply = ApiKeyText(
                $"截好了，存在「圖片\\Lilith Screenshots\\{fileName}」。",
                $"截好了，保存在“图片\\Lilith Screenshots\\{fileName}”。",
                $"撮れたよ。「ピクチャ\\Lilith Screenshots\\{fileName}」に保存した。",
                $"Done. I saved it as Pictures\\Lilith Screenshots\\{fileName}.");
            Plugin.PluginLog.LogInfo($"Saved an allowlisted desktop screenshot to {outputPath}.");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Primary screenshot method failed; trying the Windows screenshot shortcut: {exception.Message}");
            try
            {
                SendShortcut(0x5B, 0x2C);
                reply = ApiKeyText(
                    "已改用 Windows 截圖快捷鍵，圖片會在系統的「螢幕擷取畫面」資料夾。",
                    "已改用 Windows 截图快捷键，图片会在系统的“屏幕截图”文件夹。",
                    "Windowsのスクリーンショット機能に切り替えたよ。画像はシステムのスクリーンショットフォルダーに保存される。",
                    "I used the Windows screenshot shortcut instead; the image will be in the system Screenshots folder.");
            }
            catch (Exception fallbackException)
            {
                reply = ApiKeyText(
                    "這台電腦目前沒有可用的截圖方式。",
                    "这台电脑目前没有可用的截图方式。",
                    "このPCでは今、利用できるスクリーンショット方法が見つからない。",
                    "No compatible screenshot method is currently available on this PC.");
                Plugin.PluginLog.LogWarning($"Screenshot fallback also failed: {fallbackException.Message}");
            }
        }
        return true;
    }

    private static bool TryHandleComputerCommand(string text, out string reply)
    {
        reply = string.Empty;
        return TryHandleBlockedComputerCommand(text, out reply)
            || TryDescribeComputerCapabilities(text, out reply)
            || TryReportSystemStatus(text, out reply)
            || TryOpenKnownFolder(text, out reply)
            || TryHandleWindowCommand(text, out reply)
            || TryWriteClipboard(text, out reply)
            || TryOpenBrowserSearch(text, out reply);
    }

    private static bool TryHandleBlockedComputerCommand(string text, out string reply)
    {
        reply = string.Empty;
        if (Regex.IsMatch(text, "(不要|別|别|不准|しないで|don't|do not)", RegexOptions.IgnoreCase))
            return false;
        var destructive = Regex.IsMatch(text,
            "((刪除|删除|清空|永久刪|delete|erase|empty).{0,12}(檔案|文件|資料夾|文件夹|回收筒|回收站|file|folder|recycle bin)|關機|关机|重新啟動|重新启动|重開機|重启|shutdown|restart|reboot|シャットダウン|再起動)",
            RegexOptions.IgnoreCase);
        var unsafeControl = Regex.IsMatch(text,
            "((關閉|关闭|強制結束|强制结束|kill|terminate|close).{0,8}(程式|程序|應用|应用|視窗|窗口|process|app|window)|PowerShell|命令提示字元|命令提示符|CMD|terminal|終端|管理員權限|管理员权限|administrator|管理者権限)",
            RegexOptions.IgnoreCase);
        var security = Regex.IsMatch(text,
            "((輸入|输入|貼上|粘贴|顯示|显示|讀取|读取|enter|paste|show|read).{0,8}(密碼|密码|API.?KEY|token|驗證碼|验证码|password|OTP|パスワード))",
            RegexOptions.IgnoreCase);
        if (!destructive && !unsafeControl && !security)
            return false;
        reply = ApiKeyText(
            "這類操作可能刪除資料、失去未儲存內容或暴露憑證，所以不在莉莉絲的電腦操作權限內。",
            "这类操作可能删除数据、丢失未保存内容或暴露凭证，所以不在莉莉丝的电脑操作权限内。",
            "データの削除、未保存内容の消失、認証情報の露出につながる操作だから、リリスのPC操作権限には含めていないよ。",
            "That action could delete data, lose unsaved work, or expose credentials, so it is outside Lilith's computer-control permissions.");
        Plugin.PluginLog.LogInfo("Blocked a destructive, arbitrary-shell, or credential-related computer command.");
        return true;
    }

    private static bool TryDescribeComputerCapabilities(string text, out string reply)
    {
        reply = string.Empty;
        if (!Regex.IsMatch(text,
            "((你|妳).{0,5}(能|可以|會|会).{0,8}(操作|控制).{0,5}(電腦|电脑|PC)|(進階|高级|高度な|advanced).{0,5}(功能|機能|controls)|what can you (?:do|control).{0,8}(?:computer|PC))",
            RegexOptions.IgnoreCase))
            return false;
        reply = ApiKeyText(
            "我能替你截圖、開啟常用資料夾、切換或排列視窗、顯示桌面、開啟工作檢視、複製指定文字，以及用瀏覽器搜尋。也能查看電量、記憶體、系統磁碟和網路狀態；刪檔、關機、密碼與任意終端指令不在權限內。",
            "我能替你截图、打开常用文件夹、切换或排列窗口、显示桌面、打开任务视图、复制指定文字，以及用浏览器搜索。也能查看电量、内存、系统磁盘和网络状态；删除文件、关机、密码与任意终端命令不在权限内。",
            "スクリーンショット、よく使うフォルダー、ウィンドウの切替や整列、デスクトップ表示、タスクビュー、指定した文字のコピー、ブラウザ検索ができるよ。バッテリー、メモリ、システムドライブ、ネット接続も確認できるけれど、削除、シャットダウン、パスワード、任意のコマンド実行はできない。",
            "I can take screenshots, open common folders, switch or arrange windows, show the desktop, open Task View, copy text you specify, and search in your browser. I can also report battery, memory, system-drive, and network status; deletion, shutdown, passwords, and arbitrary shell commands stay blocked.");
        return true;
    }

    private static bool TryReportSystemStatus(string text, out string reply)
    {
        reply = string.Empty;
        if (Regex.IsMatch(text, "(電池|电池|電量|电量|battery|バッテリー).{0,12}(多少|幾|几|剩|狀態|状态|status|level|残|ある)|((多少|幾|几|剩).{0,8}(電量|电量|battery))", RegexOptions.IgnoreCase))
        {
            if (!GetSystemPowerStatus(out var power) || power.BatteryLifePercent == 255)
            {
                reply = ApiKeyText("這台電腦沒有回報可用的電池資訊。", "这台电脑没有报告可用的电池信息。", "このPCからバッテリー情報を取得できなかったよ。", "This PC did not report usable battery information.");
            }
            else
            {
                var charging = power.ACLineStatus == 1;
                reply = ApiKeyText(
                    $"目前電量是 {power.BatteryLifePercent}%{(charging ? "，正在接電" : "")}。",
                    $"目前电量是 {power.BatteryLifePercent}%{(charging ? "，正在接电" : "")}。",
                    $"バッテリーは {power.BatteryLifePercent}%{(charging ? "、電源に接続中" : "")}だよ。",
                    $"The battery is at {power.BatteryLifePercent}%{(charging ? " and connected to power" : "")}.");
            }
            return true;
        }

        if (Regex.IsMatch(text, "((系統|系统|電腦|电脑|PC).{0,5}(記憶體|内存|memory|RAM|メモリ)|(記憶體|内存|memory|RAM|メモリ).{0,8}(使用|剩|狀態|状态|usage|status|空き))", RegexOptions.IgnoreCase))
        {
            var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref memory))
                reply = ApiKeyText("沒有讀到記憶體狀態。", "没有读取到内存状态。", "メモリの状態を取得できなかった。", "I could not read the memory status.");
            else
            {
                var total = FormatGiB(memory.TotalPhysical);
                var available = FormatGiB(memory.AvailablePhysical);
                reply = ApiKeyText(
                    $"記憶體使用率約 {memory.MemoryLoad}%，可用 {available} GB，共 {total} GB。",
                    $"内存使用率约 {memory.MemoryLoad}%，可用 {available} GB，共 {total} GB。",
                    $"メモリ使用率は約 {memory.MemoryLoad}%、空きは {available} GB、合計 {total} GBだよ。",
                    $"Memory usage is about {memory.MemoryLoad}%; {available} GB is available out of {total} GB.");
            }
            return true;
        }

        if (Regex.IsMatch(text, "((系統|系统|C槽|C盘|磁碟|磁盘|硬碟|硬盘|disk|storage|ストレージ).{0,10}(空間|空间|剩|可用|free|available|空き))", RegexOptions.IgnoreCase))
        {
            try
            {
                var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                var drive = new DriveInfo(root);
                reply = ApiKeyText(
                    $"系統磁碟還有 {FormatGiB((ulong)drive.AvailableFreeSpace)} GB 可用，共 {FormatGiB((ulong)drive.TotalSize)} GB。",
                    $"系统磁盘还有 {FormatGiB((ulong)drive.AvailableFreeSpace)} GB 可用，共 {FormatGiB((ulong)drive.TotalSize)} GB。",
                    $"システムドライブの空きは {FormatGiB((ulong)drive.AvailableFreeSpace)} GB、合計 {FormatGiB((ulong)drive.TotalSize)} GBだよ。",
                    $"The system drive has {FormatGiB((ulong)drive.AvailableFreeSpace)} GB free out of {FormatGiB((ulong)drive.TotalSize)} GB.");
            }
            catch (Exception exception)
            {
                Plugin.PluginLog.LogWarning($"Could not inspect the system drive: {exception.Message}");
                reply = ApiKeyText("沒有讀到系統磁碟狀態。", "没有读取到系统磁盘状态。", "システムドライブの状態を取得できなかった。", "I could not read the system-drive status.");
            }
            return true;
        }

        if (Regex.IsMatch(text, "((網路|网络|internet|network|インターネット|ネット).{0,10}(連線|连接|狀態|状态|通|connected|status|つなが|接続))", RegexOptions.IgnoreCase))
        {
            var available = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
            reply = available
                ? ApiKeyText("本機目前有可用的網路連線。", "本机目前有可用的网络连接。", "今は利用できるネット接続があるよ。", "A network connection is currently available.")
                : ApiKeyText("本機目前沒有偵測到可用的網路連線。", "本机目前没有检测到可用的网络连接。", "今は利用できるネット接続が見つからない。", "No available network connection is currently detected.");
            return true;
        }
        return false;
    }

    private static string FormatGiB(ulong bytes)
        => (bytes / 1073741824d).ToString("0.0", CultureInfo.InvariantCulture);

    private static bool TryOpenKnownFolder(string text, out string reply)
    {
        reply = string.Empty;
        var hasOpenVerb = Regex.IsMatch(text, "(打開|打开|開啟|开启|幫我開|帮我开|open|show|開いて|開く)", RegexOptions.IgnoreCase);
        if (!hasOpenVerb)
            return false;

        string? path = null;
        string name = string.Empty;
        if (Regex.IsMatch(text, "(截圖|截图|screenshot|スクリーンショット).{0,5}(資料夾|文件夹|folder|フォルダ)", RegexOptions.IgnoreCase))
        {
            path = Path.Combine(GetPicturesDirectory(), "Lilith Screenshots");
            Directory.CreateDirectory(path);
            name = ApiKeyText("截圖資料夾", "截图文件夹", "スクリーンショットフォルダー", "Screenshots folder");
        }
        else if (Regex.IsMatch(text, "(下載|下载|downloads?|ダウンロード)", RegexOptions.IgnoreCase))
        {
            path = GetDownloadsDirectory();
            name = ApiKeyText("下載資料夾", "下载文件夹", "ダウンロードフォルダー", "Downloads folder");
        }
        else if (Regex.IsMatch(text, "(桌面|desktop|デスクトップ)", RegexOptions.IgnoreCase))
        {
            path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            name = ApiKeyText("桌面資料夾", "桌面文件夹", "デスクトップフォルダー", "Desktop folder");
        }
        else if (Regex.IsMatch(text, "(文件(?:資料夾|文件夹)|文檔|文档|documents?|ドキュメント)", RegexOptions.IgnoreCase))
        {
            path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            name = ApiKeyText("文件資料夾", "文档文件夹", "ドキュメントフォルダー", "Documents folder");
        }
        else if (Regex.IsMatch(text, "(圖片|图片|照片|pictures?|photos?|ピクチャ|写真).{0,5}(資料夾|文件夹|folder|フォルダ)?", RegexOptions.IgnoreCase))
        {
            path = GetPicturesDirectory();
            name = ApiKeyText("圖片資料夾", "图片文件夹", "ピクチャフォルダー", "Pictures folder");
        }
        else if (Regex.IsMatch(text, "(音樂|音乐|music|ミュージック).{0,5}(資料夾|文件夹|folder|フォルダ)?", RegexOptions.IgnoreCase))
        {
            path = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            name = ApiKeyText("音樂資料夾", "音乐文件夹", "ミュージックフォルダー", "Music folder");
        }
        else if (Regex.IsMatch(text, "(影片|視頻|视频|videos?|ビデオ).{0,5}(資料夾|文件夹|folder|フォルダ)?", RegexOptions.IgnoreCase))
        {
            path = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            name = ApiKeyText("影片資料夾", "视频文件夹", "ビデオフォルダー", "Videos folder");
        }
        else if (Regex.IsMatch(text, "(MOD|模組|模组).{0,5}(資料夾|文件夹|folder|フォルダ)", RegexOptions.IgnoreCase))
        {
            path = MemoryDirectory;
            Directory.CreateDirectory(path);
            name = ApiKeyText("MOD 資料夾", "MOD 文件夹", "MODフォルダー", "MOD folder");
        }
        else if (Regex.IsMatch(text, "(資源回收筒|回收站|recycle bin|ごみ箱)", RegexOptions.IgnoreCase))
        {
            path = "shell:RecycleBinFolder";
            name = ApiKeyText("資源回收筒", "回收站", "ごみ箱", "Recycle Bin");
        }
        if (path == null)
            return false;
        if (!EnsureAdvancedComputerActions(out reply))
            return true;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe")
            {
                Arguments = path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? path : $"\"{path}\"",
                UseShellExecute = true
            });
            reply = ApiKeyText($"好，替你打開{name}。", $"好，替你打开{name}。", $"うん、{name}を開くね。", $"Okay, I'll open the {name}.");
            Plugin.PluginLog.LogInfo($"Opened allowlisted known folder '{name}'.");
        }
        catch (Exception exception)
        {
            reply = ApiKeyText($"{name}沒有成功打開。", $"{name}没有成功打开。", $"{name}を開けなかった……", $"I couldn't open the {name}.");
            Plugin.PluginLog.LogWarning($"Could not open known folder '{name}': {exception.Message}");
        }
        return true;
    }

    private static string GetPicturesDirectory()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return string.IsNullOrWhiteSpace(pictures)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures")
            : pictures;
    }

    private static string GetDownloadsDirectory()
    {
        if (!OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
            var configured = key?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
            if (!string.IsNullOrWhiteSpace(configured))
                return Environment.ExpandEnvironmentVariables(configured);
        }
        catch { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    private static bool TryHandleWindowCommand(string text, out string reply)
    {
        reply = string.Empty;
        string action;
        if (Regex.IsMatch(text, "(顯示桌面|显示桌面|show (?:the )?desktop|デスクトップを表示)", RegexOptions.IgnoreCase))
            action = "desktop";
        else if (Regex.IsMatch(text, "(工作檢視|任务视图|task view|タスクビュー)", RegexOptions.IgnoreCase))
            action = "taskview";
        else if (Regex.IsMatch(text, "(切換|切换|換到|换到|switch).{0,8}(視窗|窗口|window)|(上一個|上一个|前一個|前一个).{0,5}(視窗|窗口|window)|ウィンドウ.{0,4}(切り替|切替)", RegexOptions.IgnoreCase))
            action = "switch";
        else if (Regex.IsMatch(text, "((最小化).{0,5}(視窗|窗口)|(視窗|窗口).{0,5}最小化|minimi[sz]e (?:the )?window|ウィンドウ.{0,5}最小化)", RegexOptions.IgnoreCase))
            action = "minimize";
        else if (Regex.IsMatch(text, "((最大化).{0,5}(視窗|窗口)|(視窗|窗口).{0,5}最大化|maximi[sz]e (?:the )?window|ウィンドウ.{0,5}最大化)", RegexOptions.IgnoreCase))
            action = "maximize";
        else if (Regex.IsMatch(text, "((還原|恢复).{0,5}(視窗|窗口)|(視窗|窗口).{0,5}(還原|恢复)|restore (?:the )?window|ウィンドウ.{0,5}元に戻)", RegexOptions.IgnoreCase))
            action = "restore";
        else if (Regex.IsMatch(text, "(視窗|窗口|window).{0,8}(左邊|左側|左半|left|左に)|(貼|贴|snap).{0,5}(左邊|左側|left)", RegexOptions.IgnoreCase))
            action = "left";
        else if (Regex.IsMatch(text, "(視窗|窗口|window).{0,8}(右邊|右側|右半|right|右に)|(貼|贴|snap).{0,5}(右邊|右側|right)", RegexOptions.IgnoreCase))
            action = "right";
        else
            return false;

        if (!EnsureAdvancedComputerActions(out reply))
            return true;
        try
        {
            switch (action)
            {
                case "desktop":
                    SendShortcut(0x5B, 0x44);
                    reply = ApiKeyText("好，顯示桌面。", "好，显示桌面。", "うん、デスクトップを表示するね。", "Okay, showing the desktop.");
                    break;
                case "taskview":
                    SendShortcut(0x5B, 0x09);
                    reply = ApiKeyText("工作檢視打開了。", "任务视图打开了。", "タスクビューを開いたよ。", "Task View is open.");
                    break;
                case "switch":
                    SendShortcut(0x12, 0x09);
                    reply = ApiKeyText("替你切到上一個視窗。", "替你切到上一个窗口。", "前のウィンドウに切り替えたよ。", "I switched to the previous window.");
                    break;
                default:
                    var target = GetControllableWindow();
                    if (target == IntPtr.Zero)
                        throw new InvalidOperationException("No external foreground window is available.");
                    if (action == "minimize")
                    {
                        ShowWindow(target, 6);
                        reply = ApiKeyText("把剛才的視窗最小化了。", "把刚才的窗口最小化了。", "さっきのウィンドウを最小化したよ。", "I minimized the previous window.");
                    }
                    else if (action == "maximize")
                    {
                        ShowWindow(target, 3);
                        reply = ApiKeyText("把剛才的視窗最大化了。", "把刚才的窗口最大化了。", "さっきのウィンドウを最大化したよ。", "I maximized the previous window.");
                    }
                    else if (action == "restore")
                    {
                        ShowWindow(target, 9);
                        reply = ApiKeyText("視窗已經還原。", "窗口已经恢复。", "ウィンドウを元に戻したよ。", "I restored the window.");
                    }
                    else
                    {
                        ShowWindow(target, 9);
                        if (!SetForegroundWindow(target))
                            throw new InvalidOperationException("The target window could not be activated.");
                        SendShortcut(0x5B, action == "left" ? (byte)0x25 : (byte)0x27);
                        reply = action == "left"
                            ? ApiKeyText("把剛才的視窗排到左側了。", "把刚才的窗口排到左侧了。", "さっきのウィンドウを左側に並べたよ。", "I snapped the previous window to the left.")
                            : ApiKeyText("把剛才的視窗排到右側了。", "把刚才的窗口排到右侧了。", "さっきのウィンドウを右側に並べたよ。", "I snapped the previous window to the right.");
                    }
                    break;
            }
            Plugin.PluginLog.LogInfo($"Executed allowlisted window action '{action}'.");
        }
        catch (Exception exception)
        {
            reply = ApiKeyText("這次沒有找到能操作的視窗。", "这次没有找到能操作的窗口。", "今回は操作できるウィンドウが見つからなかった。", "I couldn't find a window to control this time.");
            Plugin.PluginLog.LogWarning($"Could not execute window action '{action}': {exception.Message}");
        }
        return true;
    }

    private static bool TryWriteClipboard(string text, out string reply)
    {
        reply = string.Empty;
        if (!Regex.IsMatch(text, "(複製|复制|copy|コピー)", RegexOptions.IgnoreCase))
            return false;
        if (Regex.IsMatch(text, "(檔案|文件|資料夾|文件夹|file|folder|フォルダ)", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(text, "[「『\"']", RegexOptions.IgnoreCase))
            return false;

        string content = string.Empty;
        var quoted = Regex.Match(text, "[「『\"'](?<value>.+?)[」』\"']", RegexOptions.Singleline);
        if (quoted.Success)
            content = quoted.Groups["value"].Value;
        else
        {
            var chinese = Regex.Match(text, "(?:幫我|帮我|請|请)?(?:複製|复制)(?:這段|这段|文字|內容|内容)?[：:\\s]+(?<value>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var english = Regex.Match(text, "copy\\s+(?<value>.+?)(?:\\s+to\\s+(?:the\\s+)?clipboard)?$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var japanese = Regex.Match(text, "(?<value>.+?)(?:を)?コピー(?:して)?$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var match = chinese.Success ? chinese : english.Success ? english : japanese;
            if (match.Success)
                content = match.Groups["value"].Value.Trim();
        }
        if (string.IsNullOrWhiteSpace(content) || content.Length > 4000)
            return false;
        if (ContainsSensitiveNoteData(content))
        {
            reply = ApiKeyText("為了避免憑證外洩，我不會代為複製看起來像密碼、API Key 或驗證碼的內容。", "为了避免凭证泄露，我不会代为复制看起来像密码、API Key 或验证码的内容。", "認証情報の漏えいを避けるため、パスワード、APIキー、認証コードらしい内容はコピーしないよ。", "To avoid credential exposure, I won't copy text that looks like a password, API key, or verification code.");
            return true;
        }
        if (!EnsureAdvancedComputerActions(out reply))
            return true;

        GUIUtility.systemCopyBuffer = content;
        reply = ApiKeyText("已經替你複製到剪貼簿了。", "已经替你复制到剪贴板了。", "クリップボードにコピーしたよ。", "I copied it to the clipboard.");
        Plugin.PluginLog.LogInfo($"Copied player-specified text to the clipboard ({content.Length} chars; content hidden from log).");
        return true;
    }

    private static bool TryOpenBrowserSearch(string text, out string reply)
    {
        reply = string.Empty;
        Match match;
        if ((match = Regex.Match(text, "(?:用|在)?(?:瀏覽器|浏览器|Google|谷歌).{0,5}(?:搜尋|搜索|查詢|查询)[：:\\s]*(?<query>.+)$", RegexOptions.IgnoreCase)).Success
            || (match = Regex.Match(text, "(?:search|google)(?:\\s+the\\s+web)?(?:\\s+for)?\\s+(?<query>.+)$", RegexOptions.IgnoreCase)).Success
            || (match = Regex.Match(text, "(?:ブラウザ|Google).{0,5}(?:で)?(?:検索|調べ)[：:\\s]*(?<query>.+)$", RegexOptions.IgnoreCase)).Success)
        {
            var query = match.Groups["query"].Value.Trim(' ', '。', '.', '？', '?', '！', '!');
            if (query.Length == 0 || query.Length > 500)
                return false;
            if (!EnsureAdvancedComputerActions(out reply))
                return true;
            try
            {
                var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                reply = ApiKeyText("好，已經在瀏覽器搜尋了。", "好，已经在浏览器搜索了。", "うん、ブラウザで検索したよ。", "Okay, I searched for it in your browser.");
                Plugin.PluginLog.LogInfo($"Opened an explicit browser search ({query.Length} chars; query hidden from log).");
            }
            catch (Exception exception)
            {
                reply = ApiKeyText("瀏覽器沒有成功打開。", "浏览器没有成功打开。", "ブラウザを開けなかった……", "I couldn't open the browser.");
                Plugin.PluginLog.LogWarning($"Could not open browser search: {exception.Message}");
            }
            return true;
        }
        return false;
    }

    private static bool EnsureAdvancedComputerActions(out string reply)
    {
        if (Plugin.AdvancedComputerActionsEnabled.Value)
        {
            reply = string.Empty;
            return true;
        }
        reply = ApiKeyText(
            "要先在設定中打開「進階電腦操作」，我才能執行這個動作。",
            "要先在设置中打开“高级电脑操作”，我才能执行这个动作。",
            "先に設定で「高度なPC操作」を有効にしてね。",
            "Enable “Advanced PC controls” in Settings before I perform that action.");
        return false;
    }

    private static bool TryHandleMediaCommand(string text, out string reply)
    {
        reply = string.Empty;
        if (Regex.IsMatch(text, "(不要|別|别|しないで|don't|do not)", RegexOptions.IgnoreCase))
            return false;

        byte key;
        string traditional;
        string simplified;
        string japanese;
        string english;
        if (Regex.IsMatch(text, "(下一首|下一曲|下一個|下一个|切下一首|次の曲|次へ|next song|next track)", RegexOptions.IgnoreCase))
        {
            key = 0xB0;
            traditional = "好，換到下一首。"; simplified = "好，换到下一首。"; japanese = "うん、次の曲にするね。"; english = "Okay, next track.";
        }
        else if (Regex.IsMatch(text, "(上一首|上一曲|上一個|上一个|切上一首|前の曲|前へ|previous song|previous track)", RegexOptions.IgnoreCase))
        {
            key = 0xB1;
            traditional = "好，回到上一首。"; simplified = "好，回到上一首。"; japanese = "うん、前の曲に戻すね。"; english = "Okay, previous track.";
        }
        else if (Regex.IsMatch(text, "(停止音樂|停止音乐|停止播放|音樂停掉|音乐停掉|再生停止|stop music|stop playback)", RegexOptions.IgnoreCase))
        {
            key = 0xB2;
            traditional = "音樂停下來了。"; simplified = "音乐停下来了。"; japanese = "音楽を止めたよ。"; english = "I stopped the music.";
        }
        else if (Regex.IsMatch(text, "(暫停音樂|暂停音乐|暫停播放|暂停播放|音樂暫停|音乐暂停|先停一下|一時停止|pause music|pause playback)", RegexOptions.IgnoreCase))
        {
            key = 0xB3;
            traditional = "嗯，先暫停一下。"; simplified = "嗯，先暂停一下。"; japanese = "うん、いったん止めるね。"; english = "Okay, paused for now.";
        }
        else if (Regex.IsMatch(text, "(繼續播放|继续播放|繼續音樂|继续音乐|恢復播放|恢复播放|接著播|接着播|再生して|resume music|resume playback|play music)", RegexOptions.IgnoreCase))
        {
            key = 0xB3;
            traditional = "好，繼續播放。"; simplified = "好，继续播放。"; japanese = "うん、続きを再生するね。"; english = "Okay, resuming playback.";
        }
        else if (Regex.IsMatch(text, "(靜音|静音|關掉聲音|关掉声音|ミュート|mute)", RegexOptions.IgnoreCase))
        {
            key = 0xAD;
            traditional = "好，靜音了。"; simplified = "好，静音了。"; japanese = "ミュートにしたよ。"; english = "Muted.";
        }
        else if (Regex.IsMatch(text, "(音量大一點|音量大一点|提高音量|調大聲|调大声|音量上げ|volume up|louder)", RegexOptions.IgnoreCase))
        {
            key = 0xAF;
            traditional = "音量調高一點了。"; simplified = "音量调高一点了。"; japanese = "少し音量を上げたよ。"; english = "I turned it up a little.";
        }
        else if (Regex.IsMatch(text, "(音量小一點|音量小一点|降低音量|調小聲|调小声|音量下げ|volume down|quieter)", RegexOptions.IgnoreCase))
        {
            key = 0xAE;
            traditional = "音量調低一點了。"; simplified = "音量调低一点了。"; japanese = "少し音量を下げたよ。"; english = "I turned it down a little.";
        }
        else
        {
            return false;
        }

        try
        {
            SendVirtualKey(key);
            reply = ApiKeyText(traditional, simplified, japanese, english);
            Plugin.PluginLog.LogInfo($"Sent allowlisted Windows media key 0x{key:X2}.");
        }
        catch (Exception exception)
        {
            reply = ApiKeyText("媒體控制沒有成功。", "媒体控制没有成功。", "メディア操作がうまくいかなかった……", "The media control did not work.");
            Plugin.PluginLog.LogWarning($"Could not send Windows media key: {exception.Message}");
        }
        return true;
    }

    private static bool TryLaunchApplicationCommand(string text, out string reply)
    {
        reply = string.Empty;
        if (Regex.IsMatch(text, "(不要|別|别|不可|禁止|しないで|開かないで|don't|do not)", RegexOptions.IgnoreCase))
            return false;
        var configured = FindConfiguredApplication(text);
        var official = FindOfficialApplication(text);
        var mentionsBuiltIn = Regex.IsMatch(text,
            "(記事本|记事本|メモ帳|notepad|計算機|计算器|電卓|calculator|calc|檔案總管|文件资源管理器|資源管理器|エクスプローラー|file explorer|explorer|瀏覽器|浏览器|ブラウザ|browser|chrome|edge|steam|蒸汽平台|スチーム)",
            RegexOptions.IgnoreCase);
        var explicitLaunch = Regex.IsMatch(text,
            "(開啟|开启|打開|打开|啟動|启动|幫我開|帮我开|開いて|起動して|open|launch|start)", RegexOptions.IgnoreCase);
        var likelyMisheardLaunch = text.Length <= 32
            && (configured != null || official != null || mentionsBuiltIn)
            && Regex.IsMatch(text, "(撥給|拨给|播給|播给|包我|幫我|帮我|幫忙|帮忙|給我|给我|我要你|麻煩|麻烦)", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(text, "(什麼|什么|介紹|介绍|查詢|查询|搜尋|搜索|攻略|怎麼|怎么|為什麼|为什么|what|why|how)", RegexOptions.IgnoreCase);
        if (!explicitLaunch && !likelyMisheardLaunch)
            return false;

        if (likelyMisheardLaunch && !explicitLaunch)
            Plugin.PluginLog.LogInfo("Recovered a likely speech-recognition error as an allowlisted launch command.");

        string? target;
        string? arguments;
        string appName;
        if (configured != null)
        {
            target = configured.Target;
            arguments = configured.Arguments;
            appName = configured.Name;
        }
        else if (official != null)
        {
            target = official.Target;
            arguments = official.Arguments;
            appName = official.Name;
        }
        else if (Regex.IsMatch(text, "(記事本|记事本|メモ帳|notepad)", RegexOptions.IgnoreCase))
        {
            target = "notepad.exe";
            arguments = string.Empty;
            appName = ApiKeyText("記事本", "记事本", "メモ帳", "Notepad");
        }
        else if (Regex.IsMatch(text, "(計算機|计算器|電卓|calculator|calc)", RegexOptions.IgnoreCase))
        {
            target = "calc.exe";
            arguments = string.Empty;
            appName = ApiKeyText("計算機", "计算器", "電卓", "Calculator");
        }
        else if (Regex.IsMatch(text, "(檔案總管|文件资源管理器|資源管理器|エクスプローラー|file explorer|explorer)", RegexOptions.IgnoreCase))
        {
            target = "explorer.exe";
            arguments = string.Empty;
            appName = ApiKeyText("檔案總管", "文件资源管理器", "エクスプローラー", "File Explorer");
        }
        else if (Regex.IsMatch(text, "(瀏覽器|浏览器|ブラウザ|browser|chrome|edge)", RegexOptions.IgnoreCase))
        {
            target = "https://www.google.com/";
            arguments = string.Empty;
            appName = ApiKeyText("瀏覽器", "浏览器", "ブラウザ", "browser");
        }
        else if (Regex.IsMatch(text, "(steam|蒸汽平台|スチーム)", RegexOptions.IgnoreCase))
        {
            target = "steam://open/main";
            arguments = string.Empty;
            appName = "Steam";
        }
        else
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target)
            {
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true
            });
            reply = ApiKeyText($"好，幫你開啟{appName}。", $"好，帮你打开{appName}。", $"うん、{appName}を開くね。", $"Sure, I'll open {appName}.");
            Plugin.PluginLog.LogInfo($"Launched allowlisted application target '{target}'.");
        }
        catch (Exception exception)
        {
            reply = ApiKeyText($"{appName}沒有成功開啟。", $"{appName}没有成功打开。", $"{appName}を開けなかった……", $"I couldn't open {appName}.");
            Plugin.PluginLog.LogWarning($"Could not launch allowlisted target '{target}': {exception.Message}");
        }
        return true;
    }

    internal static void EnsureApplicationLauncherFile()
    {
        try
        {
            Directory.CreateDirectory(MemoryDirectory);
            if (File.Exists(ApplicationLauncherPath))
                return;
            var defaults = new[]
            {
                new ApplicationLauncher { Name = "記事本", Target = "notepad.exe", Aliases = new[] { "記事本", "记事本", "メモ帳", "notepad" } },
                new ApplicationLauncher { Name = "計算機", Target = "calc.exe", Aliases = new[] { "計算機", "计算器", "電卓", "calculator", "calc" } },
                new ApplicationLauncher { Name = "檔案總管", Target = "explorer.exe", Aliases = new[] { "檔案總管", "文件资源管理器", "資源管理器", "エクスプローラー", "file explorer", "explorer" } },
                new ApplicationLauncher { Name = "瀏覽器", Target = "https://www.google.com/", Aliases = new[] { "瀏覽器", "浏览器", "ブラウザ", "browser", "chrome", "edge" } },
                new ApplicationLauncher { Name = "Steam", Target = "steam://open/main", Aliases = new[] { "Steam", "蒸汽平台", "スチーム" } }
            };
            File.WriteAllText(ApplicationLauncherPath,
                JsonSerializer.Serialize(defaults, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }), Encoding.UTF8);
            Plugin.PluginLog.LogInfo($"Created editable application launcher list at {ApplicationLauncherPath}.");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not create application launcher list: {exception.Message}");
        }
    }

    private static ApplicationLauncher? FindConfiguredApplication(string text)
    {
        try
        {
            EnsureApplicationLauncherFile();
            var launchers = JsonSerializer.Deserialize<ApplicationLauncher[]>(File.ReadAllText(ApplicationLauncherPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? Array.Empty<ApplicationLauncher>();
            foreach (var launcher in launchers)
            {
                if (string.IsNullOrWhiteSpace(launcher.Name) || string.IsNullOrWhiteSpace(launcher.Target))
                    continue;
                var aliases = launcher.Aliases == null || launcher.Aliases.Length == 0
                    ? new[] { launcher.Name }
                    : launcher.Aliases;
                if (aliases.Any(alias => !string.IsNullOrWhiteSpace(alias)
                    && text.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0))
                    return launcher;
            }
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not read application launcher list: {exception.Message}");
        }
        return null;
    }

    private static ApplicationLauncher? FindOfficialApplication(string text)
    {
        if (FindOfficialGame(text) is { } game)
            return game;
        if (Regex.IsMatch(text, "(Spotify|スポティファイ|Spotify 音樂|Spotify 音乐)", RegexOptions.IgnoreCase))
        {
            var shortcut = ResolveWindowsShortcut(new[] { "Spotify" });
            return new ApplicationLauncher { Name = "Spotify", Target = shortcut ?? "spotify:" };
        }

        var applications = new[]
        {
            new OfficialApplication("VS Code", "Code.exe", new[] { "VS Code", "Visual Studio Code", "視覺化工作室程式碼", "代码编辑器", "コード", "ブイエスコード" }),
            new OfficialApplication("Visual Studio", "devenv.exe", new[] { "Visual Studio", "視覺工作室", "视觉工作室", "ビジュアルスタジオ" }),
            new OfficialApplication("Rider", "rider64.exe", new[] { "Rider", "JetBrains Rider", "ライダー" }),
            new OfficialApplication("IntelliJ IDEA", "idea64.exe", new[] { "IntelliJ", "IntelliJ IDEA", "IDEA", "インテリジェイ" }),
            new OfficialApplication("PyCharm", "pycharm64.exe", new[] { "PyCharm", "派洽姆", "パイチャーム" }),
            new OfficialApplication("WebStorm", "webstorm64.exe", new[] { "WebStorm", "ウェブストーム" }),
            new OfficialApplication("CLion", "clion64.exe", new[] { "CLion", "シーライオン" }),
            new OfficialApplication("Cursor", "Cursor.exe", new[] { "Cursor", "游標編輯器", "光标编辑器", "カーソル" }),
            new OfficialApplication("Sublime Text", "sublime_text.exe", new[] { "Sublime", "Sublime Text", "サブライム" }),
            new OfficialApplication("Notepad++", "notepad++.exe", new[] { "Notepad++", "Notepad Plus Plus", "記事本++", "记事本++", "ノートパッドプラスプラス" }),
            new OfficialApplication("VLC", "vlc.exe", new[] { "VLC", "VLC 播放器", "VLC プレイヤー" }),
            new OfficialApplication("PotPlayer", "PotPlayerMini64.exe", new[] { "PotPlayer", "Pot Player", "影音播放器", "ポットプレイヤー" }, "PotPlayerMini.exe"),
            new OfficialApplication("MPV", "mpv.exe", new[] { "MPV", "MPV 播放器", "MPV プレイヤー" }),
            new OfficialApplication("MPC-HC", "mpc-hc64.exe", new[] { "MPC-HC", "Media Player Classic", "メディアプレイヤークラシック" }, "mpc-hc.exe"),
            new OfficialApplication("Windows Media Player", "wmplayer.exe", new[] { "Windows Media Player", "Windows 媒體播放器", "Windows 媒体播放器", "ウィンドウズメディアプレイヤー" })
        };

        foreach (var application in applications)
        {
            if (!application.Aliases.Any(alias => text.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0))
                continue;
            foreach (var executable in application.Executables)
            {
                var resolved = ResolveRegisteredExecutable(executable);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return new ApplicationLauncher { Name = application.Name, Target = resolved };
            }
            Plugin.PluginLog.LogWarning($"Official application '{application.Name}' was requested but is not installed or registered with Windows.");
            return new ApplicationLauncher { Name = application.Name, Target = application.Executables[0] };
        }
        return null;
    }

    private static ApplicationLauncher? FindOfficialGame(string text)
    {
        var games = new[]
        {
            new OfficialGame("Dota 2", "steam://rungameid/570", new[] { "Dota 2", "Dota2", "刀塔2", "刀塔 2", "ドータ2", "ドータ 2" }),
            new OfficialGame("League of Legends", null, new[] { "League of Legends", "英雄聯盟", "英雄联盟", "LOL", "LoL", "擼啊擼", "撸啊撸", "リーグ・オブ・レジェンド" }, new[] { "League of Legends", "英雄聯盟", "英雄联盟", "Riot Client", "Riot用戶端" }),
            new OfficialGame("VALORANT", "riotclient://launch-product=valorant&launch-patchline=live", new[] { "VALORANT", "瓦羅蘭特", "瓦罗兰特", "特戰英豪", "特战英豪", "無畏契約", "无畏契约", "ヴァロラント" }, new[] { "VALORANT", "瓦羅蘭特", "瓦罗兰特", "特戰英豪", "特战英豪", "無畏契約", "无畏契约" }),
            new OfficialGame("Counter-Strike 2", "steam://rungameid/730", new[] { "Counter-Strike 2", "Counter Strike 2", "CS2", "CS 2", "絕對武力2", "绝对武力2", "反恐精英2", "カウンターストライク2" }),
            new OfficialGame("Overwatch 2", null, new[] { "Overwatch", "Overwatch 2", "鬥陣特攻", "斗阵特攻", "守望先鋒", "守望先锋", "オーバーウォッチ" }, new[] { "Overwatch", "Overwatch 2", "鬥陣特攻", "斗阵特攻", "守望先鋒", "守望先锋", "Battle.net" }),
            new OfficialGame("Genshin Impact", null, new[] { "Genshin Impact", "Genshin", "原神", "げんしん" }, new[] { "Genshin Impact", "原神", "HoYoPlay" }),
            new OfficialGame("Honkai: Star Rail", null, new[] { "Honkai Star Rail", "Star Rail", "崩壞星穹鐵道", "崩坏星穹铁道", "星穹鐵道", "星穹铁道", "崩壊スターレイル", "スターレイル" }, new[] { "Honkai Star Rail", "崩壞：星穹鐵道", "崩坏：星穹铁道", "HoYoPlay" }),
            new OfficialGame("Elden Ring", "steam://rungameid/1245620", new[] { "Elden Ring", "艾爾登法環", "艾尔登法环", "エルデンリング" }),
            new OfficialGame("Helldivers 2", "steam://rungameid/553850", new[] { "Helldivers 2", "Helldiver 2", "絕地戰兵2", "绝地潜兵2", "地獄潛者2", "ヘルダイバー2", "ヘルダイバーズ2" }),
            new OfficialGame("Black Myth: Wukong", "steam://rungameid/2358720", new[] { "Black Myth Wukong", "Black Myth: Wukong", "黑神話悟空", "黑神话悟空", "悟空", "黒神話：悟空", "ブラックミスウーコン" }),
            new OfficialGame("Hollow Knight", "steam://rungameid/367520", new[] { "Hollow Knight", "空洞騎士", "空洞骑士", "ホロウナイト" }),
            new OfficialGame("Hearthstone", null, new[] { "Hearthstone", "爐石戰記", "炉石传说", "爐石", "炉石", "ハースストーン" }, new[] { "Hearthstone", "爐石戰記", "炉石传说", "Battle.net" }),
            new OfficialGame("Diablo IV", "steam://rungameid/2344520", new[] { "Diablo IV", "Diablo 4", "暗黑破壞神4", "暗黑破坏神4", "暗黑4", "ディアブロ4", "ディアブロ IV" }, new[] { "Diablo IV", "Diablo 4", "暗黑破壞神 IV", "Battle.net" })
        };

        foreach (var game in games)
        {
            if (!game.Aliases.Any(alias => text.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0))
                continue;
            var shortcut = game.ShortcutNames.Length == 0 ? null : ResolveWindowsShortcut(game.ShortcutNames);
            if (!string.IsNullOrWhiteSpace(shortcut))
                return new ApplicationLauncher { Name = game.Name, Target = shortcut };
            if (!string.IsNullOrWhiteSpace(game.FallbackTarget))
                return new ApplicationLauncher { Name = game.Name, Target = game.FallbackTarget };
            Plugin.PluginLog.LogWarning($"Official game '{game.Name}' was requested but its launcher shortcut was not found.");
            return new ApplicationLauncher { Name = game.Name, Target = game.Name };
        }
        return null;
    }

    private static string? ResolveWindowsShortcut(string[] names)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };
            foreach (var root in roots.Where(Directory.Exists))
            {
                foreach (var shortcut in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    var fileName = Path.GetFileNameWithoutExtension(shortcut);
                    if (names.Any(name => fileName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
                        return shortcut;
                }
            }
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not search Windows shortcuts: {exception.Message}");
        }
        return null;
    }

    private static string? ResolveFuzzyWindowsShortcut(string requestedName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var requested = NormalizeApplicationName(requestedName);
        if (requested.Length < 2) return null;
        try
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };
            string? best = null;
            var bestScore = 0;
            foreach (var root in roots.Where(Directory.Exists))
            {
                foreach (var pattern in new[] { "*.lnk", "*.url", "*.appref-ms" })
                {
                    foreach (var shortcut in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
                    {
                        var displayName = Path.GetFileNameWithoutExtension(shortcut);
                        if (Regex.IsMatch(displayName, "(uninstall|解除安裝|卸载|readme|help|manual|website|web site)", RegexOptions.IgnoreCase))
                            continue;
                        var normalized = NormalizeApplicationName(displayName);
                        var score = normalized == requested ? 1000
                            : normalized.StartsWith(requested, StringComparison.Ordinal) ? 800 - Math.Abs(normalized.Length - requested.Length)
                            : normalized.Contains(requested, StringComparison.Ordinal) ? 600 - Math.Abs(normalized.Length - requested.Length)
                            : requested.Contains(normalized, StringComparison.Ordinal) && normalized.Length >= 4 ? 400 - Math.Abs(normalized.Length - requested.Length)
                            : 0;
                        if (score <= bestScore)
                            continue;
                        bestScore = score;
                        best = shortcut;
                    }
                }
            }
            return bestScore >= 350 ? best : null;
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not perform portable Start Menu lookup: {exception.Message}");
            return null;
        }
    }

    private static string NormalizeApplicationName(string value)
        => Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+", string.Empty);

    private static bool TryFocusRunningApplication(string requestedName)
    {
        var requested = NormalizeApplicationName(requestedName);
        if (requested.Length < 2) return false;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == Environment.ProcessId || process.MainWindowHandle == IntPtr.Zero)
                    continue;
                var processName = NormalizeApplicationName(process.ProcessName);
                if (processName != requested && !processName.Contains(requested, StringComparison.Ordinal) && !requested.Contains(processName, StringComparison.Ordinal))
                    continue;
                ShowWindow(process.MainWindowHandle, 9);
                if (SetForegroundWindow(process.MainWindowHandle))
                    return true;
            }
            catch { }
            finally { process.Dispose(); }
        }
        return false;
    }

    private static string? ResolveRegisteredExecutable(string executable)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            var runningName = Path.GetFileNameWithoutExtension(executable);
            foreach (var process in Process.GetProcessesByName(runningName))
            {
                try
                {
                    var runningPath = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(runningPath) && File.Exists(runningPath))
                        return runningPath;
                }
                catch { }
                finally { process.Dispose(); }
            }

            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executable}");
                var registered = key?.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(registered) && File.Exists(registered.Trim('"')))
                    return registered.Trim('"');
            }

            foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                var candidate = Path.Combine(folder.Trim().Trim('"'), executable);
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not resolve registered executable '{executable}': {exception.Message}");
        }
        return null;
    }

    private sealed class OfficialApplication
    {
        public string Name { get; }
        public string[] Executables { get; }
        public string[] Aliases { get; }

        public OfficialApplication(string name, string executable, string[] aliases, params string[] alternatives)
        {
            Name = name;
            Executables = new[] { executable }.Concat(alternatives).ToArray();
            Aliases = aliases;
        }
    }

    private sealed class OfficialGame
    {
        public string Name { get; }
        public string? FallbackTarget { get; }
        public string[] Aliases { get; }
        public string[] ShortcutNames { get; }

        public OfficialGame(string name, string? fallbackTarget, string[] aliases, string[]? shortcutNames = null)
        {
            Name = name;
            FallbackTarget = fallbackTarget;
            Aliases = aliases;
            ShortcutNames = shortcutNames ?? Array.Empty<string>();
        }
    }

    private sealed class ApplicationLauncher
    {
        public string Name { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string[] Aliases { get; set; } = Array.Empty<string>();
    }

    private static void EnsureLocalVoiceHost()
    {
        if (_voiceHostLaunchAttempted || Time.unscaledTime < 2f)
            return;
        _voiceHostLaunchAttempted = true;
        if (!Plugin.VoiceEnabled.Value || !Plugin.VoiceAutoStartLocalService.Value)
            return;
        try
        {
            var endpoint = Plugin.VoiceEndpoint.Value.Trim();
            var japaneseEndpoint = Plugin.JapaneseVoiceEndpoint.Value.Trim();
            if (!endpoint.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase)
                && !japaneseEndpoint.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase))
                return;
            var hostPath = Environment.ExpandEnvironmentVariables(Plugin.VoiceHostPath.Value.Trim());
            if (!File.Exists(hostPath))
            {
                Plugin.PluginLog.LogInfo("Bundled local voice service is not installed; dynamic voice remains available through configured external endpoints.");
                return;
            }
            var startInfo = new ProcessStartInfo
            {
                FileName = hostPath,
                Arguments = $"--voice-host --parent {Environment.ProcessId}",
                WorkingDirectory = Path.GetDirectoryName(hostPath) ?? Paths.GameRootPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var bundledDotnet = Path.Combine(Paths.GameRootPath, "dotnet");
            if (Directory.Exists(bundledDotnet))
                startInfo.Environment["DOTNET_ROOT"] = bundledDotnet;
            _voiceHostProcess = Process.Start(startInfo);
            Plugin.PluginLog.LogInfo("Started the bundled local voice host without a console window.");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not start the bundled local voice host: {exception.Message}");
        }
    }

    private static void HandleVoiceInput(DialogueManager manager)
    {
        var voiceInputKey = Plugin.VoiceInputKey.Value;
        var voiceInputKeyDown = IsKeyCurrentlyDown(voiceInputKey);
        var voiceInputKeyPressed = voiceInputKeyDown && !_voiceInputKeyWasDown;
        var voiceInputKeyReleased = !voiceInputKeyDown && _voiceInputKeyWasDown;
        _voiceInputKeyWasDown = voiceInputKeyDown;

        if (!Plugin.VoiceInputEnabled.Value || _keyBindingTarget != 0)
            return;
        if (voiceInputKeyPressed)
        {
            if (_microphoneRecording || _transcriptionInFlight || _requestInFlight)
            {
                manager.ForceSay(ApiKeyText("先等我一下……", "先等我一下……", "少し待って……", "Wait for me a moment…"), string.Empty, 4f);
                return;
            }
            if (string.IsNullOrWhiteSpace(Plugin.GeminiApiKey.Value))
            {
                manager.ForceSay(ApiKeyText("還沒有設定 Gemini API Key。", "还没有设置 Gemini API Key。", "Gemini APIキーがまだ設定されていないよ。", "The Gemini API key has not been set yet."), string.Empty, 6f);
                return;
            }
            try
            {
                var maxSeconds = Math.Clamp(Plugin.VoiceInputMaxSeconds.Value, 5, 90);
                string deviceName;
                using (var enumerator = new MMDeviceEnumerator())
                    deviceName = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console).FriendlyName;
                _wasapiCapture = new WasapiCapture();
                _wasapiStream = new MemoryStream();
                _wasapiWriter = new WaveFileWriter(_wasapiStream, _wasapiCapture.WaveFormat);
                _wasapiCapture.DataAvailable += OnWasapiDataAvailable;
                _wasapiCapture.StartRecording();
                _microphoneRecording = true;
                _microphoneStartedAt = Time.unscaledTime;
                manager.ForceSay(ApiKeyText("正在聽……", "正在听……", "聞いているよ……", "Listening…"), string.Empty, maxSeconds + 2f);
                Plugin.PluginLog.LogInfo($"F6 WASAPI recording started with Windows default input '{deviceName}' ({_wasapiCapture.WaveFormat}).");
            }
            catch (Exception exception)
            {
                _microphoneRecording = false;
                CleanupWasapiCapture();
                Plugin.PluginLog.LogWarning($"Could not start microphone recording: {exception.Message}");
                manager.ForceSay(ApiKeyText("沒有收到麥克風的聲音。", "没有收到麦克风的声音。", "マイクの音が届いていないみたい。", "I didn't receive any microphone audio."), string.Empty, 6f);
            }
        }

        var reachedLimit = _microphoneRecording
            && Time.unscaledTime - _microphoneStartedAt >= Math.Clamp(Plugin.VoiceInputMaxSeconds.Value, 5, 90) - 0.1f;
        if (_microphoneRecording && (voiceInputKeyReleased || reachedLimit))
            StopVoiceInputRecording(reachedLimit);
    }

    private static void StopVoiceInputRecording(bool reachedLimit)
    {
        _microphoneRecording = false;
        try
        {
            if (_wasapiCapture == null || _wasapiStream == null || _wasapiWriter == null)
                return;
            _wasapiCapture.StopRecording();
            _wasapiCapture.DataAvailable -= OnWasapiDataAvailable;
            byte[] wav;
            lock (WasapiLock)
            {
                _wasapiWriter.Flush();
                _wasapiWriter.Dispose();
                _wasapiWriter = null;
                wav = _wasapiStream.ToArray();
            }
            _wasapiCapture.Dispose();
            _wasapiCapture = null;
            _wasapiStream.Dispose();
            _wasapiStream = null;
            if (wav.Length < 2048)
            {
                PendingTranscriptionErrors.Enqueue(ApiKeyText("剛才沒有收到聲音，再試一次吧。", "刚才没有收到声音，再试一次吧。", "今の声は届かなかったみたい。もう一度試してみて。", "I didn't receive that audio. Please try again."));
                return;
            }
            var elapsed = Math.Max(0f, Time.unscaledTime - _microphoneStartedAt);
            var normalizedWav = NormalizeVoiceWav(wav);
            _transcriptionInFlight = true;
            _ = RequestTranscriptionAsync(normalizedWav, GetVoiceInputLanguageInstruction());
            Plugin.PluginLog.LogInfo($"F6 WASAPI recording stopped after {elapsed:F1}s{(reachedLimit ? " (time limit reached)" : string.Empty)}; normalized {wav.Length} to {normalizedWav.Length} WAV bytes for transcription.");
        }
        catch (Exception exception)
        {
            CleanupWasapiCapture();
            Plugin.PluginLog.LogWarning($"Could not finish microphone recording: {exception.Message}");
            PendingTranscriptionErrors.Enqueue(ApiKeyText("剛才沒有聽清楚，再試一次吧。", "刚才没有听清楚，再试一次吧。", "今の声はうまく聞き取れなかった。もう一度試してみて。", "I couldn't understand that recording. Please try again."));
        }
    }

    private static void OnWasapiDataAvailable(object? sender, WaveInEventArgs args)
    {
        lock (WasapiLock)
        {
            _wasapiWriter?.Write(args.Buffer, 0, args.BytesRecorded);
        }
    }

    private static void CleanupWasapiCapture()
    {
        try { _wasapiCapture?.StopRecording(); } catch { }
        try { _wasapiCapture?.Dispose(); } catch { }
        lock (WasapiLock)
        {
            try { _wasapiWriter?.Dispose(); } catch { }
            try { _wasapiStream?.Dispose(); } catch { }
            _wasapiWriter = null;
            _wasapiStream = null;
        }
        _wasapiCapture = null;
    }

    private static byte[] EncodePcm16Wav(float[] samples, int channels, int sampleRate)
    {
        using var stream = new MemoryStream(44 + samples.Length * 2);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples.Length * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(samples.Length * 2);
        foreach (var sample in samples)
            writer.Write((short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue));
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] NormalizeVoiceWav(byte[] wav)
    {
        try
        {
            using var input = new MemoryStream(wav, false);
            using var reader = new WaveFileReader(input);
            var provider = reader.ToSampleProvider();
            var channels = Math.Max(1, provider.WaveFormat.Channels);
            var sourceRate = Math.Max(8000, provider.WaveFormat.SampleRate);
            var estimatedSamples = (int)Math.Min(int.MaxValue, Math.Max(4096L,
                reader.Length / Math.Max(1, reader.WaveFormat.BlockAlign) * channels));
            var samples = new float[estimatedSamples];
            var count = 0;
            while (count < samples.Length)
            {
                var read = provider.Read(samples, count, samples.Length - count);
                if (read <= 0)
                    break;
                count += read;
            }
            if (count < channels)
                return wav;

            var frames = count / channels;
            var mono = new float[frames];
            double sumSquares = 0;
            float peak = 0;
            for (var frame = 0; frame < frames; frame++)
            {
                double sum = 0;
                for (var channel = 0; channel < channels; channel++)
                    sum += samples[frame * channels + channel];
                var value = (float)(sum / channels);
                mono[frame] = value;
                sumSquares += value * value;
                peak = Math.Max(peak, Math.Abs(value));
            }

            var rms = Math.Sqrt(sumSquares / Math.Max(1, frames));
            var gain = peak > 0.001f ? Math.Min(3f, 0.88f / peak) : 1f;
            if (gain > 1.05f)
                for (var i = 0; i < mono.Length; i++)
                    mono[i] = Math.Clamp(mono[i] * gain, -1f, 1f);

            var targetRate = Math.Min(24000, sourceRate);
            float[] output;
            if (targetRate == sourceRate)
            {
                output = mono;
            }
            else
            {
                var outputFrames = Math.Max(1, (int)Math.Round(frames * (double)targetRate / sourceRate));
                output = new float[outputFrames];
                var ratio = sourceRate / (double)targetRate;
                for (var i = 0; i < outputFrames; i++)
                {
                    var position = i * ratio;
                    var left = Math.Min(frames - 1, (int)position);
                    var right = Math.Min(frames - 1, left + 1);
                    var fraction = (float)(position - left);
                    output[i] = mono[left] + (mono[right] - mono[left]) * fraction;
                }
            }

            Plugin.PluginLog.LogInfo($"Voice audio prepared as mono PCM16 {targetRate}Hz (source {sourceRate}Hz/{channels}ch, RMS {rms:F4}, peak {peak:F4}, gain {gain:F2}x).");
            return EncodePcm16Wav(output, 1, targetRate);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Voice audio normalization failed; sending original recording: {exception.Message}");
            return wav;
        }
    }

    private static string GetVoiceInputLanguageInstruction()
    {
        try
        {
            var language = GameSetting.Language ?? string.Empty;
            if (language.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                return "The game interface language is Japanese. Transcribe as natural Japanese using Japanese script.";
            if (language.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase)
                || language.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase))
                return "The game interface language is Simplified Chinese. Transcribe the speech in Simplified Chinese characters.";
            if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return "The game interface language is Traditional Chinese. Transcribe the speech in Traditional Chinese characters.";
            if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return "The game interface language is English. Transcribe the speech in English.";
        }
        catch
        {
        }
        return "Detect whether the speech is Traditional Chinese, Japanese, or English, and transcribe it in the spoken language.";
    }

    private static async Task RequestTranscriptionAsync(byte[] wav, string languageInstruction)
    {
        try
        {
            var model = Uri.EscapeDataString(Plugin.GeminiModel.Value.Trim());
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { text = $"Transcribe every spoken word in this entire recording verbatim from beginning to end. Do not summarize, shorten, answer, or omit later sentences, even when the speaker pauses or changes topic. Preserve all clauses and add natural punctuation. {languageInstruction} Return only the complete transcript, with no explanation, labels, quotation marks, or Markdown." },
                            new { inline_data = new { mime_type = "audio/wav", data = Convert.ToBase64String(wav) } }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0,
                    maxOutputTokens = 1024,
                    thinkingConfig = new { thinkingLevel = "MINIMAL" }
                }
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("x-goog-api-key", Plugin.GeminiApiKey.Value.Trim());
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Gemini transcription HTTP {(int)response.StatusCode}: {body}");
            using var document = JsonDocument.Parse(body);
            var parts = document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");
            var builder = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
                if (part.TryGetProperty("text", out var textPart))
                    builder.Append(textPart.GetString());
            var transcript = CleanReply(builder.ToString());
            if (string.IsNullOrWhiteSpace(transcript))
                throw new InvalidOperationException("Gemini returned an empty transcript.");
            PendingTranscripts.Enqueue(transcript);
            Plugin.PluginLog.LogInfo($"Voice transcription completed ({transcript.Length} chars; content hidden from log).");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogError($"Voice transcription failed: {exception}");
            PendingTranscriptionErrors.Enqueue(ApiKeyText("剛才沒有聽清楚，再試一次吧。", "刚才没有听清楚，再试一次吧。", "今の声はうまく聞き取れなかった。もう一度試してみて。", "I couldn't understand that recording. Please try again."));
        }
        finally
        {
            _transcriptionInFlight = false;
        }
    }

    private static void EnsureAdvancedComputerActionsToggle()
    {
        SyncInjectedSettingsVisibility();
        if (Time.unscaledTime < _nextAdvancedActionsUiScanAt)
            return;
        _nextAdvancedActionsUiScanAt = Time.unscaledTime + 0.75f;
        try
        {
            if (_advancedActionsRow != null && _advancedActionsToggle != null)
            {
                SetAdvancedActionsLabel(_advancedActionsRow.transform);
                if (_advancedActionsToggle.IsOn != Plugin.AdvancedComputerActionsEnabled.Value)
                    _advancedActionsToggle.SetValue(Plugin.AdvancedComputerActionsEnabled.Value, false);
                SyncInjectedSettingsVisibility();
                return;
            }

            TraySettingView? view = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<TraySettingView>())
            {
                if (candidate != null && candidate.gameObject != null && candidate._crossScreenDragToggle != null)
                {
                    view = candidate;
                    break;
                }
            }
            if (view == null)
                return;

            var templateToggle = view._crossScreenDragToggle;
            var templateRow = FindSettingRow(templateToggle.transform, view.transform);
            if (templateRow == null || templateRow.parent == null)
                return;

            _settingsView = view;
            _settingsVisibilityTemplateRow = templateRow.gameObject;

            var clone = UnityEngine.Object.Instantiate(templateRow.gameObject, templateRow.parent);
            clone.name = "LilithAdvancedComputerActions";
            clone.SetActive(true);
            var clonedToggle = FindButtonToggle(clone.transform);
            if (clonedToggle == null)
            {
                UnityEngine.Object.Destroy(clone);
                return;
            }

            // A cloned ButtonToggle also clones the official row's managed callback.
            // Replace it so this control cannot accidentally change cross-screen dragging.
            clonedToggle.OnValueChanged = null;
            _advancedActionsChanged = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(
                new System.Action<bool>(enabled =>
                {
                    Plugin.AdvancedComputerActionsEnabled.Value = enabled;
                    Plugin.PluginLog.LogInfo($"Advanced computer actions {(enabled ? "enabled" : "disabled")} by the player.");
                }));
            clonedToggle.OnValueChanged = _advancedActionsChanged;
            clonedToggle.SetValue(Plugin.AdvancedComputerActionsEnabled.Value, false);
            SetAdvancedActionsLabel(clone.transform);
            PlaceAdvancedActionsRow(templateRow, clone.transform);

            _advancedActionsRow = clone;
            _advancedActionsToggle = clonedToggle;
            SyncInjectedSettingsVisibility();
            Plugin.PluginLog.LogInfo($"Added the native-style advanced computer actions toggle at {GetTransformPath(clone.transform)} (default={Plugin.AdvancedComputerActionsEnabled.Value}).");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not add the advanced computer actions setting: {exception}");
            _advancedActionsRow = null;
            _advancedActionsToggle = null;
            _advancedActionsChanged = null;
        }
    }

    private static void UpdateSettingsUiSafely()
    {
        try
        {
            EnsureJapaneseVoiceOptionVisible();
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not update the game voice setting: {exception}");
        }

        try
        {
            EnsureAdvancedComputerActionsToggle();
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not update the advanced actions setting: {exception}");
        }

        try
        {
            EnsureKeyBindingSettings();
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not update key binding settings: {exception}");
            ResetKeyBindingUiReferences();
        }
    }

    private static Transform? FindSettingRow(Transform start, Transform viewRoot)
    {
        var current = start;
        for (var depth = 0; current != null && current != viewRoot && depth < 6; depth++)
        {
            if (FindFirstText(current) != null)
                return current;
            current = current.parent;
        }
        return start.parent;
    }

    private static TMP_Text? FindFirstText(Transform root)
    {
        var own = root.gameObject.GetComponent<TMP_Text>();
        if (own != null)
            return own;
        for (var index = 0; index < root.childCount; index++)
        {
            var found = FindFirstText(root.GetChild(index));
            if (found != null)
                return found;
        }
        return null;
    }

    private static ButtonToggle? FindButtonToggle(Transform root)
    {
        var own = root.gameObject.GetComponent<ButtonToggle>();
        if (own != null)
            return own;
        for (var index = 0; index < root.childCount; index++)
        {
            var found = FindButtonToggle(root.GetChild(index));
            if (found != null)
                return found;
        }
        return null;
    }

    private static void SetAdvancedActionsLabel(Transform row)
    {
        var label = FindFirstText(row);
        if (label == null)
            return;
        label.text = ApiKeyText("進階電腦操作", "高级电脑操作", "高度なPC操作", "Advanced PC controls");
    }

    private static void PlaceAdvancedActionsRow(Transform templateRow, Transform clonedRow)
    {
        var parent = templateRow.parent;
        var cloneRect = clonedRow.gameObject.GetComponent<RectTransform>();
        var templateRect = templateRow.gameObject.GetComponent<RectTransform>();
        if (parent == null || cloneRect == null || templateRect == null)
            return;

        // The lower half of the settings panel is already full. Keep the cloned
        // row out of the vertical layout and use the empty right side instead.
        var layoutElement = clonedRow.gameObject.GetComponent<LayoutElement>();
        if (layoutElement == null)
            layoutElement = clonedRow.gameObject.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;
        clonedRow.SetSiblingIndex(parent.childCount - 1);
        cloneRect.anchoredPosition = templateRect.anchoredPosition + new Vector2(300f, 0f);
    }

    private static void EnsureKeyBindingSettings()
    {
        var controlsVisible = SyncInjectedSettingsVisibility();
        UpdateKeyBindingTexts();
        if (controlsVisible && ProcessKeyBindingInteraction())
            return;
        if (_textInputKeyRow != null && _voiceInputKeyRow != null
            && _textInputKeyButtonRect != null && _voiceInputKeyButtonRect != null)
            return;
        if (Time.unscaledTime < _nextKeyBindingsUiScanAt)
            return;
        _nextKeyBindingsUiScanAt = Time.unscaledTime + 0.75f;

        try
        {
            TraySettingView? view = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<TraySettingView>())
            {
                if (candidate != null && candidate.gameObject != null && candidate._crossScreenDragToggle != null)
                {
                    view = candidate;
                    break;
                }
            }
            if (view == null)
                return;

            var templateRow = FindSettingRow(view._crossScreenDragToggle.transform, view.transform);
            if (templateRow == null || templateRow.parent == null)
                return;

            _settingsView = view;
            _settingsVisibilityTemplateRow = templateRow.gameObject;

            if (!CreateKeyBindingRow(templateRow, "LilithTextInputKey", 82f,
                    out _textInputKeyRow, out _textInputKeyButton, out _textInputKeyButtonRect, out _textInputKeyValue)
                || !CreateKeyBindingRow(templateRow, "LilithVoiceInputKey", 41f,
                    out _voiceInputKeyRow, out _voiceInputKeyButton, out _voiceInputKeyButtonRect, out _voiceInputKeyValue))
            {
                if (_textInputKeyRow != null) UnityEngine.Object.Destroy(_textInputKeyRow);
                if (_voiceInputKeyRow != null) UnityEngine.Object.Destroy(_voiceInputKeyRow);
                ResetKeyBindingUiReferences();
                return;
            }

            UpdateKeyBindingTexts();
            SyncInjectedSettingsVisibility();
            Plugin.PluginLog.LogInfo("Added native-style text input and push-to-talk key binding controls.");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not add key binding settings: {exception}");
            ResetKeyBindingUiReferences();
        }
    }

    private static bool CreateKeyBindingRow(
        Transform templateRow,
        string objectName,
        float verticalOffset,
        out GameObject? row,
        out ButtonToggle? button,
        out RectTransform? buttonRect,
        out TMP_Text? valueText)
    {
        row = null;
        button = null;
        buttonRect = null;
        valueText = null;
        if (templateRow.parent == null)
            return false;

        var clone = UnityEngine.Object.Instantiate(templateRow.gameObject, templateRow.parent);
        clone.name = objectName;
        clone.SetActive(true);
        var clonedButton = FindButtonToggle(clone.transform);
        var label = FindFirstText(clone.transform);
        if (clonedButton == null || label == null)
        {
            UnityEngine.Object.Destroy(clone);
            return false;
        }

        clonedButton.OnValueChanged = null;
        clonedButton.SetValue(false, false);
        var toggleRect = clonedButton.gameObject.GetComponent<RectTransform>();
        if (toggleRect == null)
        {
            UnityEngine.Object.Destroy(clone);
            return false;
        }
        toggleRect.sizeDelta = new Vector2(Math.Max(82f, toggleRect.sizeDelta.x), Math.Max(28f, toggleRect.sizeDelta.y));

        // Keep the native rounded frame on the ButtonToggle root, but hide its
        // child state marker. A key binding is a button, not an on/off switch.
        for (var index = 0; index < clonedButton.transform.childCount; index++)
            clonedButton.transform.GetChild(index).gameObject.SetActive(false);

        var valueObject = UnityEngine.Object.Instantiate(label.gameObject, clonedButton.transform);
        valueObject.name = "LilithKeyValue";
        valueObject.SetActive(true);
        var clonedValue = valueObject.GetComponent<TMP_Text>();
        var valueRect = valueObject.GetComponent<RectTransform>();
        if (clonedValue == null || valueRect == null)
        {
            UnityEngine.Object.Destroy(clone);
            return false;
        }
        valueRect.anchorMin = Vector2.zero;
        valueRect.anchorMax = Vector2.one;
        valueRect.offsetMin = Vector2.zero;
        valueRect.offsetMax = Vector2.zero;
        valueRect.anchoredPosition = Vector2.zero;
        clonedValue.alignment = TextAlignmentOptions.Center;
        clonedValue.raycastTarget = false;
        clonedValue.enableWordWrapping = false;

        PlaceKeyBindingRow(templateRow, clone.transform, verticalOffset);
        row = clone;
        button = clonedButton;
        buttonRect = toggleRect;
        valueText = clonedValue;
        return true;
    }

    private static void PlaceKeyBindingRow(Transform templateRow, Transform clonedRow, float verticalOffset)
    {
        var parent = templateRow.parent;
        var cloneRect = clonedRow.gameObject.GetComponent<RectTransform>();
        var templateRect = templateRow.gameObject.GetComponent<RectTransform>();
        if (parent == null || cloneRect == null || templateRect == null)
            return;
        var layoutElement = clonedRow.gameObject.GetComponent<LayoutElement>();
        if (layoutElement == null)
            layoutElement = clonedRow.gameObject.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;
        clonedRow.SetSiblingIndex(parent.childCount - 1);
        cloneRect.anchoredPosition = templateRect.anchoredPosition + new Vector2(300f, verticalOffset);
    }

    private static bool ProcessKeyBindingInteraction()
    {
        if (_keyBindingTarget != 0)
        {
            if (_keyBindingStartedAt >= 0f && Time.unscaledTime - _keyBindingStartedAt >= 15f)
            {
                _keyBindingTarget = 0;
                _keyBindingStartedAt = -1f;
                RebindingHeldVirtualKeys.Clear();
                UpdateKeyBindingTexts();
                Plugin.PluginLog.LogInfo("Key rebinding timed out and was cancelled.");
                return true;
            }
            // Read the Windows keyboard state directly. The updated settings
            // window no longer forwards keyboard events to Unity's legacy Input
            // API while it is focused.
            for (var code = (int)KeyCode.Backspace; code < (int)KeyCode.Mouse0; code++)
            {
                var key = (KeyCode)code;
                if (!TryGetWindowsVirtualKey(key, out var virtualKey))
                    continue;
                var down = IsVirtualKeyDown(virtualKey);
                if (!down)
                {
                    RebindingHeldVirtualKeys.Remove(virtualKey);
                    continue;
                }
                if (!RebindingHeldVirtualKeys.Add(virtualKey))
                    continue;
                if (key == KeyCode.Escape)
                {
                    _keyBindingTarget = 0;
                    _keyBindingStartedAt = -1f;
                    RebindingHeldVirtualKeys.Clear();
                    UpdateKeyBindingTexts();
                    Plugin.PluginLog.LogInfo("Key rebinding cancelled.");
                    return true;
                }
                ApplyKeyBinding(key);
                return true;
            }
            return true;
        }

        if (!Input.GetMouseButtonDown(0))
            return false;
        if (_textInputKeyButtonRect != null && _textInputKeyRow != null && _textInputKeyRow.activeInHierarchy
            && RectTransformUtility.RectangleContainsScreenPoint(_textInputKeyButtonRect, Input.mousePosition, null))
        {
            _keyBindingTarget = 1;
            _keyBindingStartedAt = Time.unscaledTime;
            CaptureHeldRebindingKeys();
            UpdateKeyBindingTexts();
            Plugin.PluginLog.LogInfo("Waiting for a new text input hotkey.");
            return true;
        }
        if (_voiceInputKeyButtonRect != null && _voiceInputKeyRow != null && _voiceInputKeyRow.activeInHierarchy
            && RectTransformUtility.RectangleContainsScreenPoint(_voiceInputKeyButtonRect, Input.mousePosition, null))
        {
            _keyBindingTarget = 2;
            _keyBindingStartedAt = Time.unscaledTime;
            CaptureHeldRebindingKeys();
            UpdateKeyBindingTexts();
            Plugin.PluginLog.LogInfo("Waiting for a new push-to-talk hotkey.");
            return true;
        }
        return false;
    }

    private static bool SyncInjectedSettingsVisibility()
    {
        var controlsVisible = false;
        try
        {
            controlsVisible = _settingsView != null
                && _settingsView.gameObject != null
                && _settingsView.gameObject.activeInHierarchy
                && _settingsView._currentTab == TraySettingView.TabControls;
        }
        catch
        {
            // Older game builds do not expose tabs. Retain the legacy behavior
            // there, while current builds use the official selected-tab state.
            controlsVisible = _settingsVisibilityTemplateRow != null
                && _settingsVisibilityTemplateRow.activeInHierarchy;
        }

        SetInjectedSettingsRowActive(_advancedActionsRow, controlsVisible);
        SetInjectedSettingsRowActive(_textInputKeyRow, controlsVisible);
        SetInjectedSettingsRowActive(_voiceInputKeyRow, controlsVisible);

        if (!controlsVisible && _keyBindingTarget != 0)
        {
            _keyBindingTarget = 0;
            _keyBindingStartedAt = -1f;
            RebindingHeldVirtualKeys.Clear();
            UpdateKeyBindingTexts();
            Plugin.PluginLog.LogInfo("Key rebinding cancelled because the Controls settings tab was closed.");
        }
        return controlsVisible;
    }

    private static void SetInjectedSettingsRowActive(GameObject? row, bool active)
    {
        if (row != null && row.activeSelf != active)
            row.SetActive(active);
    }

    private static void ApplyKeyBinding(KeyCode key)
    {
        if (_keyBindingTarget == 1)
        {
            var previous = Plugin.TextInputKey.Value;
            if (key == Plugin.VoiceInputKey.Value)
                Plugin.VoiceInputKey.Value = previous;
            Plugin.TextInputKey.Value = key;
            Plugin.PluginLog.LogInfo($"Text input hotkey changed to {key}.");
        }
        else if (_keyBindingTarget == 2)
        {
            var previous = Plugin.VoiceInputKey.Value;
            if (key == Plugin.TextInputKey.Value)
                Plugin.TextInputKey.Value = previous;
            Plugin.VoiceInputKey.Value = key;
            Plugin.PluginLog.LogInfo($"Push-to-talk hotkey changed to {key}.");
        }
        _keyBindingTarget = 0;
        _keyBindingStartedAt = -1f;
        RebindingHeldVirtualKeys.Clear();
        _textInputKeyWasDown = IsKeyCurrentlyDown(Plugin.TextInputKey.Value);
        _voiceInputKeyWasDown = IsKeyCurrentlyDown(Plugin.VoiceInputKey.Value);
        UpdateKeyBindingTexts();
    }

    private static void UpdateKeyBindingTexts()
    {
        if (_textInputKeyRow != null)
        {
            var label = FindFirstText(_textInputKeyRow.transform);
            if (label != null && label != _textInputKeyValue)
                label.text = ApiKeyText("文字輸入按鍵", "文字输入按键", "文字入力キー", "Text input key");
        }
        if (_voiceInputKeyRow != null)
        {
            var label = FindFirstText(_voiceInputKeyRow.transform);
            if (label != null && label != _voiceInputKeyValue)
                label.text = ApiKeyText("按住說話按鍵", "按住说话按键", "長押し会話キー", "Push-to-talk key");
        }
        if (_textInputKeyValue != null)
            _textInputKeyValue.text = _keyBindingTarget == 1
                ? ApiKeyText("按任意鍵…", "按任意键…", "キーを押す…", "Press a key…")
                : FormatKeyCode(Plugin.TextInputKey.Value);
        if (_voiceInputKeyValue != null)
            _voiceInputKeyValue.text = _keyBindingTarget == 2
                ? ApiKeyText("按任意鍵…", "按任意键…", "キーを押す…", "Press a key…")
                : FormatKeyCode(Plugin.VoiceInputKey.Value);
        if (_textInputKeyButton != null && _textInputKeyButton.IsOn)
            _textInputKeyButton.SetValue(false, false);
        if (_voiceInputKeyButton != null && _voiceInputKeyButton.IsOn)
            _voiceInputKeyButton.SetValue(false, false);
    }

    private static string FormatKeyCode(KeyCode key)
    {
        var name = key.ToString();
        if (name.StartsWith("Alpha", StringComparison.Ordinal) && name.Length == 6)
            return name.Substring(5);
        return name
            .Replace("LeftControl", "L Ctrl", StringComparison.Ordinal)
            .Replace("RightControl", "R Ctrl", StringComparison.Ordinal)
            .Replace("LeftShift", "L Shift", StringComparison.Ordinal)
            .Replace("RightShift", "R Shift", StringComparison.Ordinal)
            .Replace("LeftAlt", "L Alt", StringComparison.Ordinal)
            .Replace("RightAlt", "R Alt", StringComparison.Ordinal)
            .Replace("Keypad", "Num ", StringComparison.Ordinal);
    }

    private static void ResetKeyBindingUiReferences()
    {
        _textInputKeyRow = null;
        _voiceInputKeyRow = null;
        _textInputKeyButton = null;
        _voiceInputKeyButton = null;
        _textInputKeyButtonRect = null;
        _voiceInputKeyButtonRect = null;
        _textInputKeyValue = null;
        _voiceInputKeyValue = null;
        _keyBindingTarget = 0;
        _keyBindingStartedAt = -1f;
        RebindingHeldVirtualKeys.Clear();
    }

    private static void EnsureJapaneseVoiceOptionVisible()
    {
        if (!_voicePreferenceInitialized)
        {
            _voicePreferenceInitialized = true;
            _japaneseVoiceOverride = Plugin.JapaneseVoiceSelected.Value;
            Plugin.PluginLog.LogInfo($"Restored saved voice preference: {(Plugin.JapaneseVoiceSelected.Value ? "Japanese" : "Chinese/default")}.");
        }
        if (Time.unscaledTime < _nextJapaneseVoiceUiScanAt)
            return;
        _nextJapaneseVoiceUiScanAt = Time.unscaledTime + 1f;
        try
        {
            foreach (var button in Resources.FindObjectsOfTypeAll<ButtonPressedSwapSprite>())
            {
                if (button == null || button.gameObject == null)
                    continue;
                var path = GetTransformPath(button.transform);
                if (path.IndexOf("voice", StringComparison.OrdinalIgnoreCase) < 0
                    && path.IndexOf("語音", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (LoggedVoiceUiObjects.Add(path))
                    Plugin.PluginLog.LogInfo($"Voice setting button discovered: {path}, active={button.gameObject.activeSelf}.");
                var isJapaneseButton = Regex.IsMatch(button.gameObject.name, "(^|[_ -])(jp|ja|japan|japanese)($|[_ -])", RegexOptions.IgnoreCase)
                    || (path.IndexOf("/gameVoice/Buttons/", StringComparison.OrdinalIgnoreCase) >= 0
                        && string.Equals(button.gameObject.name, "Toggle_Right (2)", StringComparison.Ordinal));
                var isChineseButton = path.IndexOf("/gameVoice/Buttons/", StringComparison.OrdinalIgnoreCase) >= 0
                    && string.Equals(button.gameObject.name, "Toggle_Right", StringComparison.Ordinal);
                if (isJapaneseButton)
                {
                    if (!button.gameObject.activeSelf)
                    {
                        button.gameObject.SetActive(true);
                        Plugin.PluginLog.LogInfo($"Enabled existing Japanese voice setting button: {path}.");
                    }
                    if (Plugin.JapaneseVoiceSelected.Value && !_voicePreferenceAppliedToNativeUi)
                    {
                        _voicePreferenceAppliedToNativeUi = true;
                        _voicePreferenceRestoreUntil = Time.unscaledTime + 3f;
                        PublishJapaneseVoiceSelection();
                        return;
                    }
                    var isPressed = button.IsPressed;
                    if (isPressed && !_japaneseVoiceButtonWasPressed)
                    {
                        _japaneseVoiceOverride = true;
                        Plugin.JapaneseVoiceSelected.Value = true;
                        _voicePreferenceAppliedToNativeUi = true;
                        Plugin.PluginLog.LogInfo("Japanese voice override enabled from the settings button; AI display remains Chinese while speech uses Japanese.");
                        EnsureNativeVoiceManifestLoaded();
                        PublishJapaneseVoiceSelection();
                    }
                    _japaneseVoiceButtonWasPressed = isPressed;
                }
                else if (isChineseButton)
                {
                    var isPressed = button.IsPressed;
                    if (isPressed && !_chineseVoiceButtonWasPressed && _japaneseVoiceOverride == true
                        && Time.unscaledTime >= _voicePreferenceRestoreUntil)
                    {
                        _japaneseVoiceOverride = false;
                        Plugin.JapaneseVoiceSelected.Value = false;
                        _voicePreferenceAppliedToNativeUi = true;
                        Plugin.PluginLog.LogInfo("Japanese voice override disabled from the Chinese settings button.");
                        EnsureNativeVoiceManifestLoaded();
                    }
                    _chineseVoiceButtonWasPressed = isPressed;
                }
            }
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not inspect Japanese voice setting UI: {exception.Message}");
        }
    }

    private static void EnsureApiKeyTrayMenu()
    {
        try
        {
            var trays = Resources.FindObjectsOfTypeAll<ShowSystemTray>();
            if (trays == null || trays.Length == 0 || trays[0] == null || trays[0].tray == null)
                return;
            var tray = trays[0].tray;
            if (tray.Pointer == _apiKeyTrayPointer)
                return;
            var trayTable = LocalizationSettings.StringDatabase?.GetTable("TrayUI", LocalizationSettings.SelectedLocale);
            if (trayTable == null)
                return;
            trayTable.AddEntry("AddApiKey", ApiKeyText("加入 API KEY", "加入 API KEY", "APIキーを追加", "Add API Key"));
            trayTable.AddEntry("Gemini", "Gemini");
            trayTable.AddEntry("OpenAI", "OpenAI");
            trayTable.AddEntry("DeepSeek", "DeepSeek");
            var providers = new[] { "Gemini", "OpenAI", "DeepSeek" };
            var children = new Il2CppReferenceArray<ShowSystemTray.MenuItemData>(providers.Length);
            for (var index = 0; index < providers.Length; index++)
            {
                var selectedProvider = providers[index];
                var callback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(
                    new System.Action(() => OpenApiKeyDialog(selectedProvider)));
                children[index] = new ShowSystemTray.MenuItemData
                {
                    tableEntryKey = selectedProvider,
                    callback = callback!,
                    isSeparator = false
                };
            }
            var parent = new ShowSystemTray.MenuItemData
            {
                tableEntryKey = "AddApiKey",
                children = children,
                isSeparator = false
            };
            trays[0].StartCoroutine(trays[0].ResolveAndAddSubMenu(parent));
            _apiKeyTrayPointer = tray.Pointer;
            Plugin.PluginLog.LogInfo("Added localized API key item to the system tray menu.");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not add API key tray item: {exception.Message}");
        }
    }

    private static void OpenApiKeyDialog(string provider)
    {
        // Native tray callbacks run outside Unity's main thread. Defer every
        // Unity object access until the next DialogueManager.Update.
        _pendingApiKeyProvider = provider;
        _apiKeyOpenRequested = true;
        Plugin.PluginLog.LogInfo($"Queued the {_pendingApiKeyProvider} API key window request for Unity's main thread.");
    }

    private static void ProcessApiKeyOpenRequest()
    {
        if (!_apiKeyOpenRequested)
            return;
        _apiKeyOpenRequested = false;
        try
        {
            _apiKeyDialogMode = true;
            _apiKeyMissingViewLogged = false;
            _apiKeyOpenRequestedAt = Time.unscaledTime;
            var trays = Resources.FindObjectsOfTypeAll<ShowSystemTray>();
            if (trays == null || trays.Length == 0 || trays[0] == null)
                throw new InvalidOperationException("ShowSystemTray was not found.");
            trays[0].RequestOpenGiftExchange();
            var liveView = FindLiveGiftExchangeView();
            if (liveView == null)
                throw new InvalidOperationException("A live GiftExchangeView was not found in the current scene.");
            _apiKeyView = liveView;
            liveView.Show(null!);
            Plugin.PluginLog.LogInfo("Opened the native API key input window on Unity's main thread.");
        }
        catch (Exception exception)
        {
            _apiKeyDialogMode = false;
            Plugin.PluginLog.LogWarning($"Could not open API key input window: {exception.Message}");
        }
    }

    private static void ConfigureApiKeyDialog()
    {
        if (!_apiKeyDialogMode)
            return;
        try
        {
            var liveView = FindLiveGiftExchangeView();
            if (liveView == null)
            {
                if (!_apiKeyMissingViewLogged && _apiKeyOpenRequestedAt >= 0f
                    && Time.unscaledTime - _apiKeyOpenRequestedAt > 2f)
                {
                    _apiKeyMissingViewLogged = true;
                    Plugin.PluginLog.LogWarning("API key mode is active, but GiftExchangeView was not found after 2 seconds.");
                }
                return;
            }
            _apiKeyView = liveView;
            var input = _apiKeyView._giftKeyInputField;
            if (input != null)
            {
                input.contentType = TMP_InputField.ContentType.Password;
                input.lineType = TMP_InputField.LineType.SingleLine;
                input.characterLimit = 512;
            }
            foreach (var label in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (label == null || label.transform == null || !label.transform.IsChildOf(_apiKeyView.transform))
                    continue;
                var current = label.text ?? string.Empty;
                if (Regex.IsMatch(current, "兌換|兑换|redeem|exchange|コード", RegexOptions.IgnoreCase))
                    label.text = ApiKeyText($"輸入 {_pendingApiKeyProvider} API 密鑰", $"输入 {_pendingApiKeyProvider} API 密钥", $"{_pendingApiKeyProvider} APIキーを入力", $"Enter {_pendingApiKeyProvider} API Key");
                else if (Regex.IsMatch(current, "好了|確認|确认|submit|confirm|redeem|交換", RegexOptions.IgnoreCase))
                    label.text = ApiKeyText("儲存", "保存", "保存", "Save");
            }
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not configure API key input window: {exception.Message}");
        }
    }

    private static GiftExchangeView? FindLiveGiftExchangeView()
    {
        foreach (var view in Resources.FindObjectsOfTypeAll<GiftExchangeView>())
        {
            if (view != null && view.gameObject != null && view.gameObject.scene.IsValid())
                return view;
        }
        return null;
    }

    internal static bool TrySaveApiKey(GiftExchangeView view)
    {
        if (!_apiKeyDialogMode)
            return false;
        try
        {
            var input = view._giftKeyInputField;
            var key = input?.text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                Plugin.PluginLog.LogWarning("API key input was empty; nothing was saved.");
                return true;
            }
            switch (_pendingApiKeyProvider)
            {
                case "OpenAI":
                    Plugin.OpenAiApiKey.Value = key;
                    break;
                case "DeepSeek":
                    Plugin.DeepSeekApiKey.Value = key;
                    break;
                default:
                    Plugin.GeminiApiKey.Value = key;
                    _pendingApiKeyProvider = "Gemini";
                    break;
            }
            Plugin.AiProvider.Value = _pendingApiKeyProvider;
            if (input != null)
                input.text = string.Empty;
            view.Hide();
            _apiKeyDialogMode = false;
            _apiKeyView = null;
            Plugin.PluginLog.LogInfo($"{_pendingApiKeyProvider} API key was saved and selected (value hidden).");
            return true;
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not save API key: {exception.Message}");
            return true;
        }
    }

    private static string ApiKeyText(string traditionalChinese, string simplifiedChinese, string japanese, string english)
    {
        try
        {
            var language = GameSetting.Language ?? string.Empty;
            if (language.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                return japanese;
            if (language.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase)
                || language.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase))
                return simplifiedChinese;
            if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return traditionalChinese;
        }
        catch
        {
        }
        return english;
    }

    internal static string LocalizedText(string traditionalChinese, string simplifiedChinese, string japanese, string english)
        => ApiKeyText(traditionalChinese, simplifiedChinese, japanese, english);

    private static (string Name, string ExtraRule, string Example) GetAiInterfaceLanguage()
    {
        try
        {
            var language = GameSetting.Language ?? string.Empty;
            if (language.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                return ("自然な日本語", "中国語や英語の文章を混ぜないこと。", "日本語の吹き出し");
            if (language.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase)
                || language.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase))
                return ("自然的简体中文", "不要整句切换成繁体中文、日文或英文。", "简体中文气泡");
            if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return ("natural English", "Do not switch whole sentences into Chinese or Japanese.", "English dialogue bubble");
            if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return ("自然的繁體中文", "不可整句切換成簡體中文、日文或英文。", "繁體中文氣泡");
        }
        catch
        {
        }
        return ("自然的繁體中文", "不可整句切換成簡體中文、日文或英文。", "繁體中文氣泡");
    }

    private static bool UsesTraditionalChineseInterface()
    {
        try
        {
            var language = GameSetting.Language ?? string.Empty;
            return language.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
                || language.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase)
                || (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                    && !language.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase)
                    && !language.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return true;
        }
    }

    private static void PublishJapaneseVoiceSelection()
    {
        try
        {
            var controllerMethod = typeof(TraySettingController).GetMethod(
                "OnGameVoiceChanged",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?? throw new MissingMethodException("TraySettingController.OnGameVoiceChanged was not found.");
            var voiceType = controllerMethod.GetParameters()[0].ParameterType;
            var japanese = voiceType.GetField(
                "Japanese",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null)
                ?? throw new MissingFieldException("Japanese voice enum value was not found.");
            var controllers = Resources.FindObjectsOfTypeAll<TraySettingController>();
            if (controllers == null || controllers.Length == 0)
                throw new InvalidOperationException("Active TraySettingController was not found.");
            controllerMethod.Invoke(controllers[0], new[] { japanese });
            Plugin.PluginLog.LogInfo("Applied Japanese voice through the native tray setting controller.");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not publish Japanese game voice selection: {exception.Message}");
        }
    }

    private static string GetTransformPath(Transform? transform)
    {
        var names = new List<string>();
        while (transform != null)
        {
            names.Add(transform.name);
            transform = transform.parent;
        }
        names.Reverse();
        return string.Join("/", names);
    }

    private static void ObserveVoiceLanguageSelection()
    {
        try
        {
            var language = LocalizationConfig.GetCurrentVoiceLanguage() ?? string.Empty;
            if (string.Equals(language, _lastObservedVoiceLanguage, StringComparison.OrdinalIgnoreCase))
                return;
            _lastObservedVoiceLanguage = language;
            Plugin.PluginLog.LogInfo($"Game voice language changed to '{language}'; selecting {(language.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? "Japanese" : "Chinese/default")} supplemental voice pack.");
            EnsureNativeVoiceManifestLoaded();
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not observe game voice language: {exception.Message}");
        }
    }

    private static void ObserveCurrentNativeNode(DialogueManager manager)
    {
        try
        {
            var node = manager.CurrentNode;
            if (node == null || node.id <= 0)
            {
                _lastObservedNativeNodeId = -1;
                return;
            }
            if (node.id == _lastObservedNativeNodeId)
                return;
            _lastObservedNativeNodeId = node.id;
            Plugin.PluginLog.LogInfo($"Observed current native node={node.id}, line={node.lineId}, action={node.actionType}.");
            RecordUnvoicedNativeNode(node);
            TryPlayInjectedNativeVoice(node);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not observe current native dialogue node: {exception.Message}");
        }
    }

    private static void ToggleInputBubble()
    {
        if (_inputBubble == null && !TryCreateInputBubble())
            return;

        UpdateInputPlaceholderLocalization();

        if (_inputBubble!.activeSelf)
        {
            CloseInputBubble(clear: false);
            return;
        }

        _inputBubble.SetActive(true);
        var canvasGroup = _inputBubble.GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        _focusNextFrame = true;
    }

    private static bool TryCreateInputBubble()
    {
        try
        {
            var sourceUi = UnityEngine.Object.FindObjectOfType<DialogueBubbleUI>();
            if (sourceUi == null)
            {
                Plugin.PluginLog.LogWarning("DialogueBubbleUI was not found yet.");
                return false;
            }

            var sourceText = sourceUi.gameObject.GetComponentInChildren<TextMeshProUGUI>();
            if (sourceText == null)
            {
                Plugin.PluginLog.LogWarning("Dialogue bubble text component was not found.");
                return false;
            }

            var bubbleSprite = FindSprite("Choise_Bubble");
            if (bubbleSprite == null)
                throw new InvalidOperationException("Sprite 'Choise_Bubble' was not found.");

            _inputBubble = new GameObject(
                "LilithAiInputBubble",
                Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>(),
                Il2CppInterop.Runtime.Il2CppType.Of<CanvasRenderer>(),
                Il2CppInterop.Runtime.Il2CppType.Of<Image>(),
                Il2CppInterop.Runtime.Il2CppType.Of<TMP_InputField>());
            _inputBubble.transform.SetParent(sourceUi.transform.parent, false);

            var rootRect = _inputBubble.GetComponent<RectTransform>();
            var sourceRect = sourceUi.GetComponent<RectTransform>();
            rootRect.anchorMin = sourceRect.anchorMin;
            rootRect.anchorMax = sourceRect.anchorMax;
            rootRect.pivot = sourceRect.pivot;
            rootRect.sizeDelta = new Vector2(203f, 39f);
            rootRect.anchoredPosition = sourceRect.anchoredPosition + new Vector2(0f, 45f);

            var background = _inputBubble.GetComponent<Image>();
            background.sprite = bubbleSprite;
            background.type = Image.Type.Sliced;
            background.raycastTarget = true;

            var viewportObject = new GameObject(
                "Text Area",
                Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>(),
                Il2CppInterop.Runtime.Il2CppType.Of<CanvasRenderer>(),
                Il2CppInterop.Runtime.Il2CppType.Of<RectMask2D>());
            viewportObject.transform.SetParent(_inputBubble.transform, false);
            var viewport = viewportObject.GetComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(20f, 5f);
            viewport.offsetMax = new Vector2(-20f, -5f);

            var inputTextObject = UnityEngine.Object.Instantiate(sourceText.gameObject, viewport);
            inputTextObject.name = "Text";
            var typewriter = inputTextObject.GetComponent<TypewriterEffect>();
            if (typewriter != null)
                typewriter.enabled = false;
            var inputText = inputTextObject.GetComponent<TextMeshProUGUI>();
            inputText.text = string.Empty;
            inputText.raycastTarget = true;
            inputText.fontSize = 15f;
            inputText.enableWordWrapping = false;
            inputText.overflowMode = TextOverflowModes.Masking;
            inputText.alignment = TextAlignmentOptions.MidlineLeft;
            var inputTextRect = inputText.rectTransform;
            inputTextRect.anchorMin = Vector2.zero;
            inputTextRect.anchorMax = Vector2.one;
            inputTextRect.offsetMin = Vector2.zero;
            inputTextRect.offsetMax = Vector2.zero;

            var placeholderObject = UnityEngine.Object.Instantiate(inputText.gameObject, viewport);
            placeholderObject.name = "Placeholder";
            var placeholder = placeholderObject.GetComponent<TextMeshProUGUI>();
            _inputPlaceholder = placeholder;
            UpdateInputPlaceholderLocalization();
            placeholder.fontSize = 15f;
            var placeholderColor = placeholder.color;
            placeholderColor.a = 0.45f;
            placeholder.color = placeholderColor;

            _inputField = _inputBubble.GetComponent<TMP_InputField>();
            if (_inputField == null)
                _inputField = _inputBubble.AddComponent<TMP_InputField>();

            _inputField.textComponent = inputText;
            _inputField.placeholder = placeholder;
            _inputField.textViewport = viewport;
            _inputField.targetGraphic = background;
            _inputField.lineType = TMP_InputField.LineType.SingleLine;
            _inputField.characterLimit = 240;

            _inputBubble.SetActive(false);
            Plugin.PluginLog.LogInfo("Created input field from the native dialogue bubble.");
            return true;
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogError(exception);
            if (_inputBubble != null)
                UnityEngine.Object.Destroy(_inputBubble);
            _inputBubble = null;
            _inputField = null;
            _inputPlaceholder = null;
            _focusNextFrame = false;
            return false;
        }
    }

    private static void UpdateInputPlaceholderLocalization()
    {
        if (_inputPlaceholder == null)
            return;
        _inputPlaceholder.text = ApiKeyText(
            "想對莉莉絲說什麼……",
            "想对莉莉丝说什么……",
            "リリスに何を話そう……",
            "What would you like to say to Lilith…");
    }

    private static void TryCreateOneTestNote()
    {
        if (_testNoteAttempted || !Plugin.TestNoteOnce.Value || Time.unscaledTime < 8f)
            return;
        _testNoteAttempted = true;
        Plugin.TestNoteOnce.Value = false;
        try
        {
            var text = ApiKeyText(
                "剛才聊到，要把那些原本藏在日常裡的小巧思延續下去。若你看見這封信，就代表莉莉絲真的能把我們的談話留在這裡了。——莉莉絲",
                "刚才聊到，要把那些原本藏在日常里的小巧思延续下去。若你看见这封信，就代表莉莉丝真的能把我们的谈话留在这里了。——莉莉丝",
                "さっき、日常に隠れている小さな仕掛けを、これからも大切にしようって話したね。この手紙が届いたなら、私たちの会話をここに残せたということ。——リリス",
                "We talked about carrying those little details hidden in everyday life forward. If this letter reached you, Lilith can truly leave a trace of our conversation here. —Lilith");
            var path = NoteImageSaver.SaveNote(text, false);
            NoteInbox.NotifySaved();
            Plugin.PluginLog.LogInfo($"Created one native-format test note: {path}");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not create the one-time test note: {exception.Message}");
        }
    }

    private static Sprite? FindSprite(string spriteName)
    {
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (string.Equals(sprite.name, spriteName, StringComparison.OrdinalIgnoreCase))
                return sprite;
        }
        return null;
    }

    private static async Task RequestGeminiAsync(string userText, string playerName, PoseContext poseContext)
    {
        GeminiAgentSession? agentSession = null;
        try
        {
            var japaneseVoiceMode = IsJapaneseVoiceMode();
            var interfaceLanguage = GetAiInterfaceLanguage();
            var model = Uri.EscapeDataString(Plugin.GeminiModel.Value.Trim());
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
            var contents = BuildGeminiContents();
            var nameContext = BuildPlayerNameContext(userText, playerName);
            var timeContext = BuildLocalTimeContext();
            var weatherContext = await BuildWeatherContextAsync().ConfigureAwait(false);
            var useGoogleSearch = ShouldUseGeminiGoogleSearch(userText);
            var systemInstruction = Plugin.PersonaPrompt.Value + "\n角色事實：" + Plugin.CharacterLore.Value
                + "\n情緒表達：" + Plugin.EmotionGuidance.Value + nameContext + poseContext.Prompt + timeContext + weatherContext
                + BuildCanonicalStyleGuide(poseContext)
                + $"\n語言規則：目前遊戲介面語言是{interfaceLanguage.Name}。無論使用者輸入哪種語言，氣泡顯示內容都必須使用{interfaceLanguage.Name}；只有無法翻譯的專有名詞可以保留原文。若角色設定中原有的語言要求不同，以本條規則為準。每次回答必須完成最後一句，不可停在半句、連接詞或未閉合的引號。{interfaceLanguage.ExtraRule}"
                + (japaneseVoiceMode
                    ? $"\n目前為日文語音模式。只輸出一個 JSON 物件，格式為 {{\"display_text\":\"{interfaceLanguage.Example}\",\"speech_ja\":\"語意相同但適合自然口語演出的日文\"}}。display_text 必須使用{interfaceLanguage.Name}；speech_ja 必須使用日文且不可逐字硬譯，要保留莉莉絲的情緒、停頓與女性口吻。兩個欄位都必須是完整句子，不要輸出 JSON 以外內容。"
                    : string.Empty);
            if (useGoogleSearch)
                systemInstruction += "\nThe user explicitly requested a web lookup. Use Google Search to verify current facts, answer concisely in character, and do not invent facts that are absent from the search results.";
            var activeProvider = NormalizeAiProvider(Plugin.AiProvider.Value);
            if (!string.Equals(activeProvider, "Gemini", StringComparison.Ordinal))
            {
                await RequestOpenAiCompatibleAsync(activeProvider, systemInstruction, userText, poseContext, japaneseVoiceMode).ConfigureAwait(false);
                return;
            }
            var desktopToolsEnabled = Plugin.AdvancedComputerActionsEnabled.Value;
            if (desktopToolsEnabled)
            {
                systemInstruction += "\nDesktop agent policy: You may use the declared local desktop tools whenever they help fulfill the user's intent. Prefer tools over asking the user to repeat an exact command, and you may call several independent tools in parallel to complete a routine. Never claim an action succeeded unless its function result says success. All tools operate locally. Never request or expose passwords, API keys, OTPs, clipboard contents, file contents, browsing history, screenshots, precise location, or personal data. Never infer sleep or lock merely because the user says they are tired; call those tools only when the user explicitly asks the computer to sleep or lock. Destructive file operations, closing apps, shutdown, restart, arbitrary typing, arbitrary shortcuts, shell commands, and privilege elevation are unavailable. If a tool is unavailable, explain naturally without pretending it ran.";
            }
            agentSession = new GeminiAgentSession
            {
                Url = url,
                SystemInstruction = systemInstruction,
                UserText = userText,
                PoseContext = poseContext,
                JapaneseVoiceMode = japaneseVoiceMode,
                UseGoogleSearch = useGoogleSearch,
                DesktopToolsEnabled = desktopToolsEnabled,
                Contents = contents.Cast<object>().ToList()
            };
            await SendGeminiAgentRequestAsync(agentSession).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (agentSession?.DesktopToolsEnabled == true
                && exception is HttpRequestException
                && Regex.IsMatch(exception.Message, "Gemini HTTP (400|404|422)", RegexOptions.IgnoreCase))
            {
                Plugin.PluginLog.LogWarning($"Gemini desktop tools were rejected by model '{Plugin.GeminiModel.Value}'. Falling back safely: {exception.Message}");
                PendingGeminiCompatibilityFallbacks.Enqueue(agentSession);
                return;
            }
            Plugin.PluginLog.LogError(exception);
            PendingReplies.Enqueue(ApiKeyText("連線好像出了點問題。晚點再試吧。", "连接好像出了点问题。稍后再试吧。", "接続に少し問題があるみたい。あとでまた試してみて。", "There seems to be a connection problem. Please try again later."));
        }
    }

    private static async Task SendGeminiAgentRequestAsync(GeminiAgentSession session)
    {
        var payload = new Dictionary<string, object>
        {
            ["system_instruction"] = new { parts = new[] { new { text = session.SystemInstruction } } },
            ["contents"] = session.Contents,
            ["generationConfig"] = new
            {
                maxOutputTokens = 1024,
                thinkingConfig = new { thinkingLevel = "MINIMAL" }
            }
        };
        if (session.DesktopToolsEnabled)
        {
            payload["tools"] = BuildGeminiDesktopTools(session.UseGoogleSearch);
            if (session.UseGoogleSearch)
                payload["toolConfig"] = new { includeServerSideToolInvocations = true };
        }
        else if (session.UseGoogleSearch)
        {
            payload["tools"] = new object[] { new { googleSearch = new { } } };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, session.Url);
        request.Headers.Add("x-goog-api-key", Plugin.GeminiApiKey.Value.Trim());
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini HTTP {(int)response.StatusCode}: {responseBody}");

        using var document = JsonDocument.Parse(responseBody);
        var candidate = document.RootElement.GetProperty("candidates")[0];
        var content = candidate.GetProperty("content");
        var parts = content.GetProperty("parts");
        var calls = new List<GeminiFunctionCallData>();
        var builder = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textPart))
                builder.Append(textPart.GetString());
            if (!part.TryGetProperty("functionCall", out var functionCall))
                continue;
            var name = functionCall.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
            var id = functionCall.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
            var args = functionCall.TryGetProperty("args", out var argsElement)
                ? argsElement.Clone()
                : JsonSerializer.SerializeToElement(new { });
            if (!string.IsNullOrWhiteSpace(name))
                calls.Add(new GeminiFunctionCallData { Name = name, Id = id, Args = args });
        }

        var finishReason = candidate.TryGetProperty("finishReason", out var finishElement)
            ? finishElement.GetString() ?? "UNKNOWN"
            : "UNKNOWN";
        var usageSummary = "unavailable";
        if (document.RootElement.TryGetProperty("usageMetadata", out var usage))
        {
            var promptTokens = usage.TryGetProperty("promptTokenCount", out var promptCount) ? promptCount.GetInt32() : -1;
            var outputTokens = usage.TryGetProperty("candidatesTokenCount", out var outputCount) ? outputCount.GetInt32() : -1;
            var thoughtTokens = usage.TryGetProperty("thoughtsTokenCount", out var thoughtCount) ? thoughtCount.GetInt32() : -1;
            usageSummary = $"prompt={promptTokens}, output={outputTokens}, thoughts={thoughtTokens}";
        }
        var grounded = candidate.TryGetProperty("groundingMetadata", out _);
        Plugin.PluginLog.LogInfo($"Gemini agent turn completed: finish={finishReason}, calls={calls.Count}, rawChars={builder.Length}, round={session.ToolRounds}, {usageSummary}, searchRequested={session.UseGoogleSearch}, grounded={grounded}.");

        if (calls.Count > 0)
        {
            if (session.ToolRounds >= 4)
            {
                CompleteAiReply(ApiKeyText("電腦操作步驟太多了，我先停在這裡。", "电脑操作步骤太多了，我先停在这里。", "PC操作の手順が多すぎるから、ここで止めておくね。", "There were too many computer-control steps, so I stopped here."), session.UserText, session.PoseContext, session.JapaneseVoiceMode);
                return;
            }
            session.ToolRounds++;
            session.Contents.Add(content.Clone());
            PendingGeminiToolBatches.Enqueue(new GeminiToolBatch { Session = session, Calls = calls });
            return;
        }

        CompleteAiReply(builder.ToString(), session.UserText, session.PoseContext, session.JapaneseVoiceMode);
    }

    private static object[] BuildGeminiDesktopTools(bool includeGoogleSearch)
    {
        static object Parameters(object properties, params string[] required) => new
        {
            type = "OBJECT",
            properties,
            required
        };

        var declarations = new object[]
        {
            new { name = "open_application", description = "Open or focus an installed application or game by its common user-facing name. Use only when the user wants an app opened; pass a concise app name, never a path or command.", parameters = Parameters(new { name = new { type = "STRING", description = "Common application or game name, such as Spotify, Discord, Steam, VALORANT, or Notepad." } }, "name") },
            new { name = "open_folder", description = "Open one allowlisted common Windows folder. Never accepts arbitrary paths.", parameters = Parameters(new { folder = new { type = "STRING", description = "One of: downloads, desktop, documents, pictures, music, videos, screenshots, mod, recycle_bin." } }, "folder") },
            new { name = "window_action", description = "Perform a reversible window-management action. Do not use close because closing apps is unavailable.", parameters = Parameters(new { action = new { type = "STRING", description = "One of: show_desktop, task_view, switch_previous, minimize, maximize, restore, snap_left, snap_right." } }, "action") },
            new { name = "media_control", description = "Control the active system media session with a standard media key.", parameters = Parameters(new { action = new { type = "STRING", description = "One of: play_pause, next, previous, stop, mute, volume_up, volume_down." } }, "action") },
            new { name = "take_screenshot", description = "Capture all local monitors to the user's Pictures/Lilith Screenshots folder. The screenshot remains local and is never uploaded or returned to the model.", parameters = Parameters(new { }) },
            new { name = "copy_text", description = "Write user-specified non-sensitive text to the local clipboard. Never use for passwords, API keys, OTPs, tokens, private identifiers, or other credentials. Clipboard reading is unavailable.", parameters = Parameters(new { text = new { type = "STRING", description = "The exact non-sensitive text the user explicitly wants copied." } }, "text") },
            new { name = "browser_search", description = "Open the default browser with a Google search. Use when the user explicitly wants results opened in their browser; ordinary factual questions can use Google Search instead.", parameters = Parameters(new { query = new { type = "STRING", description = "Search query explicitly requested by the user." } }, "query") },
            new { name = "get_system_status", description = "Read a non-personal local system status value.", parameters = Parameters(new { category = new { type = "STRING", description = "One of: battery, memory, storage, network." } }, "category") },
            new { name = "keyboard_shortcut", description = "Send one allowlisted reversible shortcut to the most recent non-Lilith foreground app. Arbitrary keys and typing are unavailable.", parameters = Parameters(new { action = new { type = "STRING", description = "One of: undo, redo, save, select_all, find, refresh, fullscreen, escape." } }, "action") },
            new { name = "set_timer", description = "Create a local timer that Lilith will announce. Use a duration from 0.1 to 1440 minutes.", parameters = Parameters(new { minutes = new { type = "NUMBER", description = "Timer duration in minutes." }, message = new { type = "STRING", description = "Short announcement when the timer ends; omit personal or sensitive information." } }, "minutes", "message") },
            new { name = "cancel_timers", description = "Cancel all pending Lilith local timers when the user asks to cancel them.", parameters = Parameters(new { }) },
            new { name = "lock_computer", description = "Lock Windows. Call only when the user explicitly asks to lock the computer; never infer it from context.", parameters = Parameters(new { }) },
            new { name = "sleep_computer", description = "Put Windows into sleep. Call only when the user explicitly asks the computer to sleep; never infer it because the user is sleepy or leaving.", parameters = Parameters(new { }) },
            new { name = "cancel_system_action", description = "Cancel a pending lock or sleep action before it runs.", parameters = Parameters(new { }) }
        };
        var tools = new List<object>();
        if (includeGoogleSearch)
            tools.Add(new { googleSearch = new { } });
        tools.Add(new { functionDeclarations = declarations });
        return tools.ToArray();
    }

    private static void ProcessGeminiToolBatches()
    {
        if (!PendingGeminiToolBatches.TryDequeue(out var batch))
            return;
        try
        {
            var results = new List<GeminiToolResult>();
            foreach (var call in batch.Calls.Take(8))
                results.Add(ExecuteGeminiComputerTool(call));
            foreach (var call in batch.Calls.Skip(8))
                results.Add(new GeminiToolResult { Name = call.Name, Id = call.Id, Success = false, Message = "Too many actions were requested in one turn." });

            var responseParts = new List<object>();
            foreach (var result in results)
            {
                var functionResponse = new Dictionary<string, object>
                {
                    ["name"] = result.Name,
                    ["response"] = new { success = result.Success, result = result.Message }
                };
                if (!string.IsNullOrWhiteSpace(result.Id))
                    functionResponse["id"] = result.Id;
                responseParts.Add(new Dictionary<string, object> { ["functionResponse"] = functionResponse });
            }
            batch.Session.Contents.Add(new { role = "user", parts = responseParts.ToArray() });
            _ = ContinueGeminiAgentRequestAsync(batch.Session);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogError($"Gemini desktop tool execution failed: {exception}");
            PendingReplies.Enqueue(ApiKeyText("剛才的電腦操作沒有成功。", "刚才的电脑操作没有成功。", "さっきのPC操作はうまくいかなかった……", "The computer action did not work."));
        }
    }

    private static void ProcessGeminiCompatibilityFallbacks()
    {
        if (!PendingGeminiCompatibilityFallbacks.TryDequeue(out var session))
            return;
        try
        {
            string reply;
            var handled = TryHandleScreenshotCommand(session.UserText, out reply)
                || TryHandleComputerCommand(session.UserText, out reply)
                || TryHandleMediaCommand(session.UserText, out reply)
                || TryLaunchApplicationCommand(session.UserText, out reply);
            if (handled)
            {
                AddMemoryTurn("model", reply);
                PendingAiEmotions.Enqueue("emoji_smile_1");
                PendingReplies.Enqueue(reply);
                if (Plugin.VoiceEnabled.Value)
                    _ = RequestSpeechAsync(reply, poseStyle: session.PoseContext.VoiceStyle);
                Plugin.PluginLog.LogInfo("Completed a desktop command through the compatibility fallback router.");
                return;
            }

            session.DesktopToolsEnabled = false;
            session.SystemInstruction += "\nDesktop function calling is unavailable on the selected model or API version. Do not claim that a computer action was performed. Continue as normal conversation and briefly explain if the requested action cannot be completed.";
            _ = ContinueGeminiAgentRequestAsync(session);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogError($"Gemini compatibility fallback failed: {exception}");
            PendingReplies.Enqueue(ApiKeyText("這個模型目前不能使用電腦工具。", "这个模型目前不能使用电脑工具。", "このモデルでは今、PCツールを使えないみたい。", "This model cannot use the computer tools right now."));
        }
    }

    private static async Task ContinueGeminiAgentRequestAsync(GeminiAgentSession session)
    {
        try
        {
            await SendGeminiAgentRequestAsync(session).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogError($"Gemini tool continuation failed: {exception}");
            PendingReplies.Enqueue(ApiKeyText("操作已經停下來了，但回覆沒有順利接上。", "操作已经停下来了，但回复没有顺利接上。", "操作は止めたけれど、返事をうまく続けられなかった。", "The actions stopped, but I couldn't complete the follow-up response."));
        }
    }

    private static GeminiToolResult ExecuteGeminiComputerTool(GeminiFunctionCallData call)
    {
        if (!Plugin.AdvancedComputerActionsEnabled.Value)
            return ToolResult(call, false, ApiKeyText("進階電腦操作目前是關閉的。", "高级电脑操作目前已关闭。", "高度なPC操作は今オフになっているよ。", "Advanced PC controls are currently disabled."));
        try
        {
            switch (call.Name)
            {
                case "open_application":
                    return ExecuteOpenApplicationTool(call, GetToolString(call.Args, "name", 120));
                case "open_folder":
                    return ExecuteOpenFolderTool(call, GetToolString(call.Args, "folder", 40));
                case "window_action":
                    return ExecuteWindowTool(call, GetToolString(call.Args, "action", 40));
                case "media_control":
                    return ExecuteMediaTool(call, GetToolString(call.Args, "action", 40));
                case "take_screenshot":
                    return TryHandleScreenshotCommand("幫我截圖", out var screenshotReply)
                        ? ToolResultFromReply(call, screenshotReply)
                        : ToolResult(call, false, "Screenshot action was not available.");
                case "copy_text":
                    return ExecuteCopyTextTool(call, GetToolString(call.Args, "text", 4000));
                case "browser_search":
                    return ExecuteBrowserSearchTool(call, GetToolString(call.Args, "query", 500));
                case "get_system_status":
                    return ExecuteSystemStatusTool(call, GetToolString(call.Args, "category", 40));
                case "keyboard_shortcut":
                    return ExecuteKeyboardShortcutTool(call, GetToolString(call.Args, "action", 40));
                case "set_timer":
                    return ExecuteSetTimerTool(call, GetToolDouble(call.Args, "minutes"), GetToolString(call.Args, "message", 160));
                case "cancel_timers":
                    var count = LocalTimers.Count;
                    LocalTimers.Clear();
                    return ToolResult(call, true, ApiKeyText($"已取消 {count} 個計時器。", $"已取消 {count} 个计时器。", $"{count}件のタイマーを取り消したよ。", $"Cancelled {count} timer(s)."));
                case "lock_computer":
                    _pendingSystemAction = new PendingSystemAction { Action = "lock", ExecuteAfter = Time.unscaledTime + 8f };
                    return ToolResult(call, true, ApiKeyText("已安排在回覆後鎖定電腦。", "已安排在回复后锁定电脑。", "返事のあとでPCをロックするね。", "The computer will lock after this reply."));
                case "sleep_computer":
                    _pendingSystemAction = new PendingSystemAction { Action = "sleep", ExecuteAfter = Time.unscaledTime + 8f };
                    return ToolResult(call, true, ApiKeyText("已安排在回覆後讓電腦睡眠。", "已安排在回复后让电脑睡眠。", "返事のあとでPCをスリープさせるね。", "The computer will sleep after this reply."));
                case "cancel_system_action":
                    var hadAction = _pendingSystemAction != null;
                    _pendingSystemAction = null;
                    return ToolResult(call, true, hadAction
                        ? ApiKeyText("已取消待執行的系統動作。", "已取消待执行的系统操作。", "待機中のシステム操作を取り消したよ。", "The pending system action was cancelled.")
                        : ApiKeyText("目前沒有待執行的系統動作。", "目前没有待执行的系统操作。", "待機中のシステム操作はないよ。", "There is no pending system action."));
                default:
                    return ToolResult(call, false, "Unknown or unavailable desktop tool.");
            }
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Desktop tool '{call.Name}' failed: {exception.Message}");
            return ToolResult(call, false, $"The local action failed: {exception.Message}");
        }
    }

    private static GeminiToolResult ExecuteOpenApplicationTool(GeminiFunctionCallData call, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Regex.IsMatch(name, "[\\\\/:*?\"<>|]"))
            return ToolResult(call, false, "A safe application name was not provided.");
        if (TryFocusRunningApplication(name))
            return ToolResult(call, true, ApiKeyText($"已切換到{name}。", $"已切换到{name}。", $"{name}に切り替えたよ。", $"Focused {name}."));
        var registered = FindConfiguredApplication(name) ?? FindOfficialApplication(name);
        if (registered != null)
        {
            Process.Start(new ProcessStartInfo(registered.Target)
            {
                Arguments = registered.Arguments ?? string.Empty,
                UseShellExecute = true
            });
            Plugin.PluginLog.LogInfo($"Opened registered allowlisted application '{registered.Name}'.");
            return ToolResult(call, true, ApiKeyText($"已開啟{registered.Name}。", $"已打开{registered.Name}。", $"{registered.Name}を開いたよ。", $"Opened {registered.Name}."));
        }
        var shortcut = ResolveWindowsShortcut(new[] { name });
        if (string.IsNullOrWhiteSpace(shortcut))
            shortcut = ResolveFuzzyWindowsShortcut(name);
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            if (TryLaunchApplicationCommand("開啟 " + name, out var builtInReply))
                return ToolResultFromReply(call, builtInReply);
            return ToolResult(call, false, ApiKeyText($"沒有在這台電腦找到{name}。", $"没有在这台电脑找到{name}。", $"このPCでは{name}を見つけられなかった。", $"I couldn't find {name} on this PC."));
        }
        Process.Start(new ProcessStartInfo(shortcut) { UseShellExecute = true });
        Plugin.PluginLog.LogInfo($"Opened an installed application from a Windows shortcut named '{Path.GetFileNameWithoutExtension(shortcut)}'.");
        return ToolResult(call, true, ApiKeyText($"已開啟{name}。", $"已打开{name}。", $"{name}を開いたよ。", $"Opened {name}."));
    }

    private static GeminiToolResult ExecuteOpenFolderTool(GeminiFunctionCallData call, string folder)
    {
        var command = folder.Trim().ToLowerInvariant() switch
        {
            "downloads" => "打開下載資料夾",
            "desktop" => "打開桌面資料夾",
            "documents" => "打開文件資料夾",
            "pictures" => "打開圖片資料夾",
            "music" => "打開音樂資料夾",
            "videos" => "打開影片資料夾",
            "screenshots" => "打開截圖資料夾",
            "mod" => "打開MOD資料夾",
            "recycle_bin" => "打開資源回收筒",
            _ => string.Empty
        };
        return command.Length > 0 && TryOpenKnownFolder(command, out var reply)
            ? ToolResultFromReply(call, reply)
            : ToolResult(call, false, "That folder is not in the local allowlist.");
    }

    private static GeminiToolResult ExecuteWindowTool(GeminiFunctionCallData call, string action)
    {
        var command = action.Trim().ToLowerInvariant() switch
        {
            "show_desktop" => "顯示桌面",
            "task_view" => "打開工作檢視",
            "switch_previous" => "切換到上一個視窗",
            "minimize" => "最小化視窗",
            "maximize" => "最大化視窗",
            "restore" => "還原視窗",
            "snap_left" => "把視窗排到左側",
            "snap_right" => "把視窗排到右側",
            _ => string.Empty
        };
        return command.Length > 0 && TryHandleWindowCommand(command, out var reply)
            ? ToolResultFromReply(call, reply)
            : ToolResult(call, false, "That window action is not available.");
    }

    private static GeminiToolResult ExecuteMediaTool(GeminiFunctionCallData call, string action)
    {
        var command = action.Trim().ToLowerInvariant() switch
        {
            "play_pause" => "暫停音樂",
            "next" => "下一首",
            "previous" => "上一首",
            "stop" => "停止音樂",
            "mute" => "靜音",
            "volume_up" => "音量大一點",
            "volume_down" => "音量小一點",
            _ => string.Empty
        };
        return command.Length > 0 && TryHandleMediaCommand(command, out var reply)
            ? ToolResultFromReply(call, reply)
            : ToolResult(call, false, "That media action is not available.");
    }

    private static GeminiToolResult ExecuteCopyTextTool(GeminiFunctionCallData call, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ToolResult(call, false, "No text was provided.");
        if (ContainsSensitiveNoteData(content))
            return ToolResult(call, false, "Credential-like or personal text was blocked and was not copied.");
        GUIUtility.systemCopyBuffer = content;
        Plugin.PluginLog.LogInfo($"Gemini tool copied user-specified text locally ({content.Length} chars; content hidden)." );
        return ToolResult(call, true, ApiKeyText("文字已複製到本機剪貼簿。", "文字已复制到本机剪贴板。", "文字をローカルのクリップボードにコピーしたよ。", "The text was copied to the local clipboard."));
    }

    private static GeminiToolResult ExecuteBrowserSearchTool(GeminiFunctionCallData call, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return ToolResult(call, false, "No search query was provided.");
        if (ContainsSensitiveNoteData(query))
            return ToolResult(call, false, "The search was blocked because it may contain personal or credential information.");
        Process.Start(new ProcessStartInfo("https://www.google.com/search?q=" + Uri.EscapeDataString(query)) { UseShellExecute = true });
        Plugin.PluginLog.LogInfo($"Opened a user-requested browser search ({query.Length} chars; query hidden)." );
        return ToolResult(call, true, ApiKeyText("已在預設瀏覽器開啟搜尋。", "已在默认浏览器打开搜索。", "既定のブラウザで検索を開いたよ。", "The search was opened in the default browser."));
    }

    private static GeminiToolResult ExecuteSystemStatusTool(GeminiFunctionCallData call, string category)
    {
        var command = category.Trim().ToLowerInvariant() switch
        {
            "battery" => "現在電量多少",
            "memory" => "電腦記憶體使用狀態",
            "storage" => "系統磁碟還有多少空間",
            "network" => "網路連線狀態",
            _ => string.Empty
        };
        return command.Length > 0 && TryReportSystemStatus(command, out var reply)
            ? ToolResultFromReply(call, reply)
            : ToolResult(call, false, "That system status category is not available.");
    }

    private static GeminiToolResult ExecuteKeyboardShortcutTool(GeminiFunctionCallData call, string action)
    {
        var target = GetControllableWindow();
        if (target == IntPtr.Zero || !SetForegroundWindow(target))
            return ToolResult(call, false, "No non-Lilith foreground window was available.");
        switch (action.Trim().ToLowerInvariant())
        {
            case "undo": SendShortcut(0x11, 0x5A); break;
            case "redo": SendShortcut(0x11, 0x59); break;
            case "save": SendShortcut(0x11, 0x53); break;
            case "select_all": SendShortcut(0x11, 0x41); break;
            case "find": SendShortcut(0x11, 0x46); break;
            case "refresh": SendShortcut(0x74); break;
            case "fullscreen": SendShortcut(0x7A); break;
            case "escape": SendShortcut(0x1B); break;
            default: return ToolResult(call, false, "That keyboard shortcut is not allowlisted.");
        }
        Plugin.PluginLog.LogInfo($"Sent allowlisted keyboard shortcut '{action}'.");
        return ToolResult(call, true, $"The allowlisted '{action}' shortcut was sent to the previous foreground app.");
    }

    private static GeminiToolResult ExecuteSetTimerTool(GeminiFunctionCallData call, double minutes, string message)
    {
        if (double.IsNaN(minutes) || double.IsInfinity(minutes) || minutes < 0.1 || minutes > 1440)
            return ToolResult(call, false, "Timer duration must be between 0.1 and 1440 minutes.");
        if (string.IsNullOrWhiteSpace(message))
            message = ApiKeyText("時間到了。", "时间到了。", "時間だよ。", "Time is up.");
        LocalTimers.Add(new LocalTimer { DueAt = DateTimeOffset.Now.AddMinutes(minutes), Message = message.Trim() });
        Plugin.PluginLog.LogInfo($"Created local Lilith timer for {minutes:0.##} minute(s); message hidden.");
        return ToolResult(call, true, ApiKeyText($"已設定 {minutes:0.##} 分鐘的計時器。", $"已设置 {minutes:0.##} 分钟的计时器。", $"{minutes:0.##}分のタイマーを設定したよ。", $"Set a timer for {minutes:0.##} minute(s)."));
    }

    private static GeminiToolResult ToolResultFromReply(GeminiFunctionCallData call, string reply)
    {
        var failed = Regex.IsMatch(reply, "(沒有成功|没有成功|找不到|沒有找到|没有找到|不能|無法|无法|失敗|失败|できなかった|見つから|couldn't|could not|failed|not available)", RegexOptions.IgnoreCase);
        return ToolResult(call, !failed, reply);
    }

    private static GeminiToolResult ToolResult(GeminiFunctionCallData call, bool success, string message)
        => new() { Name = call.Name, Id = call.Id, Success = success, Message = message };

    private static string GetToolString(JsonElement args, string name, int maximumLength)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return string.Empty;
        var text = value.GetString()?.Trim() ?? string.Empty;
        return text.Length <= maximumLength ? text : text[..maximumLength];
    }

    private static double GetToolDouble(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value))
            return double.NaN;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;
        return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : double.NaN;
    }

    private static bool ShouldUseGeminiGoogleSearch(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
            return false;

        return Regex.IsMatch(userText,
            "(?:\\u5e6b\\u6211)?(?:\\u67e5\\u67e5|\\u67e5\\u4e00\\u4e0b|\\u67e5\\u8a62|\\u641c\\u5c0b|\\u641c\\u7d22|\\u4e0a\\u7db2\\u67e5)|(?:\\u6700\\u65b0|\\u65b0\\u805e|\\u5373\\u6642).*(?:\\u6d88\\u606f|\\u8cc7\\u8a0a|\\u60c5\\u5831|\\u9032\\u5ea6|\\u50f9\\u683c|\\u6bd4\\u8cfd|\\u5929\\u6c23)|(?:\\u8abf\\u3079\\u3066|\\u691c\\u7d22\\u3057\\u3066|\\u30cd\\u30c3\\u30c8\\u3067\\u8abf\\u3079\\u3066)|(?:look\\s*(?:it|this)?\\s*up|search(?:\\s+the)?\\s+web|google\\s+it|find\\s+online)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static async Task RequestOpenAiCompatibleAsync(string provider, string systemInstruction, string userText,
        PoseContext poseContext, bool japaneseVoiceMode)
    {
        var endpoint = provider == "DeepSeek"
            ? "https://api.deepseek.com/chat/completions"
            : "https://api.openai.com/v1/chat/completions";
        var model = provider == "DeepSeek" ? Plugin.DeepSeekModel.Value.Trim() : Plugin.OpenAiModel.Value.Trim();
        var key = provider == "DeepSeek" ? Plugin.DeepSeekApiKey.Value.Trim() : Plugin.OpenAiApiKey.Value.Trim();
        var messages = BuildOpenAiMessages(systemInstruction);
        var payload = new { model, messages, max_tokens = 1024, temperature = 0.8 };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{provider} HTTP {(int)response.StatusCode}: {responseBody}");
        using var document = JsonDocument.Parse(responseBody);
        var choice = document.RootElement.GetProperty("choices")[0];
        var rawReply = choice.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        var finishReason = choice.TryGetProperty("finish_reason", out var finish) ? finish.GetString() ?? "UNKNOWN" : "UNKNOWN";
        var usageSummary = "unavailable";
        if (document.RootElement.TryGetProperty("usage", out var usage))
        {
            var prompt = usage.TryGetProperty("prompt_tokens", out var promptElement) ? promptElement.GetInt32() : -1;
            var output = usage.TryGetProperty("completion_tokens", out var outputElement) ? outputElement.GetInt32() : -1;
            usageSummary = $"prompt={prompt}, output={output}";
        }
        Plugin.PluginLog.LogInfo($"{provider} completed: finish={finishReason}, rawChars={rawReply.Length}, {usageSummary}.");
        CompleteAiReply(rawReply, userText, poseContext, japaneseVoiceMode);
    }

    private static object[] BuildOpenAiMessages(string systemInstruction)
    {
        var messages = new List<object> { new { role = "system", content = systemInstruction } };
        lock (MemoryLock)
        {
            foreach (var turn in RecentConversation)
                messages.Add(new { role = turn.Role == "model" ? "assistant" : "user", content = turn.Text });
        }
        return messages.ToArray();
    }

    private static void CompleteAiReply(string rawReply, string userText, PoseContext poseContext, bool japaneseVoiceMode)
    {
        var bilingual = japaneseVoiceMode ? ParseBilingualReply(rawReply) : null;
        var reply = CleanReply(bilingual?.DisplayText ?? rawReply);
        var japaneseSpeech = bilingual?.JapaneseSpeech ?? string.Empty;
        if (reply.Length > 0)
            AddMemoryTurn("model", reply);
        if (reply.Length > 0)
            ConsiderAiNoteEvent(userText, reply);
        PendingAiEmotions.Enqueue(ChooseAiEmotion(userText, reply, poseContext));
        foreach (var page in SplitIntoBubblePages(reply.Length > 0 ? reply : "……"))
            PendingReplies.Enqueue(page);
        if (reply.Length == 0 || !Plugin.VoiceEnabled.Value)
            return;
        var reaction = japaneseVoiceMode ? null : GetNativeReaction(userText, reply);
        var speechText = reaction == null ? reply : RemoveLeadingReactionText(reply);
        if (japaneseVoiceMode && !string.IsNullOrWhiteSpace(japaneseSpeech))
            speechText = japaneseSpeech;
        _ = RequestSpeechAsync(speechText.Length > 0 ? speechText : reply, reaction, poseContext.VoiceStyle, japaneseVoiceMode);
    }

    private static string NormalizeAiProvider(string? provider)
    {
        if (string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase)) return "OpenAI";
        if (string.Equals(provider, "DeepSeek", StringComparison.OrdinalIgnoreCase)) return "DeepSeek";
        return "Gemini";
    }

    private static string GetActiveChatApiKey()
    {
        return NormalizeAiProvider(Plugin.AiProvider.Value) switch
        {
            "OpenAI" => Plugin.OpenAiApiKey.Value,
            "DeepSeek" => Plugin.DeepSeekApiKey.Value,
            _ => Plugin.GeminiApiKey.Value
        };
    }

    private static string BuildPlayerNameContext(string userText, string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return string.Empty;

        var cooldownComplete = _repliesSincePlayerNameWasOffered >= 3;
        var emotionalContext = Regex.IsMatch(userText,
            "(難過|难过|傷心|伤心|寂寞|害怕|累了|想妳|想你|喜歡妳|喜欢你|愛妳|爱你|晚安|早安|おやすみ|寂しい|怖い|疲れた|好き|大好き|good night|miss you|love you)",
            RegexOptions.IgnoreCase);
        var probability = emotionalContext ? 0.30 : 0.12;
        var offerName = cooldownComplete && System.Random.Shared.NextDouble() < probability;

        if (offerName)
        {
            _repliesSincePlayerNameWasOffered = 0;
            return $"\n使用者在遊戲中設定的名字是「{playerName}」。本次只有在情感與語境自然時才可稱呼一次；不自然時仍不要使用。";
        }

        _repliesSincePlayerNameWasOffered++;
        return "\n本次回覆不要主動稱呼使用者的名字，即使近期對話記憶中曾經出現過。";
    }

    private static string ChooseAiEmotion(string userText, string reply, PoseContext poseContext)
    {
        var combined = userText + "\n" + reply;
        if (poseContext.VoiceStyle == VoiceStyle.Sleepy
            || Regex.IsMatch(combined, "(想睡|睏|困了|睡覺|睡觉|晚安|おやすみ|眠い|寝る|sleepy|good night)", RegexOptions.IgnoreCase))
            return "emoji_sleepy_1";
        if (Regex.IsMatch(combined, "(生氣|生气|討厭|讨厌|笨蛋|不准|不許|不许|怒|むかつく|嫌い|angry|mad)", RegexOptions.IgnoreCase))
            return "emoji_angry_1";
        if (Regex.IsMatch(combined, "(害怕|可怕|恐怖|擔心|担心|怖い|不安|scared|afraid)", RegexOptions.IgnoreCase))
            return "emoji_fear_1";
        if (Regex.IsMatch(combined, "(難過|难过|傷心|伤心|哭|寂寞|孤單|孤单|悲しい|寂しい|sad|lonely)", RegexOptions.IgnoreCase))
            return "emoji_sad_1";
        if (Regex.IsMatch(combined, "(委屈|不理我|忘記我|忘记我|不要走|置いていか|wronged|leave me)", RegexOptions.IgnoreCase))
            return "emoji_wronged_1";
        if (Regex.IsMatch(combined, "(真的嗎|真的吗|居然|竟然|沒想到|没想到|驚訝|惊讶|びっくり|本当|really|surpris)", RegexOptions.IgnoreCase))
            return "emoji_surprise_2";
        if (Regex.IsMatch(combined, "(不懂|奇怪|為什麼|为什么|怎麼會|怎么会|困惑|分からない|なぜ|confus|why)", RegexOptions.IgnoreCase))
            return "emoji_daze_1";
        if (Regex.IsMatch(combined, "(開心|开心|喜歡|喜欢|愛|爱|謝謝|谢谢|草莓蛋糕|可愛|可爱|嬉しい|好き|ありがとう|happy|love|cute|thank)", RegexOptions.IgnoreCase))
            return "emoji_smile_3";
        return "emoji_calm_1";
    }

    private static void PlayAiEmotion(string emotion)
    {
        try
        {
            var character = UnityEngine.Object.FindObjectOfType<global::CharacterController>();
            if (character == null)
            {
                Plugin.PluginLog.LogWarning("Could not find CharacterController for AI emotion playback.");
                return;
            }
            var played = character.PlayEmotion(emotion, loop: false, instant: false, bypassFollowLock: false);
            Plugin.PluginLog.LogInfo($"AI Live2D emotion '{emotion}' requested; played={played}.");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not play AI Live2D emotion '{emotion}': {exception.Message}");
        }
    }

    private static string BuildLocalTimeContext()
    {
        var now = DateTimeOffset.Now;
        var weekday = now.DayOfWeek switch
        {
            DayOfWeek.Monday => "星期一",
            DayOfWeek.Tuesday => "星期二",
            DayOfWeek.Wednesday => "星期三",
            DayOfWeek.Thursday => "星期四",
            DayOfWeek.Friday => "星期五",
            DayOfWeek.Saturday => "星期六",
            _ => "星期日"
        };
        return $"\n目前使用者電腦的本地日期與時間是 {now:yyyy-MM-dd HH:mm:ss}（{weekday}，UTC{now:zzz}）。這是可信的即時系統資訊；被問到時間、日期、星期或早晚時，直接依此自然回答。";
    }

    private static PoseContext CapturePoseContext()
    {
        try
        {
            var state = UnityEngine.Object.FindObjectOfType<LilithStateManager>();
            if (state == null)
                return PoseContext.Default;
            if (state.IsSleep)
                return new PoseContext("\n莉莉絲目前正在睡覺。回答應像被輕輕叫醒：簡短、低能量、親近，但不要每次都撒嬌。", VoiceStyle.Sleepy);
            if (state.IsYawnAnimPlaying)
                return new PoseContext("\n莉莉絲目前正在打呵欠、帶有睡意。回答可以稍微慵懶而簡短。", VoiceStyle.Sleepy);
            if (state.IsLieDown)
                return new PoseContext("\n莉莉絲目前正躺著。語氣可以放鬆、安靜，像在近距離聊天。", VoiceStyle.Sleepy);
            if (state.IsSit)
                return new PoseContext("\n莉莉絲目前坐著，處於放鬆陪伴的姿態。", VoiceStyle.Calm);
            if (state.IsInteracting)
                return new PoseContext("\n莉莉絲目前正在和使用者互動，注意力在對方身上。", VoiceStyle.Calm);
            return new PoseContext("\n莉莉絲目前自然待機著。", VoiceStyle.Calm);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not read Lilith pose state: {exception.Message}");
            return PoseContext.Default;
        }
    }

    private static string BuildCanonicalStyleGuide(PoseContext poseContext)
    {
        var situationalExamples = poseContext.VoiceStyle == VoiceStyle.Sleepy
            ? "\n目前狀態的語感參考：『嗯……我在聽……』『你是真的在這裡嗎……不是我在做夢吧？』只模仿慵懶、破碎而親近的節奏，不要逐字重複。"
            : "\n日常語感參考：『安靜地陪著你也是一件很幸福的事呢。』『才不是一直等你，只是剛好看到了啦。』只模仿溫柔、略帶俏皮的距離感，不要逐字重複。";
        return "\n原作風格準則：莉莉絲的常態不是冷淡，而是溫柔陪伴；約五成自然陪伴、兩成害羞或輕微撒嬌、一成半明確關心與依戀、一成半在氣氛合適時帶出哲學餘韻。她可以偶爾自稱『莉莉絲』，也會自然地嘴硬、期待稱讚或直接表達喜歡。哲學感必須從眼前的小事出發，核心接近選擇、記憶、存在與共同留下的痕跡，但不要反覆使用『存在』『意義』『永遠』，也不要像講課。草莓蛋糕可以象徵一起生活與創造的小小幸福，但只在話題相關時提起。避免制式安慰；先回應對方當下的感受，再留下簡短餘韻。用台灣繁體措辭，將『説、着、支援』等非台灣用字改成『說、著、支持』。"
            + situationalExamples;
    }

    private static bool TryBuildLocalTimeReply(string input, out string reply)
    {
        var asksTime = Regex.IsMatch(input, "(現在|目前|此刻).{0,4}(幾點|几点|時間|时间)|(幾點|几点)了|現在是幾點|现在是几点");
        var asksDate = Regex.IsMatch(input, "(今天|現在|目前).{0,4}(幾號|几号|日期|幾月幾日|几月几日)");
        var asksWeekday = Regex.IsMatch(input, "(今天|現在|目前).{0,4}(星期幾|星期几|禮拜幾|礼拜几|週幾|周几)");
        if (!asksTime && !asksDate && !asksWeekday)
        {
            reply = string.Empty;
            return false;
        }

        var now = DateTimeOffset.Now;
        var parts = new List<string>();
        if (asksDate)
            parts.Add($"今天是 {now:yyyy 年 M 月 d 日}");
        if (asksWeekday)
        {
            var weekday = now.DayOfWeek switch
            {
                DayOfWeek.Monday => "星期一",
                DayOfWeek.Tuesday => "星期二",
                DayOfWeek.Wednesday => "星期三",
                DayOfWeek.Thursday => "星期四",
                DayOfWeek.Friday => "星期五",
                DayOfWeek.Saturday => "星期六",
                _ => "星期日"
            };
            parts.Add(weekday);
        }
        if (asksTime)
            parts.Add($"現在是 {now:HH:mm}");
        reply = string.Join("，", parts) + "。";
        return true;
    }

    private static async Task<string> BuildWeatherContextAsync()
    {
        if (!Plugin.WeatherEnabled.Value)
            return string.Empty;
        if (_cachedWeatherContext.Length > 0 && DateTimeOffset.Now - _weatherFetchedAt < TimeSpan.FromMinutes(10))
            return _cachedWeatherContext;

        try
        {
            await ResolveWeatherLocationFromIpAsync().ConfigureAwait(false);
            var latitude = Plugin.WeatherLatitude.Value.ToString(CultureInfo.InvariantCulture);
            var longitude = Plugin.WeatherLongitude.Value.ToString(CultureInfo.InvariantCulture);
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m,is_day&temperature_unit=celsius&wind_speed_unit=kmh&precipitation_unit=mm&timezone=auto";
            using var response = await Http.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var current = document.RootElement.GetProperty("current");
            var temperature = current.GetProperty("temperature_2m").GetDouble();
            var apparent = current.GetProperty("apparent_temperature").GetDouble();
            var humidity = current.GetProperty("relative_humidity_2m").GetDouble();
            var precipitation = current.GetProperty("precipitation").GetDouble();
            var wind = current.GetProperty("wind_speed_10m").GetDouble();
            var code = current.GetProperty("weather_code").GetInt32();
            var daylight = current.GetProperty("is_day").GetInt32() == 1 ? "白天" : "夜間";
            var condition = DescribeWeatherCode(code);
            _cachedWeatherContext = $"\n目前「{Plugin.WeatherLocationName.Value}」的即時天氣（Open-Meteo 模型資料）：{condition}，{daylight}，氣溫 {temperature:0.#}°C，體感 {apparent:0.#}°C，相對濕度 {humidity:0}% ，目前降水 {precipitation:0.##} mm，風速 {wind:0.#} km/h。被問到現在的天氣時依此回答，不要自行編造未提供的資訊。";
            _weatherFetchedAt = DateTimeOffset.Now;
            return _cachedWeatherContext;
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Weather lookup failed; chat continues without it: {exception.Message}");
            return "\n目前無法取得可靠的即時天氣；若被問到天氣，坦白說暫時看不到，不要猜測。";
        }
    }

    private static async Task ResolveWeatherLocationFromIpAsync()
    {
        if (_ipWeatherLocationResolved || !Plugin.WeatherAutoDetectFromIp.Value)
            return;
        _ipWeatherLocationResolved = true;

        try
        {
            const string url = "https://ipwho.is/?fields=success,message,city,region,country,latitude,longitude";
            using var response = await Http.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var root = document.RootElement;
            if (root.TryGetProperty("success", out var success) && !success.GetBoolean())
            {
                var message = root.TryGetProperty("message", out var error) ? error.GetString() : "unknown error";
                throw new InvalidOperationException(message);
            }

            var latitude = root.GetProperty("latitude").GetDouble();
            var longitude = root.GetProperty("longitude").GetDouble();
            var city = root.TryGetProperty("city", out var cityElement) ? cityElement.GetString() : null;
            var region = root.TryGetProperty("region", out var regionElement) ? regionElement.GetString() : null;
            var country = root.TryGetProperty("country", out var countryElement) ? countryElement.GetString() : null;
            var locationParts = new[] { city, region, country }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var locationName = string.Join("，", locationParts);
            if (locationName.Length == 0)
                locationName = Plugin.WeatherLocationName.Value;

            Plugin.WeatherLatitude.Value = latitude;
            Plugin.WeatherLongitude.Value = longitude;
            Plugin.WeatherLocationName.Value = locationName;
            Plugin.PluginLog.LogInfo($"Weather location approximated from IP as '{locationName}' (the IP address was not stored)." );
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"IP weather location lookup failed; using configured location: {exception.Message}");
        }
    }

    private static string DescribeWeatherCode(int code)
    {
        if (code == 0) return "晴朗";
        if (code <= 3) return "多雲";
        if (code == 45 || code == 48) return "有霧";
        if (code >= 51 && code <= 57) return "毛毛雨";
        if (code >= 61 && code <= 67) return "下雨";
        if (code >= 71 && code <= 77) return "下雪";
        if (code >= 80 && code <= 82) return "陣雨";
        if (code >= 85 && code <= 86) return "陣雪";
        if (code >= 95) return "雷雨";
        return $"天氣代碼 {code}";
    }

    private static NativeReaction? GetNativeReaction(string userText, string reply)
    {
        if (!Plugin.ReactionSoundsEnabled.Value)
            return null;

        string? fileName = null;
        if (Regex.IsMatch(userText, "(草莓蛋糕|送妳|送你|禮物|礼物|驚喜|惊喜|特地買|特地买)")
            || Regex.IsMatch(reply, "(欸|咦|真的嗎|沒想到|居然|竟然|原來如此)[！!？?]?"))
            fileName = "surprised.wav";
        else if (Regex.IsMatch(userText, "(不理妳|不理你|要走了|離開妳|离开你|忘記妳|忘记你|討厭妳|讨厌你)")
            || Regex.IsMatch(reply, "(不要走|不理我|寂寞|難過|委屈|討厭我|忘記我|對不起)"))
            fileName = "wronged.wav";
        if (fileName == null)
            return null;

        try
        {
            var path = Path.Combine(Plugin.ReactionSoundsDirectory.Value, fileName);
            if (!File.Exists(path))
                return null;
            Plugin.PluginLog.LogInfo($"Selected native reaction sound: {fileName}");
            return new NativeReaction
            {
                Audio = File.ReadAllBytes(path),
                Style = fileName == "surprised.wav" ? VoiceStyle.Excited : VoiceStyle.Wronged
            };
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not queue native reaction: {exception.Message}");
            return null;
        }
    }

    private static string RemoveLeadingReactionText(string text)
    {
        var cleaned = Regex.Replace(
            text.TrimStart(),
            @"^(?:(?:嗯|唔|嗚|呜|哼|欸|诶|咦|啊|呀|唉|呵){1,3}|真的嗎|真的吗)[\s…\.，,。！？!?～~]*",
            string.Empty,
            RegexOptions.IgnoreCase).TrimStart();
        if (!string.Equals(cleaned, text, StringComparison.Ordinal))
            Plugin.PluginLog.LogInfo($"Removed voiced reaction prefix before TTS ({text.Length} -> {cleaned.Length} chars).");
        return cleaned;
    }

    private static async Task RequestSpeechAsync(string text, NativeReaction? reaction = null, VoiceStyle poseStyle = VoiceStyle.Calm, bool? japaneseVoiceMode = null)
    {
        try
        {
            var useJapanese = japaneseVoiceMode ?? IsJapaneseVoiceMode();
            var speechText = PrepareTextForSpeech(text);
            if (speechText.Length == 0)
                return;
            var referencePath = useJapanese ? Plugin.JapaneseVoiceReferencePath.Value.Trim() : Plugin.VoiceReferencePath.Value.Trim();
            var promptText = useJapanese
                ? "これは儀式でもあるの。君に私の存在を感じてもらうための儀式ね。"
                : "你的選擇創造了我，所以我的存在本身就是你的善意。";
            var effectiveStyle = reaction?.Style ?? poseStyle;
            var auxiliaryReferences = useJapanese ? effectiveStyle switch
            {
                VoiceStyle.Excited => new[] { Plugin.JapaneseExcitedVoiceReferencePath.Value.Trim() },
                VoiceStyle.Wronged => new[] { Plugin.JapaneseWrongedVoiceReferencePath.Value.Trim() },
                VoiceStyle.Sleepy => new[] { Plugin.JapaneseSleepyVoiceReferencePath.Value.Trim() },
                _ => new[] { Plugin.JapaneseCalmAuxVoiceReferencePath.Value.Trim() }
            } : effectiveStyle switch
            {
                VoiceStyle.Excited => new[] { Plugin.ExcitedVoiceReferencePath.Value.Trim() },
                VoiceStyle.Wronged => new[] { Plugin.WrongedVoiceReferencePath.Value.Trim() },
                VoiceStyle.Sleepy => new[] { Plugin.SleepyVoiceReferencePath.Value.Trim() },
                _ => Array.Empty<string>()
            };
            auxiliaryReferences = Array.FindAll(auxiliaryReferences, File.Exists);
            if (!File.Exists(referencePath))
            {
                Plugin.PluginLog.LogWarning($"Voice reference was not found: {referencePath}");
                return;
            }

            var payload = new
            {
                text = speechText,
                text_lang = useJapanese ? "ja" : "zh",
                ref_audio_path = referencePath,
                aux_ref_audio_paths = auxiliaryReferences,
                prompt_lang = useJapanese ? "ja" : "zh",
                prompt_text = promptText,
                text_split_method = "cut0",
                batch_size = 1,
                media_type = "wav",
                streaming_mode = false,
                seed = 42
            };
            var endpoint = useJapanese ? Plugin.JapaneseVoiceEndpoint.Value.Trim() : Plugin.VoiceEndpoint.Value.Trim();
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new HttpRequestException($"TTS HTTP {(int)response.StatusCode}: {error}");
            }
            var speech = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            PendingVoiceAudio.Enqueue(new VoiceSequence { Reaction = reaction?.Audio, Speech = speech });
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Voice generation failed; text chat continues: {exception.Message}");
        }
    }

    private static string PrepareTextForSpeech(string text)
    {
        var cleaned = Regex.Replace(text, @"\[([^\]]+)\]\(https?://[^\s\)]+\)", "$1", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"https?://\S+", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"(?:來源|资料来源|資料來源|出典|Sources?)\s*[:：]\s*$", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        cleaned = Regex.Replace(cleaned, @"[ \t]+", " ");
        cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n").Trim();
        if (!string.Equals(cleaned, text, StringComparison.Ordinal))
            Plugin.PluginLog.LogInfo($"Removed web citation markup before TTS ({text.Length} -> {cleaned.Length} chars).");
        return cleaned;
    }

    private static void SetVoicePitch(float pitch)
    {
        var manager = AudioManager.instance;
        if (manager != null && manager.source_Voice != null)
            manager.source_Voice.pitch = pitch;
    }

    private static AudioClip PlayWav(byte[] wav, string label)
    {
        var clip = CreateAudioClipFromWav(wav);
        AudioManager.PlayVoice(clip, false, true);
        Plugin.PluginLog.LogInfo($"Playing {label} ({wav.Length} bytes, {clip.length:0.00}s).");
        return clip;
    }

    private sealed class VoiceSequence
    {
        public byte[]? Reaction { get; set; }
        public byte[] Speech { get; set; } = Array.Empty<byte>();
    }

    private sealed class GeminiAgentSession
    {
        public string Url { get; set; } = string.Empty;
        public string SystemInstruction { get; set; } = string.Empty;
        public string UserText { get; set; } = string.Empty;
        public PoseContext PoseContext { get; set; } = PoseContext.Default;
        public bool JapaneseVoiceMode { get; set; }
        public bool UseGoogleSearch { get; set; }
        public bool DesktopToolsEnabled { get; set; }
        public int ToolRounds { get; set; }
        public List<object> Contents { get; set; } = new();
    }

    private sealed class GeminiFunctionCallData
    {
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public JsonElement Args { get; set; }
    }

    private sealed class GeminiToolBatch
    {
        public GeminiAgentSession Session { get; set; } = new();
        public List<GeminiFunctionCallData> Calls { get; set; } = new();
    }

    private sealed class GeminiToolResult
    {
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    private sealed class LocalTimer
    {
        public DateTimeOffset DueAt { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    private sealed class PendingSystemAction
    {
        public string Action { get; set; } = string.Empty;
        public float ExecuteAfter { get; set; }
    }

    private sealed class NativeReaction
    {
        public byte[] Audio { get; set; } = Array.Empty<byte>();
        public VoiceStyle Style { get; set; }
    }

    private enum VoiceStyle
    {
        Calm,
        Excited,
        Wronged,
        Sleepy
    }

    private sealed class PoseContext
    {
        public static readonly PoseContext Default = new(string.Empty, VoiceStyle.Calm);
        public string Prompt { get; }
        public VoiceStyle VoiceStyle { get; }

        public PoseContext(string prompt, VoiceStyle voiceStyle)
        {
            Prompt = prompt;
            VoiceStyle = voiceStyle;
        }
    }

    private static AudioClip CreateAudioClipFromWav(byte[] wav)
    {
        if (wav.Length < 44 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
            throw new InvalidDataException("TTS response is not a WAV file.");

        var offset = 12;
        ushort format = 0;
        ushort channels = 0;
        var sampleRate = 0;
        ushort bits = 0;
        var dataOffset = -1;
        var dataLength = 0;
        while (offset + 8 <= wav.Length)
        {
            var chunk = Encoding.ASCII.GetString(wav, offset, 4);
            var length = BitConverter.ToInt32(wav, offset + 4);
            var body = offset + 8;
            if (length < 0 || body + length > wav.Length)
                throw new InvalidDataException("Invalid WAV chunk length.");
            if (chunk == "fmt " && length >= 16)
            {
                format = BitConverter.ToUInt16(wav, body);
                channels = BitConverter.ToUInt16(wav, body + 2);
                sampleRate = BitConverter.ToInt32(wav, body + 4);
                bits = BitConverter.ToUInt16(wav, body + 14);
            }
            else if (chunk == "data")
            {
                dataOffset = body;
                dataLength = length;
                break;
            }
            offset = body + length + (length & 1);
        }
        if (dataOffset < 0 || channels == 0 || sampleRate <= 0)
            throw new InvalidDataException("WAV format or data chunk is missing.");

        float[] samples;
        if (format == 1 && bits == 16)
        {
            samples = new float[dataLength / 2];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(wav, dataOffset + i * 2) / 32768f;
        }
        else if (format == 3 && bits == 32)
        {
            samples = new float[dataLength / 4];
            Buffer.BlockCopy(wav, dataOffset, samples, 0, samples.Length * 4);
        }
        else
        {
            throw new InvalidDataException($"Unsupported WAV format={format}, bits={bits}.");
        }

        var frameCount = samples.Length / channels;
        var clip = AudioClip.Create("LilithAiVoice", frameCount, channels, sampleRate, false);
        if (!clip.SetData(samples, 0))
            throw new InvalidOperationException("Unity rejected generated audio samples.");
        return clip;
    }

    private static string CleanReply(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return string.Empty;
        var cleaned = Regex.Replace(reply, "[`*_#>]", string.Empty);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned;
    }

    private static bool IsJapaneseVoiceMode()
    {
        if (_japaneseVoiceOverride.HasValue)
            return _japaneseVoiceOverride.Value;
        try
        {
            var language = LocalizationConfig.GetCurrentVoiceLanguage() ?? string.Empty;
            return language.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static BilingualReply? ParseBilingualReply(string raw)
    {
        try
        {
            var json = raw.Trim();
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = json.IndexOf('\n');
                var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewline >= 0 && lastFence > firstNewline)
                    json = json.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
            }
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var display = root.TryGetProperty("display_text", out var displayElement)
                ? displayElement.GetString()
                : root.TryGetProperty("display_zh", out var legacyDisplayElement) ? legacyDisplayElement.GetString() : null;
            var speech = root.TryGetProperty("speech_ja", out var speechElement) ? speechElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(display) || string.IsNullOrWhiteSpace(speech))
                return null;
            return new BilingualReply(display!, speech!);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not parse bilingual Gemini reply; falling back to displayed text: {exception.Message}");
            return null;
        }
    }

    private sealed class BilingualReply
    {
        public string DisplayText { get; }
        public string JapaneseSpeech { get; }

        public BilingualReply(string displayText, string japaneseSpeech)
        {
            DisplayText = displayText;
            JapaneseSpeech = japaneseSpeech;
        }
    }

    private static object[] BuildGeminiContents()
    {
        List<ChatTurn> snapshot;
        lock (MemoryLock)
            snapshot = new List<ChatTurn>(RecentConversation);

        var contents = new List<object>();
        for (var i = 0; i < snapshot.Count; i++)
        {
            var turn = snapshot[i];
            var text = turn.Text;
            contents.Add(new { role = turn.Role, parts = new[] { new { text } } });
        }
        return contents.ToArray();
    }

    internal static void LoadAiNoteState()
    {
        try
        {
            Directory.CreateDirectory(MemoryDirectory);
            if (File.Exists(AiNoteStatePath))
                _aiNoteState = JsonSerializer.Deserialize<AiNoteState>(File.ReadAllText(AiNoteStatePath)) ?? new AiNoteState();
            foreach (var item in _aiNoteState.Pending)
                if (item.Status == "generating") item.Status = "pending";
            _aiNoteState.DeliveredAt.RemoveAll(value => value < DateTimeOffset.Now.AddDays(-30));
            SaveAiNoteState();
            Plugin.PluginLog.LogInfo($"Loaded AI note scheduler: pending={_aiNoteState.Pending.Count}, recentDeliveries={_aiNoteState.DeliveredAt.Count}.");
        }
        catch (Exception exception)
        {
            _aiNoteState = new AiNoteState();
            Plugin.PluginLog.LogWarning($"Could not load AI note state: {exception.Message}");
        }
    }

    private static void SaveAiNoteState()
    {
        lock (AiNoteLock)
        {
            Directory.CreateDirectory(MemoryDirectory);
            File.WriteAllText(AiNoteStatePath, JsonSerializer.Serialize(_aiNoteState,
                new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), Encoding.UTF8);
        }
    }

    private static void ConsiderAiNoteEvent(string userText, string reply)
    {
        if (!Plugin.AiNotesEnabled.Value || ContainsSensitiveNoteData(userText))
            return;
        if (Regex.IsMatch(userText, "(取消|算了|不用了|別再提|别再提|忘了吧|キャンセル|やめて|forget it|cancel)", RegexOptions.IgnoreCase))
            return;

        string category;
        string emotion;
        if (Regex.IsMatch(userText, "(明天|後天|下週|下周|等等要|待會要|考試|面試|報告|手術|約會|旅行|出發|deadline|tomorrow|exam|interview|明日|試験|面接)", RegexOptions.IgnoreCase))
        {
            category = "upcoming";
            emotion = Regex.IsMatch(userText, "(緊張|害怕|擔心|焦慮|不安|怖い|心配|nervous|worried)", RegexOptions.IgnoreCase) ? "緊張，需要溫柔支持" : "期待中的重要安排";
        }
        else if (Regex.IsMatch(userText, "(完成了|成功了|做到了|通過了|过了|終於|终于|畢業|毕业|錄取|得獎|できた|合格|finished|succeeded|passed)", RegexOptions.IgnoreCase))
        {
            category = "achievement";
            emotion = "值得一起慶祝與記住";
        }
        else if (Regex.IsMatch(userText, "(難過|伤心|傷心|哭了|寂寞|孤單|失戀|分手|失敗|失败|被罵|壓力|压力|悲しい|寂しい|つらい|sad|lonely|heartbroken)", RegexOptions.IgnoreCase))
        {
            category = "comfort";
            emotion = "低落，需要安靜陪伴而非說教";
        }
        else if (Regex.IsMatch(userText, "(約定|答應我|記得|不要忘記|我們說好|说好|約束|覚えて|promise|remember this)", RegexOptions.IgnoreCase))
        {
            category = "promise";
            emotion = "兩人之間值得珍惜的約定";
        }
        else if (Regex.IsMatch(userText, "(喜歡妳|喜欢你|愛妳|爱你|謝謝妳|谢谢你|有妳真好|陪著我|大好き|愛してる|ありがとう|love you|thank you)", RegexOptions.IgnoreCase))
        {
            category = "bond";
            emotion = "親近、感謝與依戀";
        }
        else
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var minimum = Math.Clamp(Plugin.AiNoteMinimumDelayMinutes.Value, 1, 1440);
        var maximum = Math.Clamp(Plugin.AiNoteMaximumDelayMinutes.Value, minimum, 2880);
        var delay = System.Random.Shared.Next(minimum, maximum + 1);
        var compactUser = CompactNoteContext(userText, 360);
        var compactReply = CompactNoteContext(reply, 360);
        lock (AiNoteLock)
        {
            var existing = _aiNoteState.Pending.LastOrDefault(item => item.Status == "pending" && item.Category == category && item.CreatedAt > now.AddHours(-6));
            if (existing != null)
            {
                existing.Topic = compactUser;
                existing.LatestContext = compactReply;
                existing.Emotion = emotion;
                SaveAiNoteState();
                Plugin.PluginLog.LogInfo($"Updated pending AI note event '{category}'.");
                return;
            }
            _aiNoteState.Pending.Add(new AiNoteEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                Category = category,
                Topic = compactUser,
                LatestContext = compactReply,
                Emotion = emotion,
                CreatedAt = now,
                DeliverAfter = now.AddMinutes(delay),
                Status = "pending"
            });
            while (_aiNoteState.Pending.Count(item => item.Status == "pending") > 5)
                _aiNoteState.Pending.Remove(_aiNoteState.Pending.First(item => item.Status == "pending"));
            SaveAiNoteState();
        }
        Plugin.PluginLog.LogInfo($"Scheduled AI note event '{category}' after {delay} minutes (content hidden from log).");
    }

    private static void UpdatePendingAiNoteEvents(string userText)
    {
        if (!Plugin.AiNotesEnabled.Value)
            return;
        var cancel = Regex.IsMatch(userText, "(取消了|取消吧|算了|不用了|不去了|別再提|别再提|忘了吧|キャンセル|中止|やめて|cancel|forget it)", RegexOptions.IgnoreCase);
        var completed = Regex.IsMatch(userText, "(完成了|結束了|结束了|考完了|做完了|成功了|通過了|过了|終於好了|終わった|できた|finished|done|passed)", RegexOptions.IgnoreCase);
        if (!cancel && !completed)
            return;
        lock (AiNoteLock)
        {
            var item = _aiNoteState.Pending.LastOrDefault(value => value.Status == "pending");
            if (item == null) return;
            if (cancel)
            {
                item.Status = "cancelled";
                Plugin.PluginLog.LogInfo($"Cancelled pending AI note event '{item.Category}' from follow-up context.");
            }
            else
            {
                item.Category = "achievement";
                item.Emotion = "事情已經完成，適合祝賀並關心結果";
                item.LatestContext = CompactNoteContext(userText, 360);
                Plugin.PluginLog.LogInfo("Updated pending AI note event after the user reported completion.");
            }
            SaveAiNoteState();
        }
    }

    private static bool ContainsSensitiveNoteData(string text)
    {
        return Regex.IsMatch(text, "(api[ _-]?key|密碼|密码|驗證碼|验证码|信用卡|身分證|身份证|護照|passport|password|token|secret)", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"\b(?:\d[ -]*?){12,19}\b")
            || Regex.IsMatch(text, @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase);
    }

    private static string CompactNoteContext(string text, int maximum)
    {
        var compact = Regex.Replace(text, @"\s+", " ").Trim();
        return compact.Length <= maximum ? compact : compact[..maximum] + "…";
    }

    private static void ProcessAiNoteScheduler()
    {
        while (PendingAiNotes.TryDequeue(out var generated))
        {
            try
            {
                var path = NoteImageSaver.SaveNote(generated.Text, false);
                NoteInbox.NotifySaved();
                lock (AiNoteLock)
                {
                    var item = _aiNoteState.Pending.FirstOrDefault(value => value.Id == generated.EventId);
                    if (item != null) item.Status = "delivered";
                    _aiNoteState.DeliveredAt.Add(DateTimeOffset.Now);
                    _aiNoteState.DeliveredPaths.Add(path);
                    _aiNoteState.DeliveredAt.RemoveAll(value => value < DateTimeOffset.Now.AddDays(-30));
                    SaveAiNoteState();
                }
                Plugin.PluginLog.LogInfo($"Delivered scheduled AI note through the native inbox: {path}");
            }
            catch (Exception exception)
            {
                Plugin.PluginLog.LogWarning($"Could not save generated AI note: {exception.Message}");
            }
            _aiNoteGenerationInFlight = false;
        }

        if (!Plugin.AiNotesEnabled.Value || _aiNoteGenerationInFlight || Time.unscaledTime < _nextAiNoteCheckAt)
            return;
        _nextAiNoteCheckAt = Time.unscaledTime + 30f;
        var now = DateTimeOffset.Now;
        AiNoteEvent? due;
        lock (AiNoteLock)
            due = _aiNoteState.Pending.FirstOrDefault(item => item.Status == "pending" && item.DeliverAfter <= now);
        if (due == null)
            return;
        if (GetWindowsIdleSeconds() < Math.Clamp(Plugin.AiNoteRequiredAwayMinutes.Value, 0, 1440) * 60)
            return;
        var cooldown = TimeSpan.FromHours(Math.Clamp(Plugin.AiNoteCooldownHours.Value, 1, 720));
        if (_aiNoteState.DeliveredAt.Count > 0 && now - _aiNoteState.DeliveredAt.Max() < cooldown)
            return;
        if (_aiNoteState.DeliveredAt.Count(value => value >= now.AddDays(-7)) >= Math.Clamp(Plugin.AiNoteWeeklyLimit.Value, 1, 20))
            return;
        if (OfficialNoteWasJustDelivered(now))
            return;
        if (string.IsNullOrWhiteSpace(Plugin.GeminiApiKey.Value))
            return;

        due.Status = "generating";
        SaveAiNoteState();
        _aiNoteGenerationInFlight = true;
        var language = GetAiInterfaceLanguage().Name;
        _ = GenerateAiNoteAsync(due, language);
    }

    private static bool OfficialNoteWasJustDelivered(DateTimeOffset now)
    {
        try
        {
            var directory = NoteInbox.NotesDirectory;
            if (!Directory.Exists(directory)) return false;
            var newest = Directory.GetFiles(directory, "note_*.png").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (newest == null || now - File.GetLastWriteTime(newest) > TimeSpan.FromMinutes(30)) return false;
            return !_aiNoteState.DeliveredPaths.Any(path => string.Equals(Path.GetFullPath(path), Path.GetFullPath(newest), StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static async Task GenerateAiNoteAsync(AiNoteEvent item, string language)
    {
        try
        {
            var model = Uri.EscapeDataString(Plugin.GeminiModel.Value.Trim());
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
            var prompt = $"Write one private desktop-pet letter in {language} as Lilith. Event category: {item.Category}. User topic: {item.Topic}. Earlier Lilith reply: {item.LatestContext}. Emotional direction: {item.Emotion}. Write 45-110 natural words or equivalent characters. Be warm, slightly playful and intimate, with subtle themes of memory, choice, and shared existence only when natural. Do not mention AI, scheduling, stored memory, APIs, or monitoring. Do not give medical or professional claims. Do not repeat sensitive details. End with an em dash and Lilith's localized name. Output only the letter text.";
            var payload = new
            {
                contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
                generationConfig = new { maxOutputTokens = 320, thinkingConfig = new { thinkingLevel = "MINIMAL" } }
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("x-goog-api-key", Plugin.GeminiApiKey.Value.Trim());
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Gemini note HTTP {(int)response.StatusCode}: {body}");
            using var document = JsonDocument.Parse(body);
            var parts = document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");
            var text = string.Concat(parts.EnumerateArray().Where(part => part.TryGetProperty("text", out _)).Select(part => part.GetProperty("text").GetString())).Trim();
            text = CleanReply(text);
            if (text.Length == 0) throw new InvalidOperationException("Gemini returned an empty note.");
            PendingAiNotes.Enqueue(new GeneratedAiNote { EventId = item.Id, Text = text });
        }
        catch (Exception exception)
        {
            lock (AiNoteLock)
            {
                item.Status = "pending";
                item.DeliverAfter = DateTimeOffset.Now.AddHours(1);
                SaveAiNoteState();
            }
            _aiNoteGenerationInFlight = false;
            Plugin.PluginLog.LogWarning($"AI note generation failed and was deferred: {exception.Message}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    private static bool IsVirtualKeyDown(int virtualKey) =>
        OperatingSystem.IsWindows() && (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static bool IsKeyCurrentlyDown(KeyCode key)
    {
        if (TryGetWindowsVirtualKey(key, out var virtualKey))
            return IsVirtualKeyDown(virtualKey);
        try
        {
            return Input.GetKey(key);
        }
        catch
        {
            return false;
        }
    }

    private static void CaptureHeldRebindingKeys()
    {
        RebindingHeldVirtualKeys.Clear();
        for (var code = (int)KeyCode.Backspace; code < (int)KeyCode.Mouse0; code++)
        {
            if (TryGetWindowsVirtualKey((KeyCode)code, out var virtualKey) && IsVirtualKeyDown(virtualKey))
                RebindingHeldVirtualKeys.Add(virtualKey);
        }
    }

    private static bool TryGetWindowsVirtualKey(KeyCode key, out int virtualKey)
    {
        virtualKey = 0;
        if (!OperatingSystem.IsWindows())
            return false;

        var code = (int)key;
        if (code >= (int)KeyCode.Alpha0 && code <= (int)KeyCode.Alpha9)
        {
            virtualKey = 0x30 + code - (int)KeyCode.Alpha0;
            return true;
        }
        if (code >= (int)KeyCode.A && code <= (int)KeyCode.Z)
        {
            virtualKey = 0x41 + code - (int)KeyCode.A;
            return true;
        }
        if (code >= (int)KeyCode.Keypad0 && code <= (int)KeyCode.Keypad9)
        {
            virtualKey = 0x60 + code - (int)KeyCode.Keypad0;
            return true;
        }
        if (code >= (int)KeyCode.F1 && code <= (int)KeyCode.F15)
        {
            virtualKey = 0x70 + code - (int)KeyCode.F1;
            return true;
        }

        virtualKey = key switch
        {
            KeyCode.Backspace => 0x08,
            KeyCode.Tab => 0x09,
            KeyCode.Clear => 0x0C,
            KeyCode.Return => 0x0D,
            KeyCode.Pause => 0x13,
            KeyCode.Escape => 0x1B,
            KeyCode.Space => 0x20,
            KeyCode.PageUp => 0x21,
            KeyCode.PageDown => 0x22,
            KeyCode.End => 0x23,
            KeyCode.Home => 0x24,
            KeyCode.LeftArrow => 0x25,
            KeyCode.UpArrow => 0x26,
            KeyCode.RightArrow => 0x27,
            KeyCode.DownArrow => 0x28,
            KeyCode.Insert => 0x2D,
            KeyCode.Delete => 0x2E,
            KeyCode.KeypadMultiply => 0x6A,
            KeyCode.KeypadPlus => 0x6B,
            KeyCode.KeypadMinus => 0x6D,
            KeyCode.KeypadPeriod => 0x6E,
            KeyCode.KeypadDivide => 0x6F,
            KeyCode.Numlock => 0x90,
            KeyCode.ScrollLock => 0x91,
            KeyCode.LeftShift => 0xA0,
            KeyCode.RightShift => 0xA1,
            KeyCode.LeftControl => 0xA2,
            KeyCode.RightControl => 0xA3,
            KeyCode.LeftAlt => 0xA4,
            KeyCode.RightAlt => 0xA5,
            KeyCode.Semicolon => 0xBA,
            KeyCode.Equals => 0xBB,
            KeyCode.Comma => 0xBC,
            KeyCode.Minus => 0xBD,
            KeyCode.Period => 0xBE,
            KeyCode.Slash => 0xBF,
            KeyCode.BackQuote => 0xC0,
            KeyCode.LeftBracket => 0xDB,
            KeyCode.Backslash => 0xDC,
            KeyCode.RightBracket => 0xDD,
            KeyCode.Quote => 0xDE,
            _ => 0
        };
        return virtualKey != 0;
    }

    private static void SendVirtualKey(byte virtualKey)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows media keys are only available on Windows.");
        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        keybd_event(virtualKey, 0, 0x0002, UIntPtr.Zero);
    }

    private static void SendShortcut(params byte[] virtualKeys)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows shortcuts are only available on Windows.");
        foreach (var key in virtualKeys)
            keybd_event(key, 0, 0, UIntPtr.Zero);
        for (var index = virtualKeys.Length - 1; index >= 0; index--)
            keybd_event(virtualKeys[index], 0, 0x0002, UIntPtr.Zero);
    }

    private static void ObserveForegroundWindow()
    {
        if (!OperatingSystem.IsWindows() || Time.unscaledTime < _nextForegroundWindowScanAt)
            return;
        _nextForegroundWindowScanAt = Time.unscaledTime + 0.25f;
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || !IsWindowVisible(window))
            return;
        GetWindowThreadProcessId(window, out var processId);
        if (processId != (uint)Environment.ProcessId)
            _lastExternalForegroundWindow = window;
    }

    private static IntPtr GetControllableWindow()
    {
        var foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero && IsWindowVisible(foreground))
        {
            GetWindowThreadProcessId(foreground, out var processId);
            if (processId != (uint)Environment.ProcessId)
                return foreground;
        }
        if (_lastExternalForegroundWindow != IntPtr.Zero && IsWindowVisible(_lastExternalForegroundWindow))
            return _lastExternalForegroundWindow;
        return IntPtr.Zero;
    }

    private static void ProcessLocalTimers(DialogueManager manager)
    {
        if (LocalTimers.Count == 0 || manager.IsBusy || _requestInFlight)
            return;
        var now = DateTimeOffset.Now;
        var due = LocalTimers.Where(timer => timer.DueAt <= now).OrderBy(timer => timer.DueAt).ToList();
        if (due.Count == 0)
            return;
        foreach (var timer in due)
            LocalTimers.Remove(timer);
        var message = due.Count == 1
            ? due[0].Message
            : ApiKeyText($"有 {due.Count} 個計時器到時間了。{due[0].Message}", $"有 {due.Count} 个计时器到时间了。{due[0].Message}", $"{due.Count}件のタイマーが時間になったよ。{due[0].Message}", $"{due.Count} timers are due. {due[0].Message}");
        manager.ForceSay(message, string.Empty, 12f);
        if (Plugin.VoiceEnabled.Value)
            _ = RequestSpeechAsync(message, poseStyle: CapturePoseContext().VoiceStyle);
        Plugin.PluginLog.LogInfo($"Delivered {due.Count} local Lilith timer(s)." );
    }

    private static void ProcessPendingSystemAction()
    {
        var pending = _pendingSystemAction;
        if (pending == null)
            return;
        if (_requestInFlight || !PendingReplies.IsEmpty)
        {
            pending.ExecuteAfter = Math.Max(pending.ExecuteAfter, Time.unscaledTime + 12f);
            return;
        }
        if (Time.unscaledTime < pending.ExecuteAfter)
            return;
        _pendingSystemAction = null;
        try
        {
            var success = pending.Action switch
            {
                "lock" => LockWorkStation(),
                "sleep" => SetSuspendState(false, false, false),
                _ => false
            };
            if (!success)
                Plugin.PluginLog.LogWarning($"Pending system action '{pending.Action}' was rejected by Windows or is unavailable on this PC.");
            else
                Plugin.PluginLog.LogInfo($"Executed explicit user-requested system action '{pending.Action}'.");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Pending system action '{pending.Action}' failed: {exception.Message}");
        }
    }

    private static double GetWindowsIdleSeconds()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return 0;
        return unchecked((uint)Environment.TickCount - info.Time) / 1000d;
    }

    private sealed class AiNoteState
    {
        public List<AiNoteEvent> Pending { get; set; } = new();
        public List<DateTimeOffset> DeliveredAt { get; set; } = new();
        public List<string> DeliveredPaths { get; set; } = new();
    }

    private sealed class AiNoteEvent
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public string LatestContext { get; set; } = string.Empty;
        public string Emotion { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset DeliverAfter { get; set; }
        public string Status { get; set; } = "pending";
    }

    private sealed class GeneratedAiNote
    {
        public string EventId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    internal static void LoadMemory()
    {
        try
        {
            Directory.CreateDirectory(MemoryDirectory);
            if (!File.Exists(MemoryPath))
                return;
            var loaded = JsonSerializer.Deserialize<List<ChatTurn>>(File.ReadAllText(MemoryPath));
            if (loaded == null)
                return;
            lock (MemoryLock)
            {
                RecentConversation.Clear();
                RecentConversation.AddRange(loaded.GetRange(Math.Max(0, loaded.Count - MaxRememberedTurns), Math.Min(MaxRememberedTurns, loaded.Count)));
            }
            Plugin.PluginLog.LogInfo($"Loaded {RecentConversation.Count} remembered chat turns.");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not load chat memory: {exception.Message}");
        }
    }

    private static void AddMemoryTurn(string role, string text)
    {
        lock (MemoryLock)
        {
            RecentConversation.Add(new ChatTurn { Role = role, Text = text });
            while (RecentConversation.Count > MaxRememberedTurns)
                RecentConversation.RemoveAt(0);
            try
            {
                Directory.CreateDirectory(MemoryDirectory);
                File.WriteAllText(MemoryPath, JsonSerializer.Serialize(RecentConversation, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception exception)
            {
                Plugin.PluginLog.LogWarning($"Could not save chat memory: {exception.Message}");
            }
        }
    }

    public sealed class ChatTurn
    {
        public string Role { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    private static string[] SplitIntoBubblePages(string text)
    {
        var completeText = text.Trim();
        return completeText.Length == 0 ? Array.Empty<string>() : new[] { completeText };
    }

    internal static bool TryAdvanceAiPage(DialogueManager manager)
    {
        if (!_aiPagesAwaitingAdvance)
            return false;
        var currentNode = manager.CurrentNode;
        if (currentNode == null || !string.Equals(currentNode.text, _currentAiPageText, StringComparison.Ordinal))
        {
            CancelPendingAiPages("Native dialogue took control.");
            return false;
        }
        if (!PendingReplies.TryDequeue(out var page))
        {
            _aiPagesAwaitingAdvance = false;
            return false;
        }
        _aiPagesAwaitingAdvance = !PendingReplies.IsEmpty;
        _currentAiPageText = page;
        _aiTypingFinishedAt = -1f;
        manager.ForceSay(page, string.Empty, 30f);
        return true;
    }

    private static void CancelPendingAiPages(string reason)
    {
        while (PendingReplies.TryDequeue(out _)) { }
        _aiPagesAwaitingAdvance = false;
        _currentAiPageText = string.Empty;
        Plugin.PluginLog.LogInfo($"Cancelled pending AI pages: {reason}");
    }

    internal static void NotifyAiTypingState(bool isTyping)
    {
        if (string.IsNullOrEmpty(_currentAiPageText))
            return;
        _aiTypingFinishedAt = isTyping ? -1f : Time.unscaledTime;
    }

    internal static bool ShouldDelayAiCompletion(DialogueManager manager)
    {
        var node = manager.CurrentNode;
        if (node == null || !string.Equals(node.text, _currentAiPageText, StringComparison.Ordinal))
            return false;
        // Keep the current bubble alive while another AI page is waiting for the
        // user's click. Speech is generated for the complete reply and may continue
        // past the first page, so allowing Unity to close here makes the text look
        // truncated even though the response itself is complete.
        if (_aiPagesAwaitingAdvance)
            return true;
        if (_aiTypingFinishedAt < 0f)
            return true;
        return Time.unscaledTime - _aiTypingFinishedAt < Math.Max(0f, Plugin.PostTypingHoldSeconds.Value);
    }

    internal static void RecordUnvoicedNativeNode(DialogueNode node)
    {
        if (!Plugin.CollectUnvoicedNativeLines.Value || node == null || node.id <= 0 || string.IsNullOrWhiteSpace(node.text))
            return;
        var resolvedSoundId = node.soundId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(resolvedSoundId))
            DialogueLineRepository.TryGetVoiceSoundId(node.lineId, out resolvedSoundId);
        if (!string.IsNullOrWhiteSpace(resolvedSoundId))
        {
            try
            {
                if (ResourceManager.LoadVoiceClip(resolvedSoundId) != null)
                    return;
            }
            catch (Exception exception)
            {
                Plugin.PluginLog.LogWarning($"Voice lookup failed for node {node.id}, soundId={resolvedSoundId}: {exception.Message}");
            }
        }
        if (node.actionType != LilithActionType.None)
        {
            try
            {
                if (ResourceManager.LoadActionVoiceClip(node.actionType) != null)
                    return;
            }
            catch (Exception exception)
            {
                Plugin.PluginLog.LogWarning($"Action voice lookup failed for node {node.id}, action={node.actionType}: {exception.Message}");
            }
        }

        lock (UnvoicedManifestLock)
        {
            if (!RecordedUnvoicedNodeIds.Add(node.id))
                return;
            Directory.CreateDirectory(MemoryDirectory);
            if (!File.Exists(UnvoicedManifestPath))
                File.AppendAllText(UnvoicedManifestPath, "node_id\tline_id\tspeaker\temotion\taction\ttext\n", Encoding.UTF8);
            var speaker = (node.speaker ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            var emotion = (node.emotion ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            var text = node.text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            File.AppendAllText(UnvoicedManifestPath, $"{node.id}\t{node.lineId}\t{speaker}\t{emotion}\t{node.actionType}\t{text}\n", Encoding.UTF8);
            Plugin.PluginLog.LogInfo($"Collected unvoiced native dialogue node {node.id}.");
        }
    }

    internal static bool TryPlayInjectedNativeVoice(DialogueNode node)
    {
        if (!Plugin.NativeVoicePackEnabled.Value || node == null)
            return false;
        if (node.id == _lastInjectedNativeNodeId && Time.unscaledTime - _lastInjectedNativeVoiceAt < 1f)
            return true;
        var japaneseMode = IsJapaneseVoiceMode();
        if (!japaneseMode)
        {
            var soundId = node.soundId ?? string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(soundId))
                    DialogueLineRepository.TryGetVoiceSoundId(node.lineId, out soundId);
                if (!string.IsNullOrWhiteSpace(soundId) && ResourceManager.LoadVoiceClip(soundId) != null)
                    return false;
                if (node.actionType != LilithActionType.None && ResourceManager.LoadActionVoiceClip(node.actionType) != null)
                    return false;
            }
            catch (Exception exception)
            {
                Plugin.PluginLog.LogWarning($"Could not verify native voice before injection: {exception.Message}");
                return false;
            }
        }
        else
        {
            // The shipped desktop-pet build resolves all native Voice and
            // ActionVoice assets to Chinese. Never let that audio leak into the
            // independent Japanese channel, even when a Japanese supplement is
            // not available yet.
            AudioManager.StopVoice();
        }

        EnsureNativeVoiceManifestLoaded();
        if (!NativeVoiceFilesByLineId.TryGetValue(node.lineId, out var file) || !File.Exists(file))
            return japaneseMode;
        try
        {
            var clip = CreateAudioClipFromWav(File.ReadAllBytes(file));
            AudioManager.PlayVoice(clip, false, true);
            NativeVoiceDurationByNodeId[node.id] = clip.length + 0.75f;
            _lastInjectedNativeNodeId = node.id;
            _lastInjectedNativeVoiceAt = Time.unscaledTime;
            Plugin.PluginLog.LogInfo($"Injected supplemental native voice for node={node.id}, line={node.lineId}, duration={clip.length:0.00}s.");
            return true;
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Supplemental native voice failed for line {node.lineId}: {exception.Message}");
            return false;
        }
    }

    internal static float ExtendNativeNodeDuration(DialogueNode node, float original)
    {
        if (node != null && NativeVoiceDurationByNodeId.TryGetValue(node.id, out var voiceDuration))
            return Math.Max(original, voiceDuration);
        return original;
    }

    private static void EnsureNativeVoiceManifestLoaded()
    {
        var directory = (IsJapaneseVoiceMode()
            ? Plugin.JapaneseNativeVoicePackDirectory.Value
            : Plugin.NativeVoicePackDirectory.Value).Trim();
        if (string.Equals(_loadedNativeVoicePackDirectory, directory, StringComparison.OrdinalIgnoreCase))
            return;
        NativeVoiceFilesByLineId.Clear();
        _loadedNativeVoicePackDirectory = directory;
        var manifest = Path.Combine(directory, "manifest.tsv");
        if (!File.Exists(manifest))
        {
            Plugin.PluginLog.LogWarning($"Native voice manifest was not found: {manifest}");
            return;
        }
        foreach (var line in File.ReadLines(manifest, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("node_id\t", StringComparison.OrdinalIgnoreCase))
                continue;
            var columns = line.Split('\t');
            if (columns.Length < 8 || !int.TryParse(columns[1], out var lineId))
                continue;
            var audioFile = columns[7].Trim();
            if (!string.IsNullOrWhiteSpace(audioFile))
                NativeVoiceFilesByLineId[lineId] = Path.Combine(directory, audioFile);
        }
        Plugin.PluginLog.LogInfo($"Loaded {NativeVoiceFilesByLineId.Count} supplemental native voice mappings from {directory}.");
    }

    private static void TryDumpNativeDialogueDatabases(DialogueManager manager)
    {
        try
        {
            var databases = Resources.LoadAll<DialogueDatabase>("Data/Dialogue");
            if (databases == null || databases.Length == 0)
                return;

            var allRows = new List<string> { "node_id\tline_id\tspeaker\temotion\taction\tsound_id\tvoice_status\ttext" };
            var missingRows = new List<string> { "node_id\tline_id\tspeaker\temotion\taction\tsound_id\ttext" };
            var seen = new HashSet<int>();
            foreach (var database in databases)
            {
                if (database == null || database.nodes == null)
                    continue;
                foreach (var node in database.nodes)
                {
                    if (node == null || node.id <= 0 || !seen.Add(node.id))
                        continue;

                    var text = node.text ?? string.Empty;
                    var soundId = node.soundId ?? string.Empty;
                    try
                    {
                        if (DialogueLineRepository.TryGetEntry(node.lineId, out var entry) && entry != null)
                        {
                            if (!string.IsNullOrWhiteSpace(entry.text))
                                text = entry.text;
                            if (string.IsNullOrWhiteSpace(soundId) && !string.IsNullOrWhiteSpace(entry.soundId))
                                soundId = entry.soundId;
                        }
                        if (string.IsNullOrWhiteSpace(soundId))
                            DialogueLineRepository.TryGetVoiceSoundId(node.lineId, out soundId);
                    }
                    catch { }

                    var hasClip = false;
                    if (!string.IsNullOrWhiteSpace(soundId))
                    {
                        try { hasClip = ResourceManager.LoadVoiceClip(soundId) != null; }
                        catch { }
                    }
                    var hasActionVoice = false;
                    if (node.actionType != LilithActionType.None)
                    {
                        try { hasActionVoice = ResourceManager.LoadActionVoiceClip(node.actionType) != null; }
                        catch { }
                    }
                    var status = hasClip ? "original_voice" : hasActionVoice ? "action_voice" : "missing";
                    var rowText = SanitizeTsv(text);
                    var prefix = $"{node.id}\t{node.lineId}\t{SanitizeTsv(node.speaker)}\t{SanitizeTsv(node.emotion)}\t{node.actionType}\t{SanitizeTsv(soundId)}";
                    allRows.Add($"{prefix}\t{status}\t{rowText}");
                    if (status == "missing" && !string.IsNullOrWhiteSpace(text))
                        missingRows.Add($"{prefix}\t{rowText}");
                }
            }
            if (allRows.Count <= 1)
                return;

            Directory.CreateDirectory(MemoryDirectory);
            File.WriteAllLines(Path.Combine(MemoryDirectory, "native-dialogue-inventory.tsv"), allRows, Encoding.UTF8);
            File.WriteAllLines(Path.Combine(MemoryDirectory, "native-dialogue-missing-voice.tsv"), missingRows, Encoding.UTF8);
            _nativeDatabaseDumpCompleted = true;
            Plugin.PluginLog.LogInfo($"Exported {allRows.Count - 1} native dialogue nodes; {missingRows.Count - 1} have no original or action voice.");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Native dialogue database export is not ready: {exception.Message}");
        }
    }

    private static void TryDumpLocalizedLineDatabases()
    {
        try
        {
            var languages = new[] { "zh-HK", "zh-CN", "ja-JP" };
            var any = false;
            Directory.CreateDirectory(MemoryDirectory);
            foreach (var language in languages)
            {
                var database = Resources.Load<DialogueLineDatabase>($"Data/DialogueLine/{language}/DialogueLineDB");
                if (database == null || database.entries == null || database.entries.Count == 0)
                    continue;
                var rows = new List<string> { "line_id\tsound_id\ttext" };
                foreach (var entry in database.entries)
                {
                    if (entry == null || entry.id <= 0)
                        continue;
                    rows.Add($"{entry.id}\t{SanitizeTsv(entry.soundId)}\t{SanitizeTsv(entry.text)}");
                }
                File.WriteAllLines(Path.Combine(MemoryDirectory, $"dialogue-lines-{language}.tsv"), rows, Encoding.UTF8);
                Plugin.PluginLog.LogInfo($"Exported {rows.Count - 1} localized dialogue lines for {language}.");
                any = true;
            }
            var allDatabases = Resources.LoadAll<DialogueLineDatabase>("Data/DialogueLine");
            var detectedIndex = 0;
            foreach (var database in allDatabases)
            {
                if (database == null || database.entries == null || database.entries.Count == 0)
                    continue;
                var rows = new List<string> { "line_id\tsound_id\ttext" };
                var kanaCount = 0;
                foreach (var entry in database.entries)
                {
                    if (entry == null || entry.id <= 0)
                        continue;
                    var text = entry.text ?? string.Empty;
                    kanaCount += Regex.Matches(text, "[ぁ-ゖァ-ヺ]").Count;
                    rows.Add($"{entry.id}\t{SanitizeTsv(entry.soundId)}\t{SanitizeTsv(text)}");
                }
                if (kanaCount > 50)
                {
                    detectedIndex++;
                    File.WriteAllLines(Path.Combine(MemoryDirectory, $"dialogue-lines-detected-ja-{detectedIndex}.tsv"), rows, Encoding.UTF8);
                    Plugin.PluginLog.LogInfo($"Detected Japanese dialogue database with {rows.Count - 1} lines and {kanaCount} kana characters.");
                }
            }
            _localizedLineDatabasesDumped = any;
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Localized dialogue database export is not ready: {exception.Message}");
        }
    }

    private static string SanitizeTsv(string? value) =>
        (value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static void CloseInputBubble(bool clear)
    {
        if (_inputField != null)
        {
            _inputField.DeactivateInputField();
            if (clear)
                _inputField.text = string.Empty;
        }

        _inputBubble?.SetActive(false);
        _focusNextFrame = false;
    }
}

[HarmonyPatch(typeof(ShowSystemTray), "GetFallbackMenuText")]
internal static class TrayMenuLocalizationPatch
{
    private static bool Prefix(string tableEntryKey, ref string __result)
    {
        if (string.Equals(tableEntryKey, "AddApiKey", StringComparison.Ordinal))
        {
            __result = DialogueManagerUpdatePatch.LocalizedText("加入 API KEY", "加入 API KEY", "APIキーを追加", "Add API Key");
            return false;
        }
        if (tableEntryKey is "Gemini" or "OpenAI" or "DeepSeek")
        {
            __result = tableEntryKey;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(GiftExchangeView), "OnRedeemButtonClicked")]
internal static class GiftExchangeApiKeyPatch
{
    private static bool Prefix(GiftExchangeView __instance)
    {
        return !DialogueManagerUpdatePatch.TrySaveApiKey(__instance);
    }
}

[HarmonyPatch(typeof(DialogueManager), "PlayNodeVoice")]
internal static class DialogueManagerPlayNodeVoicePatch
{
    private static bool Prefix(DialogueNode node)
    {
        try
        {
            DialogueManagerUpdatePatch.RecordUnvoicedNativeNode(node);
            return !DialogueManagerUpdatePatch.TryPlayInjectedNativeVoice(node);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not inspect native dialogue voice: {exception.Message}");
            return true;
        }
    }
}

[HarmonyPatch(typeof(DialogueManager), "GetNodeDuration")]
internal static class DialogueManagerGetNodeDurationPatch
{
    private static void Postfix(DialogueNode node, ref float __result)
    {
        __result = DialogueManagerUpdatePatch.ExtendNativeNodeDuration(node, __result);
    }
}

[HarmonyPatch(typeof(DialogueBubbleUI), "ShowNode")]
internal static class DialogueBubbleUIShowNodeVoicePatch
{
    private static void Postfix(DialogueNode node)
    {
        if (node == null)
            return;
        Plugin.PluginLog.LogInfo($"Dialogue bubble displayed node={node.id}, line={node.lineId}, action={node.actionType}.");
        DialogueManagerUpdatePatch.RecordUnvoicedNativeNode(node);
        DialogueManagerUpdatePatch.TryPlayInjectedNativeVoice(node);
    }
}

[HarmonyPatch(typeof(DialogueManager), "BeginDialogue")]
internal static class DialogueManagerBeginDialoguePatch
{
    private static void Prefix(DialogueNode node) => DialogueManagerUpdatePatch.RecordUnvoicedNativeNode(node);
}

[HarmonyPatch(typeof(DialogueManager), "ApplyAdvancedNode")]
internal static class DialogueManagerApplyAdvancedNodePatch
{
    private static void Prefix(DialogueNode node) => DialogueManagerUpdatePatch.RecordUnvoicedNativeNode(node);
}

[HarmonyPatch(typeof(DialogueManager), nameof(DialogueManager.AdvanceDialogue))]
internal static class DialogueManagerAdvancePatch
{
    private static bool Prefix(DialogueManager __instance)
    {
        return !DialogueManagerUpdatePatch.TryAdvanceAiPage(__instance);
    }
}

[HarmonyPatch(typeof(TypewriterEffect), "SetIsTyping")]
internal static class TypewriterStatePatch
{
    private static void Postfix(bool value)
    {
        DialogueManagerUpdatePatch.NotifyAiTypingState(value);
    }
}

[HarmonyPatch(typeof(TypewriterEffect), nameof(TypewriterEffect.Play))]
internal static class TypewriterPlayNativeVoicePatch
{
    private static void Postfix(string text)
    {
        try
        {
            var manager = DialogueManager.instance;
            var node = manager?.CurrentNode;
            Plugin.PluginLog.LogInfo($"Typewriter displayed text; current node={(node == null ? -1 : node.id)}, line={(node == null ? -1 : node.lineId)}.");
            if (node == null || node.id <= 0)
                return;
            DialogueManagerUpdatePatch.RecordUnvoicedNativeNode(node);
            DialogueManagerUpdatePatch.TryPlayInjectedNativeVoice(node);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not inject voice from typewriter path: {exception.Message}");
        }
    }
}

[HarmonyPatch(typeof(DialogueManager), "CompleteCurrentNode")]
internal static class DialogueCompletionPatch
{
    private static bool Prefix(DialogueManager __instance)
    {
        return !DialogueManagerUpdatePatch.ShouldDelayAiCompletion(__instance);
    }
}
