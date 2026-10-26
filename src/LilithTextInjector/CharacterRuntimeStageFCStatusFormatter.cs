using System;

namespace LilithTextInjector;

internal static class CharacterRuntimeStageFCStatusFormatter
{
    internal static string Format(
        CharacterLanguageStyleStageFCSnapshot snapshot,
        OrchestratorObserveStatusLanguage language)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        var latest = snapshot.LatestPreview;
        var label = snapshot.LatestLabel?.Label.ToString() ?? "-";
        var status = snapshot.Status.ToString();
        var candidate = latest?.CharacterStyleId ?? "-";
        var baseline = latest?.BaselineStyleId ?? "-";
        var arm = snapshot.Armed
            ? "Armed"
            : snapshot.ProcessArmConsumed
                ? "Consumed"
                : "Available";
        return language switch
        {
            OrchestratorObserveStatusLanguage.TraditionalChinese =>
                "<b>D-023 F-C 語言風格 Preview</b>\n"
                + "<color=#6fdcff>唯讀·不改 Prompt·不改回覆·不呼叫 AI</color>\n"
                + $"狀態 {status}  Arm {arm}\n"
                + $"預覽 {snapshot.PreviewCount}/1  標記 "
                + $"{snapshot.LabelCount}/1  拒絕 {snapshot.RejectedCount}\n"
                + $"候選 {candidate}  基準 {baseline}\n"
                + $"最後 {snapshot.LastAdmission}  標籤 {label}\n"
                + "權威輸出 prompt 0·reply 0·provider 0·behavior 0",
            OrchestratorObserveStatusLanguage.SimplifiedChinese =>
                "<b>D-023 F-C 语言风格 Preview</b>\n"
                + "<color=#6fdcff>只读·不改 Prompt·不改回复·不调用 AI</color>\n"
                + $"状态 {status}  Arm {arm}\n"
                + $"预览 {snapshot.PreviewCount}/1  标记 "
                + $"{snapshot.LabelCount}/1  拒绝 {snapshot.RejectedCount}\n"
                + $"候选 {candidate}  基准 {baseline}\n"
                + $"最后 {snapshot.LastAdmission}  标签 {label}\n"
                + "权限输出 prompt 0·reply 0·provider 0·behavior 0",
            OrchestratorObserveStatusLanguage.Japanese =>
                "<b>D-023 F-C 言語スタイル Preview</b>\n"
                + "<color=#6fdcff>読み取り専用・Prompt/応答変更なし・AI 呼出なし</color>\n"
                + $"状態 {status}  Arm {arm}\n"
                + $"Preview {snapshot.PreviewCount}/1  Label "
                + $"{snapshot.LabelCount}/1  Reject {snapshot.RejectedCount}\n"
                + $"候補 {candidate}  基準 {baseline}\n"
                + $"最終 {snapshot.LastAdmission}  Label {label}\n"
                + "authority prompt 0·reply 0·provider 0·behavior 0",
            _ =>
                "<b>D-023 F-C Language-style Preview</b>\n"
                + "<color=#6fdcff>read-only · no Prompt/reply change · no AI call</color>\n"
                + $"status {status}  Arm {arm}\n"
                + $"preview {snapshot.PreviewCount}/1  label "
                + $"{snapshot.LabelCount}/1  rejected {snapshot.RejectedCount}\n"
                + $"candidate {candidate}  baseline {baseline}\n"
                + $"last {snapshot.LastAdmission}  label {label}\n"
                + "authority prompt 0·reply 0·provider 0·behavior 0"
        };
    }
}
