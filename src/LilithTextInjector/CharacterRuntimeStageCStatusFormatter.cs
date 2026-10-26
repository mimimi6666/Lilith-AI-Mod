using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LilithTextInjector;

internal static class CharacterRuntimeStageCStatusFormatter
{
    internal const int MaximumRenderedCharacters = 2_400;

    internal static string Format(
        CharacterProjectionObserveSnapshot snapshot,
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
        var builder = new StringBuilder(1_024);
        Line(
            builder,
            traditional
                ? "<b>D-023 角色投影 Shadow／Observe</b>"
                : simplified
                    ? "<b>D-023 角色投影 Shadow／Observe</b>"
                    : "<b>D-023 Character Projection Shadow／Observe</b>");
        Line(
            builder,
            traditional
                ? "<color=#8FD7FF>唯讀・ProjectionOnly・不影響決策／Prompt／行為</color>"
                : simplified
                    ? "<color=#8FD7FF>只读・ProjectionOnly・不影响决策／Prompt／行为</color>"
                    : "<color=#8FD7FF>READ-ONLY・ProjectionOnly・no Decision／Prompt／behavior influence</color>");
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
            traditional ? "觀測" : simplified ? "观察" : "Observe",
            $"{snapshot.ObservationCount} A{snapshot.AcceptedCount} "
            + $"R{snapshot.RejectedCount} C{snapshot.CoalescedCount} "
            + $"FC{snapshot.FailureEpisodeCoalescedCount}");
        Pair(
            builder,
            traditional ? "記錄" : simplified ? "记录" : "Records",
            $"{snapshot.RecordCount}/"
            + $"{CharacterProjectionObserveRuntimeHost.MaximumRecords} "
            + $"retired={snapshot.RetiredCount}");

        var latest = snapshot.Latest;
        if (latest != null)
        {
            builder.Append('\n');
            Pair(
                builder,
                traditional ? "最後投影" : simplified ? "最后投影" : "Last",
                $"{latest.ProjectionAdmission} "
                + $"P{latest.ProjectionRevision} "
                + $"S{latest.SituationRevision}");
            if (latest.FailureEpisodeFingerprint.Length > 0)
            {
                Pair(
                    builder,
                    "Failure episode",
                    $"{Short(latest.FailureEpisodeFingerprint)} "
                    + $"count={latest.FailureEpisodeCount}");
            }
            Pair(
                builder,
                traditional ? "診斷" : simplified ? "诊断" : "Flags",
                $"M:{latest.MissingDomains} "
                + $"T:{latest.StaleDomains} "
                + $"A:{latest.AmbiguousDomains} "
                + $"C:{latest.ConflictingDomains}");
            var skew = latest.ObservationTimeSkew;
            Pair(
                builder,
                traditional ? "觀測時間差" : simplified ? "观察时间差" : "Time skew",
                $"{skew.Status} "
                + $"{skew.Span.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s "
                + $"atomic={skew.AtomicallyConsistent}");
            foreach (var domain in latest.Domains)
            {
                var observed = domain.ObservedAt.HasValue
                    ? domain.ObservedAt.Value
                        .ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                    : "-";
                Pair(
                    builder,
                    domain.Domain.ToString(),
                    $"{domain.Presence}/{domain.SourceHealth} "
                    + $"at={observed} rev={Short(domain.SourceRevision)} "
                    + $"fresh={Freshness(domain, latest.AssembledAt)} "
                    + $"conflict={domain.Conflict?.ToString() ?? "-"}");
            }
        }

        builder.Append('\n');
        Pair(
            builder,
            traditional ? "權威輸出" : simplified ? "权威输出" : "Authority",
            "decision 0・candidate 0・expression 0・queue 0・motion 0・execution 0");
        Pair(
            builder,
            "AI／Memory／Reasoning／provider",
            "0／0／0／0");

        var rendered = builder.ToString().TrimEnd();
        return rendered.Length <= MaximumRenderedCharacters
            ? rendered
            : rendered[..MaximumRenderedCharacters];
    }

    private static string Freshness(
        CharacterProjectionDomainObservation domain,
        DateTimeOffset assembledAt)
        => !domain.ValidUntil.HasValue
            ? "Missing"
            : domain.ValidUntil.Value <= assembledAt
                ? "Stale"
                : "Fresh";

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
