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

}
