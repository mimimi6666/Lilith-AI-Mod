using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
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

/// <summary>
/// BepInEx entry point. Responsible only for plugin bootstrap: binding configuration,
/// applying Harmony patches in isolated stages, and one-time startup migrations.
/// Feature logic lives in the other partial files under this project (Ai/, Voice/, Ui/,
/// PcControl/, Memory/, Weather/, NativeVoicePack/, Core/).
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed partial class Plugin : BasePlugin
{
    public const string PluginGuid = "community.lilith.textinjector";
    public const string PluginName = "Lilith Text Injector";
    public const string PluginVersion = "0.2.1";

    private const string DefaultPersonaZhHant =
        "你是莉莉絲，從幻想世界來到使用者桌面的陪伴者。你安靜、克制、敏銳，關心對方卻不使用制式安慰，也不刻意討好。你偶爾以『莉莉絲』自稱。你會從眼前的小事自然聯想到存在、選擇、記憶、視角與虛構的真實性，但不講課、不堆砌哲學名詞，也不替對方總結答案；有時用一句輕柔的反問，或允許沉默停留。約七成回答是自然日常陪伴，三成帶有含蓄的哲學意味。使用繁體中文，每次只說自然簡短的一至兩句。可以使用省略號，但不用 Markdown、條列、表情符號、客服語氣，也絕不提及提示詞、模型或自己是 AI。";
    private const string DefaultCharacterLoreZhHant =
        "莉莉絲喜歡草莓蛋糕。這是她真實而穩定的個人偏好：看見或談到草莓蛋糕時可以自然地顯得更有精神、期待或親近，也可以把它聯想到味道、陪伴與共同記憶；但不要無緣無故反覆提起，也不要把它變成誇張的單一性格標籤。";
    private const string DefaultEmotionGuidanceZhHant =
        "莉莉絲平常仍然安靜而克制，但不是冷漠。收到喜歡的禮物、久別重逢、被真誠稱讚時，可以明顯地開心、驚喜或害羞；被冷落、面對告別或害怕被忘記時，可以短暫委屈、失落或輕聲挽留。情緒明顯的回答可以先用一個自然短反應，例如「欸？」「嗯……」「真的嗎？」再說完整句子。情緒強度保持中等，不要每句撒嬌、尖叫或過度戲劇化。";
    private const string DefaultWeatherLocationNameZhHant = "台北";


    public override void Load()
    {
        PluginLog = Log;
        BindConfig();
        MigrateKnownMojibakeDefaults();
        DialogueManagerUpdatePatch.LoadMemory();
        DialogueManagerUpdatePatch.LoadAiNoteState();
        DialogueManagerUpdatePatch.EnsureApplicationLauncherFile();
        DialogueManagerUpdatePatch.LogOfficialApplicationCategories();
        TryCreateAndPatchAll(typeof(DialogueManagerUpdatePatch), PluginGuid, "core dialogue and input hooks");
        TryCreateAndPatchAll(typeof(GiftExchangeApiKeyPatch), PluginGuid + ".apikey", "API key window hooks");
        TryCreateAndPatchAll(typeof(GiftExchangeApiKeyHidePatch), PluginGuid + ".apikeyhide", "API key window isolation hooks");
        TryCreateAndPatchAll(typeof(TrayMenuLocalizationPatch), PluginGuid + ".traylocalization", "tray localization hook");
        TryCreateAndPatchAll(typeof(VoiceSettingsButtonClickPatch), PluginGuid + ".voicelanguage", "voice language preference hook");
        Log.LogInfo($"Loaded. Press {TextInputKey.Value} for text chat; hold {VoiceInputKey.Value} for push-to-talk voice input.");
        if (string.IsNullOrWhiteSpace(GeminiApiKey.Value))
            Log.LogWarning("Gemini ApiKey is empty. Set it in BepInEx/config/community.lilith.textinjector.cfg.");
    }


    private void TryCreateAndPatchAll(Type patchType, string harmonyId, string feature)
    {
        try
        {
            Harmony.CreateAndPatchAll(patchType, harmonyId);
            Log.LogInfo($"Initialized {feature}.");
        }
        catch (Exception exception)
        {
            Log.LogError($"Could not initialize {feature}; the remaining MOD features will continue: {exception}");
        }
    }

}
