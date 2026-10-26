using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace LilithTextInjector;

internal enum CognitiveSemanticEnvelopeV2Admission
{
    NoSidecar = 0,
    NonTerminalFramePreserved = 1,
    EnvelopeObserved = 2,
    MalformedTailStripped = 3,
    OversizeTailStripped = 4
}

internal sealed class CognitiveSemanticEnvelopeV2Proposal
{
    internal CognitiveDomain Domain { get; }
    internal CognitiveClaimKind ClaimKind { get; }
    internal CognitiveSubjectScope SubjectScope { get; }
    internal CognitiveTemporalScope TemporalScope { get; }
    internal CognitiveEpistemicState EpistemicState { get; }
    internal CognitiveConfidenceBand Confidence { get; }
    internal CognitiveShadowDisposition SuggestedDisposition { get; }
    internal CognitiveInteractionStance SuggestedStance { get; }
    internal CognitiveFollowUpMode SuggestedFollowUp { get; }
    internal CognitiveMemoryPreviewKind SuggestedMemoryPreview { get; }
    internal bool RequiresConfirmation { get; }
    internal string EvidenceQuote { get; }
    internal IReadOnlyList<CognitiveEvidenceKind> MetadataEvidence { get; }

    internal CognitiveSemanticEnvelopeV2Proposal(
        CognitiveDomain domain,
        CognitiveClaimKind claimKind,
        CognitiveSubjectScope subjectScope,
        CognitiveTemporalScope temporalScope,
        CognitiveEpistemicState epistemicState,
        CognitiveConfidenceBand confidence,
        CognitiveShadowDisposition suggestedDisposition,
        CognitiveInteractionStance suggestedStance,
        CognitiveFollowUpMode suggestedFollowUp,
        CognitiveMemoryPreviewKind suggestedMemoryPreview,
        bool requiresConfirmation,
        string evidenceQuote,
        IEnumerable<CognitiveEvidenceKind> metadataEvidence)
    {
        Domain = CognitiveShadowContractGuard.Domain(domain, nameof(domain));
        ClaimKind = CognitiveShadowContractGuard.Enum(
            claimKind,
            nameof(claimKind));
        SubjectScope = CognitiveShadowContractGuard.Enum(
            subjectScope,
            nameof(subjectScope));
        TemporalScope = CognitiveShadowContractGuard.Enum(
            temporalScope,
            nameof(temporalScope));
        EpistemicState = CognitiveShadowContractGuard.Enum(
            epistemicState,
            nameof(epistemicState));
        Confidence = CognitiveShadowContractGuard.Enum(
            confidence,
            nameof(confidence));
        SuggestedDisposition = CognitiveShadowContractGuard.Enum(
            suggestedDisposition,
            nameof(suggestedDisposition));
        SuggestedStance = CognitiveShadowContractGuard.Enum(
            suggestedStance,
            nameof(suggestedStance));
        SuggestedFollowUp = CognitiveShadowContractGuard.Enum(
            suggestedFollowUp,
            nameof(suggestedFollowUp));
        SuggestedMemoryPreview = CognitiveShadowContractGuard.Enum(
            suggestedMemoryPreview,
            nameof(suggestedMemoryPreview));
        if (!CognitiveSemanticEnvelopeV2Policy.IsClaimOwned(
                Domain,
                ClaimKind)
            || !CognitiveSemanticEnvelopeV2Policy.IsMemoryPreviewValid(
                Domain,
                ClaimKind,
                SuggestedMemoryPreview)
            || (CognitiveSemanticEnvelopeV2Policy.RequiresStateConfirmation(
                    Domain)
                && !requiresConfirmation))
        {
            throw new ArgumentException(
                "The v2 proposal violates domain or state-confirmation policy.");
        }
        RequiresConfirmation = requiresConfirmation;
        EvidenceQuote = evidenceQuote
            ?? throw new ArgumentNullException(nameof(evidenceQuote));
        var sources = (metadataEvidence
                ?? throw new ArgumentNullException(nameof(metadataEvidence)))
            .ToArray();
        if (sources.Length
                > CognitiveSemanticEnvelopeV2Parser.MaximumMetadataEvidence
            || sources.Distinct().Count() != sources.Length
            || sources.Any(source =>
                !CognitiveSemanticEnvelopeV2Policy
                    .IsMetadataEvidence(source)))
        {
            throw new ArgumentException(
                "Metadata evidence must be unique and allowlisted.",
                nameof(metadataEvidence));
        }
        MetadataEvidence = new ReadOnlyCollection<CognitiveEvidenceKind>(
            sources);
    }
}

