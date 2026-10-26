using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using LilithPermissionControl;

namespace LilithTextInjector;

[Flags]
internal enum D045CodexTaskGoal : ulong
{
    None = 0,
    ClipboardWrite = 1UL << 0,
    Diagnostics = 1UL << 1,
    Inventory = 1UL << 2,
    SearchPath = 1UL << 3,
    SearchContent = 1UL << 4,
    ModifyFile = 1UL << 5,
    Copy = 1UL << 6,
    MoveRename = 1UL << 7,
    CreateFolders = 1UL << 8,
    Archive = 1UL << 9,
    WindowControl = 1UL << 10,
    UiInspect = 1UL << 11,
    UiAction = 1UL << 12,
    PointerInput = 1UL << 13,
    LaunchWithArguments = 1UL << 14,
    ExecuteProcess = 1UL << 15,
    BackgroundProcess = 1UL << 16,
    WebSearch = 1UL << 17,
    BrowserNavigate = 1UL << 18,
    Download = 1UL << 19,
    CreateData = 1UL << 20,
    VisualObserve = 1UL << 21,
    KeyboardInput = 1UL << 22,
    ApplicationLaunch = 1UL << 23
}

/// <summary>
/// Host-owned projection of an explicit workstation request.  It is a
/// conservative permission preflight and completion checklist; it never
/// grants authority and never lets a provider add goals after the turn was
/// admitted.
/// </summary>
internal sealed class D045CodexTaskPlan
{
    private readonly ReadOnlyCollection<D045PermissionCapability> _required;

    internal D045CodexTaskGoal Goals { get; }
    internal IReadOnlyList<D045PermissionCapability> RequiredCapabilities
        => _required;
    internal bool IsExplicit => Goals != D045CodexTaskGoal.None;

    private D045CodexTaskPlan(
        D045CodexTaskGoal goals,
        IEnumerable<D045PermissionCapability> required)
    {
        Goals = goals;
        _required = Array.AsReadOnly(required
            .Distinct()
            .OrderBy(value => (int)value)
            .ToArray());
    }

    internal bool IsAdmittedBy(D045AgentToolManifest manifest)
    {
        if (manifest is null)
            throw new ArgumentNullException(nameof(manifest));
        return _required.All(manifest.Contains);
    }

