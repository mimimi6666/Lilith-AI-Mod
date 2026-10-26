using System;
using System.Text;

namespace LilithTextInjector;

internal static class CharacterCanonDualReadStatusFormatter
{
    internal const int MaximumRenderedCharacters = 1_800;

    internal static string Format(
        CharacterCanonDualReadObserveSnapshot snapshot,
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

        var traditional =
            language == OrchestratorObserveStatusLanguage
                .TraditionalChinese;
        var simplified =
            language == OrchestratorObserveStatusLanguage
                .SimplifiedChinese;
        var builder = new StringBuilder(768);
        Line(
            builder,
            traditional
                ? "<b>D-024 CG-C 典範 Profile 雙讀觀察</b>"
                : simplified
                    ? "<b>D-024 CG-C 典范 Profile 双读观察</b>"
                    : "<b>D-024 CG-C Canon Profile Dual-Read Observe</b>");
        Line(
            builder,
            traditional
                ? "<color=#8FD7FF>唯讀・預設關閉・profile-v1 仍是唯一作用中擁有者</color>"
                : simplified
                    ? "<color=#8FD7FF>只读・默认关闭・profile-v1 仍是唯一活动所有者</color>"
                    : "<color=#8FD7FF>READ-ONLY・Default Off・profile-v1 remains sole active owner</color>");
        builder.Append('\n');
        Pair(
            builder,
            traditional ? "狀態" : simplified ? "状态" : "Status",
            snapshot.Status.ToString());
        Pair(
            builder,
            traditional ? "模式" : simplified ? "模式" : "Mode",
            snapshot.InfluenceMode.ToString());
        Pair(
            builder,
            traditional ? "觀察" : simplified ? "观察" : "Observe",
            $"{snapshot.ObservationCount} "
            + $"A{snapshot.AcceptedCount} "
            + $"C{snapshot.CoalescedCount} "
            + $"R{snapshot.RejectedCount}");
        Pair(
            builder,
            traditional ? "記錄" : simplified ? "记录" : "Records",
            $"{snapshot.Records.Count}/"
            + $"{CharacterCanonDualReadObserveHost.MaximumRecords} "
            + $"retired={snapshot.RetiredCount}");

        var latest = snapshot.Latest;
        if (latest != null)
        {
            builder.Append('\n');
            Pair(
                builder,
                traditional
                    ? "作用中擁有者"
                    : simplified
                        ? "活动所有者"
                        : "Active owner",
                latest.ActiveOwner.ToString());
            Pair(
                builder,
                "profile-v1",
                $"{latest.LegacyProfileId} "
                + $"v{latest.LegacyProfileVersion} "
                + $"#{Short(latest.LegacySemanticHash)}");
            Pair(
                builder,
                "profile-v2 candidate",
                $"{latest.CandidateProfileId} "
                + $"v{latest.CandidateProfileVersion} "
                + $"#{Short(latest.CandidateSemanticHash)} "
                + "active=False");
            Pair(
                builder,
                traditional
                    ? "典範結構"
                    : simplified
                        ? "典范结构"
                        : "Canon",
                $"D{latest.StableDispositionCount} "
                + $"T{latest.CanonicalTensionCount} "
                + $"B{latest.InterpretiveBoundaryCount} "
                + $"C{latest.ContextCount} "
                + $"U{latest.TypedUnknownCount}");
            Pair(
                builder,
                traditional ? "連續性" : simplified ? "连续性" : "Continuity",
                latest.ContinuityStatus.ToString());
            Pair(
                builder,
                traditional
                    ? "比較指紋"
                    : simplified
                        ? "比较指纹"
                        : "Fingerprint",
                Short(latest.SemanticComparisonFingerprint));
        }

        builder.Append('\n');
        Pair(
            builder,
            traditional ? "權威輸出" : simplified ? "权威输出" : "Authority",
            "owner 0・decision 0・expression 0・prompt 0・queue 0・motion 0");
        Pair(
            builder,
            "AI / Memory / Reasoning / provider",
            "0 / 0 / 0 / 0");

        var rendered = builder.ToString().TrimEnd();
        return rendered.Length <= MaximumRenderedCharacters
            ? rendered
            : rendered[..MaximumRenderedCharacters];
    }

    private static string Short(string value)
        => string.IsNullOrEmpty(value)
            ? "-"
            : value[..Math.Min(8, value.Length)];

    private static void Pair(
        StringBuilder builder,
        string label,
        string value)
        => Line(builder, $"<b>{label}</b>  {value}");

    private static void Line(StringBuilder builder, string value)
    {
        builder.Append(value);
        builder.Append('\n');
    }
}