internal sealed class CognitiveSemanticEnvelopeV2Relation
{
    internal int FromProposalIndex { get; }
    internal int ToProposalIndex { get; }
    internal CognitiveRelationKind Kind { get; }
    internal CognitiveRelationReason Reason { get; }
    internal CognitiveRelationStrength Strength { get; }

    internal CognitiveSemanticEnvelopeV2Relation(
        int fromProposalIndex,
        int toProposalIndex,
        CognitiveRelationKind kind,
        CognitiveRelationReason reason,
        CognitiveRelationStrength strength)
    {
        if (fromProposalIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(fromProposalIndex));
        if (toProposalIndex < 0 || toProposalIndex == fromProposalIndex)
            throw new ArgumentOutOfRangeException(nameof(toProposalIndex));
        Kind = CognitiveShadowContractGuard.Enum(kind, nameof(kind));
        Reason = CognitiveShadowContractGuard.Enum(reason, nameof(reason));
        Strength = CognitiveShadowContractGuard.Enum(
            strength,
            nameof(strength));
        if (Kind == CognitiveRelationKind.Blocks
            || Strength == CognitiveRelationStrength.Deterministic)
        {
            throw new ArgumentException(
                "A semantic v2 relation cannot claim hard policy authority.");
        }
        FromProposalIndex = fromProposalIndex;
        ToProposalIndex = toProposalIndex;
    }
}

internal sealed class CognitiveSemanticEnvelopeV2
{
    internal int Version => CognitiveSemanticEnvelopeV2Parser.SchemaVersion;
    internal IReadOnlyList<CognitiveSemanticEnvelopeV2Proposal> Proposals {
        get;
    }
    internal IReadOnlyList<CognitiveSemanticEnvelopeV2Relation> Relations {
        get;
    }

    internal CognitiveSemanticEnvelopeV2(
        IEnumerable<CognitiveSemanticEnvelopeV2Proposal> proposals,
        IEnumerable<CognitiveSemanticEnvelopeV2Relation> relations)
    {
        var proposalItems = (proposals
                ?? throw new ArgumentNullException(nameof(proposals)))
            .ToArray();
        var relationItems = (relations
                ?? throw new ArgumentNullException(nameof(relations)))
            .ToArray();
        if (proposalItems.Length
                > CognitiveSemanticEnvelopeV2Parser.MaximumProposals
            || relationItems.Length
                > CognitiveSemanticEnvelopeV2Parser.MaximumRelations
            || proposalItems.Any(item => item == null)
            || relationItems.Any(item => item == null)
            || relationItems.Any(item =>
                item.FromProposalIndex >= proposalItems.Length
                || item.ToProposalIndex >= proposalItems.Length)
            || relationItems
                .Select(CognitiveSemanticEnvelopeV2Policy.RelationKey)
                .Distinct(StringComparer.Ordinal)
                .Count() != relationItems.Length)
        {
            throw new ArgumentException(
                "The v2 envelope exceeds bounds or has invalid relations.");
        }
        Proposals = new ReadOnlyCollection<
            CognitiveSemanticEnvelopeV2Proposal>(proposalItems);
        Relations = new ReadOnlyCollection<
            CognitiveSemanticEnvelopeV2Relation>(relationItems);
    }
}

internal sealed class CognitiveSemanticEnvelopeV2Observation
{
    internal string ReplyPrefix { get; }
    internal CognitiveSemanticEnvelopeV2? Envelope { get; }
    internal CognitiveSemanticEnvelopeV2Admission Admission { get; }
    internal bool TailStripped { get; }

