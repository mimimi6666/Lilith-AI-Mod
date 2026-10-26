using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LilithControlCenter;

internal sealed class ControlCenterSettingRow
{
    public string Name { get; }
    public string Desired { get; }
    public string Effective { get; }
    public string State { get; }

    internal ControlCenterSettingRow(
        string name,
        string desired,
        string effective,
        string state)
    {
        Name = name;
        Desired = desired;
        Effective = effective;
        State = state;
    }
}

internal sealed class ControlCenterFeatureRow
{
    public string Name { get; }
    public string State { get; }

    internal ControlCenterFeatureRow(string name, string state)
    {
        Name = name;
        State = state;
    }
}

internal sealed class ControlCenterTelemetryRow
{
    public string Group { get; }
    public string Label { get; }
    public string DisplayValue { get; }
    public int Health { get; }

    internal ControlCenterTelemetryRow(
        string group,
        string label,
        string value,
        string unit,
        int health)
    {
        Group = FriendlyGroup(group);
        Label = string.IsNullOrWhiteSpace(label) ? "未知項目" : label;
        DisplayValue = string.IsNullOrWhiteSpace(unit)
            ? value
            : value + " " + unit;
        Health = health is >= 0 and <= 3 ? health : 0;
    }

    private static string FriendlyGroup(string value)
        => value switch
        {
            "situation" => "情境",
            "memory" => "記憶",
            "character" => "人格與情緒",
            "initiative" => "主動決策",
            "motion" => "自主動作",
            "provider" => "AI 模型",
            "input" => "玩家活動",
            _ => "系統"
        };
}

internal sealed class ControlCenterReadOnlyProjection
{
    public bool Connected { get; }
    public string ConnectionText { get; }
    public string ConnectionDetail { get; }
    public string Provider { get; }
    public string CredentialState { get; }
    public string LocalVoiceOutputState { get; }
    public string RuntimeGeneration { get; }
    public string ConfigurationGeneration { get; }
    public string RuntimeIdentity { get; }
    public string SessionIdentity { get; }
    public string SchemaFingerprint { get; }
    public string SemanticFingerprint { get; }
    public string ObservationFingerprint { get; }
    public string ObservedAt { get; }
    public string IntegrationState { get; }
    public string Interruptibility { get; }
    public string InterruptibilityReason { get; }
    public string TelemetryObservedAt { get; }
    public string MemorySummary { get; }
    public string CharacterSummary { get; }
    public string InitiativeSummary { get; }
    public IReadOnlyList<ControlCenterSettingRow> Settings { get; }
    public IReadOnlyList<ControlCenterFeatureRow> Features { get; }
    public IReadOnlyList<ControlCenterTelemetryRow> TelemetryRows { get; }

    // Keep raw telemetry for protocol compatibility, but never bind retired metrics to the UI.
    public IReadOnlyList<ControlCenterTelemetryRow> DiagnosticTelemetryRows { get; private init; }
        = Array.Empty<ControlCenterTelemetryRow>();

    private static bool IsCurrentDiagnosticMetric(ControlCenterClientMetric value)
        => value.Id is "situation.interruptibility"
            or "input.system-idle-seconds"
            or "input.last-keyboard"
            or "input.last-mouse";

    private ControlCenterReadOnlyProjection(
        bool connected,
        string connectionText,
        string connectionDetail,
        string provider,
        string credentialState,
        string localVoiceOutputState,
        string runtimeGeneration,
        string configurationGeneration,
        string runtimeIdentity,
        string sessionIdentity,
        string schemaFingerprint,
        string semanticFingerprint,
        string observationFingerprint,
        string observedAt,
        string integrationState,
        string interruptibility,
        string interruptibilityReason,
        string telemetryObservedAt,
        string memorySummary,
        string characterSummary,
        string initiativeSummary,
        IReadOnlyList<ControlCenterSettingRow> settings,
        IReadOnlyList<ControlCenterFeatureRow> features,
        IReadOnlyList<ControlCenterTelemetryRow> telemetryRows)
    {
        Connected = connected;
        ConnectionText = connectionText;
        ConnectionDetail = connectionDetail;
        Provider = provider;
        CredentialState = credentialState;
        LocalVoiceOutputState = localVoiceOutputState;
        RuntimeGeneration = runtimeGeneration;
        ConfigurationGeneration = configurationGeneration;
        RuntimeIdentity = runtimeIdentity;
        SessionIdentity = sessionIdentity;
        SchemaFingerprint = schemaFingerprint;
        SemanticFingerprint = semanticFingerprint;
        ObservationFingerprint = observationFingerprint;
        ObservedAt = observedAt;
        IntegrationState = integrationState;
        Interruptibility = interruptibility;
        InterruptibilityReason = interruptibilityReason;
        TelemetryObservedAt = telemetryObservedAt;
        MemorySummary = memorySummary;
        CharacterSummary = characterSummary;
        InitiativeSummary = initiativeSummary;
        Settings = settings;
        Features = features;
        TelemetryRows = telemetryRows;
    }

