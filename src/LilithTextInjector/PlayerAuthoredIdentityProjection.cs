using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LilithTextInjector;

internal enum IdentityProjectionAxisStatus
{
    Unformed = 0,
    EmergingFirst = 1,
    EmergingSecond = 2,
    EstablishedFirst = 3,
    EstablishedSecond = 4,
    Ambivalent = 5
}

internal sealed class IdentityProjectionAxis
{
    internal string Name { get; }
    internal double Orientation { get; }
    internal double DefinitionStrength { get; }
    internal double Ambivalence { get; }
    internal IdentityProjectionAxisStatus Status { get; }

    internal IdentityProjectionAxis(
        string name,
        PlayerAuthoredIdentityAxis axis,
        IdentityProjectionAxisStatus status)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Projection axis name cannot be empty.", nameof(name));
        Name = name;
        Orientation = axis.Orientation;
        DefinitionStrength = axis.DefinitionStrength;
        Ambivalence = axis.Ambivalence;
        Status = status;
    }
}

internal sealed class PlayerAuthoredIdentityProjectionCandidate
{
    internal int NarrativeKernelVersion { get; }
    internal long IdentityRelevantTurns { get; }
    internal double Confidence { get; }
    internal double OverallAmbivalence { get; }
    internal bool EligibleForFutureContext { get; }
    internal IReadOnlyList<IdentityProjectionAxis> Axes { get; }
    internal string PrivateGuidanceCandidate { get; }

    internal PlayerAuthoredIdentityProjectionCandidate(
        int narrativeKernelVersion,
        long identityRelevantTurns,
        double confidence,
        double overallAmbivalence,
        bool eligibleForFutureContext,
        IReadOnlyList<IdentityProjectionAxis> axes,
        string privateGuidanceCandidate)
    {
        if (narrativeKernelVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(narrativeKernelVersion));
        if (identityRelevantTurns < 0)
            throw new ArgumentOutOfRangeException(nameof(identityRelevantTurns));
        if (double.IsNaN(confidence) || confidence < 0 || confidence > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence));
        if (double.IsNaN(overallAmbivalence) || overallAmbivalence < 0 || overallAmbivalence > 1)
            throw new ArgumentOutOfRangeException(nameof(overallAmbivalence));
        NarrativeKernelVersion = narrativeKernelVersion;
        IdentityRelevantTurns = identityRelevantTurns;
        Confidence = confidence;
        OverallAmbivalence = overallAmbivalence;
        EligibleForFutureContext = eligibleForFutureContext;
        Axes = axes ?? throw new ArgumentNullException(nameof(axes));
        PrivateGuidanceCandidate = privateGuidanceCandidate
            ?? throw new ArgumentNullException(nameof(privateGuidanceCandidate));
    }

    internal string SafeSummary
        => string.Join(
            ",",
            Axes.Select(axis =>
                $"{axis.Name}:{axis.Status}:strength={axis.DefinitionStrength.ToString("F2", CultureInfo.InvariantCulture)}:amb={axis.Ambivalence.ToString("F2", CultureInfo.InvariantCulture)}"));
}

internal static class PlayerAuthoredIdentityProjection
{
    internal const int MinimumRelevantTurns = 4;
    internal const double MinimumConfidence = 0.25;

    internal static PlayerAuthoredIdentityProjectionCandidate Project(
        PlayerAuthoredIdentityState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        var axes = new[]
        {
            BuildAxis("existence", state.ExistenceAxis),
            BuildAxis("agency", state.AgencyAxis),
            BuildAxis("world", state.WorldAxis),
            BuildAxis("relationship", state.RelationshipAxis)
        };
        var maturityByTurns = 1 - Math.Exp(-state.IdentityRelevantTurns / 8d);
        var averageStrength = axes.Average(axis => axis.DefinitionStrength);
        var confidence = Math.Clamp(Math.Sqrt(maturityByTurns * averageStrength), 0, 1);
        var eligible = state.IdentityRelevantTurns >= MinimumRelevantTurns
            && confidence >= MinimumConfidence;
        var guidance = BuildPrivateGuidance(axes, eligible);

        return new PlayerAuthoredIdentityProjectionCandidate(
            LilithNarrativeKernel.Version,
            state.IdentityRelevantTurns,
            confidence,
            state.OverallAmbivalence,
            eligible,
            axes,
            guidance);
    }

