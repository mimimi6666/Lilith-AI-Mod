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
    private static void EnsureLocalVoiceHost()
    {
        if (Time.unscaledTime < 2f)
            return;

        var useJapanese = IsJapaneseVoiceMode();
        if (_voiceHostProcess != null)
        {
            try
            {
                if (_voiceHostProcess.HasExited)
                {
                    _voiceHostProcess.Dispose();
                    _voiceHostProcess = null;
                    _voiceHostJapaneseMode = null;
                    _voiceHostLaunchAttempted = false;
                }
                else if (_voiceHostJapaneseMode.HasValue && _voiceHostJapaneseMode.Value != useJapanese)
                {
                    StopLocalVoiceHost();
                    _voiceHostRestartAt = Time.unscaledTime + 1f;
                    Plugin.PluginLog.LogInfo($"Voice language changed to {(useJapanese ? "Japanese" : "Chinese")}; restarting the single local voice service.");
                    return;
                }
            }
            catch
            {
                StopLocalVoiceHost();
            }
        }

        if (Time.unscaledTime < _voiceHostRestartAt || _voiceHostLaunchAttempted)
            return;

        if (!Plugin.VoiceEnabled.Value || !Plugin.VoiceAutoStartLocalService.Value)
        {
            StopLocalVoiceHost();
            return;
        }

        try
        {
            var endpoint = (useJapanese ? Plugin.JapaneseVoiceEndpoint.Value : Plugin.VoiceEndpoint.Value).Trim();
            if (!endpoint.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase))
                return;
            _voiceHostLaunchAttempted = true;
            var hostPath = Environment.ExpandEnvironmentVariables(Plugin.VoiceHostPath.Value.Trim());
            if (!File.Exists(hostPath))
            {
                Plugin.PluginLog.LogInfo("Bundled local voice service is not installed; dynamic voice remains available through configured external endpoints.");
                return;
            }
            var startInfo = new ProcessStartInfo
            {
                FileName = hostPath,
                Arguments = $"--voice-host --parent {Environment.ProcessId} --language {(useJapanese ? "ja" : "zh")}",
                WorkingDirectory = Path.GetDirectoryName(hostPath) ?? Paths.GameRootPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var bundledDotnet = Path.Combine(Paths.GameRootPath, "dotnet");
            if (Directory.Exists(bundledDotnet))
                startInfo.Environment["DOTNET_ROOT"] = bundledDotnet;
            _voiceHostProcess = Process.Start(startInfo);
            _voiceHostJapaneseMode = useJapanese;
            Plugin.PluginLog.LogInfo($"Started the bundled local {(useJapanese ? "Japanese" : "Chinese")} voice host without a console window.");
        }
        catch (Exception exception)
        {
            _voiceHostLaunchAttempted = false;
            Plugin.PluginLog.LogWarning($"Could not start the bundled local voice host: {exception.Message}");
        }
    }

    private static void StopLocalVoiceHost()
    {
        var process = _voiceHostProcess;
        _voiceHostProcess = null;
        _voiceHostJapaneseMode = null;
        _voiceHostLaunchAttempted = false;
        if (process == null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not stop the previous local voice host: {exception.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

}