    internal static ControlCenterReadOnlyProjection Offline(string? reason)
        => new(
            false,
            "離線",
            SafeReason(reason),
            "—",
            "未讀取",
            "設定狀態未知",
            "—",
            "—",
            "—",
            "—",
            "—",
            "—",
            "—",
            "—",
            "等待遊戲端",
            "未知",
            "尚未收到即時情境",
            "—",
            "尚未連線",
            "尚未連線",
            "尚未連線",
            Array.Empty<ControlCenterSettingRow>(),
            Array.Empty<ControlCenterFeatureRow>(),
            Array.Empty<ControlCenterTelemetryRow>());

    internal static string OfflineReasonForReadFailure(Exception exception)
        => exception switch
        {
            TimeoutException or OperationCanceledException =>
                "尚未收到遊戲端回應；若尚未啟動遊戲，這是正常狀態。AI 與工具的連線請在各自頁面查看。",
            UnauthorizedAccessException =>
                "目前無法存取遊戲端連線，請確認遊戲與控制中心使用同一個 Windows 帳戶執行。AI 與工具的連線請在各自頁面查看。",
            System.IO.InvalidDataException or System.Text.Json.JsonException =>
                "遊戲端回應格式不相符，暫時無法顯示遊戲狀態。AI 與工具的連線請在各自頁面查看。",
            System.IO.IOException =>
                "遊戲端連線暫時中斷，尚未取得最新遊戲狀態。AI 與工具的連線請在各自頁面查看。",
            _ =>
                "暫時無法讀取遊戲端狀態，請稍後重新整理。AI 與工具的連線請在各自頁面查看。"
        };

    internal static ControlCenterReadOnlyProjection From(
        ControlCenterClientStatus status)
    {
        if (status == null)
            throw new ArgumentNullException(nameof(status));
        var settings = (status.Settings ?? Array.Empty<ControlCenterClientSetting>())
            .OrderBy(value => value.SettingId, StringComparer.Ordinal)
            .Select(
                value => new ControlCenterSettingRow(
                    FriendlyName(value.SettingId),
                    "—（Stage D 唯讀）",
                    FormatValue(value),
                    EffectiveState(value.State)))
            .ToArray();
        var features = (status.Features ?? Array.Empty<ControlCenterClientFeature>())
            .OrderBy(value => value.FeatureId, StringComparer.Ordinal)
            .Select(
                value => new ControlCenterFeatureRow(
                    FriendlyName(value.FeatureId),
                    FeatureState(value.State)))
            .ToArray();
        var provider = status.Settings?.FirstOrDefault(
            value => string.Equals(
                value.SettingId,
                "provider.active",
                StringComparison.Ordinal));
        var credential = status.Settings?.FirstOrDefault(
            value => string.Equals(
                value.SettingId,
                "provider.active-credential-configured",
                StringComparison.Ordinal));
        var localVoice = status.Settings?.FirstOrDefault(
            value => string.Equals(
                value.SettingId,
                "voice.enabled",
                StringComparison.Ordinal));
        var telemetry = status.Telemetry;
        var telemetryRows = (telemetry?.Metrics
                ?? Array.Empty<ControlCenterClientMetric>())
            .Select(value => new ControlCenterTelemetryRow(
                value.Group,
                value.Label,
                value.Value,
                value.Unit,
                value.Health))
            .ToArray();
        return new(
            true,
            "遊戲已連線",
            "已取得遊戲端狀態。AI 與工具的連線請在各自頁面查看。",
            provider == null ? "未知" : FormatValue(provider),
            credential?.BooleanValue == true ? "已設定（內容不可讀）" : "未設定",
            localVoice == null
                ? "未回報"
                : localVoice.BooleanValue == true
                    ? "設定已開啟"
                    : "設定已關閉",
            status.RuntimeGeneration.ToString(CultureInfo.InvariantCulture),
            status.ConfigurationGeneration.ToString(
                CultureInfo.InvariantCulture),
            Short(status.RuntimeFingerprint),
            Short(status.RuntimeSessionId),
            Short(status.SchemaFingerprint),
            Short(status.SemanticFingerprint),
            Short(status.ObservationFingerprint),
            FormatObservedAt(status.ObservedAtUnixMilliseconds),
            telemetry?.IntegrationState ?? "等待資料",
            telemetry?.Interruptibility ?? "未知",
            telemetry?.InterruptibilityReason ?? "尚未收到即時情境",
            telemetry == null
                ? "—"
                : FormatObservedAt(telemetry.ObservedAtUnixMilliseconds),
            MetricSummary(telemetry, "memory.recent-turns", "回合"),
            "舊數值介面已退役",
            InitiativeSummaryText(telemetry),
            new ReadOnlyCollection<ControlCenterSettingRow>(settings),
            new ReadOnlyCollection<ControlCenterFeatureRow>(features),
            new ReadOnlyCollection<ControlCenterTelemetryRow>(telemetryRows))
        {
            DiagnosticTelemetryRows = (telemetry?.Metrics
                    ?? Array.Empty<ControlCenterClientMetric>())
                .Where(IsCurrentDiagnosticMetric)
                .Select(value => new ControlCenterTelemetryRow(
                    value.Group, value.Label, value.Value, value.Unit, value.Health))
                .ToArray()
        };
    }