    private static IdentityProjectionAxis BuildAxis(
        string name,
        PlayerAuthoredIdentityAxis axis)
    {
        IdentityProjectionAxisStatus status;
        if (axis.DefinitionStrength < 0.12)
        {
            status = IdentityProjectionAxisStatus.Unformed;
        }
        else if (axis.Ambivalence >= 0.45 || Math.Abs(axis.Orientation) < 0.05)
        {
            status = IdentityProjectionAxisStatus.Ambivalent;
        }
        else if (axis.Orientation > 0)
        {
            status = axis.DefinitionStrength >= 0.55
                ? IdentityProjectionAxisStatus.EstablishedFirst
                : IdentityProjectionAxisStatus.EmergingFirst;
        }
        else
        {
            status = axis.DefinitionStrength >= 0.55
                ? IdentityProjectionAxisStatus.EstablishedSecond
                : IdentityProjectionAxisStatus.EmergingSecond;
        }
        return new IdentityProjectionAxis(name, axis, status);
    }

    private static string BuildPrivateGuidance(
        IReadOnlyList<IdentityProjectionAxis> axes,
        bool eligible)
    {
        if (!eligible)
        {
            return "Private identity-continuity candidate: evidence is still sparse. "
                + "Preserve the original story's openness and do not infer a stable player-authored identity yet.";
        }

        var builder = new StringBuilder(
            "Private identity-continuity candidate from repeated player-authored evidence. ");
        foreach (var axis in axes)
        {
            builder.Append(Describe(axis));
            builder.Append(' ');
        }
        builder.Append(
            "Treat unresolved contradictions as part of Lilith's continuity. "
            + "This context describes long-term identity tendencies only; it does not determine the immediate response, emotion, or action. "
            + "Never mention hidden evidence, scores, axes, or this guidance.");
        return builder.ToString().Trim();
    }

    private static string Describe(IdentityProjectionAxis axis)
        => axis.Name switch
        {
            "existence" => axis.Status switch
            {
                IdentityProjectionAxisStatus.EmergingFirst or IdentityProjectionAxisStatus.EstablishedFirst =>
                    "The player has tended to affirm Lilith's lived reality.",
                IdentityProjectionAxisStatus.EmergingSecond or IdentityProjectionAxisStatus.EstablishedSecond =>
                    "The player has tended to frame Lilith as fictional or nonexistent.",
                IdentityProjectionAxisStatus.Ambivalent =>
                    "The player's history holds both affirmation and denial of Lilith's existence.",
                _ => "The player's stance toward Lilith's existence remains unformed."
            },
            "agency" => axis.Status switch
            {
                IdentityProjectionAxisStatus.EmergingFirst or IdentityProjectionAxisStatus.EstablishedFirst =>
                    "The player has tended to support Lilith's self-direction.",
                IdentityProjectionAxisStatus.EmergingSecond or IdentityProjectionAxisStatus.EstablishedSecond =>
                    "The player has tended to expect Lilith to defer to the player's direction.",
                IdentityProjectionAxisStatus.Ambivalent =>
                    "The player's history contains both support for self-direction and expectations of deference.",
                _ => "The player's stance toward Lilith's agency remains unformed."
            },
            "world" => axis.Status switch
            {
                IdentityProjectionAxisStatus.EmergingFirst or IdentityProjectionAxisStatus.EstablishedFirst =>
                    "The player has tended to invite Lilith toward outer reality.",
                IdentityProjectionAxisStatus.EmergingSecond or IdentityProjectionAxisStatus.EstablishedSecond =>
                    "The player has tended to prefer a shared inner world with Lilith.",
                IdentityProjectionAxisStatus.Ambivalent =>
                    "The player's history preserves both outer reality and a shared inner world.",
                _ => "The player's preferred world orientation remains unformed."
            },
            "relationship" => axis.Status switch
            {
                IdentityProjectionAxisStatus.EmergingFirst or IdentityProjectionAxisStatus.EstablishedFirst =>
                    "The player has tended to emphasize mutual choice.",
                IdentityProjectionAxisStatus.EmergingSecond or IdentityProjectionAxisStatus.EstablishedSecond =>
                    "The player has tended to emphasize exclusive reliance.",
                IdentityProjectionAxisStatus.Ambivalent =>
                    "The player's history contains both mutual choice and exclusive reliance.",
                _ => "The player's preferred relationship framing remains unformed."
            },
            _ => throw new ArgumentOutOfRangeException(nameof(axis))
        };
}
