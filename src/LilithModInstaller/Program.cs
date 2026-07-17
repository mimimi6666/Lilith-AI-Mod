using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LilithModInstaller;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--voice-host", StringComparer.OrdinalIgnoreCase))
        {
            var parent = 0;
            var index = Array.FindIndex(args, value => string.Equals(value, "--parent", StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && index + 1 < args.Length)
                int.TryParse(args[index + 1], out parent);
            VoiceHost.RunAsync(parent).GetAwaiter().GetResult();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
    }
}

internal sealed class ReleaseManifest
{
    public string Version { get; set; } = "0.1.0";
    public Dictionary<string, PackageSpec> Packages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class PackageSpec
{
    public string File { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Bytes { get; set; }
}

internal sealed class InstalledManifest
{
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset InstalledAt { get; set; }
    public Dictionary<string, List<string>> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class InstallerForm : Form
{
    private const string AppId = "4643090";
    private readonly bool _zhTraditional;
    private readonly bool _zhSimplified;
    private readonly bool _japanese;
    private readonly TextBox _path = new();
    private readonly Button _browse = new();
    private readonly CheckBox _core = new();
    private readonly CheckBox _voicePack = new();
    private readonly CheckBox _dynamicVoice = new();
    private readonly CheckBox _launch = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _status = new();
    private readonly Button _install = new();
    private readonly Button _uninstall = new();
    private readonly Button _close = new();
    private readonly string _baseDirectory = AppContext.BaseDirectory;
    private ReleaseManifest _manifest = new();

    internal InstallerForm()
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        _zhSimplified = culture.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase)
            || culture.StartsWith("zh-SG", StringComparison.OrdinalIgnoreCase)
            || culture.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase);
        _zhTraditional = !_zhSimplified && culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        _japanese = culture.StartsWith("ja", StringComparison.OrdinalIgnoreCase);

        Text = L("莉莉絲 AI MOD 一鍵安裝程式", "莉莉丝 AI MOD 一键安装程序", "リリス AI MOD セットアップ", "Lilith AI Mod Setup");
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(680, 485);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var title = new Label
        {
            Text = L("讓莉莉絲能聊天、聽你說話，並使用中日語音。", "让莉莉丝能聊天、听你说话，并使用中日语音。", "リリスとの会話・音声入力・中国語／日本語音声を追加します。", "Adds AI chat, voice input, and Chinese/Japanese voices to Lilith."),
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = false,
            Bounds = new Rectangle(24, 20, 630, 28)
        };
        Controls.Add(title);

        Controls.Add(new Label { Text = L("遊戲資料夾", "游戏文件夹", "ゲームフォルダー", "Game folder"), Bounds = new Rectangle(24, 66, 630, 24) });
        _path.Bounds = new Rectangle(24, 92, 535, 30);
        _browse.Text = L("瀏覽…", "浏览…", "参照…", "Browse…");
        _browse.Bounds = new Rectangle(570, 91, 84, 31);
        _browse.Click += (_, _) => BrowseForGame();
        Controls.Add(_path);
        Controls.Add(_browse);

        _core.Text = L("核心 MOD 與 BepInEx（必要）", "核心 MOD 与 BepInEx（必需）", "コアMODとBepInEx（必須）", "Core mod and BepInEx (required)");
        _core.Checked = true;
        _core.Enabled = false;
        _core.Bounds = new Rectangle(32, 145, 610, 28);
        _voicePack.Text = L("完整中／日文補充台詞語音（約 301 MB）", "完整中／日文补充台词语音（约 301 MB）", "中国語／日本語の追加台詞音声（約301 MB）", "Complete Chinese/Japanese supplemental dialogue voices (~301 MB)");
        _voicePack.Checked = true;
        _voicePack.Bounds = new Rectangle(32, 180, 610, 28);
        _dynamicVoice.Text = L("AI 動態語音（模型約 1.98 GB；首次會自動建立推理環境）", "AI 动态语音（模型约 1.98 GB；首次会自动建立推理环境）", "AI動的音声（モデル約1.98 GB・初回に推論環境を自動構築）", "Dynamic AI voice (~1.98 GB models; builds its inference environment on first install)");
        _dynamicVoice.Checked = true;
        _dynamicVoice.Bounds = new Rectangle(32, 215, 620, 28);
        _launch.Text = L("安裝完成後啟動桌寵", "安装完成后启动桌宠", "完了後にデスクトップペットを起動", "Launch the desktop pet after installation");
        _launch.Checked = true;
        _launch.Bounds = new Rectangle(32, 250, 610, 28);
        Controls.AddRange([_core, _voicePack, _dynamicVoice, _launch]);

        var privacy = new Label
        {
            Text = L("隱私：安裝包不含任何 API Key、聊天紀錄或開發者電腦路徑；更新時也不會覆蓋玩家資料。", "隐私：安装包不含任何 API Key、聊天记录或开发者电脑路径；更新时也不会覆盖玩家数据。", "プライバシー：APIキー、会話履歴、開発PCのパスは含まれず、更新時も個人データを上書きしません。", "Privacy: no API keys, chat history, or developer paths are included; upgrades preserve player data."),
            AutoSize = false,
            ForeColor = Color.DimGray,
            Bounds = new Rectangle(24, 292, 630, 48)
        };
        Controls.Add(privacy);

        _progress.Bounds = new Rectangle(24, 350, 630, 20);
        _status.Text = L("準備就緒", "准备就绪", "準備完了", "Ready");
        _status.AutoEllipsis = true;
        _status.Bounds = new Rectangle(24, 378, 630, 28);
        Controls.Add(_progress);
        Controls.Add(_status);

        _install.Text = L("安裝／更新", "安装／更新", "インストール／更新", "Install / Update");
        _uninstall.Text = L("移除 MOD", "移除 MOD", "MODを削除", "Remove mod");
        _close.Text = L("關閉", "关闭", "閉じる", "Close");
        _install.Bounds = new Rectangle(326, 425, 112, 36);
        _uninstall.Bounds = new Rectangle(446, 425, 100, 36);
        _close.Bounds = new Rectangle(554, 425, 100, 36);
        _install.Click += async (_, _) => await InstallAsync();
        _uninstall.Click += async (_, _) => await UninstallAsync();
        _close.Click += (_, _) => Close();
        Controls.AddRange([_install, _uninstall, _close]);

        Load += (_, _) => InitializeInstaller();
    }