    internal CognitiveSemanticEnvelopeV2Observation(
        string replyPrefix,
        CognitiveSemanticEnvelopeV2? envelope,
        CognitiveSemanticEnvelopeV2Admission admission,
        bool tailStripped)
    {
        ReplyPrefix = replyPrefix
            ?? throw new ArgumentNullException(nameof(replyPrefix));
        Admission = CognitiveShadowContractGuard.Enum(
            admission,
            nameof(admission));
        if ((Admission
                == CognitiveSemanticEnvelopeV2Admission.EnvelopeObserved)
                != (envelope != null)
            || tailStripped
                != (Admission is
                    CognitiveSemanticEnvelopeV2Admission.EnvelopeObserved
                    or CognitiveSemanticEnvelopeV2Admission
                        .MalformedTailStripped
                    or CognitiveSemanticEnvelopeV2Admission
                        .OversizeTailStripped))
        {
            throw new ArgumentException(
                "Envelope and tail stripping must match admission.");
        }
        Envelope = envelope;
        TailStripped = tailStripped;
    }
}

internal static class CognitiveSemanticEnvelopeV2Protocol
{
    internal static string BuildPromptInstruction(bool japaneseVoiceMode)
    {
        var language = japaneseVoiceMode
            ? "The visible reply still follows Japanese voice JSON mode. "
            : string.Empty;
        return language
            + "After the complete visible reply, you may append exactly one "
            + "terminal Shadow-only semantic frame on a new line: "
            + CognitiveSemanticEnvelopeV2Parser.OpeningTag
            + "{\"v\":2,\"p\":[{\"d\":\"Memory\",\"k\":\"Fact\","
            + "\"s\":\"User\",\"t\":\"CurrentTurn\","
            + "\"e\":\"Plausible\",\"c\":\"Medium\","
            + "\"x\":\"Observe\",\"st\":\"Acknowledge\","
            + "\"f\":\"Optional\",\"m\":\"Fact\",\"rc\":true,"
            + "\"q\":\"\",\"src\":[]}],\"r\":[]}"
            + CognitiveSemanticEnvelopeV2Parser.ClosingTag
            + ". Closed values: d=Memory|UserAffect|Boundary|OpenLoop|Proactive; "
            + "k=Episode|Fact|Correction|ExplicitRemember|UserAffect|Boundary|"
            + "OpenLoop|OpenLoopResolution|CareIntent|FollowUpIntent|"
            + "SubjectAttribution|InteractionPreference; s=Unknown|User|"
            + "ThirdParty|Lilith|Conversation; t=Unknown|CurrentTurn|Recent|"
            + "Historical|Durable; e=Supported|Plausible|Ambiguous|Contradicted|"
            + "NeedMoreEvidence|Abstained; c=Unspecified|Low|Medium|High; "
            + "x=NoOpinion|Silence|Observe|Defer|Respond|AskConsent; "
            + "st=NoOpinion|Neutral|NonCommittal|Acknowledge|Care|Clarify; "
            + "f=NoOpinion|None|Optional|AskClarification; "
            + "m=None|Episode|Fact|Correction|ExplicitRemember; "
            + "src=MemoryWorkingSet|ActiveBoundary|ActiveOpenLoop|"
            + "CharacterProjection|ProactiveSituation. Relation k=Supports|"
            + "Contradicts|Cautions|Defers|RequiresConfirmation|"
            + "InsufficientEvidenceFor|Unrelated|Supersedes|TemporallyLimits; "
            + "relation r=EvidenceSupport|EvidenceConflict|ExplicitBoundary|"
            + "PrivacyPolicy|SafetyPolicy|ConsentPolicy|ExplicitCorrection|"
            + "SubjectAttribution|TemporalScope|MissingReferent|"
            + "InsufficientEvidence|InteractionPressure|UnrelatedScope|"
            + "OpenLoopResolution; relation s=Weak|Moderate|Strong. All 13 "
            + "proposal fields and all 5 relation fields are required. "
            + "q is an exact unique substring of the current user turn (empty "
            + "means the whole turn). Use at most 4 proposals, 4 relations, "
            + "96 Unicode scalars per q, 2 metadata sources and 2048 UTF-16 "
            + "units for the whole frame. Never emit IDs, hashes, timestamps, "
            + "model, Prompt, Provider or completion identity. Never emit a "
            + "Blocks relation or Deterministic strength. Omit the frame when "
            + "unnecessary and never damage the visible reply to include it. "
            + "Use only a claim kind owned by its domain; m must be None outside "
            + "Memory and rc must be true for Memory, Boundary or OpenLoop. "
            + "This frame is non-applying Shadow data and authorizes no state "
            + "write, reply change, tool, proactive action or additional call.";
    }
}

