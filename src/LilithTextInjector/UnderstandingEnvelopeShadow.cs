using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace LilithTextInjector;

internal enum UnderstandingEnvelopeShadowAdmission
{
    NotRequested = 0,
    NoSidecar = 1,
    NonTerminalFramePreserved = 2,
    EnvelopeObserved = 3,
    MalformedTailStripped = 4,
    OversizeTailStripped = 5
}

internal enum UnderstandingProposalKind
{
    FactProposal = 1,
    EpisodeProposal = 2,
    BoundaryProposal = 3,
    CorrectionProposal = 4,
    UserAffectProposal = 5,
    OpenLoopProposal = 6,
    FollowUpIntent = 7
}

internal enum UnderstandingProposalConfidence
{
    Unspecified = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

internal sealed class UnderstandingEnvelopeProposal
{
    internal UnderstandingProposalKind Kind { get; }
    internal string Value { get; }
    internal string Quote { get; }
    internal UnderstandingProposalConfidence Confidence { get; }

    internal UnderstandingEnvelopeProposal(
        UnderstandingProposalKind kind,
        string value,
        string quote,
        UnderstandingProposalConfidence confidence)
    {
        if (!Enum.IsDefined(typeof(UnderstandingProposalKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(
                typeof(UnderstandingProposalConfidence),
                confidence))
        {
            throw new ArgumentOutOfRangeException(nameof(confidence));
        }
        Kind = kind;
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Quote = quote ?? throw new ArgumentNullException(nameof(quote));
        Confidence = confidence;
    }
}

internal sealed class UnderstandingEnvelope
{
    internal int Version { get; }
    internal IReadOnlyList<UnderstandingEnvelopeProposal> Proposals { get; }

    internal UnderstandingEnvelope(
        int version,
        IEnumerable<UnderstandingEnvelopeProposal> proposals)
    {
        if (version != UnderstandingEnvelopeShadowParser.SchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(version));
        Version = version;
        Proposals = new ReadOnlyCollection<UnderstandingEnvelopeProposal>(
            new List<UnderstandingEnvelopeProposal>(
                proposals ?? throw new ArgumentNullException(nameof(proposals))));
    }
}

internal sealed class UnderstandingEnvelopeShadowObservation
{
    internal string ReplyText { get; }
    internal UnderstandingEnvelope? Envelope { get; }
    internal UnderstandingEnvelopeShadowAdmission Admission { get; }
    internal bool TailStripped { get; }
    internal string ReplyPrefix => ReplyText;
    internal UnderstandingEnvelopeShadowAdmission Status => Admission;
    internal IReadOnlyList<UnderstandingEnvelopeProposal> Proposals =>
        Envelope?.Proposals ?? Array.Empty<UnderstandingEnvelopeProposal>();

    internal UnderstandingEnvelopeShadowObservation(
        string replyText,
        UnderstandingEnvelope? envelope,
        UnderstandingEnvelopeShadowAdmission admission,
        bool tailStripped)
    {
        ReplyText = replyText
            ?? throw new ArgumentNullException(nameof(replyText));
        if (!Enum.IsDefined(
                typeof(UnderstandingEnvelopeShadowAdmission),
                admission))
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission == UnderstandingEnvelopeShadowAdmission.EnvelopeObserved)
            != (envelope != null))
        {
            throw new ArgumentException(
                "Only an observed envelope may carry proposals.",
                nameof(envelope));
        }
        if (tailStripped
            != (admission
                is UnderstandingEnvelopeShadowAdmission.EnvelopeObserved
                    or UnderstandingEnvelopeShadowAdmission.MalformedTailStripped
                    or UnderstandingEnvelopeShadowAdmission.OversizeTailStripped))
        {
            throw new ArgumentException(
                "Tail stripping must match the admission.",
                nameof(tailStripped));
        }
        Envelope = envelope;
        Admission = admission;
        TailStripped = tailStripped;
    }
}

internal sealed class UnderstandingEnvelopeShadowSnapshot
{
    internal long Observations { get; }
    internal long NotRequestedObservations { get; }
    internal long NoSidecarObservations { get; }
    internal long PreservedNonTerminalFrames { get; }
    internal long AcceptedEnvelopes { get; }
    internal long RejectedEnvelopes { get; }
    internal long MalformedTails { get; }
    internal long OversizeTails { get; }
    internal long AcceptedProposalCount { get; }
    internal UnderstandingEnvelopeShadowAdmission LastAdmission { get; }

