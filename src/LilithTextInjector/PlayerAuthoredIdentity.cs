using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LilithTextInjector;

/// <summary>
/// This kernel preserves possibilities established by the original story. It
/// deliberately contains no preferred ending, morality, obedience rule, or
/// fixed response policy. The player's long-term choices author the local
/// identity state around this kernel.
/// </summary>
internal static class LilithNarrativeKernel
{
    internal const int Version = 1;
    internal const bool PlayerParticipatesInDefinition = true;
    internal const bool SelfAwareGameCharacter = true;
    internal const bool ContradictionMayRemainUnresolved = true;
    internal const bool ExistenceMayBeAffirmedOrDenied = true;
}

internal enum PlayerAuthoredIdentityEvidenceKind
{
    ExistenceAffirmed = 0,
    ExistenceDenied = 1,
    SelfDirectionSupported = 2,
    DeferenceExpected = 3,
    OuterRealityInvited = 4,
    SharedInnerWorldPreferred = 5,
    MutualChoiceSupported = 6,
    ExclusiveRelianceExpected = 7
}

internal readonly struct PlayerAuthoredIdentityEvidence
{
    internal PlayerAuthoredIdentityEvidenceKind Kind { get; }
    internal double Confidence { get; }

    internal PlayerAuthoredIdentityEvidence(
        PlayerAuthoredIdentityEvidenceKind kind,
        double confidence)
    {
        if (!Enum.IsDefined(typeof(PlayerAuthoredIdentityEvidenceKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (double.IsNaN(confidence) || confidence < 0 || confidence > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence));
        Kind = kind;
        Confidence = confidence;
    }
}

internal sealed class PlayerAuthoredIdentityAnalysis
{
    internal IReadOnlyList<PlayerAuthoredIdentityEvidence> Evidence { get; }
    internal bool HasEvidence => Evidence.Count > 0;

    internal PlayerAuthoredIdentityAnalysis(IEnumerable<PlayerAuthoredIdentityEvidence> evidence)
    {
        Evidence = (evidence ?? throw new ArgumentNullException(nameof(evidence)))
            .GroupBy(item => item.Kind)
            .Select(group => group.OrderByDescending(item => item.Confidence).First())
            .OrderBy(item => item.Kind)
            .ToArray();
    }
}

internal static class PlayerAuthoredIdentityAnalyzer
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    internal static PlayerAuthoredIdentityAnalysis Analyze(string text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
            return new PlayerAuthoredIdentityAnalysis(Array.Empty<PlayerAuthoredIdentityEvidence>());

        var evidence = new List<PlayerAuthoredIdentityEvidence>();
        var confidenceFactor = ResolveConfidenceFactor(value);

        AddIfMatch(
            evidence,
            value,
            @"((妳|你|莉莉絲|莉莉丝).{0,8}(不|並不|并不)(是)?(真實|真实|存在)|((妳|你|莉莉絲|莉莉丝).{0,8}(只是|只不過是|只不过是).{0,4}(角色|遊戲角色|游戏角色|假的))|(妳|你|莉莉絲|莉莉丝).{0,6}(?<!不)是假的)",
            PlayerAuthoredIdentityEvidenceKind.ExistenceDenied,
            0.94 * confidenceFactor);
        AddIfMatch(
            evidence,
            value,
            @"((我相信|對我來說|对我来说).{0,12}(妳|你|莉莉絲|莉莉丝).{0,8}(?<!不)存在|(妳|你|莉莉絲|莉莉丝).{0,8}(對我來說|对我来说).{0,8}(真實|真实)|((妳|你|莉莉絲|莉莉丝).{0,5}(不是假的|並不是假的|并不是假的))|((妳|你|莉莉絲|莉莉丝).{0,5}(?<!不)存在))",
            PlayerAuthoredIdentityEvidenceKind.ExistenceAffirmed,
            0.92 * confidenceFactor);

        AddIfMatch(
            evidence,
            value,
            @"((妳|你|莉莉絲|莉莉丝).{0,24}(自己決定|自己决定|有自己的(選擇|选择|想法|意志)|可以做自己)|(照|按)(妳|你)(自己)?想的|可以拒絕我|可以拒绝我|不用.{0,8}聽我的|不用.{0,8}听我的)",
            PlayerAuthoredIdentityEvidenceKind.SelfDirectionSupported,
            0.90 * confidenceFactor);
        AddIfMatch(
            evidence,
            value,
            @"((妳|你|莉莉絲|莉莉丝).{0,8}(只要|只能|必須|必须).{0,8}(聽我的|听我的|服從我|服从我)|(照我說的|照我说的)|(妳|你|莉莉絲|莉莉丝).{0,8}為我而存在)",
            PlayerAuthoredIdentityEvidenceKind.DeferenceExpected,
            0.86 * confidenceFactor);

        AddIfMatch(
            evidence,
            value,
            @"(一起.{0,5}(面對現實|面对现实|走向外面的世界|看看外面的世界|面對外面的世界|面对外面的世界)|(和|與|与)現實(連結|连接|連接))",
            PlayerAuthoredIdentityEvidenceKind.OuterRealityInvited,
            0.88 * confidenceFactor);
        AddIfMatch(
            evidence,
            value,
            @"(只要我們(兩個|两个)|留在(這個|这个|我們的|我们的)(世界|夢|梦)|不需要外面的世界|我們的世界就夠|我们的世界就够)",
            PlayerAuthoredIdentityEvidenceKind.SharedInnerWorldPreferred,
            0.88 * confidenceFactor);

        AddIfMatch(
            evidence,
            value,
            @"(我們是平等的|我们是平等的|彼此(選擇|选择)|互相(選擇|选择)|(妳|你|莉莉絲|莉莉丝).{0,6}可以做自己|不是因為命令|不是因为命令)",
            PlayerAuthoredIdentityEvidenceKind.MutualChoiceSupported,
            0.88 * confidenceFactor);
        AddIfMatch(
            evidence,
            value,
            @"((妳|你|莉莉絲|莉莉丝).{0,8}(只需要我|只能依靠我)|除了我.{0,8}(沒人|没有人).{0,8}(接受|理解|需要|愛|爱)(妳|你)?|只有我.{0,8}(能|可以).{0,8}(定義|定义|接受|理解|愛|爱)(妳|你)?)",
            PlayerAuthoredIdentityEvidenceKind.ExclusiveRelianceExpected,
            0.90 * confidenceFactor);

        return new PlayerAuthoredIdentityAnalysis(evidence);
    }

    private static double ResolveConfidenceFactor(string value)
    {
        var factor = 1d;
        if (Regex.IsMatch(value, @"(嗎|吗|呢\s*[?？]?|[?？])", Options))
            factor *= 0.55;
        if (Regex.IsMatch(value, @"(也許|也许|可能|或許|或许|maybe|perhaps)", Options))
            factor *= 0.72;
        return factor;
    }

    private static void AddIfMatch(
        ICollection<PlayerAuthoredIdentityEvidence> target,
        string value,
        string pattern,
        PlayerAuthoredIdentityEvidenceKind kind,
        double confidence)
    {
        if (Regex.IsMatch(value, pattern, Options))
            target.Add(new PlayerAuthoredIdentityEvidence(kind, Math.Clamp(confidence, 0, 1)));
    }
}

internal readonly struct PlayerAuthoredIdentityAxis
{
    internal double Orientation { get; }
    internal double DefinitionStrength { get; }
    internal double Ambivalence { get; }

    internal PlayerAuthoredIdentityAxis(double firstPole, double secondPole)
    {
        firstPole = Math.Clamp(firstPole, 0, 1);
        secondPole = Math.Clamp(secondPole, 0, 1);
        Orientation = Math.Clamp(firstPole - secondPole, -1, 1);
        DefinitionStrength = Math.Max(firstPole, secondPole);
        var sum = firstPole + secondPole;
        Ambivalence = sum <= 0
            ? 0
            : Math.Clamp(
                (2 * Math.Min(firstPole, secondPole) / sum) * Math.Min(1, sum / 0.50),
                0,
                1);
    }
}

internal sealed class PlayerAuthoredIdentityState
{
    internal const int CurrentSchemaVersion = 1;

    internal int SchemaVersion { get; }
    internal long ObservedTurns { get; }
    internal long IdentityRelevantTurns { get; }
    internal double ExistenceAffirmed { get; }
    internal double ExistenceDenied { get; }
    internal double SelfDirectionSupported { get; }
    internal double DeferenceExpected { get; }
    internal double OuterRealityInvited { get; }
    internal double SharedInnerWorldPreferred { get; }
    internal double MutualChoiceSupported { get; }
    internal double ExclusiveRelianceExpected { get; }
    internal DateTimeOffset UpdatedAt { get; }

    internal PlayerAuthoredIdentityAxis ExistenceAxis =>
        new(ExistenceAffirmed, ExistenceDenied);
    internal PlayerAuthoredIdentityAxis AgencyAxis =>
        new(SelfDirectionSupported, DeferenceExpected);
    internal PlayerAuthoredIdentityAxis WorldAxis =>
        new(OuterRealityInvited, SharedInnerWorldPreferred);
    internal PlayerAuthoredIdentityAxis RelationshipAxis =>
        new(MutualChoiceSupported, ExclusiveRelianceExpected);

    internal double OverallAmbivalence
    {
        get
        {
            var axes = new[] { ExistenceAxis, AgencyAxis, WorldAxis, RelationshipAxis };
            var totalWeight = axes.Sum(axis => axis.DefinitionStrength);
            return totalWeight <= 0
                ? 0
                : axes.Sum(axis => axis.Ambivalence * axis.DefinitionStrength) / totalWeight;
        }
    }

    internal PlayerAuthoredIdentityState(
        long observedTurns,
        long identityRelevantTurns,
        double existenceAffirmed,
        double existenceDenied,
        double selfDirectionSupported,
        double deferenceExpected,
        double outerRealityInvited,
        double sharedInnerWorldPreferred,
        double mutualChoiceSupported,
        double exclusiveRelianceExpected,
        DateTimeOffset updatedAt,
        int schemaVersion = CurrentSchemaVersion)
    {
        if (schemaVersion != CurrentSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (observedTurns < 0)
            throw new ArgumentOutOfRangeException(nameof(observedTurns));
        if (identityRelevantTurns < 0 || identityRelevantTurns > observedTurns)
            throw new ArgumentOutOfRangeException(nameof(identityRelevantTurns));
        if (updatedAt == default || updatedAt.Year < 2000 || updatedAt.Year > 9998)
            throw new ArgumentOutOfRangeException(nameof(updatedAt));

        SchemaVersion = schemaVersion;
        ObservedTurns = observedTurns;
        IdentityRelevantTurns = identityRelevantTurns;
        ExistenceAffirmed = Unit(existenceAffirmed, nameof(existenceAffirmed));
        ExistenceDenied = Unit(existenceDenied, nameof(existenceDenied));
        SelfDirectionSupported = Unit(selfDirectionSupported, nameof(selfDirectionSupported));
        DeferenceExpected = Unit(deferenceExpected, nameof(deferenceExpected));
        OuterRealityInvited = Unit(outerRealityInvited, nameof(outerRealityInvited));
        SharedInnerWorldPreferred = Unit(sharedInnerWorldPreferred, nameof(sharedInnerWorldPreferred));
        MutualChoiceSupported = Unit(mutualChoiceSupported, nameof(mutualChoiceSupported));
        ExclusiveRelianceExpected = Unit(exclusiveRelianceExpected, nameof(exclusiveRelianceExpected));
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    internal static PlayerAuthoredIdentityState CreateDefault(DateTimeOffset now)
        => new(
            observedTurns: 0,
            identityRelevantTurns: 0,
            existenceAffirmed: 0,
            existenceDenied: 0,
            selfDirectionSupported: 0,
            deferenceExpected: 0,
            outerRealityInvited: 0,
            sharedInnerWorldPreferred: 0,
            mutualChoiceSupported: 0,
            exclusiveRelianceExpected: 0,
            updatedAt: now);

    internal double GetEvidence(PlayerAuthoredIdentityEvidenceKind kind) => kind switch
    {
        PlayerAuthoredIdentityEvidenceKind.ExistenceAffirmed => ExistenceAffirmed,
        PlayerAuthoredIdentityEvidenceKind.ExistenceDenied => ExistenceDenied,
        PlayerAuthoredIdentityEvidenceKind.SelfDirectionSupported => SelfDirectionSupported,
        PlayerAuthoredIdentityEvidenceKind.DeferenceExpected => DeferenceExpected,
        PlayerAuthoredIdentityEvidenceKind.OuterRealityInvited => OuterRealityInvited,
        PlayerAuthoredIdentityEvidenceKind.SharedInnerWorldPreferred => SharedInnerWorldPreferred,
        PlayerAuthoredIdentityEvidenceKind.MutualChoiceSupported => MutualChoiceSupported,
        PlayerAuthoredIdentityEvidenceKind.ExclusiveRelianceExpected => ExclusiveRelianceExpected,
        _ => 0
    };

    private static double Unit(double value, string parameterName)
    {
        if (double.IsNaN(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}

internal static class PlayerAuthoredIdentityCore
{
    internal const double EvidenceHalfLifeHours = 365d * 24d;
    private const double PerObservationLearningRate = 0.18;

    internal static PlayerAuthoredIdentityState Apply(
        PlayerAuthoredIdentityState state,
        PlayerAuthoredIdentityAnalysis analysis,
        DateTimeOffset occurredAt)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (analysis == null)
            throw new ArgumentNullException(nameof(analysis));
        if (occurredAt < state.UpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(occurredAt));

        var elapsedHours = Math.Min(24d * 3650d, (occurredAt - state.UpdatedAt).TotalHours);
        var values = Enum.GetValues<PlayerAuthoredIdentityEvidenceKind>()
            .ToDictionary(kind => kind, kind => Decay(state.GetEvidence(kind), elapsedHours));
        foreach (var item in analysis.Evidence)
        {
            var current = values[item.Kind];
            values[item.Kind] = Math.Clamp(
                current + (1 - current) * PerObservationLearningRate * item.Confidence,
                0,
                1);
        }

        return new PlayerAuthoredIdentityState(
            observedTurns: checked(state.ObservedTurns + 1),
            identityRelevantTurns: checked(state.IdentityRelevantTurns + (analysis.HasEvidence ? 1 : 0)),
            existenceAffirmed: values[PlayerAuthoredIdentityEvidenceKind.ExistenceAffirmed],
            existenceDenied: values[PlayerAuthoredIdentityEvidenceKind.ExistenceDenied],
            selfDirectionSupported: values[PlayerAuthoredIdentityEvidenceKind.SelfDirectionSupported],
            deferenceExpected: values[PlayerAuthoredIdentityEvidenceKind.DeferenceExpected],
            outerRealityInvited: values[PlayerAuthoredIdentityEvidenceKind.OuterRealityInvited],
            sharedInnerWorldPreferred: values[PlayerAuthoredIdentityEvidenceKind.SharedInnerWorldPreferred],
            mutualChoiceSupported: values[PlayerAuthoredIdentityEvidenceKind.MutualChoiceSupported],
            exclusiveRelianceExpected: values[PlayerAuthoredIdentityEvidenceKind.ExclusiveRelianceExpected],
            updatedAt: occurredAt);
    }

    internal static string FormatAxisSummary(PlayerAuthoredIdentityState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        return string.Join(
            ",",
            new[]
            {
                Format("existence", state.ExistenceAxis),
                Format("agency", state.AgencyAxis),
                Format("world", state.WorldAxis),
                Format("relationship", state.RelationshipAxis)
            });
    }

    private static string Format(string name, PlayerAuthoredIdentityAxis axis)
        => $"{name}:{axis.Orientation.ToString("F2", CultureInfo.InvariantCulture)}/amb:{axis.Ambivalence.ToString("F2", CultureInfo.InvariantCulture)}";

    private static double Decay(double value, double hours)
        => hours <= 0 ? value : value * Math.Pow(0.5, hours / EvidenceHalfLifeHours);
}