    internal static D045CodexTaskPlan Classify(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
            return Empty();

        const RegexOptions options =
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        bool Has(string pattern) => Regex.IsMatch(value, pattern, options);
        bool HasAffirmative(string pattern) => Regex.Matches(
                value,
                pattern,
                options)
            .Cast<Match>()
            .Any(match => !IsNegatedEffect(value, match.Index));

        // Questions about how an effect works are conversation, not an effect
        // request.  Politeness ("please explain" / 「教えてください」) must not
        // turn instructional wording into workstation authority.
        var educational = Has(
            @"(?:\b(?:how\s+(?:do|can|should|would)\b|how\s+to\b|explain\b|what\s+is\b|why\b|tutorial\b|guide\b|show\s+me\s+how\b)|怎麼|怎么|如何|解釋|解释|為什麼|为什么|教我|教學|教学|教程|どうやって|どのように|なぜ|とは|(?:作成|実行|使用|構築)方法|説明して|教えて)" );
        if (educational)
            return Empty();

        var imperative = Has(
            @"(?:\b(?:create|make|generate|scaffold|initialize|write|edit|modify|update|append|patch|fix|refactor|copy|move|rename|search|find|locate|inspect|list|look|view|run|execute|build|compile|test|start|launch|download|fetch|install|unpack|extract|unzip|compress|archive|open|navigate|click|tap|type|paste|focus|maximize|minimize|restore|arrange)\b|建立|創建|创建|新增|寫入|写入|編輯|编辑|修改|更新|修補|修复|重構|重构|複製|复制|移動|移动|重新命名|重命名|搜尋|搜索|尋找|查找|查看|檢視|检查|執行|执行|運行|运行|跑|建置|構建|构建|編譯|编译|測試|测试|下載|下载|安裝|安装|解壓|解压|打包|開啟|开启|打開|打开|點擊|点击|點按|点按|輸入|输入|貼上|粘贴|最大化|最小化|還原|还原|排列|作成|生成|編集|修正|更新|コピー|移動|名前変更|検索|探して|確認|見て|表示|実行|動かして|ビルド|コンパイル|テスト|ダウンロード|取得|インストール|解凍|展開|圧縮|起動|開いて|クリック|押して|入力|貼り付け|最大化|最小化|復元)" );
        var namedApplicationLaunch = HasAffirmative(
            @"(?:\b(?:open|launch|start)\s+(?:the\s+)?[\p{L}\p{N}][\p{L}\p{N} ._+\-]{0,79}?(?:\s+(?:app|application|program|software))?\s*(?=,|;|\bthen\b|\band\s+(?:find|locate|focus|type|enter|input)\b)|(?:請|请|幫我|帮我|替我)?\s*(?:開啟|开启|打開|打开|啟動|启动)\s*[\p{L}\p{N} ._+\-]{1,80}?(?:應用程式|应用程序|程式|程序)?\s*(?=[，,；;]|(?:然後|然后|接著|接着|並|并)\s*(?:找到|尋找|查找|聚焦|輸入|输入))|(?:開いて|起動して)\s*[\p{L}\p{N} ._+\-]{1,80}?(?:アプリ|プログラム)?\s*(?=[、,；;]|(?:それから|次に|そして)\s*(?:探して|見つけて|フォーカス|入力)))" );
        var workstationObject = namedApplicationLaunch || Has(
            @"(?:\b(?:code|coding|source|script|project|repository|repo|workspace|file|folder|directory|archive|zip|package|environment|sdk|runtime|compiler|test|example|sample|application|app|window|screen|screenshot|button|field|browser|website|webpage|url|download|process|command|tool|keyboard|mouse|pointer|ui|interface|control)\b|程式碼|代码|原始碼|源码|腳本|脚本|專案|项目|倉庫|仓库|工作區|工作区|檔案|文件|資料夾|文件夹|壓縮|压缩|封存|套件|包管理|環境|环境|測試|测试|範例|示例|應用|应用|視窗|窗口|畫面|画面|螢幕|屏幕|截圖|截图|按鈕|按钮|欄位|字段|表單|表单|瀏覽器|浏览器|網站|网站|網頁|网页|網址|网址|下載|下载|程序|進程|命令|工具|鍵盤|键盘|滑鼠|鼠標|鼠标|介面|界面|控件|コード|ソース|スクリプト|プロジェクト|リポジトリ|ワークスペース|ファイル|フォルダ|ディレクトリ|アーカイブ|パッケージ|環境|ランタイム|コンパイラ|テスト|サンプル|アプリ|ウィンドウ|画面|スクリーンショット|ボタン|フィールド|フォーム|ブラウザ|ウェブ|ダウンロード|プロセス|コマンド|ツール|キーボード|マウス|ポインター|インターフェース|コントロール)" );
        if (!imperative || !workstationObject)
            return Empty();

        var goals = D045CodexTaskGoal.None;
        var required = new List<D045PermissionCapability>();
        void Add(
            D045CodexTaskGoal goal,
            D045PermissionCapability capability)
        {
            goals |= goal;
            required.Add(capability);
        }

        if (namedApplicationLaunch)
        {
            Add(
                D045CodexTaskGoal.ApplicationLaunch,
                D045PermissionCapability.ApplicationLaunch);
        }

        var createsProject = HasAffirmative(
            @"(?:\b(?:create|make|generate|scaffold|initialize)\b[^.!?]{0,100}\b(?:project|repository|repo|workspace|application|app)\b|(?:建立|創建|创建|新增)[^。.!?]{0,60}(?:專案|项目|倉庫|仓库|工作區|工作区|應用|应用)|(?:プロジェクト|リポジトリ|ワークスペース|アプリ)[^。.!?]{0,50}(?:を)?(?:作成|生成|初期化)|(?:作成|生成|初期化)[^。.!?]{0,50}(?:プロジェクト|リポジトリ|ワークスペース|アプリ))" );
        var createsNewFile = HasAffirmative(
            @"(?:\b(?:create|make|generate)\b[^.!?]{0,80}\b(?:file|source|script)\b|\b(?:new)\b[^.!?]{0,40}\b(?:file|source|script)\b|(?:建立|創建|创建|新增)[^。.!?]{0,50}(?:檔案|文件|原始碼|源码|腳本|脚本)|(?:新規|新しい)[^。.!?]{0,40}(?:ファイル|ソース|スクリプト)|(?:ファイル|ソース|スクリプト)[^。.!?]{0,40}(?:を)?(?:作成|生成)|(?:\b(?:create|make|generate|write)\b|建立|創建|创建|新增|寫入|写入|作成|生成)[^。.!?]{0,160}(?:[\p{L}\p{N}_+ .:\\\/-]+\.[A-Za-z0-9]{1,12})\b)" );
        if (createsProject || createsNewFile)
            Add(D045CodexTaskGoal.CreateData, D045PermissionCapability.LocalDataCreate);

        var createsFolderTree = createsProject || HasAffirmative(
            @"(?:\b(?:create|make|generate)\b[^.!?]{0,80}\b(?:folder|director(?:y|ies))\b|(?:建立|創建|创建|新增)[^。.!?]{0,50}(?:資料夾|文件夹|目錄|目录)|(?:フォルダ|ディレクトリ)[^。.!?]{0,40}(?:を)?(?:作成|生成)|(?:作成|生成)[^。.!?]{0,40}(?:フォルダ|ディレクトリ))" );
        if (createsFolderTree)
            Add(D045CodexTaskGoal.CreateFolders, D045PermissionCapability.FolderTreeCreate);

        if (!namedApplicationLaunch && HasAffirmative(
            @"(?:\b(?:edit|modify|update|append|overwrite|patch|fix|refactor|implement)\b[^.!?]{0,100}\b(?:file|source|code|script|function|method|class|project|repo(?:sitory)?)\b|(?:編輯|编辑|修改|更新|追加|覆寫|覆盖|修補|修复|重構|重构|實作|实现)[^。.!?]{0,80}(?:檔案|文件|原始碼|源码|程式碼|代码|腳本|脚本|函式|函数|方法|類別|类|專案|项目|倉庫|仓库)?|(?:ファイル|ソース|コード|スクリプト|関数|メソッド|クラス|プロジェクト)[^。.!?]{0,60}(?:を)?(?:編集|修正|更新|追記|上書き|実装|リファクタリング)|(?:編集|修正|更新|実装|リファクタリング)[^。.!?]{0,60}(?:ファイル|ソース|コード|スクリプト|関数|メソッド|クラス|プロジェクト))" ))
        {
            Add(D045CodexTaskGoal.ModifyFile, D045PermissionCapability.FileModify);
        }

        if (HasAffirmative(@"(?:\b(?:copy)\b|複製|复制|コピー)" ))
            Add(D045CodexTaskGoal.Copy, D045PermissionCapability.LocalContentCopy);
        if (HasAffirmative(@"(?:\b(?:move|rename)\b|移動|移动|重新命名|重命名|名前変更)" ))
            Add(D045CodexTaskGoal.MoveRename, D045PermissionCapability.LocalContentMoveRename);

        var archiveEffect = HasAffirmative(
            @"(?:\b(?:unzip|extract|unpack|compress)\b|\b(?:create|make|pack)\b[^.!?]{0,60}\b(?:zip|archive)\b|解壓|解压|打包|壓縮|压缩|封存|解凍|展開|圧縮|アーカイブ化)" );
        if (archiveEffect)
            Add(D045CodexTaskGoal.Archive, D045PermissionCapability.ArchiveManage);

        var executesProcess = HasAffirmative(
            @"(?:\b(?:run|execute|build|compile)\b[^.!?:]{0,120}\b(?:command|tool|compiler|tests?|project|code|example|sample|demo|dotnet|python|node|npm|git|executable|program)\b|\b(?:build|compile)\b(?:\s+it|\s+them)?(?:\s|[.!?]|$)|\btest\b[^.!?:]{0,60}\b(?:it|them|tests?|project|code|example|sample|demo|dotnet|python|node|npm|git|executable|program)\b|(?:執行|执行|運行|运行|跑|建置|構建|构建|編譯|编译)[^。.!?：:]{0,80}(?:命令|工具|編譯器|编译器|測試|测试|專案|项目|程式碼|代码|範例|示例|dotnet|python|node|npm|git)?|(?:^|[；;。.!?\n\r]|請|请|並|并|然後|然后|接著|接着|再)\s*(?:幫我|帮我)?(?:測試|测试)(?![：:])[^。.!?：:]{0,40}(?:專案|项目|程式碼|代码|範例|示例|dotnet|python|node|npm|git)?|(?:ビルド|コンパイル)(?:して|する|を実行)?|テスト(?:して|する|を実行)|(?:コマンド|ツール|テスト|プロジェクト|コード|サンプル|デモ|dotnet|python|node|npm|git)[^。.!?]{0,60}(?:を)?(?:実行|動かして))" );
        if (executesProcess)
            Add(D045CodexTaskGoal.ExecuteProcess, D045PermissionCapability.ProcessExecuteNonInteractive);

        if (HasAffirmative(
            @"(?:\b(?:background|server|watch(?:er)?|daemon|long[- ]running)\b|背景|伺服器|服务器|持續運行|持续运行|バックグラウンド|サーバー|デーモン|常駐)" ))
        {
            Add(D045CodexTaskGoal.BackgroundProcess, D045PermissionCapability.ProcessBackgroundManage);
        }

        var namedUiElement = Has(
            @"(?:\b(?:text\s+(?:editing\s+)?area|editor\s+area|input\s+area|text\s+field|edit\s+control|ui\s+element|interface\s+element)\b|文字編輯區|文字编辑区|編輯區|编辑区|輸入區|输入区|文字欄位|文字字段|介面元素|界面元素|UI\s*元素|テキスト編集領域|入力欄|UI\s*要素)" );
        var semanticUi = HasAffirmative(
            @"(?:\b(?:semantic\s+ui|accessibility|ui\s+automation|element\s+tree|control\s+tree)\b|UI\s*(?:元素|元件|控件)(?:樹|树)?|無障礙|无障碍|語意介面|语义界面|アクセシビリティ|UI\s*オートメーション|要素ツリー|コントロールツリー)" )
            || (namedApplicationLaunch && namedUiElement);
        var semanticInspect = semanticUi && HasAffirmative(
            @"(?:\b(?:inspect|read|list|find|locate)\b|檢視|查看|檢查|检查|讀取|读取|列出|找到|尋找|查找|検査|確認|読み取|一覧|探して)" );
        var semanticAction = semanticUi && HasAffirmative(
            @"(?:\b(?:click|select|scroll|type|enter|input)\b|點擊|点击|點按|点按|選取|选择|滾動|滚动|輸入|输入|クリック|選択|スクロール|入力)" );
        if (semanticInspect)
            Add(D045CodexTaskGoal.UiInspect, D045PermissionCapability.ExternalAppSemanticUiInspect);
        if (semanticAction)
            Add(D045CodexTaskGoal.UiAction, D045PermissionCapability.ExternalAppSemanticUiAction);

        var visualObserve = HasAffirmative(
            @"(?:\b(?:look\s+at|view|inspect|check|read|capture|take)\b[^.!?]{0,60}\b(?:screen|screenshot|visual|window|display)\b|\b(?:screenshot|screen\s*capture)\b|(?:看|查看|檢視|检查|辨識|识别|截取|擷取)[^。.!?]{0,40}(?:畫面|画面|螢幕|屏幕|截圖|截图|視窗|窗口)|(?:画面|スクリーンショット|ウィンドウ)[^。.!?]{0,40}(?:を)?(?:見て|確認|表示|撮影|取得))" );
        if (visualObserve)
            Add(D045CodexTaskGoal.VisualObserve, D045PermissionCapability.ScreenshotCapture);

        var pointerInput = HasAffirmative(
            @"(?:\b(?:click|tap|drag|mouse|pointer)\b|\bpress\b[^.!?]{0,20}\bbutton\b|點擊|点击|點按|点按|滑鼠|鼠標|鼠标|拖曳|拖放|按下[^。.!?]{0,12}按鈕|按下[^。.!?]{0,12}按钮|クリック|マウス|ポインター|ドラッグ|ボタン[^。.!?]{0,20}(?:を)?押して)" );
        if (pointerInput && !semanticAction)
            Add(D045CodexTaskGoal.PointerInput, D045PermissionCapability.ExternalAppPointerInput);

        var keyboardInput = HasAffirmative(
            @"(?:\b(?:type|enter|input|paste)\b(?!\s*=)|\bpress\b[^.!?]{0,30}\b(?:key|keyboard|shortcut)\b|\bkeyboard\s+shortcut\b|輸入|输入|貼上|粘贴|鍵盤|键盘|按鍵|按键|快捷鍵|快捷键|入力|貼り付け|キーボード|キー入力|ショートカット)" );
        if (keyboardInput)
            Add(
                D045CodexTaskGoal.KeyboardInput,
                D045PermissionCapability.ArbitraryExternalAppInputKeystrokes);

        if (HasAffirmative(
            @"(?:\b(?:focus|maximize|minimize|restore|arrange)\b[^.!?]{0,60}\bwindow\b|聚焦視窗|聚焦窗口|最大化|最小化|排列視窗|排列窗口|視窗還原|窗口还原|ウィンドウ[^。.!?]{0,40}(?:最大化|最小化|復元|整列))" ))
        {
            Add(D045CodexTaskGoal.WindowControl, D045PermissionCapability.WindowControl);
        }

        if (HasAffirmative(
            @"(?:\b(?:launch|start|run)\b[^.!?]{0,100}\b(?:with|argument|flag|option)\b|帶參數啟動|带参数启动|引數付きで起動|オプション付きで起動)" ))
        {
            Add(
                D045CodexTaskGoal.LaunchWithArguments,
                D045PermissionCapability.ApplicationLaunchWithArguments);
        }

        var inventory = HasAffirmative(
            @"(?:\b(?:list|show|inspect|enumerate)\b[^.!?]{0,80}\b(?:installed\s+apps?|process(?:es)?|open\s+windows?|window\s+list)\b|已安裝應用(?:清單|列表)|已安装应用(?:清单|列表)|程序清單|进程列表|視窗清單|窗口列表|インストール済みアプリ[^。.!?]{0,20}(?:一覧|リスト)|プロセス[^。.!?]{0,20}(?:一覧|リスト)|ウィンドウ[^。.!?]{0,20}(?:一覧|リスト))" );
        if (inventory)
        {
            goals |= D045CodexTaskGoal.Inventory;
            if (HasAffirmative(@"(?:\bprocess(?:es)?\b|程序清單|进程列表|プロセス)" ))
                required.Add(D045PermissionCapability.ProcessInventoryRead);
            else if (HasAffirmative(@"(?:\bwindows?\b|視窗清單|窗口列表|ウィンドウ)" ))
                required.Add(D045PermissionCapability.WindowInventoryRead);
            else
                required.Add(D045PermissionCapability.ApplicationInventoryRead);
        }

        if (HasAffirmative(
            @"(?:\b(?:diagnos\w*|system\s+info|hardware|disk\s+space)\b|診斷|诊断|系統資訊|系统信息|システム情報|診断|ディスク容量)" ))
        {
            Add(D045CodexTaskGoal.Diagnostics, D045PermissionCapability.SystemDiagnosticsRead);
        }

        var contentSearch = HasAffirmative(
            @"(?:\b(?:search|grep|find)\b[^.!?]{0,80}\b(?:content|text|symbol|string|reference)\b|搜尋內容|搜索内容|查找文字|尋找符號|查找符号|内容検索|文字列検索|シンボル検索)" );
        if (contentSearch)
            Add(D045CodexTaskGoal.SearchContent, D045PermissionCapability.LocalContentSearch);
        else if (HasAffirmative(
            @"(?:\b(?:find|search|locate)\b[^.!?]{0,80}\b(?:file|folder|path|directory)\b|搜尋[^。.!?]{0,40}(?:檔案|文件|資料夾|文件夹|路徑|路径)|尋找[^。.!?]{0,40}(?:檔案|文件|資料夾|文件夹|路徑|路径)|查找[^。.!?]{0,40}(?:文件|文件夹|路径)|(?:ファイル|フォルダ|パス|ディレクトリ)[^。.!?]{0,40}(?:を)?(?:検索|探して))" ))
        {
            Add(D045CodexTaskGoal.SearchPath, D045PermissionCapability.LocalPathSearch);
        }

        if (HasAffirmative(
            @"(?:\b(?:copy|put|write|set)\b[^.!?]{0,60}\bclipboard\b|寫入剪貼簿|写入剪贴板|複製到剪貼簿|复制到剪贴板|クリップボード[^。.!?]{0,30}(?:へ|に)(?:コピー|書き込))" ))
        {
            Add(D045CodexTaskGoal.ClipboardWrite, D045PermissionCapability.ClipboardWrite);
        }

        if (HasAffirmative(@"(?:\b(?:web|internet|online)\s+search\b|網路搜尋|网络搜索|ウェブ検索|インターネット検索)" ))
            Add(D045CodexTaskGoal.WebSearch, D045PermissionCapability.WebPublicSearch);
        if (HasAffirmative(
            @"(?:\b(?:open|navigate|browse)\b[^.!?]{0,80}\b(?:url|website|webpage|browser)\b|瀏覽[^。.!?]{0,40}(?:網站|网页|網頁|網址)|浏览[^。.!?]{0,40}(?:网站|网页|网址)|開啟網頁|打开网页|(?:URL|ウェブサイト|ウェブページ)[^。.!?]{0,40}(?:を)?(?:開いて|表示))" ))
        {
            Add(D045CodexTaskGoal.BrowserNavigate, D045PermissionCapability.BrowserNavigate);
        }
        if (HasAffirmative(@"(?:\b(?:download|fetch)\b|下載|下载|ダウンロード)" ))
            Add(D045CodexTaskGoal.Download, D045PermissionCapability.DownloadPublicHttps);

        return new D045CodexTaskPlan(goals, required);
    }

