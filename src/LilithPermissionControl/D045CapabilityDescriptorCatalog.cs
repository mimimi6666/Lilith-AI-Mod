using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LilithPermissionControl;

internal enum D045CapabilityCategory
{
    ComputerRead = 1,
    FilesAndData = 2,
    ApplicationsAndInterface = 3,
    NetworkAndSharing = 4,
    SystemAndDevice = 5,
    DevelopmentAndAutomation = 6
}

internal enum D045CapabilityRiskTier
{
    Low = 1,
    Moderate = 2,
    High = 3,
    Critical = 4
}

internal sealed class D045CapabilityDescriptor
{
    internal D045PermissionCapability Capability { get; }
    internal int Ordinal => (int)Capability;
    internal string StableId { get; }
    internal string ToolToken { get; }
    internal D045CapabilityCategory Category { get; }
    internal string Title { get; }
    internal string Description { get; }
    internal string ToolUseDetails { get; }
    internal D045CapabilityRiskTier RiskTier { get; }
    internal string RiskDetails { get; }

    internal D045CapabilityDescriptor(
        D045PermissionCapability capability,
        string stableId,
        string toolToken,
        D045CapabilityCategory category,
        string title,
        string description,
        string toolUseDetails,
        D045CapabilityRiskTier riskTier,
        string riskDetails)
    {
        Capability = capability;
        StableId = stableId;
        ToolToken = toolToken;
        Category = category;
        Title = title;
        Description = description;
        ToolUseDetails = toolUseDetails;
        RiskTier = riskTier;
        RiskDetails = riskDetails;
    }
}

internal sealed class D045CapabilityCategoryDescriptor
{
    internal D045CapabilityCategory Category { get; }
    internal string StableId { get; }
    internal string Title { get; }
    internal string Summary { get; }
    internal string Details { get; }

    internal D045CapabilityCategoryDescriptor(
        D045CapabilityCategory category,
        string stableId,
        string title,
        string summary,
        string details)
    {
        Category = category;
        StableId = stableId;
        Title = title;
        Summary = summary;
        Details = details;
    }
}

/// <summary>
/// The single descriptive catalog for the 42 independent permission leaves.
/// Categories are presentation-only partitions; only leaf capabilities may be
/// stored, admitted or sent to the Harness.
/// </summary>
internal static class D045CapabilityDescriptorCatalog
{
    internal const int ExactCapabilityCount = 42;
    internal const int ExactCategoryCount = 6;

    private static readonly ReadOnlyCollection<D045CapabilityDescriptor>
        CanonicalDescriptors = Array.AsReadOnly(CreateDescriptors());
    private static readonly ReadOnlyCollection<D045CapabilityCategoryDescriptor>
        CanonicalCategories = Array.AsReadOnly(CreateCategories());

    internal static ReadOnlyCollection<D045CapabilityDescriptor> Descriptors
        => CanonicalDescriptors;

    internal static ReadOnlyCollection<D045CapabilityCategoryDescriptor> Categories
        => CanonicalCategories;

    internal static D045CapabilityDescriptor Get(
        D045PermissionCapability capability)
    {
        D045PermissionControlSnapshot.DemandCapability(capability);
        return CanonicalDescriptors[(int)capability - 1];
    }

    internal static D045CapabilityCategoryDescriptor GetCategory(
        D045CapabilityCategory category)
    {
        if ((int)category is < 1 or > ExactCategoryCount)
            throw new ArgumentOutOfRangeException(nameof(category));
        return CanonicalCategories[(int)category - 1];
    }

    internal static ReadOnlyCollection<D045CapabilityDescriptor> ForCategory(
        D045CapabilityCategory category)
    {
        _ = GetCategory(category);
        return CanonicalDescriptors
            .Where(value => value.Category == category)
            .ToList()
            .AsReadOnly();
    }