    internal string BuildFallbackText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Lilith Control Center — 唯讀安全畫面");
        builder.AppendLine("狀態：" + ConnectionText);
        builder.AppendLine("Provider：" + Provider);
        builder.AppendLine("憑證：" + CredentialState);
        builder.AppendLine(
            "Runtime / Config：" + RuntimeGeneration + " / "
            + ConfigurationGeneration);
        builder.AppendLine("設定寫入：0");
        builder.AppendLine("Provider 呼叫：0");
        return builder.ToString();
    }

    private static string FormatValue(ControlCenterClientSetting value)
        => value.ValueKind switch
        {
            1 => value.BooleanValue == true ? "開啟" : "關閉",
            2 => value.IntegerValue?.ToString(
                CultureInfo.InvariantCulture) ?? "—",
            3 => value.DecimalValue?.ToString(
                CultureInfo.InvariantCulture) ?? "—",
            4 or 5 or 6 or 7 => string.IsNullOrWhiteSpace(value.SafeTokenValue)
                ? "—"
                : value.SafeTokenValue,
            _ => "不可用"
        };

    private static string EffectiveState(int state)
        => state switch
        {
            1 => "生效中",
            2 => "不可用",
            3 => "等待重啟",
            4 => "已拒絕",
            _ => "無效"
        };

    private static string FeatureState(int state)
        => state switch
        {
            1 => "關閉",
            2 => "Observe",
            3 => "Assist",
            4 => "Fault",
            5 => "Capacity",
            6 => "不可用",
            _ => "無效"
        };

    private static string MetricSummary(
        ControlCenterClientTelemetry? telemetry,
        string id,
        string fallbackUnit)
    {
        var metric = telemetry?.Metrics?.FirstOrDefault(value =>
            string.Equals(value.Id, id, StringComparison.Ordinal));
        if (metric == null)
            return "尚未回報";
        var unit = string.IsNullOrWhiteSpace(metric.Unit)
            ? fallbackUnit
            : metric.Unit;
        return metric.Value + " " + unit;
    }

    private static string InitiativeSummaryText(
        ControlCenterClientTelemetry? telemetry)
    {
        var legacyObserverRetired = telemetry?.Metrics?.Any(value =>
            string.Equals(value.Id, "initiative.status", StringComparison.Ordinal)
            && string.Equals(value.Value, "Retired", StringComparison.Ordinal)) == true;
        var proactiveMode = telemetry?.Metrics?.FirstOrDefault(value =>
            string.Equals(
                value.Id,
                "initiative.proactive-mode",
                StringComparison.Ordinal));
        if (string.Equals(
                proactiveMode?.Value,
                "FormalBounded",
                StringComparison.Ordinal))
        {
            var operation = telemetry?.Metrics?.FirstOrDefault(value =>
                string.Equals(
                    value.Id,
                    "initiative.proactive-operation",
                    StringComparison.Ordinal));
            var budget = telemetry?.Metrics?.FirstOrDefault(value =>
                string.Equals(
                    value.Id,
                    "initiative.proactive-budget",
                    StringComparison.Ordinal));
            var state = string.Equals(
                    operation?.Value,
                    "Active",
                    StringComparison.Ordinal)
                ? "進行中"
                : "待命";
            var summary = string.IsNullOrWhiteSpace(budget?.Value)
                ? "正式主動・" + state
                : "正式主動・" + state + "（" + budget.Value + "）";
            return legacyObserverRetired ? summary + "；舊觀察已退役" : summary;
        }

        if (legacyObserverRetired)
            return "舊觀察已退役";

        var latest = telemetry?.Metrics?.FirstOrDefault(value =>
            string.Equals(
                value.Id,
                "initiative.latest-disposition",
                StringComparison.Ordinal));
        return latest?.Value ?? "尚未回報";
    }

    private static string FriendlyName(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "未知"
            : value.Replace('.', ' ');

    private static string FormatObservedAt(long unixMilliseconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds)
                .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
        }
        catch (ArgumentOutOfRangeException)
        {
            return "無效時間";
        }
    }

    private static string Short(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "—";
        return value.Length <= 16 ? value : value[..16] + "…";
    }

    private static string SafeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "遊戲端尚未提供唯讀連線。";
        var firstLine = reason.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
            return "遊戲端尚未提供唯讀連線。";
        return firstLine.Length <= 180 ? firstLine : firstLine[..180] + "…";
    }
}
