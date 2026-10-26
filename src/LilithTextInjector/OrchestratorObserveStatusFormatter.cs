using System;
using System.Globalization;
using System.Text;

namespace LilithTextInjector;

internal enum OrchestratorObserveStatusLanguage
{
    TraditionalChinese = 0,
    SimplifiedChinese = 1,
    Japanese = 2,
    English = 3
}

internal static class OrchestratorObserveStatusFormatter
{
    internal const int MaximumRenderedCharacters = 2_000;

    internal static string Format(
        OrchestratorObserveRuntimeSnapshot snapshot,
        OrchestratorObserveStatusLanguage language)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        if (!Enum.IsDefined(
                typeof(OrchestratorObserveStatusLanguage),
                language))
        {
            throw new ArgumentOutOfRangeException(nameof(language));
        }

        var builder = new StringBuilder(768);
        AppendLine(
            builder,
            Localize(
                language,
                "<b>Orchestrator 觀察模式</b>",
                "<b>Orchestrator 观察模式</b>",
                "<b>Orchestrator 観察モード</b>",
                "<b>Orchestrator Observe</b>"));
        AppendLine(
            builder,
            Localize(
                language,
                "<color=#8FD7FF>唯讀 · 不影響行為 · 不增加 AI 呼叫</color>",
                "<color=#8FD7FF>只读 · 不影响行为 · 不增加 AI 调用</color>",
                "<color=#8FD7FF>読み取り専用 · 行動に影響なし · AI 呼び出し追加なし</color>",
                "<color=#8FD7FF>READ-ONLY · no behavior influence · no extra AI calls</color>"));
        builder.Append('\n');
        AppendLabelValue(
            builder,
            Localize(
                language,
                "主機",
                "主机",
                "ホスト",
                "Host"),
            HostStatus(snapshot.Status, language));

        foreach (var source in snapshot.Sources)
        {
            AppendLabelValue(
                builder,
                SourceName(source.Source, language),
                SourceStatus(source.Status, language)
                + "  "
                + Counters(source));
        }

        builder.Append('\n');
        AppendLabelValue(
            builder,
            Localize(
                language,
                "工作階段事件",
                "会话事件",
                "セッションイベント",
                "Session events"),
            snapshot.RetainedSessionEventCount.ToString(
                CultureInfo.InvariantCulture));
        AppendLabelValue(
            builder,
            Localize(
                language,
                "待處理調用",
                "待处理调用",
                "保留中の呼び出し",
                "Pending invocations"),
            snapshot.PendingInvocationCount.ToString(
                CultureInfo.InvariantCulture));
        AppendLabelValue(
            builder,
            Localize(
                language,
                "診斷",
                "诊断",
                "診断",
                "Diagnostics"),
            snapshot.Diagnostics.Entries.Count.ToString(
                CultureInfo.InvariantCulture)
            + "/"
            + OrchestratorObserveDiagnosticBuffer.MaximumEntries.ToString(
                CultureInfo.InvariantCulture)
            + "  "
            + Localize(
                language,
                "淘汰",
                "淘汰",
                "破棄",
                "dropped")
            + "="
            + snapshot.Diagnostics.DroppedEntryCount.ToString(
                CultureInfo.InvariantCulture));

        if (snapshot.Diagnostics.Entries.Count > 0)
        {
            var entry =
                snapshot.Diagnostics.Entries[
                    snapshot.Diagnostics.Entries.Count - 1];
            builder.Append('\n');
            AppendLabelValue(
                builder,
                Localize(
                    language,
                    "最後事件",
                    "最后事件",
                    "最新イベント",
                    "Last event"),
                entry.OccurredAt.ToLocalTime().ToString(
                    "HH:mm:ss",
                    CultureInfo.InvariantCulture)
                + "  "
                + SourceName(entry.Source, language)
                + "  "
                + entry.Kind);
            AppendLabelValue(
                builder,
                Localize(
                    language,
                    "中介資料",
                    "中介数据",
                    "メタデータ",
                    "Metadata"),
                OptionalEnum(entry.EventKind)
                + "/"
                + OptionalEnum(entry.ProcessDisposition)
                + "/"
                + OptionalEnum(entry.DecisionDisposition)
                + "  #"
                + entry.CorrelationSuffix);
        }