    internal static bool TryResolveExactToolToken(
        string? toolToken,
        out D045PermissionCapability capability)
    {
        if (toolToken is not null)
        {
            foreach (var descriptor in CanonicalDescriptors)
            {
                if (string.Equals(
                        toolToken,
                        descriptor.ToolToken,
                        StringComparison.Ordinal))
                {
                    capability = descriptor.Capability;
                    return true;
                }
            }
        }

        capability = default;
        return false;
    }

    private static D045CapabilityCategoryDescriptor[] CreateCategories()
        =>
        new D045CapabilityCategoryDescriptor[]
        {
            new(
                D045CapabilityCategory.ComputerRead,
                "ui.d045.category.computer-read",
                "查看電腦",
                "理解畫面、剪貼簿、系統、程式與視窗狀態",
                "允許莉莉絲取得目前任務所需的受限電腦資訊。擷取到的內容不會因此自動傳到網路。"),
            new(
                D045CapabilityCategory.FilesAndData,
                "ui.d045.category.files-data",
                "檔案與資料",
                "搜尋、讀寫、建立、整理、封存與刪除",
                "每個讀取、修改、搬移及刪除能力仍是獨立 leaf；開啟這一類不會放寬路徑或目標驗證。"),
            new(
                D045CapabilityCategory.ApplicationsAndInterface,
                "ui.d045.category.applications-interface",
                "應用程式與介面",
                "啟動、關閉、切換及操作桌面程式",
                "包含視窗、鍵鼠與語意 UI 操作。執行時仍須鎖定明確程序、視窗或 UI 元素。"),
            new(
                D045CapabilityCategory.NetworkAndSharing,
                "ui.d045.category.network-sharing",
                "網路與分享",
                "搜尋、瀏覽、下載、上傳及對外分享",
                "本機讀取不代表可以傳送；每個對外目的地與本機寫入能力仍會分開核對。"),
            new(
                D045CapabilityCategory.SystemAndDevice,
                "ui.d045.category.system-device",
                "系統與裝置",
                "管理員權限、電源與高影響系統操作",
                "這一類可能中斷工作或觸發 Windows UAC，應只在玩家理解影響時開啟。"),
            new(
                D045CapabilityCategory.DevelopmentAndAutomation,
                "ui.d045.category.development-automation",
                "開發與自動化",
                "終端、程式執行與長時間背景工作",
                "適合 Coding、建置、測試與環境工具；命令、工作目錄、參數及程序生命週期仍需明確。")
        };