    private static D045CodexTaskPlan Empty()
        => new(
            D045CodexTaskGoal.None,
            Array.Empty<D045PermissionCapability>());

    private static bool IsNegatedEffect(string text, int effectIndex)
    {
        var clauseStart = Math.Max(
            0,
            text.LastIndexOfAny(
                new[] { '.', '!', '?', '。', '！', '？', ';', '；', '\n', '\r' },
                Math.Max(0, effectIndex - 1)) + 1);
        clauseStart = Math.Max(clauseStart, effectIndex - 48);
        var prefix = text[clauseStart..effectIndex];
        return Regex.IsMatch(
            prefix,
            @"(?:\b(?:do\s+not|don't|dont|never|without|must\s+not|should\s+not|no)\b|不要|不可|不得|請勿|请勿|別|别|禁止|無需|无需|しない|しなくて|しないで|禁止|不要)[^.!?。！？;；]{0,32}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}

/// <summary>
/// Provider-neutral ordering for workstation effects whose authority is
/// already frozen in a task plan.  This does not add capabilities; it only
/// prevents a later effect from running before the host has verified the
/// prerequisite receipt for the same turn.
/// </summary>
internal static class D045CodexCapabilitySequence
{
    internal static bool IsExecutableNow(
        D045CodexTaskPlan? plan,
        D045PermissionCapability capability,
        Func<D045PermissionCapability, bool> isVerified)
    {
        if (plan is null)
            return false;
        if (!plan.RequiredCapabilities.Contains(capability))
            return false;
        if (isVerified is null)
            throw new ArgumentNullException(nameof(isVerified));
        return !isVerified(capability)
            && !TryGetPendingPrerequisite(
                plan,
                capability,
                isVerified,
                out _);
    }

    internal static bool TryGetPendingPrerequisite(
        D045CodexTaskPlan? plan,
        D045PermissionCapability capability,
        Func<D045PermissionCapability, bool> isVerified,
        out D045PermissionCapability prerequisite)
    {
        prerequisite = default;
        if (plan is null
            || !plan.RequiredCapabilities.Contains(capability))
        {
            return false;
        }
        if (isVerified is null)
            throw new ArgumentNullException(nameof(isVerified));

        if (IsApplicationTargetedUiCapability(capability)
            && Requires(plan, D045PermissionCapability.ApplicationLaunch)
            && !isVerified(D045PermissionCapability.ApplicationLaunch))
        {
            prerequisite = D045PermissionCapability.ApplicationLaunch;
            return true;
        }
        if (RequiresSemanticInspectionFirst(capability)
            && Requires(
                plan,
                D045PermissionCapability.ExternalAppSemanticUiInspect)
            && !isVerified(
                D045PermissionCapability.ExternalAppSemanticUiInspect))
        {
            prerequisite =
                D045PermissionCapability.ExternalAppSemanticUiInspect;
            return true;
        }
        if (IsDirectInputCapability(capability)
            && Requires(
                plan,
                D045PermissionCapability.ExternalAppSemanticUiAction)
            && !isVerified(
                D045PermissionCapability.ExternalAppSemanticUiAction))
        {
            prerequisite =
                D045PermissionCapability.ExternalAppSemanticUiAction;
            return true;
        }
        if (capability == D045PermissionCapability.ExternalAppPointerInput
            && Requires(plan, D045PermissionCapability.ScreenshotCapture)
            && !isVerified(D045PermissionCapability.ScreenshotCapture))
        {
            prerequisite = D045PermissionCapability.ScreenshotCapture;
            return true;
        }
        return false;
    }

    private static bool Requires(
        D045CodexTaskPlan plan,
        D045PermissionCapability capability)
        => plan.RequiredCapabilities.Contains(capability);

    private static bool IsApplicationTargetedUiCapability(
        D045PermissionCapability capability)
        => capability is
            D045PermissionCapability.ExternalAppSemanticUiInspect
            or D045PermissionCapability.ExternalAppSemanticUiAction
            or D045PermissionCapability.ArbitraryExternalAppInputKeystrokes
            or D045PermissionCapability.ExternalAppPointerInput
            or D045PermissionCapability.WindowControl
            or D045PermissionCapability.ScreenshotCapture;

    private static bool RequiresSemanticInspectionFirst(
        D045PermissionCapability capability)
        => capability is
            D045PermissionCapability.ExternalAppSemanticUiAction
            or D045PermissionCapability.ArbitraryExternalAppInputKeystrokes
            or D045PermissionCapability.ExternalAppPointerInput;

    private static bool IsDirectInputCapability(
        D045PermissionCapability capability)
        => capability is
            D045PermissionCapability.ArbitraryExternalAppInputKeystrokes
            or D045PermissionCapability.ExternalAppPointerInput;
}