    internal long ReplyMutations => 0;
    internal long StateMutations => 0;
    internal long AdditionalPromptCompositions => 0;
    internal long AdditionalProviderCalls => 0;
    internal long MemoryMutations => 0;
    internal long DecisionInvocations => 0;
    internal long ToolInvocations => 0;
    internal long ProactiveInvocations => 0;

    internal UnderstandingEnvelopeShadowSnapshot(
        long observations,
        long notRequestedObservations,
        long noSidecarObservations,
        long preservedNonTerminalFrames,
        long acceptedEnvelopes,
        long rejectedEnvelopes,
        long malformedTails,
        long oversizeTails,
        long acceptedProposalCount,
        UnderstandingEnvelopeShadowAdmission lastAdmission)
    {
        Observations = observations;
        NotRequestedObservations = notRequestedObservations;
        NoSidecarObservations = noSidecarObservations;
        PreservedNonTerminalFrames = preservedNonTerminalFrames;
        AcceptedEnvelopes = acceptedEnvelopes;
        RejectedEnvelopes = rejectedEnvelopes;
        MalformedTails = malformedTails;
        OversizeTails = oversizeTails;
        AcceptedProposalCount = acceptedProposalCount;
        LastAdmission = lastAdmission;
    }
}

internal sealed class UnderstandingEnvelopeShadowHost
{
    private readonly object _gate = new();
    private long _observations;
    private long _notRequestedObservations;
    private long _noSidecarObservations;
    private long _preservedNonTerminalFrames;
    private long _acceptedEnvelopes;
    private long _rejectedEnvelopes;
    private long _malformedTails;
    private long _oversizeTails;
    private long _acceptedProposalCount;
    private UnderstandingEnvelopeShadowAdmission _lastAdmission;

    internal UnderstandingEnvelopeShadowObservation Observe(
        string rawReply,
        bool requested)
    {
        if (rawReply == null)
            throw new ArgumentNullException(nameof(rawReply));
        var observation = requested
            ? UnderstandingEnvelopeShadowParser.Parse(rawReply)
            : new UnderstandingEnvelopeShadowObservation(
                rawReply,
                null,
                UnderstandingEnvelopeShadowAdmission.NotRequested,
                tailStripped: false);
        lock (_gate)
        {
            _observations = Increment(_observations);
            _lastAdmission = observation.Admission;
            switch (observation.Admission)
            {
                case UnderstandingEnvelopeShadowAdmission.NotRequested:
                    _notRequestedObservations = Increment(
                        _notRequestedObservations);
                    break;
                case UnderstandingEnvelopeShadowAdmission.NoSidecar:
                    _noSidecarObservations = Increment(
                        _noSidecarObservations);
                    break;
                case UnderstandingEnvelopeShadowAdmission
                    .NonTerminalFramePreserved:
                    _preservedNonTerminalFrames = Increment(
                        _preservedNonTerminalFrames);
                    break;
                case UnderstandingEnvelopeShadowAdmission.EnvelopeObserved:
                    _acceptedEnvelopes = Increment(_acceptedEnvelopes);
                    _acceptedProposalCount = Add(
                        _acceptedProposalCount,
                        observation.Envelope!.Proposals.Count);
                    break;
                case UnderstandingEnvelopeShadowAdmission
                    .MalformedTailStripped:
                    _rejectedEnvelopes = Increment(_rejectedEnvelopes);
                    _malformedTails = Increment(_malformedTails);
                    break;
                case UnderstandingEnvelopeShadowAdmission
                    .OversizeTailStripped:
                    _rejectedEnvelopes = Increment(_rejectedEnvelopes);
                    _oversizeTails = Increment(_oversizeTails);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown Understanding Envelope admission.");
            }
        }
        return observation;
    }

    internal UnderstandingEnvelopeShadowSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new UnderstandingEnvelopeShadowSnapshot(
                _observations,
                _notRequestedObservations,
                _noSidecarObservations,
                _preservedNonTerminalFrames,
                _acceptedEnvelopes,
                _rejectedEnvelopes,
                _malformedTails,
                _oversizeTails,
                _acceptedProposalCount,
                _lastAdmission);
        }
    }

    private static long Increment(long value)
        => value == long.MaxValue ? value : value + 1;

    private static long Add(long value, int increment)
        => value >= long.MaxValue - increment
            ? long.MaxValue
            : value + increment;
}

internal static class UnderstandingEnvelopeShadowProtocol
{
    internal static string BuildPromptInstruction(bool japaneseVoiceMode)
    {
        var languageInstruction = japaneseVoiceMode
            ? "The visible reply should follow the requested Japanese voice mode. "
            : string.Empty;
        return languageInstruction
            + "After the complete user-visible reply, you may append one "
            + "Shadow-only Understanding Envelope on a new line as the final "
            + "non-whitespace content. Use exactly "
            + UnderstandingEnvelopeShadowParser.OpeningTag
            + "{\"version\":1,\"proposals\":[{\"kind\":\"FactProposal\","
            + "\"value\":\"...\",\"quote\":\"\","
            + "\"confidence\":\"Unspecified\"}]}"
            + UnderstandingEnvelopeShadowParser.ClosingTag
            + ". The only kind values are FactProposal, EpisodeProposal, "
            + "BoundaryProposal, CorrectionProposal, UserAffectProposal, "
            + "OpenLoopProposal and FollowUpIntent. The only confidence "
            + "values are Unspecified, Low, Medium and High. Include at most "
            + "4 proposals; value is at most 192 Unicode scalars and quote is "
            + "at most 96. Keep the complete framed sidecar within 2048 UTF-16 "
            + "units and JSON depth at most 6. kind and value are required; "
            + "quote may be omitted "
            + "as empty and confidence may be omitted as Unspecified. Use no "
            + "unknown fields. Omit the entire envelope when unnecessary, and "
            + "never shorten or damage the visible reply to include it. If a "
            + "plain motion suffix exists, place that suffix before this "
            + "Understanding Envelope. In Japanese JSON mode, this framed "
            + "sidecar is the sole machine exception to the rule forbidding "
            + "content outside JSON. Its proposals have no authority and cause "
            + "no second Prompt composition, additional Provider call, Memory "
            + "or "
            + "Decision mutation, tool call, D-036 action or proactive action.";
    }
}

