using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal sealed class CharacterLanguageStyleStageFCReplayScenario
{
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal CharacterLanguageStyleStageFBTurnCorrelation Correlation {
        get;
    }
    internal CharacterLanguageStyleStageFBRequestObservation Observation {
        get;
    }
    internal CharacterSituationProjection Projection { get; }
    internal DateTimeOffset StartedAt { get; }
    internal CharacterLanguageStyleStageFCComparisonLabel? Label {
        get;
    }
    internal bool DisableAfterPreview { get; }
    internal bool RestartAfterPreview { get; }

    internal CharacterLanguageStyleStageFCReplayScenario(
        CharacterRuntimeIdentity runtimeIdentity,
        CharacterLanguageStyleStageFBTurnCorrelation correlation,
        CharacterLanguageStyleStageFBRequestObservation observation,
        CharacterSituationProjection projection,
        DateTimeOffset startedAt,
        CharacterLanguageStyleStageFCComparisonLabel? label = null,
        bool disableAfterPreview = false,
        bool restartAfterPreview = false)
    {
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        Correlation = correlation
            ?? throw new ArgumentNullException(nameof(correlation));
        Observation = observation
            ?? throw new ArgumentNullException(nameof(observation));
        Projection = projection
            ?? throw new ArgumentNullException(nameof(projection));
        StartedAt =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                startedAt,
                nameof(startedAt));
        if (label.HasValue
            && !Enum.IsDefined(
                typeof(CharacterLanguageStyleStageFCComparisonLabel),
                label.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(label));
        }
        Label = label;
        DisableAfterPreview = disableAfterPreview;
        RestartAfterPreview = restartAfterPreview;
    }
}

internal sealed class CharacterLanguageStyleStageFCReplayResult
{
    internal CharacterLanguageStyleStageFCAdmission ArmAdmission {
        get;
    }
    internal CharacterLanguageStyleStageFCAdmission PreviewAdmission {
        get;
    }
    internal CharacterLanguageStyleStageFCAdmission? LabelAdmission {
        get;
    }
    internal CharacterLanguageStyleStageFCSnapshot Snapshot { get; }
    internal string TraceFingerprint { get; }

    internal CharacterLanguageStyleStageFCReplayResult(
        CharacterLanguageStyleStageFCAdmission armAdmission,
        CharacterLanguageStyleStageFCAdmission previewAdmission,
        CharacterLanguageStyleStageFCAdmission? labelAdmission,
        CharacterLanguageStyleStageFCSnapshot snapshot,
        string traceFingerprint)
    {
        ArmAdmission = armAdmission;
        PreviewAdmission = previewAdmission;
        LabelAdmission = labelAdmission;
        Snapshot = snapshot
            ?? throw new ArgumentNullException(nameof(snapshot));
        TraceFingerprint =
            CharacterLanguageStyleStageFContractGuard.Sha256Identifier(
                traceFingerprint,
                nameof(traceFingerprint));
    }
}

internal static class CharacterLanguageStyleStageFCReplay
{
    internal static CharacterLanguageStyleStageFCReplayResult Run(
        CharacterLanguageStyleStageFCReplayScenario scenario)
    {
        if (scenario == null)
            throw new ArgumentNullException(nameof(scenario));
        var host = new CharacterLanguageStyleStageFCPreviewHost();
        host.Reconcile(
            new CharacterLanguageStyleStageFCConfiguration(
                enabled: true,
                CharacterLanguageStyleStageFCConfiguration.MaximumCapacity),
            sessionOpen: true,
            scenario.RuntimeIdentity,
            scenario.StartedAt);
        var arm = host.Arm();
        var previewAdmission = host.TryPreview(
            scenario.Correlation,
            scenario.Observation,
            scenario.Projection,
            scenario.RuntimeIdentity,
            scenario.Correlation.Authorization.SessionId,
            scenario.Correlation.Authorization.SituationId,
            scenario.Correlation.Authorization.SituationRevision,
            scenario.StartedAt.AddMilliseconds(10),
            out var preview);
        CharacterLanguageStyleStageFCAdmission? labelAdmission = null;
        if (scenario.Label.HasValue && preview != null)
        {
            labelAdmission = host.SubmitLabel(
                preview.PreviewId,
                scenario.Label.Value,
                scenario.StartedAt.AddMilliseconds(20));
        }
        if (scenario.DisableAfterPreview)
        {
            host.Reconcile(
                CharacterLanguageStyleStageFCConfiguration.Off,
                sessionOpen: false,
                runtimeIdentity: null,
                scenario.StartedAt.AddMilliseconds(30));
        }
        if (scenario.RestartAfterPreview)
            host.Restart();

        var snapshot = host.GetSnapshot();
        return new CharacterLanguageStyleStageFCReplayResult(
            arm,
            previewAdmission,
            labelAdmission,
            snapshot,
            Fingerprint(
                arm,
                previewAdmission,
                labelAdmission,
                snapshot));
    }

    private static string Fingerprint(
        CharacterLanguageStyleStageFCAdmission arm,
        CharacterLanguageStyleStageFCAdmission preview,
        CharacterLanguageStyleStageFCAdmission? label,
        CharacterLanguageStyleStageFCSnapshot snapshot)
    {
        var material = string.Join(
            "|",
            ((int)arm).ToString(CultureInfo.InvariantCulture),
            ((int)preview).ToString(CultureInfo.InvariantCulture),
            label.HasValue
                ? ((int)label.Value).ToString(CultureInfo.InvariantCulture)
                : "-",
            ((int)snapshot.Status).ToString(CultureInfo.InvariantCulture),
            snapshot.Enabled,
            snapshot.Armed,
            snapshot.ProcessArmConsumed,
            snapshot.PreviewCount.ToString(CultureInfo.InvariantCulture),
            snapshot.LabelCount.ToString(CultureInfo.InvariantCulture),
            snapshot.RejectedCount.ToString(CultureInfo.InvariantCulture),
            snapshot.LatestPreview?.PreviewId ?? "-",
            snapshot.LatestPreview?.CharacterStyleId ?? "-",
            snapshot.LatestLabel?.Label.ToString() ?? "-");
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(material)))
            .ToLowerInvariant();
    }
}
