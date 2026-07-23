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
            var requestedName = ExtractRequestedApplicationName(text);
            if (string.IsNullOrWhiteSpace(requestedName))
                return false;
            appName = requestedName;
            if (TryFocusRunningApplication(requestedName))
            {
                reply = ApiKeyText($"好，已切換到{appName}。", $"好，已切换到{appName}。", $"うん、{appName}に切り替えたよ。", $"Sure, I focused {appName}.");
                Plugin.PluginLog.LogInfo($"Focused installed application requested through the local router: '{requestedName}'.");
                return true;
            }
            var startApplication = ResolveWindowsStartApplication(requestedName);
            if (startApplication != null && TryLaunchWindowsStartApplication(startApplication))
            {
                reply = ApiKeyText($"好，正在開啟{startApplication.Name}。", $"好，正在打开{startApplication.Name}。", $"うん、{startApplication.Name}を開くね。", $"Sure, I'm opening {startApplication.Name}.");
                return true;
            }
            var shortcut = ResolveWindowsShortcut(new[] { requestedName }) ?? ResolveFuzzyWindowsShortcut(requestedName);
            if (string.IsNullOrWhiteSpace(shortcut))
                return false;
            target = shortcut;
            arguments = string.Empty;
            appName = Path.GetFileNameWithoutExtension(shortcut);
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

    private static string ExtractRequestedApplicationName(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 160)
            return string.Empty;

        var suffixPattern = "(?:幫我開|帮我开|替我開|替我开|開啟|开启|打開|打开|啟動|启动|撥給|拨给|播給|播给|open|launch|start)\\s*(?<name>[^,，。！？!?;；]{1,80})";
        var suffixMatches = Regex.Matches(text, suffixPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (suffixMatches.Count > 0)
        {
            var name = suffixMatches[suffixMatches.Count - 1].Groups["name"].Value;
            name = Regex.Replace(name, "(?:一下|看看|好嗎|好吗|可以嗎|可以吗|拜託|拜托|please|for me|吧|嗎|吗|呢)\\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            name = Regex.Replace(name, "^(?:應用程式|应用程序|app|application)\\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
            return IsSafeApplicationDisplayName(name) ? name : string.Empty;
        }

        var japanese = Regex.Match(text, "(?<name>[^,，。！？!?;；]{1,80}?)(?:を)?(?:開いて|起動して|立ち上げて)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (japanese.Success)
        {
            var name = Regex.Replace(japanese.Groups["name"].Value, "^(?:ねえ|お願い|ちょっと)\\s*", string.Empty).Trim();
            return IsSafeApplicationDisplayName(name) ? name : string.Empty;
        }
        return string.Empty;
    }

    private static bool IsSafeApplicationDisplayName(string name)
        => name.Length is >= 2 and <= 80
            && !Regex.IsMatch(name, "[\\\\/:*?\"<>|`$]");

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

    private static WindowsStartApplication? ResolveWindowsStartApplication(string requestedName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var requested = NormalizeApplicationName(requestedName);
        if (requested.Length < 2) return null;

        var applications = GetWindowsStartApplications();
        WindowsStartApplication? best = null;
        var bestScore = 0;
        foreach (var application in applications)
        {
            if (Regex.IsMatch(application.Name, "(uninstall|remove|readme|help|manual|website|web site)", RegexOptions.IgnoreCase))
                continue;
            var normalized = NormalizeApplicationName(application.Name);
            var score = normalized == requested ? 1000
                : normalized.StartsWith(requested, StringComparison.Ordinal) ? 800 - Math.Abs(normalized.Length - requested.Length)
                : normalized.Contains(requested, StringComparison.Ordinal) ? 600 - Math.Abs(normalized.Length - requested.Length)
                : requested.Contains(normalized, StringComparison.Ordinal) && normalized.Length >= 4 ? 400 - Math.Abs(normalized.Length - requested.Length)
                : 0;
            if (score <= bestScore)
                continue;
            bestScore = score;
            best = application;
        }
        return bestScore >= 350 ? best : null;
    }

    private static List<WindowsStartApplication> GetWindowsStartApplications()
    {
        lock (WindowsStartAppsLock)
        {
            if (CachedWindowsStartApps.Count > 0
                && DateTimeOffset.UtcNow - _windowsStartAppsLoadedAt < TimeSpan.FromMinutes(10))
                return CachedWindowsStartApps.ToList();

            var discovered = new List<WindowsStartApplication>();
            try
            {
                var startInfo = new ProcessStartInfo("powershell.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                startInfo.ArgumentList.Add("-NoLogo");
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-NonInteractive");
                startInfo.ArgumentList.Add("-WindowStyle");
                startInfo.ArgumentList.Add("Hidden");
                startInfo.ArgumentList.Add("-Command");
                startInfo.ArgumentList.Add("[Console]::OutputEncoding=[Text.UTF8Encoding]::new(); Get-StartApps | Select-Object Name,AppID | ConvertTo-Json -Compress");

                using var process = Process.Start(startInfo);
                if (process == null)
                    return CachedWindowsStartApps.ToList();
                var json = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(true); } catch { }
                    Plugin.PluginLog.LogWarning("Timed out while enumerating Windows Start applications.");
                    return CachedWindowsStartApps.ToList();
                }
                if (process.ExitCode != 0)
                {
                    Plugin.PluginLog.LogWarning($"Could not enumerate Windows Start applications: {error.Trim()}");
                    return CachedWindowsStartApps.ToList();
                }

                using var document = JsonDocument.Parse(json);
                var elements = document.RootElement.ValueKind == JsonValueKind.Array
                    ? document.RootElement.EnumerateArray().ToArray()
                    : new[] { document.RootElement };
                foreach (var element in elements)
                {
                    if (!element.TryGetProperty("Name", out var nameProperty)
                        || !element.TryGetProperty("AppID", out var idProperty))
                        continue;
                    var name = nameProperty.GetString()?.Trim() ?? string.Empty;
                    var appId = idProperty.GetString()?.Trim() ?? string.Empty;
                    if (name.Length == 0 || appId.Length == 0)
                        continue;
                    discovered.Add(new WindowsStartApplication { Name = name, AppId = appId });
                }
            }
            catch (Exception exception)
            {
                Plugin.PluginLog.LogWarning($"Could not enumerate Windows Start applications: {exception.Message}");
                return CachedWindowsStartApps.ToList();
            }

            CachedWindowsStartApps.Clear();
            CachedWindowsStartApps.AddRange(discovered);
            _windowsStartAppsLoadedAt = DateTimeOffset.UtcNow;
            Plugin.PluginLog.LogInfo($"Discovered {CachedWindowsStartApps.Count} Windows Start applications for portable launching.");
            return CachedWindowsStartApps.ToList();
        }
    }

    private static bool TryLaunchWindowsStartApplication(WindowsStartApplication application)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(application.AppId))
            return false;
        var process = Process.Start(new ProcessStartInfo("explorer.exe")
        {
            Arguments = "shell:AppsFolder\\" + application.AppId,
            UseShellExecute = true
        });
        Plugin.PluginLog.LogInfo($"Sent Windows Start application launch for '{application.Name}' ({application.AppId}).");
        return process != null;
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

    private sealed class WindowsStartApplication
    {
        public string Name { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
    }

}
