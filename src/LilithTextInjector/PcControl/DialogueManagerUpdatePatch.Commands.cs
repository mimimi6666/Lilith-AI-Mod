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

}
