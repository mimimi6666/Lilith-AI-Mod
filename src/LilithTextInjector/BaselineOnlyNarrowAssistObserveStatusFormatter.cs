using System;
using System.Globalization;

namespace LilithTextInjector;

internal enum D022SliceDStatusLanguage
{
    English = 0,
    TraditionalChinese = 1,
    SimplifiedChinese = 2,
    Japanese = 3
}

internal static class D022SliceDObserveStatusFormatter
{
    internal static string Format(
        D022SliceDObserveSnapshot snapshot,
        D022SliceDStatusLanguage language)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        if (!Enum.IsDefined(typeof(D022SliceDStatusLanguage), language))
            throw new ArgumentOutOfRangeException(nameof(language));

        var status = Status(snapshot.Status, language);
        var source = snapshot.SourceEnabled
            ? Word("Enabled", "啟用", "启用", "有効", language)
            : Word("Off", "關閉", "关闭", "無効", language);
        var latest = snapshot.Latest == null
            ? "-"
            : snapshot.Latest.Kind
                + "/"
                + snapshot.Latest.ExistingOwner
                + "/"
                + Short(snapshot.Latest.ObservationId);

        return string.Join(
            "\n",
            Word(
                "<b>D-022 Narrow Assist Observe</b>",
                "<b>D-022 狹窄 Assist 觀察</b>",
                "<b>D-022 狭窄 Assist 观察</b>",
                "<b>D-022 Narrow Assist Observe</b>",
                language),
            Word(
                "<color=#72D7FF>Read-only · no ticket · no Queue · no motion</color>",
                "<color=#72D7FF>唯讀・無 Ticket・不寫 Queue・不執行動作</color>",
                "<color=#72D7FF>只读・无 Ticket・不写 Queue・不执行动作</color>",
                "<color=#72D7FF>読み取り専用・Ticket/Queue/動作なし</color>",
                language),
            string.Empty,
            Word("Status", "狀態", "状态", "状態", language)
                + "  "
                + status,
            Word("Source", "來源", "来源", "ソース", language)
                + "  "
                + source,
            Word("Generation", "世代", "世代", "世代", language)
                + "  "
                + snapshot.ConfigurationGeneration
                    .ToString(CultureInfo.InvariantCulture),
            Word("Observed", "觀察", "观察", "観察", language)
                + "  "
                + snapshot.ObservedCount
                + "  "
                + Word("eligible", "合格", "合格", "対象", language)
                + " "
                + snapshot.EligiblePreviewCount,
            Word("Retired", "已退休", "已退役", "破棄", language)
                + "  "
                + snapshot.RetiredCount
                + "  "
                + Word("capacity", "容量", "容量", "容量", language)
                + " "
                + snapshot.RecordCount
                + "/"
                + D022SliceDObserveRuntimeHost.MaximumRecords,
            Word("Latest", "最後", "最后", "最新", language)
                + "  "
                + latest,
            Word(
                "Authority  ticket 0 · queue 0 · motion 0 · execution 0",
                "權威輸出  ticket 0・queue 0・motion 0・execution 0",
                "权威输出  ticket 0・queue 0・motion 0・execution 0",
                "権限出力  ticket 0・queue 0・motion 0・execution 0",
                language),
            Word(
                "AI / Memory / Evidence / Reasoning  0 / 0 / 0 / 0",
                "AI／記憶／證據／推理  0／0／0／0",
                "AI／记忆／证据／推理  0／0／0／0",
                "AI／記憶／証拠／推論  0／0／0／0",
                language));
    }

    private static string Status(
        D022SliceDObserveStatus status,
        D022SliceDStatusLanguage language)
        => status switch
        {
            D022SliceDObserveStatus.Off =>
                Word("Off", "關閉", "关闭", "オフ", language),
            D022SliceDObserveStatus.Observe =>
                Word("Observe", "觀察", "观察", "観察", language),
            D022SliceDObserveStatus.Fault =>
                Word("Fault", "故障", "故障", "障害", language),
            D022SliceDObserveStatus.Capacity =>
                Word("Capacity", "容量已滿", "容量已满", "容量上限", language),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

    private static string Word(
        string english,
        string traditional,
        string simplified,
        string japanese,
        D022SliceDStatusLanguage language)
        => language switch
        {
            D022SliceDStatusLanguage.English => english,
            D022SliceDStatusLanguage.TraditionalChinese => traditional,
            D022SliceDStatusLanguage.SimplifiedChinese => simplified,
            D022SliceDStatusLanguage.Japanese => japanese,
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };

    private static string Short(string value)
        => value.Length <= 18 ? value : value[..18];
}