    private string L(string zhTw, string zhCn, string ja, string en)
        => _zhTraditional ? zhTw : _zhSimplified ? zhCn : _japanese ? ja : en;

    private void InitializeInstaller()
    {
        try
        {
            var manifestPath = Path.Combine(_baseDirectory, "release-manifest.json");
            if (File.Exists(manifestPath))
                _manifest = JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(manifestPath), JsonOptions()) ?? new ReleaseManifest();
        }
        catch (Exception exception)
        {
            SetStatus(L("無法讀取發佈資訊：", "无法读取发布信息：", "リリース情報を読み込めません：", "Could not read release information: ") + exception.Message);
        }

        _path.Text = SteamLocator.FindGameDirectory() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_path.Text))
            SetStatus(L("未自動找到遊戲，請按「瀏覽」。", "未自动找到游戏，请点击“浏览”。", "ゲームが見つかりません。［参照］で選択してください。", "Game not found automatically. Please use Browse."));
    }

    private void BrowseForGame()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = L("選擇包含 Lilith.exe 的遊戲資料夾", "选择包含 Lilith.exe 的游戏文件夹", "Lilith.exe があるゲームフォルダーを選択", "Select the game folder containing Lilith.exe"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            InitialDirectory = Directory.Exists(_path.Text) ? _path.Text : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _path.Text = dialog.SelectedPath;
    }

    private async Task InstallAsync()
    {
        var game = Path.GetFullPath(_path.Text.Trim());
        if (!SteamLocator.IsGameDirectory(game))
        {
            MessageBox.Show(this, L("請選擇正確的遊戲資料夾（必須包含 Lilith.exe）。", "请选择正确的游戏文件夹（必须包含 Lilith.exe）。", "正しいゲームフォルダー（Lilith.exeを含む）を選択してください。", "Select the correct game folder containing Lilith.exe."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (Process.GetProcessesByName("Lilith").Length > 0)
        {
            MessageBox.Show(this, L("請先關閉莉莉絲桌寵，再重新按安裝。", "请先关闭莉莉丝桌宠，再重新点击安装。", "リリスを終了してから、もう一度インストールしてください。", "Close Lilith before installing, then try again."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetBusy(true);
        var installed = new InstalledManifest { Version = _manifest.Version, InstalledAt = DateTimeOffset.Now };
        try
        {
            var selections = new List<string> { "core" };
            if (_voicePack.Checked) selections.Add("voicePack");
            if (_dynamicVoice.Checked) selections.Add("voiceRuntime");
            var step = 0;
            foreach (var name in selections)
            {
                step++;
                _progress.Value = Math.Min(90, (step - 1) * 80 / Math.Max(1, selections.Count));
                var package = await AcquirePackageAsync(name);
                SetStatus(string.Format(L("正在安裝 {0}…", "正在安装 {0}…", "{0} をインストール中…", "Installing {0}…"), name));
                installed.Files[name] = ExtractPackage(package, game);
            }

            DisableBepInExConsole(game);

            if (_dynamicVoice.Checked)
            {
                var runtime = Path.Combine(game, "BepInEx", "data", "LilithTextInjector", "voice-runtime");
                Directory.CreateDirectory(runtime);
                if (!File.Exists(Path.Combine(runtime, "LilithVoiceHost.exe")))
                    throw new FileNotFoundException("The dynamic voice package does not contain LilithVoiceHost.exe.");
                await PrepareVoiceRuntimeAsync(runtime);
            }

            var manifestDirectory = Path.Combine(game, "BepInEx", "data", "LilithTextInjector");
            Directory.CreateDirectory(manifestDirectory);
            File.WriteAllText(Path.Combine(manifestDirectory, "installed-files.json"), JsonSerializer.Serialize(installed, JsonOptions(true)), new UTF8Encoding(false));
            _progress.Value = 100;
            SetStatus(L("安裝完成。API Key 請在左下角莉莉絲選單中由玩家自行輸入。", "安装完成。API Key 请在左下角莉莉丝菜单中由玩家自行输入。", "インストール完了。APIキーは左下のリリスメニューから入力してください。", "Installation complete. Enter your own API key from Lilith's lower-left tray menu."));
            if (_launch.Checked)
                Process.Start(new ProcessStartInfo(Path.Combine(game, "Lilith.exe")) { WorkingDirectory = game, UseShellExecute = true });
        }
        catch (UnauthorizedAccessException)
        {
            SetStatus(L("需要系統管理員權限，正在重新開啟安裝程式…", "需要管理员权限，正在重新打开安装程序…", "管理者権限で再起動します…", "Restarting the installer with administrator privileges…"));
            RestartElevated();
            Close();
        }
        catch (Exception exception)
        {
            _progress.Value = 0;
            SetStatus(L("安裝失敗：", "安装失败：", "インストール失敗：", "Installation failed: ") + exception.Message);
            MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static void DisableBepInExConsole(string game)
    {
        var configDirectory = Path.Combine(game, "BepInEx", "config");
        Directory.CreateDirectory(configDirectory);
        var path = Path.Combine(configDirectory, "BepInEx.cfg");
        var lines = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : new List<string>();

        var sectionIndex = lines.FindIndex(line =>
            string.Equals(line.Trim(), "[Logging.Console]", StringComparison.OrdinalIgnoreCase));
        if (sectionIndex < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1])) lines.Add(string.Empty);
            lines.Add("[Logging.Console]");
            lines.Add(string.Empty);
            lines.Add("Enabled = false");
        }
        else
        {
            var nextSection = lines.FindIndex(sectionIndex + 1, line =>
                line.TrimStart().StartsWith("[", StringComparison.Ordinal));
            if (nextSection < 0) nextSection = lines.Count;
            var enabledIndex = -1;
            for (var index = sectionIndex + 1; index < nextSection; index++)
            {
                if (Regex.IsMatch(lines[index], @"^\s*Enabled\s*=", RegexOptions.IgnoreCase))
                {
                    enabledIndex = index;
                    break;
                }
            }
            if (enabledIndex >= 0)
                lines[enabledIndex] = "Enabled = false";
            else
                lines.Insert(sectionIndex + 1, "Enabled = false");
        }

        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private async Task UninstallAsync()
    {
        var game = Path.GetFullPath(_path.Text.Trim());
        if (!SteamLocator.IsGameDirectory(game)) return;
        var answer = MessageBox.Show(this,
            L("移除 MOD 檔案？API Key、聊天記憶與玩家設定會保留。", "移除 MOD 文件？API Key、聊天记忆和玩家设置将会保留。", "MODを削除しますか？APIキー、会話履歴、設定は保持されます。", "Remove mod files? API keys, chat memory, and player settings will be preserved."),
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        SetBusy(true);
        try
        {
            var manifestPath = Path.Combine(game, "BepInEx", "data", "LilithTextInjector", "installed-files.json");
            var installed = File.Exists(manifestPath)
                ? JsonSerializer.Deserialize<InstalledManifest>(File.ReadAllText(manifestPath), JsonOptions())
                : null;
            if (installed != null)
            {
                foreach (var relative in installed.Files.SelectMany(pair => pair.Value).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(value => value.Length))
                {
                    if (IsSharedLoaderFile(relative)) continue;
                    var full = Path.GetFullPath(Path.Combine(game, relative));
                    if (full.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                        File.Delete(full);
                }
            }
            else
            {
                foreach (var name in new[] { "LilithTextInjector.dll", "NAudio.dll", "NAudio.Core.dll", "NAudio.Wasapi.dll" })
                {
                    var path = Path.Combine(game, "BepInEx", "plugins", name);
                    if (File.Exists(path)) File.Delete(path);
                }
            }
            if (File.Exists(manifestPath)) File.Delete(manifestPath);
            _progress.Value = 100;
            SetStatus(L("MOD 已移除；個人設定與 API Key 已保留。", "MOD 已移除；个人设置和 API Key 已保留。", "MODを削除しました。個人設定とAPIキーは保持されています。", "Mod removed; personal settings and API keys were preserved."));
        }
        catch (Exception exception)
        {
            SetStatus(L("移除失敗：", "移除失败：", "削除失敗：", "Removal failed: ") + exception.Message);
        }
        finally { SetBusy(false); }
        await Task.CompletedTask;
    }

    private async Task<string> AcquirePackageAsync(string name)
    {
        if (!_manifest.Packages.TryGetValue(name, out var spec))
            throw new InvalidOperationException($"Package '{name}' is missing from release-manifest.json.");
        var local = Path.Combine(_baseDirectory, "packages", spec.File);
        if (!File.Exists(local))
        {
            if (string.IsNullOrWhiteSpace(spec.Url))
                throw new FileNotFoundException(L("缺少安裝元件且尚未設定下載網址：", "缺少安装组件且尚未设置下载地址：", "コンポーネントがなく、ダウンロードURLも未設定です：", "A package is missing and has no download URL: ") + spec.File);
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            var temporary = local + ".download";
            using var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };
            using var response = await client.GetAsync(spec.Url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? spec.Bytes;
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var destination = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[1024 * 256];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read));
                received += read;
                if (total > 0) _progress.Value = Math.Clamp((int)(received * 70 / total), 0, 70);
                SetStatus(string.Format(L("正在下載 {0}：{1:0.0} MB", "正在下载 {0}：{1:0.0} MB", "{0} をダウンロード中：{1:0.0} MB", "Downloading {0}: {1:0.0} MB"), name, received / 1048576d));
            }
            File.Move(temporary, local, true);
        }
        if (!string.IsNullOrWhiteSpace(spec.Sha256))
        {
            using var stream = File.OpenRead(local);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            if (!hash.Equals(spec.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Checksum mismatch for {spec.File}.");
        }
        return local;
    }

    private static List<string> ExtractPackage(string archive, string game)
    {
        var files = new List<string>();
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var destination = Path.GetFullPath(Path.Combine(game, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Package contains an unsafe path.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
            files.Add(Path.GetRelativePath(game, destination));
        }
        return files;
    }

    private async Task PrepareVoiceRuntimeAsync(string runtime)
    {
        var ready = Path.Combine(runtime, ".ready");
        if (File.Exists(ready)) return;
        var uv = Path.Combine(runtime, "uv.exe");
        var requirements = Path.Combine(runtime, "requirements-inference.txt");
        if (!File.Exists(uv) || !File.Exists(requirements))
            throw new FileNotFoundException("The dynamic voice package is incomplete (uv.exe or requirements-inference.txt is missing).");
        var pythonDirectory = Path.Combine(runtime, "python");
        SetStatus(L("正在準備獨立 Python 語音環境…", "正在准备独立 Python 语音环境…", "独立Python音声環境を準備中…", "Preparing the isolated Python voice environment…"));
        await RunProcessAsync(uv, $"venv \"{pythonDirectory}\" --python 3.10 --python-preference managed --relocatable", runtime);
        var python = Path.Combine(pythonDirectory, "Scripts", "python.exe");
        var nvidia = VoiceHost.HasNvidiaGpu();
        File.WriteAllText(Path.Combine(runtime, "device.txt"), nvidia ? "cuda" : "cpu");
        var torchIndex = nvidia ? "https://download.pytorch.org/whl/cu124" : "https://download.pytorch.org/whl/cpu";
        SetStatus(nvidia
            ? L("偵測到 NVIDIA 顯示卡，正在下載 GPU 語音元件…", "检测到 NVIDIA 显卡，正在下载 GPU 语音组件…", "NVIDIA GPUを検出。GPU音声コンポーネントを取得中…", "NVIDIA GPU detected; downloading GPU voice components…")
            : L("未偵測到相容 NVIDIA 顯示卡，正在下載 CPU 語音元件…", "未检测到兼容 NVIDIA 显卡，正在下载 CPU 语音组件…", "対応NVIDIA GPUなし。CPU音声コンポーネントを取得中…", "No compatible NVIDIA GPU detected; downloading CPU voice components…"));
        await RunProcessAsync(uv, $"pip install --python \"{python}\" torch==2.6.0 torchaudio==2.6.0 --index-url {torchIndex}", runtime);
        SetStatus(L("正在安裝語音辨識與合成相依元件…", "正在安装语音识别与合成依赖组件…", "音声合成の依存コンポーネントをインストール中…", "Installing voice synthesis dependencies…"));
        await RunProcessAsync(uv, $"pip install --python \"{python}\" -r \"{requirements}\"", runtime);
        File.WriteAllText(ready, DateTimeOffset.Now.ToString("O"));
    }

    private static async Task RunProcessAsync(string file, string arguments, string workingDirectory)
    {
        var log = Path.Combine(workingDirectory, "voice-runtime-install.log");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        await File.AppendAllTextAsync(log, $"> {Path.GetFileName(file)} {arguments}\n{output}\n{error}\n", new UTF8Encoding(false));
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Voice dependency installer exited with code {process.ExitCode}. See {log}");
    }

    private static bool IsSharedLoaderFile(string relative)
    {
        var normalized = relative.Replace('/', '\\');
        return normalized.StartsWith("BepInEx\\core\\", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("BepInEx\\patchers\\", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("BepInEx\\unity-libs\\", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("dotnet\\", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("doorstop_config.ini", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(".doorstop_version", StringComparison.OrdinalIgnoreCase);
    }

    private void SetBusy(bool busy)
    {
        _install.Enabled = !busy;
        _uninstall.Enabled = !busy;
        _browse.Enabled = !busy;
        _close.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        _status.Refresh();
    }

    private static JsonSerializerOptions JsonOptions(bool indented = false) => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = indented
    };

    private void RestartElevated()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return;
        try
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" });
        }
        catch { }
    }
}

internal static class SteamLocator
{
    internal static string? FindGameDirectory()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (IsGameDirectory(AppContext.BaseDirectory)) return Path.GetFullPath(AppContext.BaseDirectory);
        foreach (var registry in new[]
                 {
                     (RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                     (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                     (RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath")
                 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(registry.Item1, RegistryView.Default);
                using var key = baseKey.OpenSubKey(registry.Item2);
                if (key?.GetValue(registry.Item3) is string path && Directory.Exists(path)) candidates.Add(path);
            }
            catch { }
        }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));

        foreach (var steam in candidates.ToArray())
        {
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steam };
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                    libraries.Add(match.Groups["path"].Value.Replace("\\\\", "\\"));
            }
            foreach (var library in libraries)
            {
                var steamApps = Path.Combine(library, "steamapps");
                var manifest = Path.Combine(steamApps, "appmanifest_4643090.acf");
                if (File.Exists(manifest))
                {
                    var match = Regex.Match(File.ReadAllText(manifest), "\\\"installdir\\\"\\s+\\\"(?<dir>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var game = Path.Combine(steamApps, "common", match.Groups["dir"].Value);
                        if (IsGameDirectory(game)) return Path.GetFullPath(game);
                    }
                }
                var fallback = Path.Combine(steamApps, "common", "The NOexistenceN of Lilith");
                if (IsGameDirectory(fallback)) return Path.GetFullPath(fallback);
            }
        }
        return null;
    }

    internal static bool IsGameDirectory(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && File.Exists(Path.Combine(path, "Lilith.exe"))
           && File.Exists(Path.Combine(path, "GameAssembly.dll"))
           && Directory.Exists(Path.Combine(path, "Lilith_Data"));
}

internal static class VoiceHost
{
    internal static async Task RunAsync(int parentPid)
    {
        using var mutex = new Mutex(true, "Local\\LilithAIVoiceHost", out var created);
        if (!created) return;
        var root = AppContext.BaseDirectory;
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        var log = Path.Combine(root, "logs", "voice-host.log");
        var owned = new List<Process>();
        try
        {
            var python = Path.Combine(root, "python", "Scripts", "python.exe");
            var api = Path.Combine(root, "gpt-sovits", "api_v2.py");
            if (!File.Exists(python) || !File.Exists(api) || !File.Exists(Path.Combine(root, ".ready")))
            {
                await LogAsync(log, "Voice runtime is not ready.");
                return;
            }
            var device = File.Exists(Path.Combine(root, "device.txt"))
                ? File.ReadAllText(Path.Combine(root, "device.txt")).Trim().ToLowerInvariant()
                : HasNvidiaGpu() ? "cuda" : "cpu";
            foreach (var service in new[] { (Port: 9880, Name: "zh"), (Port: 9881, Name: "ja") })
            {
                if (await PortOpenAsync(service.Port, 300)) continue;
                var config = Path.Combine(root, "config", $"{service.Name}-{device}.yaml");
                if (!File.Exists(config)) throw new FileNotFoundException("Voice configuration is missing.", config);
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = $"\"{api}\" -a 127.0.0.1 -p {service.Port} -c \"{config}\"",
                    WorkingDirectory = Path.Combine(root, "gpt-sovits"),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }) ?? throw new InvalidOperationException("Could not start the voice service.");
                process.OutputDataReceived += async (_, e) => { if (e.Data != null) await LogAsync(log, $"[{service.Name}] {e.Data}"); };
                process.ErrorDataReceived += async (_, e) => { if (e.Data != null) await LogAsync(log, $"[{service.Name}:err] {e.Data}"); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                owned.Add(process);
            }
            await LogAsync(log, $"Voice host started ({device}); owned processes={owned.Count}.");
            while (parentPid > 0)
            {
                try
                {
                    using var parent = Process.GetProcessById(parentPid);
                    if (parent.HasExited) break;
                }
                catch { break; }
                await Task.Delay(2000);
            }
        }
        catch (Exception exception)
        {
            await LogAsync(log, exception.ToString());
        }
        finally
        {
            foreach (var process in owned)
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                process.Dispose();
            }
            await LogAsync(log, "Voice host stopped.");
        }
    }

    internal static bool HasNvidiaGpu()
    {
        foreach (var candidate in new[]
                 {
                     "nvidia-smi.exe",
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
                 })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(candidate, "-L")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                });
                if (process == null) continue;
                if (process.WaitForExit(3000) && process.ExitCode == 0 && process.StandardOutput.ReadToEnd().Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
        }
        return false;
    }

    private static async Task<bool> PortOpenAsync(int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync("127.0.0.1", port, timeout.Token);
            return true;
        }
        catch { return false; }
    }

    private static readonly SemaphoreSlim LogLock = new(1, 1);
    private static async Task LogAsync(string path, string message)
    {
        await LogLock.WaitAsync();
        try { await File.AppendAllTextAsync(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}", new UTF8Encoding(false)); }
        finally { LogLock.Release(); }
    }
}