internal static class CognitiveSemanticEnvelopeV2Parser
{
    internal const int SchemaVersion = 2;
    internal const int MaximumFramedSidecarUtf16Units = 2048;
    internal const int MaximumProposals = 4;
    internal const int MaximumRelations = 4;
    internal const int MaximumEvidenceQuoteUnicodeScalars = 96;
    internal const int MaximumMetadataEvidence = 2;
    internal const int MaximumJsonDepth = 6;
    internal const int MaximumClosingCandidateJsonParses = 16;
    internal const string OpeningTag = "<lilith_understanding_v2>";
    internal const string ClosingTag = "</lilith_understanding_v2>";
    private const string LegacyOpeningTag = "<lilith_understanding_v1>";
    private const string LegacyClosingTag = "</lilith_understanding_v1>";

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumJsonDepth
    };

    internal static CognitiveSemanticEnvelopeV2Observation Parse(
        string response)
    {
        if (response == null)
            throw new ArgumentNullException(nameof(response));
        var openingIndex = FindLineStartOpening(response, OpeningTag, 0);
        if (openingIndex < 0)
        {
            return Preserved(
                response,
                CognitiveSemanticEnvelopeV2Admission.NoSidecar);
        }
        var legacyIndex = FindLineStartOpening(
            response,
            LegacyOpeningTag,
            0);
        var secondOpeningIndex = FindLineStartOpening(
            response,
            OpeningTag,
            openingIndex + OpeningTag.Length);
        if (legacyIndex >= 0 || secondOpeningIndex >= 0)
        {
            var tailStart = legacyIndex >= 0
                ? Math.Min(legacyIndex, openingIndex)
                : openingIndex;
            if (!IsPureRecognizableFrameTail(response, tailStart))
            {
                return Preserved(
                    response,
                    CognitiveSemanticEnvelopeV2Admission
                        .NonTerminalFramePreserved);
            }
            return Stripped(
                response,
                tailStart,
                CognitiveSemanticEnvelopeV2Admission.MalformedTailStripped);
        }
        var jsonStart = openingIndex + OpeningTag.Length;
        var closingIndex = FindFrameClosingIndex(
            response,
            openingIndex,
            OpeningTag,
            ClosingTag,
            response.Length);
        if (closingIndex < jsonStart)
        {
            return Stripped(
                response,
                openingIndex,
                response.Length - openingIndex
                    > MaximumFramedSidecarUtf16Units
                    ? CognitiveSemanticEnvelopeV2Admission
                        .OversizeTailStripped
                    : CognitiveSemanticEnvelopeV2Admission
                        .MalformedTailStripped);
        }
        var closingEnd = closingIndex + ClosingTag.Length;
        if (!IsWhitespaceOnly(response, closingEnd))
        {
            return Preserved(
                response,
                CognitiveSemanticEnvelopeV2Admission
                    .NonTerminalFramePreserved);
        }
        if (closingEnd - openingIndex > MaximumFramedSidecarUtf16Units)
        {
            return Stripped(
                response,
                openingIndex,
                CognitiveSemanticEnvelopeV2Admission.OversizeTailStripped);
        }
        var json = response.Substring(
            jsonStart,
            closingIndex - jsonStart);
        if (!TryParseEnvelope(json, out var envelope) || envelope == null)
        {
            return Stripped(
                response,
                openingIndex,
                CognitiveSemanticEnvelopeV2Admission.MalformedTailStripped);
        }
        return new CognitiveSemanticEnvelopeV2Observation(
            response.Substring(0, openingIndex),
            envelope,
            CognitiveSemanticEnvelopeV2Admission.EnvelopeObserved,
            tailStripped: true);
    }

    private static CognitiveSemanticEnvelopeV2Observation Preserved(
        string response,
        CognitiveSemanticEnvelopeV2Admission admission)
        => new(response, null, admission, tailStripped: false);

    private static CognitiveSemanticEnvelopeV2Observation Stripped(
        string response,
        int openingIndex,
        CognitiveSemanticEnvelopeV2Admission admission)
        => new(
            response.Substring(0, openingIndex),
            null,
            admission,
            tailStripped: true);

    private static int FindLineStartOpening(
        string response,
        string tag,
        int searchFrom)
    {
        while (searchFrom <= response.Length - tag.Length)
        {
            var found = response.IndexOf(
                tag,
                searchFrom,
                StringComparison.Ordinal);
            if (found < 0)
                return -1;
            if (found == 0 || response[found - 1] == '\n')
                return found;
            searchFrom = found + tag.Length;
        }
        return -1;
    }

    private static int FindNextRecognizableLineStartOpening(
        string response,
        int searchFrom)
    {
        var cursor = Math.Max(0, searchFrom);
        while (cursor < response.Length)
        {
            if ((cursor == 0 || response[cursor - 1] == '\n')
                && (StartsWithAt(response, OpeningTag, cursor)
                    || StartsWithAt(response, LegacyOpeningTag, cursor)))
            {
                return cursor;
            }
            var newline = response.IndexOf('\n', cursor);
            if (newline < 0)
                return -1;
            cursor = newline + 1;
        }
        return -1;
    }

    private static bool StartsWithAt(
        string response,
        string tag,
        int index)
        => index >= 0
            && index <= response.Length - tag.Length
            && string.CompareOrdinal(
                response,
                index,
                tag,
                0,
                tag.Length) == 0;

    private static bool IsPureRecognizableFrameTail(
        string response,
        int tailStart)
    {
        var cursor = tailStart;
        while (cursor < response.Length)
        {
            string openingTag;
            string closingTag;
            if (StartsWithAt(response, OpeningTag, cursor))
            {
                openingTag = OpeningTag;
                closingTag = ClosingTag;
            }
            else if (StartsWithAt(response, LegacyOpeningTag, cursor))
            {
                openingTag = LegacyOpeningTag;
                closingTag = LegacyClosingTag;
            }
            else
            {
                return false;
            }

            var next = FindNextRecognizableLineStartOpening(
                response,
                cursor + openingTag.Length);
            var closingIndex = FindFrameClosingIndex(
                response,
                cursor,
                openingTag,
                closingTag,
                next < 0 ? response.Length : next);
            if (closingIndex < 0)
                return true;
            cursor = closingIndex + closingTag.Length;
            if (next < 0)
                return IsWhitespaceOnly(response, cursor);
            if (!IsWhitespaceOnly(response, cursor, next))
                return false;
            cursor = next;
        }
        return true;
    }

    private static int FindFrameClosingIndex(
        string response,
        int openingIndex,
        string openingTag,
        string closingTag,
        int boundary)
    {
        var contentStart = openingIndex + openingTag.Length;
        var searchFrom = contentStart;
        var lastCandidate = -1;
        var boundedEnd = (int)Math.Min(
            boundary,
            (long)openingIndex + MaximumFramedSidecarUtf16Units);
        var candidateJsonParses = 0;
        while (searchFrom <= boundedEnd - closingTag.Length
            && candidateJsonParses < MaximumClosingCandidateJsonParses)
        {
            var candidate = response.IndexOf(
                closingTag,
                searchFrom,
                boundedEnd - searchFrom,
                StringComparison.Ordinal);
            if (candidate < 0)
                break;
            lastCandidate = candidate;
            candidateJsonParses++;
            if (IsCompleteJson(
                    response,
                    contentStart,
                    candidate - contentStart))
            {
                return candidate;
            }
            searchFrom = candidate + closingTag.Length;
        }
        var fallbackEnd = boundary - 1;
        if (fallbackEnd < contentStart)
            return lastCandidate;
        var fallback = response.LastIndexOf(
            closingTag,
            fallbackEnd,
            fallbackEnd - contentStart + 1,
            StringComparison.Ordinal);
        return fallback >= contentStart
                && fallback + closingTag.Length <= boundary
            ? fallback
            : lastCandidate;
    }

    private static bool IsCompleteJson(
        string response,
        int start,
        int length)
    {
        try
        {
            using var _ = JsonDocument.Parse(
                response.Substring(start, length),
                JsonOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsWhitespaceOnly(string value, int start)
        => IsWhitespaceOnly(value, start, value.Length);

    private static bool IsWhitespaceOnly(
        string value,
        int start,
        int end)
    {
        for (var index = start; index < end; index++)
        {
            if (!char.IsWhiteSpace(value[index]))
                return false;
        }
        return true;
    }

    private static bool TryParseEnvelope(
        string json,
        out CognitiveSemanticEnvelopeV2? envelope)
    {
        envelope = null;
        try
        {
            using var document = JsonDocument.Parse(json, JsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            var names = new HashSet<string>(StringComparer.Ordinal);
            var versionSeen = false;
            var proposalsSeen = false;
            var relationsSeen = false;
            List<CognitiveSemanticEnvelopeV2Proposal>? proposals = null;
            List<CognitiveSemanticEnvelopeV2Relation>? relations = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    return false;
                switch (property.Name)
                {
                    case "v":
                        if (property.Value.ValueKind != JsonValueKind.Number
                            || !string.Equals(
                                property.Value.GetRawText(),
                                "2",
                                StringComparison.Ordinal))
                        {
                            return false;
                        }
                        versionSeen = true;
                        break;
                    case "p":
                        if (!TryParseProposals(property.Value, out proposals))
                            return false;
                        proposalsSeen = true;
                        break;
                    case "r":
                        if (!TryParseRelations(property.Value, out relations))
                            return false;
                        relationsSeen = true;
                        break;
                    default:
                        return false;
                }
            }
            if (!versionSeen
                || !proposalsSeen
                || !relationsSeen
                || proposals == null
                || relations == null)
            {
                return false;
            }
            envelope = new CognitiveSemanticEnvelopeV2(
                proposals,
                relations);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParseProposals(
        JsonElement element,
        out List<CognitiveSemanticEnvelopeV2Proposal>? proposals)
    {
        proposals = null;
        if (element.ValueKind != JsonValueKind.Array)
            return false;
        var result = new List<CognitiveSemanticEnvelopeV2Proposal>();
        foreach (var item in element.EnumerateArray())
        {
            if (result.Count == MaximumProposals
                || !TryParseProposal(item, out var proposal)
                || proposal == null)
            {
                return false;
            }
            result.Add(proposal);
        }
        proposals = result;
        return true;
    }

    private static bool TryParseProposal(
        JsonElement element,
        out CognitiveSemanticEnvelopeV2Proposal? proposal)
    {
        proposal = null;
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        CognitiveDomain? domain = null;
        CognitiveClaimKind? claim = null;
        CognitiveSubjectScope? subject = null;
        CognitiveTemporalScope? temporal = null;
        CognitiveEpistemicState? epistemic = null;
        CognitiveConfidenceBand? confidence = null;
        CognitiveShadowDisposition? disposition = null;
        CognitiveInteractionStance? stance = null;
        CognitiveFollowUpMode? followUp = null;
        CognitiveMemoryPreviewKind? memory = null;
        bool? requiresConfirmation = null;
        string? quote = null;
        List<CognitiveEvidenceKind>? metadata = null;
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
                return false;
            switch (property.Name)
            {
                case "d":
                    if (!TryParseEnum(property.Value, out domain))
                        return false;
                    break;
                case "k":
                    if (!TryParseEnum(property.Value, out claim))
                        return false;
                    break;
                case "s":
                    if (!TryParseEnum(property.Value, out subject))
                        return false;
                    break;
                case "t":
                    if (!TryParseEnum(property.Value, out temporal))
                        return false;
                    break;
                case "e":
                    if (!TryParseEnum(property.Value, out epistemic))
                        return false;
                    break;
                case "c":
                    if (!TryParseEnum(property.Value, out confidence))
                        return false;
                    break;
                case "x":
                    if (!TryParseEnum(property.Value, out disposition))
                        return false;
                    break;
                case "st":
                    if (!TryParseEnum(property.Value, out stance))
                        return false;
                    break;
                case "f":
                    if (!TryParseEnum(property.Value, out followUp))
                        return false;
                    break;
                case "m":
                    if (!TryParseEnum(property.Value, out memory))
                        return false;
                    break;
                case "rc":
                    if (property.Value.ValueKind is not JsonValueKind.True
                        and not JsonValueKind.False)
                    {
                        return false;
                    }
                    requiresConfirmation = property.Value.GetBoolean();
                    break;
                case "q":
                    if (!TryParseBoundedScalarString(
                            property.Value,
                            MaximumEvidenceQuoteUnicodeScalars,
                            out quote))
                    {
                        return false;
                    }
                    break;
                case "src":
                    if (!TryParseMetadataEvidence(
                            property.Value,
                            out metadata))
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }
        }
        if (!domain.HasValue
            || !claim.HasValue
            || !subject.HasValue
            || !temporal.HasValue
            || !epistemic.HasValue
            || !confidence.HasValue
            || !disposition.HasValue
            || !stance.HasValue
            || !followUp.HasValue
            || !memory.HasValue
            || !requiresConfirmation.HasValue
            || quote == null
            || metadata == null)
        {
            return false;
        }
        proposal = new CognitiveSemanticEnvelopeV2Proposal(
            domain.Value,
            claim.Value,
            subject.Value,
            temporal.Value,
            epistemic.Value,
            confidence.Value,
            disposition.Value,
            stance.Value,
            followUp.Value,
            memory.Value,
            requiresConfirmation.Value,
            quote,
            metadata);
        return true;
    }

    private static bool TryParseRelations(
        JsonElement element,
        out List<CognitiveSemanticEnvelopeV2Relation>? relations)
    {
        relations = null;
        if (element.ValueKind != JsonValueKind.Array)
            return false;
        var result = new List<CognitiveSemanticEnvelopeV2Relation>();
        foreach (var item in element.EnumerateArray())
        {
            if (result.Count == MaximumRelations
                || !TryParseRelation(item, out var relation)
                || relation == null)
            {
                return false;
            }
            result.Add(relation);
        }
        relations = result;
        return true;
    }

    private static bool TryParseRelation(
        JsonElement element,
        out CognitiveSemanticEnvelopeV2Relation? relation)
    {
        relation = null;
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        int? from = null;
        int? to = null;
        CognitiveRelationKind? kind = null;
        CognitiveRelationReason? reason = null;
        CognitiveRelationStrength? strength = null;
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
                return false;
            switch (property.Name)
            {
                case "a":
                    if (!TryParseNonNegativeInt(property.Value, out from))
                        return false;
                    break;
                case "b":
                    if (!TryParseNonNegativeInt(property.Value, out to))
                        return false;
                    break;
                case "k":
                    if (!TryParseEnum(property.Value, out kind)
                        || kind == CognitiveRelationKind.Blocks)
                    {
                        return false;
                    }
                    break;
                case "r":
                    if (!TryParseEnum(property.Value, out reason))
                        return false;
                    break;
                case "s":
                    if (!TryParseEnum(property.Value, out strength)
                        || strength
                            == CognitiveRelationStrength.Deterministic)
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }
        }
        if (!from.HasValue
            || !to.HasValue
            || !kind.HasValue
            || !reason.HasValue
            || !strength.HasValue)
        {
            return false;
        }
        relation = new CognitiveSemanticEnvelopeV2Relation(
            from.Value,
            to.Value,
            kind.Value,
            reason.Value,
            strength.Value);
        return true;
    }

    private static bool TryParseMetadataEvidence(
        JsonElement element,
        out List<CognitiveEvidenceKind>? evidence)
    {
        evidence = null;
        if (element.ValueKind != JsonValueKind.Array)
            return false;
        var result = new List<CognitiveEvidenceKind>();
        foreach (var item in element.EnumerateArray())
        {
            if (result.Count == MaximumMetadataEvidence
                || !TryParseEnum(item, out CognitiveEvidenceKind? kind)
                || !kind.HasValue
                || !CognitiveSemanticEnvelopeV2Policy
                    .IsMetadataEvidence(kind.Value)
                || result.Contains(kind.Value))
            {
                return false;
            }
            result.Add(kind.Value);
        }
        evidence = result;
        return true;
    }

    private static bool TryParseEnum<T>(
        JsonElement element,
        out T? value)
        where T : struct, Enum
    {
        value = null;
        if (element.ValueKind != JsonValueKind.String)
            return false;
        var text = element.GetString();
        if (text == null
            || !Enum.TryParse<T>(text, ignoreCase: false, out var parsed)
            || !Enum.IsDefined(typeof(T), parsed)
            || !string.Equals(parsed.ToString(), text, StringComparison.Ordinal))
        {
            return false;
        }
        value = parsed;
        return true;
    }

    private static bool TryParseNonNegativeInt(
        JsonElement element,
        out int? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out var parsed)
            || parsed < 0
            || !string.Equals(
                element.GetRawText(),
                parsed.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            return false;
        }
        value = parsed;
        return true;
    }

    private static bool TryParseBoundedScalarString(
        JsonElement element,
        int maximumScalars,
        out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.String)
            return false;
        var parsed = element.GetString();
        if (parsed == null
            || !TryCountUnicodeScalars(parsed, out var count)
            || count > maximumScalars)
        {
            return false;
        }
        value = parsed;
        return true;
    }

    internal static bool TryCountUnicodeScalars(
        string value,
        out int scalarCount)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));
        scalarCount = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (char.IsHighSurrogate(current))
            {
                if (index + 1 >= value.Length
                    || !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }
                index++;
            }
            else if (char.IsLowSurrogate(current))
            {
                return false;
            }
            scalarCount++;
        }
        return true;
    }
}

