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
        if (tableEntryKey is "Gemini" or "Qwen" or "OpenAI" or "DeepSeek")
        {
            __result = tableEntryKey == "Qwen"
                ? DialogueManagerUpdatePatch.LocalizedText("千問", "千问", "Qwen（千問）", "Qwen")
                : tableEntryKey;
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

[HarmonyPatch(typeof(GiftExchangeView), "Hide")]
internal static class GiftExchangeApiKeyHidePatch
{
    private static void Postfix(GiftExchangeView __instance)
    {
        DialogueManagerUpdatePatch.NotifyGiftExchangeViewHidden(__instance);
    }
}

[HarmonyPatch(typeof(ButtonPressedSwapSprite), nameof(ButtonPressedSwapSprite.OnPointerClick))]
internal static class VoiceSettingsButtonClickPatch
{
    private static void Postfix(ButtonPressedSwapSprite __instance)
    {
        DialogueManagerUpdatePatch.NotifyVoiceSettingsButtonClicked(__instance);
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

