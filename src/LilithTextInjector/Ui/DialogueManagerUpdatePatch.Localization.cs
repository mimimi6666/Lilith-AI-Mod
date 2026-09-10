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

internal static partial class DialogueManagerUpdatePatch
{
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

}