internal static class UnderstandingEnvelopeShadowParser
{
    internal const int SchemaVersion = 1;
    internal const int MaximumFramedSidecarUtf16Units = 2048;
    internal const int MaximumProposals = 4;
    internal const int MaximumValueUnicodeScalars = 192;
    internal const int MaximumQuoteUnicodeScalars = 96;
    internal const int MaximumJsonDepth = 6;
    internal const string OpeningTag = "<lilith_understanding_v1>";
    internal const string ClosingTag = "</lilith_understanding_v1>";

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumJsonDepth
    };

    internal static UnderstandingEnvelopeShadowObservation Parse(
        string response)
    {
        if (response == null)
            throw new ArgumentNullException(nameof(response));

        var openingIndex = FindLineStartOpening(response, 0);
        if (openingIndex < 0)
            return Preserved(response, UnderstandingEnvelopeShadowAdmission.NoSidecar);

        if (FindLineStartOpening(
                response,
                openingIndex + OpeningTag.Length) >= 0)
        {
            return Stripped(
                response,
                openingIndex,
                UnderstandingEnvelopeShadowAdmission.MalformedTailStripped);
        }

        var jsonStart = openingIndex + OpeningTag.Length;
        var closingIndex = response.LastIndexOf(
            ClosingTag,
            StringComparison.Ordinal);
        if (closingIndex < jsonStart)
        {
            return Stripped(
                response,
                openingIndex,
                response.Length - openingIndex
                    > MaximumFramedSidecarUtf16Units
                        ? UnderstandingEnvelopeShadowAdmission
                            .OversizeTailStripped
                        : UnderstandingEnvelopeShadowAdmission
                            .MalformedTailStripped);
        }

        var closingEnd = closingIndex + ClosingTag.Length;
        if (!IsWhitespaceOnly(response, closingEnd))
        {
            return Preserved(
                response,
                UnderstandingEnvelopeShadowAdmission
                    .NonTerminalFramePreserved);
        }

        if (closingEnd - openingIndex > MaximumFramedSidecarUtf16Units)
        {
            return Stripped(
                response,
                openingIndex,
                UnderstandingEnvelopeShadowAdmission.OversizeTailStripped);
        }

        var json = response.Substring(
            jsonStart,
            closingIndex - jsonStart);
        if (!TryParseEnvelope(json, out var envelope) || envelope == null)
        {
            return Stripped(
                response,
                openingIndex,
                UnderstandingEnvelopeShadowAdmission.MalformedTailStripped);
        }

        return new UnderstandingEnvelopeShadowObservation(
            response.Substring(0, openingIndex),
            envelope,
            UnderstandingEnvelopeShadowAdmission.EnvelopeObserved,
            tailStripped: true);
    }

    private static UnderstandingEnvelopeShadowObservation Preserved(
        string response,
        UnderstandingEnvelopeShadowAdmission admission)
        => new(response, null, admission, tailStripped: false);

    private static UnderstandingEnvelopeShadowObservation Stripped(
        string response,
        int openingIndex,
        UnderstandingEnvelopeShadowAdmission admission)
        => new(
            response.Substring(0, openingIndex),
            null,
            admission,
            tailStripped: true);

    private static int FindLineStartOpening(string response, int searchFrom)
    {
        while (searchFrom <= response.Length - OpeningTag.Length)
        {
            var found = response.IndexOf(
                OpeningTag,
                searchFrom,
                StringComparison.Ordinal);
            if (found < 0)
                break;
            if (found == 0 || response[found - 1] == '\n')
                return found;
            searchFrom = found + OpeningTag.Length;
        }
        return -1;
    }

    private static bool IsWhitespaceOnly(string value, int start)
    {
        for (var index = start; index < value.Length; index++)
        {
            if (!char.IsWhiteSpace(value[index]))
                return false;
        }
        return true;
    }

    private static bool TryParseEnvelope(
        string json,
        out UnderstandingEnvelope? envelope)
    {
        envelope = null;
        try
        {
            using var document = JsonDocument.Parse(json, JsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var rootNames = new HashSet<string>(StringComparer.Ordinal);
            var versionSeen = false;
            var proposalsSeen = false;
            List<UnderstandingEnvelopeProposal>? proposals = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!rootNames.Add(property.Name))
                    return false;
                switch (property.Name)
                {
                    case "version":
                        if (property.Value.ValueKind != JsonValueKind.Number
                            || !string.Equals(
                                property.Value.GetRawText(),
                                "1",
                                StringComparison.Ordinal))
                        {
                            return false;
                        }
                        versionSeen = true;
                        break;
                    case "proposals":
                        if (!TryParseProposals(property.Value, out proposals))
                            return false;
                        proposalsSeen = true;
                        break;
                    default:
                        return false;
                }
            }
            if (!versionSeen || !proposalsSeen || proposals == null)
                return false;
            envelope = new UnderstandingEnvelope(SchemaVersion, proposals);
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
        out List<UnderstandingEnvelopeProposal>? proposals)
    {
        proposals = null;
        if (element.ValueKind != JsonValueKind.Array)
            return false;
        var result = new List<UnderstandingEnvelopeProposal>();
        foreach (var proposalElement in element.EnumerateArray())
        {
            if (result.Count == MaximumProposals
                || !TryParseProposal(proposalElement, out var proposal)
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
        out UnderstandingEnvelopeProposal? proposal)
    {
        proposal = null;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        var names = new HashSet<string>(StringComparer.Ordinal);
        UnderstandingProposalKind? kind = null;
        var confidence = UnderstandingProposalConfidence.Unspecified;
        string? value = null;
        var quote = string.Empty;
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
                return false;
            switch (property.Name)
            {
                case "kind":
                    if (!TryParseKind(property.Value, out var parsedKind))
                        return false;
                    kind = parsedKind;
                    break;
                case "value":
                    if (!TryParseBoundedScalarString(
                            property.Value,
                            MaximumValueUnicodeScalars,
                            allowEmpty: false,
                            out value))
                    {
                        return false;
                    }
                    break;
                case "quote":
                    if (!TryParseBoundedScalarString(
                            property.Value,
                            MaximumQuoteUnicodeScalars,
                            allowEmpty: true,
                            out quote))
                    {
                        return false;
                    }
                    break;
                case "confidence":
                    if (!TryParseConfidence(
                            property.Value,
                            out var parsedConfidence))
                    {
                        return false;
                    }
                    confidence = parsedConfidence;
                    break;
                default:
                    return false;
            }
        }
        if (!kind.HasValue
            || value == null)
        {
            return false;
        }
        proposal = new UnderstandingEnvelopeProposal(
            kind.Value,
            value,
            quote ?? string.Empty,
            confidence);
        return true;
    }

    private static bool TryParseKind(
        JsonElement element,
        out UnderstandingProposalKind kind)
    {
        kind = default;
        if (element.ValueKind != JsonValueKind.String)
            return false;
        return element.GetString() switch
        {
            "FactProposal" => Set(
                UnderstandingProposalKind.FactProposal,
                out kind),
            "EpisodeProposal" => Set(
                UnderstandingProposalKind.EpisodeProposal,
                out kind),
            "BoundaryProposal" => Set(
                UnderstandingProposalKind.BoundaryProposal,
                out kind),
            "CorrectionProposal" => Set(
                UnderstandingProposalKind.CorrectionProposal,
                out kind),
            "UserAffectProposal" => Set(
                UnderstandingProposalKind.UserAffectProposal,
                out kind),
            "OpenLoopProposal" => Set(
                UnderstandingProposalKind.OpenLoopProposal,
                out kind),
            "FollowUpIntent" => Set(
                UnderstandingProposalKind.FollowUpIntent,
                out kind),
            _ => false
        };
    }

    private static bool TryParseConfidence(
        JsonElement element,
        out UnderstandingProposalConfidence confidence)
    {
        confidence = default;
        if (element.ValueKind != JsonValueKind.String)
            return false;
        return element.GetString() switch
        {
            "Unspecified" => Set(
                UnderstandingProposalConfidence.Unspecified,
                out confidence),
            "Low" => Set(
                UnderstandingProposalConfidence.Low,
                out confidence),
            "Medium" => Set(
                UnderstandingProposalConfidence.Medium,
                out confidence),
            "High" => Set(
                UnderstandingProposalConfidence.High,
                out confidence),
            _ => false
        };
    }

    private static bool Set<T>(T value, out T destination)
    {
        destination = value;
        return true;
    }

    private static bool TryParseBoundedScalarString(
        JsonElement element,
        int maximumScalars,
        bool allowEmpty,
        out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.String)
            return false;
        var parsed = element.GetString();
        if (parsed == null
            || !TryCountUnicodeScalars(parsed, out var scalarCount)
            || scalarCount > maximumScalars
            || (!allowEmpty && scalarCount == 0))
        {
            return false;
        }
        value = parsed;
        return true;
    }

    private static bool TryCountUnicodeScalars(
        string value,
        out int scalarCount)
    {
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