        AppendLabelValue(
            builder,
            Localize(
                language,
                "權威輸出",
                "权威输出",
                "権限出力",
                "Authority output"),
            snapshot.ObserveCausedProviderCallCount.ToString(
                CultureInfo.InvariantCulture)
            + "/"
            + snapshot.ObserveCausedStateWriteCount.ToString(
                CultureInfo.InvariantCulture)
            + "/"
            + snapshot.ObserveCausedBehaviorCallCount.ToString(
                CultureInfo.InvariantCulture));

        if (builder.Length > MaximumRenderedCharacters)
            return builder.ToString(0, MaximumRenderedCharacters);
        return builder.ToString();
    }

    private static string HostStatus(
        OrchestratorObserveHostStatus status,
        OrchestratorObserveStatusLanguage language)
        => status switch
        {
            OrchestratorObserveHostStatus.Off =>
                Localize(language, "關閉", "关闭", "オフ", "Off"),
            OrchestratorObserveHostStatus.Observe =>
                Localize(language, "觀察中", "观察中", "観察中", "Observe"),
            OrchestratorObserveHostStatus.StoppedByFault =>
                Localize(
                    language,
                    "故障後停止",
                    "故障后停止",
                    "障害停止",
                    "Stopped by fault"),
            OrchestratorObserveHostStatus.CapacityReached =>
                Localize(
                    language,
                    "容量已滿",
                    "容量已满",
                    "容量上限",
                    "Capacity reached"),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

    private static string SourceName(
        OrchestratorObserveSourceKind source,
        OrchestratorObserveStatusLanguage language)
        => source switch
        {
            OrchestratorObserveSourceKind.PlayerTurnLifecycle =>
                Localize(
                    language,
                    "玩家回合",
                    "玩家回合",
                    "プレイヤーターン",
                    "Player turn"),
            OrchestratorObserveSourceKind.AvatarInteractionGates =>
                Localize(
                    language,
                    "角色互動閘門",
                    "角色互动闸门",
                    "アバター操作ゲート",
                    "Avatar gates"),
            OrchestratorObserveSourceKind.ExistingExecutionLifecycle =>
                Localize(
                    language,
                    "既有動作執行",
                    "既有动作执行",
                    "既存動作の実行",
                    "Existing execution"),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };

    private static string SourceStatus(
        OrchestratorObserveSourceStatus status,
        OrchestratorObserveStatusLanguage language)
        => status switch
        {
            OrchestratorObserveSourceStatus.Disabled =>
                Localize(language, "未啟用", "未启用", "無効", "Disabled"),
            OrchestratorObserveSourceStatus.Enabled =>
                Localize(language, "啟用", "启用", "有効", "Enabled"),
            OrchestratorObserveSourceStatus.Faulted =>
                Localize(language, "已隔離", "已隔离", "隔離済み", "Isolated"),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

    private static string Counters(
        OrchestratorObserveRuntimeSourceSnapshot source)
        => "A"
            + source.Accepted.ToString(CultureInfo.InvariantCulture)
            + " R"
            + source.Rejected.ToString(CultureInfo.InvariantCulture)
            + " C"
            + source.Coalesced.ToString(CultureInfo.InvariantCulture)
            + " D"
            + source.Dropped.ToString(CultureInfo.InvariantCulture)
            + " F"
            + source.Faulted.ToString(CultureInfo.InvariantCulture);

    private static string OptionalEnum<T>(T? value)
        where T : struct, Enum
        => value?.ToString() ?? "-";

    private static void AppendLabelValue(
        StringBuilder builder,
        string label,
        string value)
        => AppendLine(builder, "<b>" + label + "</b>  " + value);

    private static void AppendLine(StringBuilder builder, string value)
        => builder.Append(value).Append('\n');

    private static string Localize(
        OrchestratorObserveStatusLanguage language,
        string traditionalChinese,
        string simplifiedChinese,
        string japanese,
        string english)
        => language switch
        {
            OrchestratorObserveStatusLanguage.TraditionalChinese =>
                traditionalChinese,
            OrchestratorObserveStatusLanguage.SimplifiedChinese =>
                simplifiedChinese,
            OrchestratorObserveStatusLanguage.Japanese => japanese,
            OrchestratorObserveStatusLanguage.English => english,
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };
}