    private static D045CapabilityDescriptor[] CreateDescriptors()
        =>
        new[]
        {
            D(D045PermissionCapability.SanitizedLilithStatusRead, "d045.status.sanitized-read", "sanitized_status_read", D045CapabilityCategory.ComputerRead, "讀取莉莉絲狀態摘要", "讀取不含對話正文、憑證與路徑的運行摘要。", "需要確認莉莉絲或執行環境的受限狀態時使用。", D045CapabilityRiskTier.Low, "只讀取清理過的 metadata。"),
            D(D045PermissionCapability.PublicHttpsUrlFetch, "d045.url.public-https-fetch", "public_https_fetch", D045CapabilityCategory.NetworkAndSharing, "讀取公開 HTTPS 網址", "讀取玩家或任務明確指定的公開 HTTPS 內容。", "需要取得單一公開網頁或 API 的唯讀資料時使用。", D045CapabilityRiskTier.Moderate, "會把網址與請求送到外部服務；不允許私有網路。"),
            D(D045PermissionCapability.ClipboardRead, "d045.clipboard.read", "clipboard_read", D045CapabilityCategory.ComputerRead, "讀取剪貼簿", "讀取目前剪貼簿內容並交給目前任務。", "玩家要求處理剛複製的內容時使用。", D045CapabilityRiskTier.Moderate, "剪貼簿可能含私人或敏感資料。"),
            D(D045PermissionCapability.LocalFileRead, "d045.file.read", "local_file_read", D045CapabilityCategory.FilesAndData, "讀取本機檔案", "讀取任務明確指定的單一文字檔案。", "需要理解原始碼、設定或文件內容時使用。", D045CapabilityRiskTier.Moderate, "只讀不外傳；路徑與大小仍受限制。"),
            D(D045PermissionCapability.LocalFolderEnumerate, "d045.folder.enumerate", "local_folder_enumerate", D045CapabilityCategory.FilesAndData, "列出本機資料夾", "列出明確資料夾中的受限項目 metadata。", "需要先理解資料夾結構或尋找候選項目時使用。", D045CapabilityRiskTier.Moderate, "只列出 metadata，不代表可讀取內容。"),
            D(D045PermissionCapability.ScreenshotCapture, "d045.screenshot.capture", "screenshot_capture", D045CapabilityCategory.ComputerRead, "擷取螢幕截圖", "擷取目前畫面供本機任務理解。", "文字或 accessibility 資訊不足，需要視覺判斷時使用。", D045CapabilityRiskTier.High, "畫面可能包含其他應用程式與私人資訊；不會自動上傳。"),
            D(D045PermissionCapability.ScreenshotUpload, "d045.screenshot.upload", "screenshot_upload", D045CapabilityCategory.NetworkAndSharing, "上傳螢幕截圖", "把明確指定的截圖送往明確 HTTPS 目的地。", "只有任務確實需要把截圖交給外部服務時使用。", D045CapabilityRiskTier.High, "會將畫面內容傳出電腦；擷取權限仍需另外允許。"),
            D(D045PermissionCapability.LocalContentExternalShare, "d045.local-content.external-share", "local_content_external_share", D045CapabilityCategory.NetworkAndSharing, "對外分享本機內容", "把明確指定的本機內容送往明確 HTTPS 目的地。", "只有玩家要求上傳或分享確切內容時使用。", D045CapabilityRiskTier.High, "本機內容會離開電腦；讀取權限仍需另外允許。"),
            D(D045PermissionCapability.GracefulProcessClose, "d045.process.graceful-close", "graceful_process_close", D045CapabilityCategory.ApplicationsAndInterface, "正常關閉程式", "要求明確程序正常關閉並驗證退出。", "程式需要正常保存並結束時優先使用。", D045CapabilityRiskTier.High, "可能觸發未儲存資料提示或中斷工作。"),
            D(D045PermissionCapability.ForceTerminateProcess, "d045.process.force-terminate", "force_terminate_process", D045CapabilityCategory.ApplicationsAndInterface, "強制終止程式", "強制終止明確程序並驗證退出。", "只有正常關閉失敗且玩家仍要求停止時使用。", D045CapabilityRiskTier.Critical, "可能造成資料遺失或損壞。"),
            D(D045PermissionCapability.RecycleBinDelete, "d045.delete.recycle-bin", "recycle_bin_delete", D045CapabilityCategory.FilesAndData, "移至資源回收筒", "把明確本機路徑移到資源回收筒。", "玩家要求可復原刪除時使用。", D045CapabilityRiskTier.High, "來源會消失，但通常仍可從資源回收筒復原。"),
            D(D045PermissionCapability.PermanentDelete, "d045.delete.permanent", "permanent_delete", D045CapabilityCategory.FilesAndData, "永久刪除", "永久刪除明確本機路徑並驗證來源消失。", "只有玩家明確要求不可復原刪除時使用。", D045CapabilityRiskTier.Critical, "通常無法復原；受保護路徑與子樹仍會拒絕。"),
            D(D045PermissionCapability.Shutdown, "d045.power.shutdown", "shutdown", D045CapabilityCategory.SystemAndDevice, "關機", "安排 Windows 關機。", "玩家明確要求結束目前 Windows 工作階段時使用。", D045CapabilityRiskTier.Critical, "會中斷所有程式與未儲存工作。"),
            D(D045PermissionCapability.Restart, "d045.power.restart", "restart", D045CapabilityCategory.SystemAndDevice, "重新啟動", "安排 Windows 重新啟動。", "安裝或設定需要重啟且玩家明確要求時使用。", D045CapabilityRiskTier.Critical, "會中斷所有程式與未儲存工作。"),
            D(D045PermissionCapability.RawShellArbitraryCommand, "d045.shell.raw-arbitrary-command", "raw_shell", D045CapabilityCategory.DevelopmentAndAutomation, "任意 Shell／命令", "執行完整 Shell 命令並回傳受限輸出與退出碼。", "只有無法用更窄的型別化工具完成且玩家已明確開啟時使用。", D045CapabilityRiskTier.Critical, "可間接影響多種本機資源；不等於管理員權限。"),
            D(D045PermissionCapability.ArbitraryExternalAppInputKeystrokes, "d045.external-app.arbitrary-input", "external_app_input", D045CapabilityCategory.ApplicationsAndInterface, "外部程式鍵盤輸入", "向明確外部視窗送出文字與按鍵。", "目標程式缺乏語意 UI 操作且玩家要求輸入時使用。", D045CapabilityRiskTier.Critical, "焦點錯誤可能把輸入送到其他視窗；不包含滑鼠。"),
            D(D045PermissionCapability.GeneralElevationAdminAccess, "d045.privilege.general-elevation", "elevation", D045CapabilityCategory.SystemAndDevice, "系統管理員／提權", "啟動明確程式的管理員版本；UAC 仍由玩家確認。", "工作確實需要管理員權限且玩家同意時使用。", D045CapabilityRiskTier.Critical, "不包含 Shell、參數或其他能力；Windows UAC 仍是最終關卡。"),
            D(D045PermissionCapability.LocalDataCreate, "d045.data.create", "local_data_create", D045CapabilityCategory.FilesAndData, "建立資料", "在明確路徑建立一個新檔案或資料夾，不覆寫既有資料。", "需要建立單一新檔案或資料夾時使用。", D045CapabilityRiskTier.High, "新增本機資料；修改既有檔案需另一權限。"),
            D(D045PermissionCapability.ApplicationLaunch, "d045.application.launch", "application_launch", D045CapabilityCategory.ApplicationsAndInterface, "開啟應用程式", "唯一解析應用程式後啟動，不帶參數。", "需要安全開啟已安裝應用程式時使用。", D045CapabilityRiskTier.High, "命令主機、腳本與參數不在此能力內。"),
            D(D045PermissionCapability.LocalContentOpen, "d045.local-content.open", "local_content_open", D045CapabilityCategory.FilesAndData, "開啟本機內容", "用 Windows 預設程式開啟安全資料檔或非根目錄。", "需要向玩家顯示一個既有檔案或資料夾時使用。", D045CapabilityRiskTier.Moderate, "程式、腳本、捷徑、URL、分享與根目錄會拒絕。"),
            D(D045PermissionCapability.ClipboardWrite, "d045.clipboard.write", "clipboard_write", D045CapabilityCategory.ComputerRead, "寫入剪貼簿", "把目前任務產生的受限內容寫入剪貼簿。", "玩家要求複製文字供其他程式使用時使用。", D045CapabilityRiskTier.Moderate, "會覆蓋目前剪貼簿內容。"),
            D(D045PermissionCapability.SystemDiagnosticsRead, "d045.system.diagnostics-read", "system_diagnostics_read", D045CapabilityCategory.ComputerRead, "讀取系統診斷", "讀取受限 OS、硬體、磁碟與環境診斷。", "排查跨電腦環境或執行問題時使用。", D045CapabilityRiskTier.Low, "只回傳受限 metadata，不讀取祕密環境值。"),
            D(D045PermissionCapability.ApplicationInventoryRead, "d045.application.inventory-read", "application_inventory_read", D045CapabilityCategory.ComputerRead, "查看已安裝應用程式", "列出受限已安裝應用程式 metadata。", "需要選擇可用程式或判斷環境時使用。", D045CapabilityRiskTier.Moderate, "只讀取產品名稱、版本與受限位置資訊。"),
            D(D045PermissionCapability.ProcessInventoryRead, "d045.process.inventory-read", "process_inventory_read", D045CapabilityCategory.ComputerRead, "查看執行中程序", "列出受限程序身分與狀態。", "需要辨識正在運行的應用或精確 PID 時使用。", D045CapabilityRiskTier.Moderate, "只讀取程序 metadata；不代表可以關閉程序。"),
            D(D045PermissionCapability.WindowInventoryRead, "d045.window.inventory-read", "window_inventory_read", D045CapabilityCategory.ComputerRead, "查看視窗", "列出可見頂層視窗及其所屬程序。", "需要找到玩家可見的目標視窗時使用。", D045CapabilityRiskTier.Moderate, "視窗標題可能包含文件名稱或私人文字。"),
            D(D045PermissionCapability.ExternalAppSemanticUiInspect, "d045.external-app.semantic-ui-inspect", "external_app_semantic_ui_inspect", D045CapabilityCategory.ComputerRead, "理解應用程式介面", "讀取明確視窗的 accessibility／UI Automation 元素樹。", "需要用語意而非座標找到按鈕、欄位或清單時使用。", D045CapabilityRiskTier.High, "介面元素可能包含私人內容；只檢視不操作。"),
            D(D045PermissionCapability.LocalPathSearch, "d045.local-path.search", "local_path_search", D045CapabilityCategory.FilesAndData, "搜尋檔案與資料夾", "在玩家範圍內依名稱與 metadata 搜尋路徑。", "不知道精確路徑但知道名稱、類型或位置時使用。", D045CapabilityRiskTier.Moderate, "只回傳受限候選 metadata；不讀取檔案內容。"),
            D(D045PermissionCapability.LocalContentSearch, "d045.local-content.search", "local_content_search", D045CapabilityCategory.FilesAndData, "搜尋檔案內容", "在玩家範圍內搜尋受限文字內容。", "需要跨檔案找符號、文字或設定時使用。", D045CapabilityRiskTier.High, "可能讀取多個檔案片段；不會因此外傳。"),
            D(D045PermissionCapability.FileModify, "d045.file.modify", "file_modify", D045CapabilityCategory.FilesAndData, "修改檔案", "原子覆寫、追加或修補一個既有檔案。", "Coding、設定或文件編輯需要改變既有內容時使用。", D045CapabilityRiskTier.High, "可能覆蓋既有資料；需明確路徑與模式。"),
            D(D045PermissionCapability.LocalContentCopy, "d045.local-content.copy", "local_content_copy", D045CapabilityCategory.FilesAndData, "複製本機內容", "複製一個明確檔案或資料夾到明確目的地。", "建立備份或複製專案資料時使用。", D045CapabilityRiskTier.High, "會新增資料；既有目的地不得被靜默覆寫。"),
            D(D045PermissionCapability.LocalContentMoveRename, "d045.local-content.move-rename", "local_content_move_rename", D045CapabilityCategory.FilesAndData, "移動或重新命名", "移動或重新命名一個明確本機項目。", "整理或修正名稱且來源與目的地都明確時使用。", D045CapabilityRiskTier.High, "來源位置會改變；既有目的地不得被靜默覆寫。"),
            D(D045PermissionCapability.FolderTreeCreate, "d045.folder.tree-create", "folder_tree_create", D045CapabilityCategory.FilesAndData, "建立資料夾樹", "一次建立明確的多層資料夾結構。", "專案或解包需要尚不存在的父層時使用。", D045CapabilityRiskTier.High, "只建立目錄，不建立或修改檔案內容。"),
            D(D045PermissionCapability.ArchiveManage, "d045.archive.manage", "archive_manage", D045CapabilityCategory.FilesAndData, "封存與解壓縮", "建立封存檔或安全解壓到明確目的地。", "下載環境、發佈或專案素材需要打包／解包時使用。", D045CapabilityRiskTier.High, "解壓會拒絕穿越目的地、連結與靜默覆寫。"),
            D(D045PermissionCapability.WindowControl, "d045.window.control", "window_control", D045CapabilityCategory.ApplicationsAndInterface, "控制視窗", "聚焦、切換、最小化、最大化或排列明確視窗。", "需要準備 UI 操作的目標視窗或整理畫面時使用。", D045CapabilityRiskTier.High, "會改變玩家桌面焦點與視窗位置。"),
            D(D045PermissionCapability.ExternalAppSemanticUiAction, "d045.external-app.semantic-ui-action", "external_app_semantic_ui_action", D045CapabilityCategory.ApplicationsAndInterface, "操作應用程式介面", "對唯一語意 UI 元素點擊、選取、捲動或輸入。", "已透過 UI 檢視唯一辨識目標元素時使用。", D045CapabilityRiskTier.High, "可能提交表單或改變外部應用狀態；仍需元素身分驗證。"),
            D(D045PermissionCapability.ExternalAppPointerInput, "d045.external-app.pointer-input", "external_app_pointer_input", D045CapabilityCategory.ApplicationsAndInterface, "滑鼠與指標輸入", "對明確視窗送出滑鼠移動、點擊或拖放。", "語意 UI 不可用且確實需要座標操作時使用。", D045CapabilityRiskTier.Critical, "座標與焦點錯誤可能操作到非預期目標。"),
            D(D045PermissionCapability.ApplicationLaunchWithArguments, "d045.application.launch-with-arguments", "application_launch_with_arguments", D045CapabilityCategory.ApplicationsAndInterface, "帶參數啟動應用程式", "不經 Shell，以 argument array 啟動明確程式。", "程式需要檔案、專案或安全旗標參數時使用。", D045CapabilityRiskTier.High, "禁止 Shell 字串拼接；執行檔與每個參數分開驗證。"),
            D(D045PermissionCapability.ProcessExecuteNonInteractive, "d045.process.execute-noninteractive", "process_execute_noninteractive", D045CapabilityCategory.DevelopmentAndAutomation, "執行非互動程式", "以明確執行檔、參數與工作目錄執行並擷取受限輸出。", "Coding、建置、測試或環境檢查需要直接執行工具時使用。", D045CapabilityRiskTier.Critical, "可修改目前使用者可存取的資源；不包含 Shell 或提權。"),
            D(D045PermissionCapability.ProcessBackgroundManage, "d045.process.background-manage", "process_background_manage", D045CapabilityCategory.DevelopmentAndAutomation, "管理背景工作", "啟動、觀察或終止任務擁有的長時間背景程序。", "開發伺服器、下載器或長時間建置需要跨回合運行時使用。", D045CapabilityRiskTier.Critical, "背景程序會持續使用資源；只管理目前任務擁有的程序。"),
            D(D045PermissionCapability.WebPublicSearch, "d045.web.public-search", "web_public_search", D045CapabilityCategory.NetworkAndSharing, "搜尋公開網路", "以公開搜尋服務查找網頁候選。", "需要最新公開資訊或官方文件來源時使用。", D045CapabilityRiskTier.Moderate, "搜尋詞會送到外部服務；不存取私有網路。"),
            D(D045PermissionCapability.BrowserNavigate, "d045.browser.navigate", "browser_navigate", D045CapabilityCategory.NetworkAndSharing, "瀏覽器導覽", "在瀏覽器開啟一個明確網址。", "需要向玩家顯示或進一步操作明確網頁時使用。", D045CapabilityRiskTier.High, "網站可能追蹤或改變瀏覽器狀態；不代表可使用登入 session。"),
            D(D045PermissionCapability.DownloadPublicHttps, "d045.download.public-https", "download_public_https", D045CapabilityCategory.NetworkAndSharing, "下載公開 HTTPS 檔案", "從公開 HTTPS 下載到明確本機目的地。", "安裝環境、素材或封存檔需要取得公開檔案時使用。", D045CapabilityRiskTier.High, "另需建立或修改目的地的 leaf 權限；拒絕私有網路與靜默覆寫。")
        };

    private static D045CapabilityDescriptor D(
        D045PermissionCapability capability,
        string stableId,
        string toolToken,
        D045CapabilityCategory category,
        string title,
        string description,
        string toolUseDetails,
        D045CapabilityRiskTier riskTier,
        string riskDetails)
        => new(
            capability,
            stableId,
            toolToken,
            category,
            title,
            description,
            toolUseDetails,
            riskTier,
            riskDetails);
}