internal static class CognitiveSemanticEnvelopeV2Policy
{
    internal static bool IsClaimOwned(
        CognitiveDomain domain,
        CognitiveClaimKind claim)
        => domain switch
        {
            CognitiveDomain.Memory => claim is
                CognitiveClaimKind.Episode
                or CognitiveClaimKind.Fact
                or CognitiveClaimKind.Correction
                or CognitiveClaimKind.ExplicitRemember
                or CognitiveClaimKind.SubjectAttribution,
            CognitiveDomain.UserAffect =>
                claim == CognitiveClaimKind.UserAffect,
            CognitiveDomain.Boundary => claim is
                CognitiveClaimKind.Boundary
                or CognitiveClaimKind.InteractionPreference,
            CognitiveDomain.OpenLoop => claim is
                CognitiveClaimKind.OpenLoop
                or CognitiveClaimKind.OpenLoopResolution,
            CognitiveDomain.Proactive => claim is
                CognitiveClaimKind.CareIntent
                or CognitiveClaimKind.FollowUpIntent
                or CognitiveClaimKind.InteractionPreference,
            _ => false
        };

    internal static bool IsMemoryPreviewValid(
        CognitiveDomain domain,
        CognitiveClaimKind claim,
        CognitiveMemoryPreviewKind preview)
    {
        if (domain != CognitiveDomain.Memory)
            return preview == CognitiveMemoryPreviewKind.None;
        if (preview == CognitiveMemoryPreviewKind.None)
            return true;
        return claim switch
        {
            CognitiveClaimKind.Episode =>
                preview == CognitiveMemoryPreviewKind.Episode,
            CognitiveClaimKind.Fact or CognitiveClaimKind.SubjectAttribution =>
                preview == CognitiveMemoryPreviewKind.Fact,
            CognitiveClaimKind.Correction =>
                preview == CognitiveMemoryPreviewKind.Correction,
            CognitiveClaimKind.ExplicitRemember =>
                preview == CognitiveMemoryPreviewKind.ExplicitRemember,
            _ => false
        };
    }

    internal static bool RequiresStateConfirmation(CognitiveDomain domain)
        => domain is CognitiveDomain.Memory
            or CognitiveDomain.Boundary
            or CognitiveDomain.OpenLoop;

    internal static bool IsMetadataEvidence(CognitiveEvidenceKind kind)
        => kind is CognitiveEvidenceKind.MemoryWorkingSet
            or CognitiveEvidenceKind.ActiveBoundary
            or CognitiveEvidenceKind.ActiveOpenLoop
            or CognitiveEvidenceKind.CharacterProjection
            or CognitiveEvidenceKind.ProactiveSituation;

    internal static string RelationKey(
        CognitiveSemanticEnvelopeV2Relation relation)
        => string.Join(
            ":",
            relation.FromProposalIndex.ToString(CultureInfo.InvariantCulture),
            relation.ToProposalIndex.ToString(CultureInfo.InvariantCulture),
            ((int)relation.Kind).ToString(CultureInfo.InvariantCulture),
            ((int)relation.Reason).ToString(CultureInfo.InvariantCulture),
            ((int)relation.Strength).ToString(CultureInfo.InvariantCulture));
}
