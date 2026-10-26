using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum SemanticIntentInputOrigin
{
    FixedCorpusReplay = 1,
    SyntheticReplay = 2,
    SemanticInference = 3
}

internal enum SemanticIntentProducerOrigin
{
    FixedCorpusReplay = 1,
    SyntheticReplay = 2,
    SemanticInference = 3
}

internal enum SemanticConversationRole
{
    User = 1,
    Assistant = 2
}

internal enum SemanticPurposeAxis
{
    Converse = 1,
    Recall = 2,
    Explain = 3,
    Research = 4,
    Act = 5,
    Manage = 6,
    Ambiguous = 7
}

internal enum SemanticFreshnessAxis
{
    None = 0,
    Stable = 1,
    Current = 2,
    Future = 3,
    Unknown = 4
}

internal enum SemanticEffectAxis
{
    LanguageOnly = 1,
    ObservationRequested = 2,
    ActionRequested = 3,
    ChangeRequested = 4,
    Unknown = 5
}

internal enum SemanticTargetAxis
{
    None = 0,
    Self = 1,
    User = 2,
    Memory = 3,
    PublicInformation = 4,
    Application = 5,
    Document = 6,
    System = 7,
    Unknown = 8
}

internal enum SemanticContextKind
{
    ConversationWindow = 1,
    MemoryProjection = 2,
    LocalObservation = 3,
    ActiveBoundary = 4,
    ActiveOpenLoop = 5,
    OpaqueExternalTarget = 6
}

internal enum SemanticAmbiguityKind
{
    None = 0,
    MissingReferent = 1,
    CompetingIntent = 2,
    InsufficientContext = 3,
    UnresolvedDescriptor = 4
}

internal enum SemanticEvidenceRole
{
    IntentCue = 1,
    TargetCue = 2,
    ContextCue = 3,
    NegationCue = 4,
    TemporalCue = 5
}

internal enum SemanticTargetReferenceKind
{
    None = 0,
    OpaqueUnresolved = 1
}

internal enum SemanticIntentConfidenceBand
{
    Unspecified = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

internal enum SemanticIntentShadowAdmission
{
    Projected = 1,
    Ambiguous = 2,
    Abstained = 3,
    Expired = 4
}

internal enum SemanticIntentAuthority
{
    ShadowOnly = 1
}

internal enum SemanticExperienceSignalKind
{
    FixedCorpusComparison = 1,
    PlayerCorrection = 2,
    VerifiedOutcome = 3,
    EvidenceResult = 4,
    ConversationContinuation = 5,
    PolicyRejection = 6
}

internal enum SemanticExperienceAssessment
{
    Supports = 1,
    Contradicts = 2,
    Inconclusive = 3
}

internal enum SemanticAdaptationDimension
{
    ConfidenceCalibration = 1,
    IntentRanking = 2,
    DescriptorRanking = 3
}

internal enum SemanticAdaptationShadowAdmission
{
    EligibleForShadowReview = 1,
    RegressionRejected = 2,
    EvidenceRejected = 3
}

internal enum SemanticIntentComparisonDifference
{
    Admission = 1,
    ProjectionCount = 2,
    Descriptor = 3,
    Purpose = 4,
    Freshness = 5,
    Effect = 6,
    Target = 7,
    Ambiguity = 8,
    TargetReference = 9,
    Confidence = 10
}

internal sealed class SemanticIntentComparisonMismatch
{
    internal SemanticIntentComparisonDifference Kind { get; }
    internal string Fingerprint { get; }

    internal SemanticIntentComparisonMismatch(
        SemanticIntentComparisonDifference kind,
        params object?[] exactExpectedAndActual)
    {
        Kind = SemanticIntentContractGuard.Enum(kind, nameof(kind));
        if (exactExpectedAndActual == null
            || exactExpectedAndActual.Length == 0)
        {
            throw new ArgumentException(
                "A comparison mismatch must bind exact expected and actual values.",
                nameof(exactExpectedAndActual));
        }
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            Kind,
            SemanticIntentFingerprint.Canonical(exactExpectedAndActual));
    }
}

internal sealed class SemanticConversationTurn
{
    internal string TurnId { get; }
    internal int Ordinal { get; }
    internal SemanticConversationRole Role { get; }
    internal string Text { get; }
    internal string TextFingerprint { get; }
    internal int UnicodeScalarCount { get; }

    internal SemanticConversationTurn(
        string turnId,
        int ordinal,
        SemanticConversationRole role,
        string text)
    {
        TurnId = SemanticIntentContractGuard.Identifier(
            turnId,
            nameof(turnId));
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        Ordinal = ordinal;
        Role = SemanticIntentContractGuard.Enum(role, nameof(role));
        Text = SemanticIntentContractGuard.Text(
            text,
            SemanticIntentInputSnapshot.MaximumTurnUnicodeScalars,
            nameof(text));
        UnicodeScalarCount = Text.EnumerateRunes().Count();
        TextFingerprint = SemanticIntentFingerprint.Hash(Text);
    }
}

internal sealed class SemanticContextReference
{
    internal string ContextId { get; }
    internal SemanticContextKind Kind { get; }
    internal string SourceFingerprint { get; }
    internal long ObservedAtMonotonicTicks { get; }
    internal long ExpiresAtMonotonicTicks { get; }
    internal string Fingerprint { get; }

    internal SemanticContextReference(
        string contextId,
        SemanticContextKind kind,
        string sourceFingerprint,
        long observedAtMonotonicTicks,
        long expiresAtMonotonicTicks)
    {
        ContextId = SemanticIntentContractGuard.Identifier(
            contextId,
            nameof(contextId));
        Kind = SemanticIntentContractGuard.Enum(kind, nameof(kind));
        SourceFingerprint = SemanticIntentContractGuard.Fingerprint(
            sourceFingerprint,
            nameof(sourceFingerprint));
        SemanticIntentContractGuard.Lifetime(
            observedAtMonotonicTicks,
            expiresAtMonotonicTicks);
        ObservedAtMonotonicTicks = observedAtMonotonicTicks;
        ExpiresAtMonotonicTicks = expiresAtMonotonicTicks;
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            ContextId,
            Kind,
            SourceFingerprint,
            ObservedAtMonotonicTicks,
            ExpiresAtMonotonicTicks);
    }
}

internal sealed class SemanticRegistryDescriptor
{
    internal const int MaximumRequiredContexts = 4;

    internal string DescriptorId { get; }
    internal int DescriptorVersion { get; }
    internal SemanticPurposeAxis Purpose { get; }
    internal SemanticFreshnessAxis Freshness { get; }
    internal SemanticEffectAxis Effect { get; }
    internal SemanticTargetAxis Target { get; }
    internal IReadOnlyList<SemanticContextKind> RequiredContexts { get; }
    internal bool AdaptiveRankingAllowed { get; }
    internal string Fingerprint { get; }

    internal SemanticRegistryDescriptor(
        string descriptorId,
        int descriptorVersion,
        SemanticPurposeAxis purpose,
        SemanticFreshnessAxis freshness,
        SemanticEffectAxis effect,
        SemanticTargetAxis target,
        IEnumerable<SemanticContextKind> requiredContexts,
        bool adaptiveRankingAllowed)
    {
        DescriptorId = SemanticIntentContractGuard.Identifier(
            descriptorId,
            nameof(descriptorId));
        DescriptorVersion = SemanticIntentContractGuard.Positive(
            descriptorVersion,
            nameof(descriptorVersion));
        Purpose = SemanticIntentContractGuard.Enum(purpose, nameof(purpose));
        Freshness = SemanticIntentContractGuard.Enum(
            freshness,
            nameof(freshness));
        Effect = SemanticIntentContractGuard.Enum(effect, nameof(effect));
        Target = SemanticIntentContractGuard.Enum(target, nameof(target));
        AdaptiveRankingAllowed = adaptiveRankingAllowed;

        var contextItems = (requiredContexts
                ?? throw new ArgumentNullException(nameof(requiredContexts)))
            .Take(MaximumRequiredContexts + 1)
            .Select(value => SemanticIntentContractGuard.Enum(
                value,
                nameof(requiredContexts)))
            .ToArray();
        if (contextItems.Length > MaximumRequiredContexts)
        {
            throw new ArgumentException(
                "A descriptor has too many context requirements.",
                nameof(requiredContexts));
        }
        var contexts = contextItems
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        RequiredContexts = new ReadOnlyCollection<SemanticContextKind>(
            contexts);
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            DescriptorId,
            DescriptorVersion,
            Purpose,
            Freshness,
            Effect,
            Target,
            SemanticIntentFingerprint.CanonicalSequence(
                contexts.Select(value => value.ToString())),
            AdaptiveRankingAllowed);
    }

    internal string Key => DescriptorId + "@"
        + DescriptorVersion.ToString(CultureInfo.InvariantCulture);
}

internal sealed class SemanticIntentRegistrySnapshot
{
    internal const int MaximumDescriptors = 64;

    private readonly IReadOnlyDictionary<string, SemanticRegistryDescriptor>
        _byKey;

    internal string RegistryId { get; }
    internal long Generation { get; }
    internal IReadOnlyList<SemanticRegistryDescriptor> Descriptors { get; }
    internal string Fingerprint { get; }

    internal SemanticIntentRegistrySnapshot(
        string registryId,
        long generation,
        IEnumerable<SemanticRegistryDescriptor> descriptors)
    {
        RegistryId = SemanticIntentContractGuard.Identifier(
            registryId,
            nameof(registryId));
        Generation = SemanticIntentContractGuard.Positive(
            generation,
            nameof(generation));
        var items = (descriptors
                ?? throw new ArgumentNullException(nameof(descriptors)))
            .Take(MaximumDescriptors + 1)
            .ToArray();
        if (items.Length == 0
            || items.Length > MaximumDescriptors
            || items.Any(item => item == null)
            || items.Select(item => item.DescriptorId)
                .Distinct(StringComparer.Ordinal)
                .Count() != items.Length)
        {
            throw new ArgumentException(
                "An active registry must have one bounded version per descriptor.",
                nameof(descriptors));
        }
        var canonical = items
            .OrderBy(item => item.DescriptorId, StringComparer.Ordinal)
            .ThenBy(item => item.DescriptorVersion)
            .ToArray();
        Descriptors = new ReadOnlyCollection<SemanticRegistryDescriptor>(
            canonical);
        _byKey = new ReadOnlyDictionary<string, SemanticRegistryDescriptor>(
            canonical.ToDictionary(
                item => item.Key,
                item => item,
                StringComparer.Ordinal));
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            RegistryId,
            Generation,
            SemanticIntentFingerprint.CanonicalSequence(
                canonical.Select(item => item.Fingerprint)));
    }

    internal SemanticRegistryDescriptor? Find(
        string descriptorId,
        int descriptorVersion)
    {
        var key = SemanticIntentContractGuard.Identifier(
                descriptorId,
                nameof(descriptorId))
            + "@"
            + SemanticIntentContractGuard.Positive(
                descriptorVersion,
                nameof(descriptorVersion))
                .ToString(CultureInfo.InvariantCulture);
        return _byKey.TryGetValue(key, out var descriptor)
            ? descriptor
            : null;
    }
}

internal sealed class SemanticIntentInputSnapshot
{
    internal const int MaximumTurns = 6;
    internal const int MaximumContexts = 8;
    internal const int MaximumTurnUnicodeScalars = 512;
    internal const int MaximumTotalTurnUnicodeScalars = 2048;

    private readonly IReadOnlyDictionary<string, SemanticConversationTurn>
        _turnsById;
    private readonly IReadOnlyDictionary<string, SemanticContextReference>
        _contextsById;

    internal string SnapshotId { get; }
    internal SemanticIntentInputOrigin Origin { get; }
    internal string RuntimeId { get; }
    internal string SessionId { get; }
    internal string OperationId { get; }
    internal long RegistryGeneration { get; }
    internal string RegistryFingerprint { get; }
    internal IReadOnlyList<SemanticConversationTurn> Turns { get; }
    internal IReadOnlyList<SemanticContextReference> Contexts { get; }
    internal string CurrentTurnId { get; }
    internal long CapturedAtMonotonicTicks { get; }
    internal long ExpiresAtMonotonicTicks { get; }
    internal string ScenarioFingerprint { get; }
    internal string Fingerprint { get; }

    internal SemanticIntentInputSnapshot(
        string snapshotId,
        SemanticIntentInputOrigin origin,
        string runtimeId,
        string sessionId,
        string operationId,
        long registryGeneration,
        string registryFingerprint,
        IEnumerable<SemanticConversationTurn> turns,
        string currentTurnId,
        IEnumerable<SemanticContextReference> contexts,
        long capturedAtMonotonicTicks,
        long expiresAtMonotonicTicks)
    {
        SnapshotId = SemanticIntentContractGuard.Identifier(
            snapshotId,
            nameof(snapshotId));
        Origin = SemanticIntentContractGuard.Enum(origin, nameof(origin));
        RuntimeId = SemanticIntentContractGuard.Identifier(
            runtimeId,
            nameof(runtimeId));
        SessionId = SemanticIntentContractGuard.Identifier(
            sessionId,
            nameof(sessionId));
        OperationId = SemanticIntentContractGuard.Identifier(
            operationId,
            nameof(operationId));
        RegistryGeneration = SemanticIntentContractGuard.Positive(
            registryGeneration,
            nameof(registryGeneration));
        RegistryFingerprint = SemanticIntentContractGuard.Fingerprint(
            registryFingerprint,
            nameof(registryFingerprint));
        CurrentTurnId = SemanticIntentContractGuard.Identifier(
            currentTurnId,
            nameof(currentTurnId));
        SemanticIntentContractGuard.Lifetime(
            capturedAtMonotonicTicks,
            expiresAtMonotonicTicks);
        CapturedAtMonotonicTicks = capturedAtMonotonicTicks;
        ExpiresAtMonotonicTicks = expiresAtMonotonicTicks;

        var turnItems = (turns
                ?? throw new ArgumentNullException(nameof(turns)))
            .Take(MaximumTurns + 1)
            .ToArray();
        if (turnItems.Length == 0
            || turnItems.Length > MaximumTurns
            || turnItems.Any(item => item == null)
            || turnItems.Select(item => item.TurnId)
                .Distinct(StringComparer.Ordinal)
                .Count() != turnItems.Length
            || turnItems.Select(item => item.Ordinal)
                .Distinct()
                .Count() != turnItems.Length)
        {
            throw new ArgumentException(
                "Turns must be non-empty, bounded and uniquely identified.",
                nameof(turns));
        }
        var orderedTurns = turnItems.OrderBy(item => item.Ordinal).ToArray();
        if (orderedTurns
                .Select((item, index) => item.Ordinal == index)
                .Any(matches => !matches)
            || orderedTurns[^1].TurnId != CurrentTurnId
            || orderedTurns[^1].Role != SemanticConversationRole.User
            || orderedTurns.Sum(item => item.UnicodeScalarCount)
                > MaximumTotalTurnUnicodeScalars)
        {
            throw new ArgumentException(
                "Turns must be contiguous, bounded and end at the current user turn.",
                nameof(turns));
        }
        Turns = new ReadOnlyCollection<SemanticConversationTurn>(orderedTurns);
        _turnsById = new ReadOnlyDictionary<string, SemanticConversationTurn>(
            orderedTurns.ToDictionary(
                item => item.TurnId,
                item => item,
                StringComparer.Ordinal));

        var contextItems = (contexts
                ?? throw new ArgumentNullException(nameof(contexts)))
            .Take(MaximumContexts + 1)
            .ToArray();
        if (contextItems.Length > MaximumContexts
            || contextItems.Any(item => item == null)
            || contextItems.Select(item => item.ContextId)
                .Distinct(StringComparer.Ordinal)
                .Count() != contextItems.Length)
        {
            throw new ArgumentException(
                "Context references must be bounded and unique.",
                nameof(contexts));
        }
        var orderedContexts = contextItems
            .OrderBy(item => item.ContextId, StringComparer.Ordinal)
            .ToArray();
        if (orderedContexts.Any(item =>
                item.ObservedAtMonotonicTicks > CapturedAtMonotonicTicks
                || item.ExpiresAtMonotonicTicks < ExpiresAtMonotonicTicks))
        {
            throw new ArgumentException(
                "Every context must cover the complete frozen input lifetime.",
                nameof(contexts));
        }
        Contexts = new ReadOnlyCollection<SemanticContextReference>(
            orderedContexts);
        _contextsById = new ReadOnlyDictionary<string, SemanticContextReference>(
            orderedContexts.ToDictionary(
                item => item.ContextId,
                item => item,
                StringComparer.Ordinal));

        ScenarioFingerprint = SemanticIntentFingerprint.HashCanonical(
            RegistryFingerprint,
            SemanticIntentFingerprint.CanonicalSequence(
                orderedTurns.Select(item => SemanticIntentFingerprint.Canonical(
                    item.Ordinal,
                    item.Role,
                    item.TextFingerprint))),
            SemanticIntentFingerprint.CanonicalSequence(
                orderedContexts.Select(item =>
                    SemanticIntentFingerprint.Canonical(
                        item.Kind,
                        item.SourceFingerprint))));

        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            SnapshotId,
            Origin,
            RuntimeId,
            SessionId,
            OperationId,
            RegistryGeneration,
            RegistryFingerprint,
            CurrentTurnId,
            CapturedAtMonotonicTicks,
            ExpiresAtMonotonicTicks,
            ScenarioFingerprint,
            SemanticIntentFingerprint.CanonicalSequence(
                orderedTurns.Select(item => SemanticIntentFingerprint.Canonical(
                    item.TurnId,
                    item.Ordinal,
                    item.Role,
                    item.TextFingerprint))),
            SemanticIntentFingerprint.CanonicalSequence(
                orderedContexts.Select(item => item.Fingerprint)));
    }

    internal SemanticConversationTurn? FindTurn(string turnId)
        => _turnsById.TryGetValue(turnId, out var turn) ? turn : null;

    internal SemanticContextReference? FindContext(string contextId)
        => _contextsById.TryGetValue(contextId, out var context)
            ? context
            : null;
}

internal sealed class SemanticEvidenceSpan
{
    internal string TurnId { get; }
    internal int ScalarStart { get; }
    internal int ScalarLength { get; }
    internal SemanticEvidenceRole Role { get; }
    internal string Fingerprint { get; }

    internal SemanticEvidenceSpan(
        string turnId,
        int scalarStart,
        int scalarLength,
        SemanticEvidenceRole role)
    {
        TurnId = SemanticIntentContractGuard.Identifier(
            turnId,
            nameof(turnId));
        if (scalarStart < 0)
            throw new ArgumentOutOfRangeException(nameof(scalarStart));
        if (scalarLength <= 0
            || scalarLength > SemanticIntentInputSnapshot.MaximumTurnUnicodeScalars)
        {
            throw new ArgumentOutOfRangeException(nameof(scalarLength));
        }
        ScalarStart = scalarStart;
        ScalarLength = scalarLength;
        Role = SemanticIntentContractGuard.Enum(role, nameof(role));
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            TurnId,
            ScalarStart,
            ScalarLength,
            Role);
    }
}

internal sealed class SemanticTargetReference
{
    internal SemanticTargetReferenceKind Kind { get; }
    internal string EvidenceFingerprint { get; }
    internal string Fingerprint { get; }

    private SemanticTargetReference(
        SemanticTargetReferenceKind kind,
        string evidenceFingerprint)
    {
        Kind = SemanticIntentContractGuard.Enum(kind, nameof(kind));
        EvidenceFingerprint = evidenceFingerprint == "NONE"
            ? evidenceFingerprint
            : SemanticIntentContractGuard.Fingerprint(
                evidenceFingerprint,
                nameof(evidenceFingerprint));
        if ((Kind == SemanticTargetReferenceKind.None)
            != (EvidenceFingerprint == "NONE"))
        {
            throw new ArgumentException(
                "Only an unresolved target may carry an evidence fingerprint.",
                nameof(evidenceFingerprint));
        }
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            Kind,
            EvidenceFingerprint);
    }

    internal static SemanticTargetReference None()
        => new(SemanticTargetReferenceKind.None, "NONE");

    internal static SemanticTargetReference OpaqueUnresolved(
        SemanticEvidenceSpan targetEvidence)
    {
        if (targetEvidence == null)
            throw new ArgumentNullException(nameof(targetEvidence));
        if (targetEvidence.Role != SemanticEvidenceRole.TargetCue)
        {
            throw new ArgumentException(
                "An opaque target must bind an exact target evidence span.",
                nameof(targetEvidence));
        }
        return new SemanticTargetReference(
            SemanticTargetReferenceKind.OpaqueUnresolved,
            targetEvidence.Fingerprint);
    }
}

internal sealed class SemanticDescriptorReference
{
    internal string DescriptorId { get; }
    internal int DescriptorVersion { get; }
    internal int Rank { get; }
    internal string Fingerprint { get; }

    internal SemanticDescriptorReference(
        string descriptorId,
        int descriptorVersion,
        int rank)
    {
        DescriptorId = SemanticIntentContractGuard.Identifier(
            descriptorId,
            nameof(descriptorId));
        DescriptorVersion = SemanticIntentContractGuard.Positive(
            descriptorVersion,
            nameof(descriptorVersion));
        if (rank <= 0 || rank > SemanticIntentProposal.MaximumDescriptors)
            throw new ArgumentOutOfRangeException(nameof(rank));
        Rank = rank;
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            DescriptorId,
            DescriptorVersion,
            Rank);
    }

    internal string Key => DescriptorId + "@"
        + DescriptorVersion.ToString(CultureInfo.InvariantCulture);
}

internal sealed class SemanticIntentProposal
{
    internal const int MaximumDescriptors = 4;
    internal const int MaximumContextReferences = 4;
    internal const int MaximumEvidenceSpans = 8;

    internal string ProposalId { get; }
    internal int Rank { get; }
    internal string InputFingerprint { get; }
    internal long RegistryGeneration { get; }
    internal string RegistryFingerprint { get; }
    internal SemanticPurposeAxis Purpose { get; }
    internal SemanticFreshnessAxis Freshness { get; }
    internal SemanticEffectAxis Effect { get; }
    internal SemanticTargetAxis Target { get; }
    internal SemanticAmbiguityKind Ambiguity { get; }
    internal SemanticIntentConfidenceBand Confidence { get; }
    internal SemanticTargetReference TargetReference { get; }
    internal IReadOnlyList<SemanticDescriptorReference> Descriptors { get; }
    internal IReadOnlyList<string> ContextReferenceIds { get; }
    internal IReadOnlyList<SemanticEvidenceSpan> EvidenceSpans { get; }
    internal string Fingerprint { get; }

    internal SemanticIntentProposal(
        string proposalId,
        int rank,
        string inputFingerprint,
        long registryGeneration,
        string registryFingerprint,
        SemanticPurposeAxis purpose,
        SemanticFreshnessAxis freshness,
        SemanticEffectAxis effect,
        SemanticTargetAxis target,
        SemanticAmbiguityKind ambiguity,
        SemanticIntentConfidenceBand confidence,
        SemanticTargetReference targetReference,
        IEnumerable<SemanticDescriptorReference> descriptors,
        IEnumerable<string> contextReferenceIds,
        IEnumerable<SemanticEvidenceSpan> evidenceSpans)
    {
        ProposalId = SemanticIntentContractGuard.Identifier(
            proposalId,
            nameof(proposalId));
        if (rank <= 0 || rank > SemanticIntentProposalBatch.MaximumProposals)
            throw new ArgumentOutOfRangeException(nameof(rank));
        Rank = rank;
        InputFingerprint = SemanticIntentContractGuard.Fingerprint(
            inputFingerprint,
            nameof(inputFingerprint));
        RegistryGeneration = SemanticIntentContractGuard.Positive(
            registryGeneration,
            nameof(registryGeneration));
        RegistryFingerprint = SemanticIntentContractGuard.Fingerprint(
            registryFingerprint,
            nameof(registryFingerprint));
        Purpose = SemanticIntentContractGuard.Enum(purpose, nameof(purpose));
        Freshness = SemanticIntentContractGuard.Enum(
            freshness,
            nameof(freshness));
        Effect = SemanticIntentContractGuard.Enum(effect, nameof(effect));
        Target = SemanticIntentContractGuard.Enum(target, nameof(target));
        Ambiguity = SemanticIntentContractGuard.Enum(
            ambiguity,
            nameof(ambiguity));
        Confidence = SemanticIntentContractGuard.Enum(
            confidence,
            nameof(confidence));
        TargetReference = targetReference
            ?? throw new ArgumentNullException(nameof(targetReference));

        var descriptorItems = (descriptors
                ?? throw new ArgumentNullException(nameof(descriptors)))
            .Take(MaximumDescriptors + 1)
            .ToArray();
        if (descriptorItems.Length == 0
            || descriptorItems.Length > MaximumDescriptors
            || descriptorItems.Any(item => item == null)
            || descriptorItems.Select(item => item.DescriptorId)
                .Distinct(StringComparer.Ordinal)
                .Count() != descriptorItems.Length
            || descriptorItems.Select(item => item.Rank).Distinct().Count()
                != descriptorItems.Length
            || descriptorItems.Min(item => item.Rank) != 1)
        {
            throw new ArgumentException(
                "Descriptor references must be bounded, unique and start at rank one.",
                nameof(descriptors));
        }
        var orderedDescriptors = descriptorItems
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.DescriptorId, StringComparer.Ordinal)
            .ToArray();
        if (orderedDescriptors
            .Select((item, index) => item.Rank == index + 1)
            .Any(matches => !matches))
        {
            throw new ArgumentException(
                "Descriptor ranks must be contiguous.",
                nameof(descriptors));
        }
        Descriptors = new ReadOnlyCollection<SemanticDescriptorReference>(
            orderedDescriptors);

        var contextItems = (contextReferenceIds
                ?? throw new ArgumentNullException(nameof(contextReferenceIds)))
            .Select(value => SemanticIntentContractGuard.Identifier(
                value,
                nameof(contextReferenceIds)))
            .Take(MaximumContextReferences + 1)
            .ToArray();
        if (contextItems.Length > MaximumContextReferences
            || contextItems.Distinct(StringComparer.Ordinal).Count()
                != contextItems.Length)
        {
            throw new ArgumentException(
                "Context references must be bounded and unique.",
                nameof(contextReferenceIds));
        }
        var orderedContextItems = contextItems
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        ContextReferenceIds = new ReadOnlyCollection<string>(
            orderedContextItems);

        var evidenceItems = (evidenceSpans
                ?? throw new ArgumentNullException(nameof(evidenceSpans)))
            .Take(MaximumEvidenceSpans + 1)
            .ToArray();
        if (evidenceItems.Length == 0
            || evidenceItems.Length > MaximumEvidenceSpans
            || evidenceItems.Any(item => item == null)
            || evidenceItems.Select(item => item.Fingerprint)
                .Distinct(StringComparer.Ordinal)
                .Count() != evidenceItems.Length)
        {
            throw new ArgumentException(
                "Evidence spans must be non-empty, bounded and unique.",
                nameof(evidenceSpans));
        }
        var orderedEvidence = evidenceItems
            .OrderBy(item => item.TurnId, StringComparer.Ordinal)
            .ThenBy(item => item.ScalarStart)
            .ThenBy(item => item.ScalarLength)
            .ThenBy(item => item.Role)
            .ToArray();
        EvidenceSpans = new ReadOnlyCollection<SemanticEvidenceSpan>(
            orderedEvidence);

        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            ProposalId,
            Rank,
            InputFingerprint,
            RegistryGeneration,
            RegistryFingerprint,
            Purpose,
            Freshness,
            Effect,
            Target,
            Ambiguity,
            Confidence,
            TargetReference.Fingerprint,
            SemanticIntentFingerprint.CanonicalSequence(
                orderedDescriptors.Select(item => item.Fingerprint)),
            SemanticIntentFingerprint.CanonicalSequence(orderedContextItems),
            SemanticIntentFingerprint.CanonicalSequence(
                orderedEvidence.Select(item => item.Fingerprint)));
    }
}

internal sealed class SemanticIntentProposalBatch
{
    internal const int MaximumProposals = 4;

    internal string ProducerId { get; }
    internal int ProducerVersion { get; }
    internal SemanticIntentProducerOrigin ProducerOrigin { get; }
    internal string ProducerProfileFingerprint { get; }
    internal string InputFingerprint { get; }
    internal long RegistryGeneration { get; }
    internal string RegistryFingerprint { get; }
    internal IReadOnlyList<SemanticIntentProposal> Proposals { get; }
    internal string Fingerprint { get; }

    internal SemanticIntentProposalBatch(
        string producerId,
        int producerVersion,
        SemanticIntentProducerOrigin producerOrigin,
        string producerProfileFingerprint,
        string inputFingerprint,
        long registryGeneration,
        string registryFingerprint,
        IEnumerable<SemanticIntentProposal> proposals)
    {
        ProducerId = SemanticIntentContractGuard.Identifier(
            producerId,
            nameof(producerId));
        ProducerVersion = SemanticIntentContractGuard.Positive(
            producerVersion,
            nameof(producerVersion));
        ProducerOrigin = SemanticIntentContractGuard.Enum(
            producerOrigin,
            nameof(producerOrigin));
        ProducerProfileFingerprint = SemanticIntentContractGuard.Fingerprint(
            producerProfileFingerprint,
            nameof(producerProfileFingerprint));
        InputFingerprint = SemanticIntentContractGuard.Fingerprint(
            inputFingerprint,
            nameof(inputFingerprint));
        RegistryGeneration = SemanticIntentContractGuard.Positive(
            registryGeneration,
            nameof(registryGeneration));
        RegistryFingerprint = SemanticIntentContractGuard.Fingerprint(
            registryFingerprint,
            nameof(registryFingerprint));
        var items = (proposals
                ?? throw new ArgumentNullException(nameof(proposals)))
            .Take(MaximumProposals + 1)
            .ToArray();
        if (items.Length > MaximumProposals
            || items.Any(item => item == null)
            || items.Select(item => item.ProposalId)
                .Distinct(StringComparer.Ordinal)
                .Count() != items.Length
            || items.Select(item => item.Rank).Distinct().Count()
                != items.Length)
        {
            throw new ArgumentException(
                "Proposals must be bounded and uniquely ranked.",
                nameof(proposals));
        }
        var canonical = items
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.ProposalId, StringComparer.Ordinal)
            .ToArray();
        if (canonical
            .Select((item, index) => item.Rank == index + 1)
            .Any(matches => !matches))
        {
            throw new ArgumentException(
                "Proposal ranks must be contiguous.",
                nameof(proposals));
        }
        Proposals = new ReadOnlyCollection<SemanticIntentProposal>(canonical);
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            ProducerId,
            ProducerVersion,
            ProducerOrigin,
            ProducerProfileFingerprint,
            InputFingerprint,
            RegistryGeneration,
            RegistryFingerprint,
            SemanticIntentFingerprint.CanonicalSequence(
                canonical.Select(item => item.Fingerprint)));
    }
}

internal sealed class SemanticIntentShadowProjection
{
    internal string ProposalFingerprint { get; }
    internal int Rank { get; }
    internal string PrimaryDescriptorId { get; }
    internal int PrimaryDescriptorVersion { get; }
    internal SemanticPurposeAxis Purpose { get; }
    internal SemanticFreshnessAxis Freshness { get; }
    internal SemanticEffectAxis Effect { get; }
    internal SemanticTargetAxis Target { get; }
    internal SemanticAmbiguityKind Ambiguity { get; }
    internal SemanticIntentConfidenceBand Confidence { get; }
    internal SemanticTargetReferenceKind TargetReferenceKind { get; }
    internal string TargetReferenceFingerprint { get; }
    internal IReadOnlyList<string> DescriptorReferenceFingerprints { get; }
    internal int DescriptorCount { get; }
    internal int ContextReferenceCount { get; }
    internal int EvidenceSpanCount { get; }
    internal string Fingerprint { get; }

    private SemanticIntentShadowProjection(SemanticIntentProposal proposal)
    {
        if (proposal == null)
            throw new ArgumentNullException(nameof(proposal));
        var primary = proposal.Descriptors[0];
        ProposalFingerprint = proposal.Fingerprint;
        Rank = proposal.Rank;
        PrimaryDescriptorId = primary.DescriptorId;
        PrimaryDescriptorVersion = primary.DescriptorVersion;
        Purpose = proposal.Purpose;
        Freshness = proposal.Freshness;
        Effect = proposal.Effect;
        Target = proposal.Target;
        Ambiguity = proposal.Ambiguity;
        Confidence = proposal.Confidence;
        TargetReferenceKind = proposal.TargetReference.Kind;
        TargetReferenceFingerprint = proposal.TargetReference.Fingerprint;
        DescriptorReferenceFingerprints = new ReadOnlyCollection<string>(
            proposal.Descriptors.Select(item => item.Fingerprint).ToArray());
        DescriptorCount = proposal.Descriptors.Count;
        ContextReferenceCount = proposal.ContextReferenceIds.Count;
        EvidenceSpanCount = proposal.EvidenceSpans.Count;
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            ProposalFingerprint,
            Rank,
            PrimaryDescriptorId,
            PrimaryDescriptorVersion,
            Purpose,
            Freshness,
            Effect,
            Target,
            Ambiguity,
            Confidence,
            TargetReferenceKind,
            TargetReferenceFingerprint,
            SemanticIntentFingerprint.CanonicalSequence(
                DescriptorReferenceFingerprints),
            DescriptorCount,
            ContextReferenceCount,
            EvidenceSpanCount);
    }

    internal static SemanticIntentShadowProjection CreateValidated(
        SemanticIntentProposal proposal)
        => new(proposal);
}

internal sealed class SemanticIntentShadowOutcome
{
    internal string InputFingerprint { get; }
    internal string ScenarioFingerprint { get; }
    internal string RegistryFingerprint { get; }
    internal string BatchFingerprint { get; }
    internal SemanticIntentProducerOrigin ProducerOrigin { get; }
    internal string ProducerProfileFingerprint { get; }
    internal SemanticIntentShadowAdmission Admission { get; }
    internal IReadOnlyList<SemanticIntentShadowProjection> Projections { get; }
    internal string Fingerprint { get; }
    internal SemanticIntentAuthority Authority =>
        SemanticIntentAuthority.ShadowOnly;

    internal bool ContainsRawContent => false;
    internal bool CanRoute => false;
    internal bool CanAdmitLocalObservation => false;
    internal bool CanRequestResearch => false;
    internal bool CanInvokeProvider => false;
    internal bool CanInvokeTool => false;
    internal bool CanMutateMemory => false;
    internal bool CanAffectDecision => false;
    internal bool CanPublish => false;
    internal long PromptMutations => 0;
    internal long ProviderCalls => 0;
    internal long BrowserCalls => 0;
    internal long ToolCalls => 0;
    internal long StateWrites => 0;
    internal long PolicyAdmissions => 0;
    internal long CapabilityTickets => 0;
    internal long TelemetryPublications => 0;

    private SemanticIntentShadowOutcome(
        string inputFingerprint,
        string scenarioFingerprint,
        string registryFingerprint,
        string batchFingerprint,
        SemanticIntentProducerOrigin producerOrigin,
        string producerProfileFingerprint,
        SemanticIntentShadowAdmission admission,
        IEnumerable<SemanticIntentShadowProjection> projections)
    {
        InputFingerprint = SemanticIntentContractGuard.Fingerprint(
            inputFingerprint,
            nameof(inputFingerprint));
        ScenarioFingerprint = SemanticIntentContractGuard.Fingerprint(
            scenarioFingerprint,
            nameof(scenarioFingerprint));
        RegistryFingerprint = SemanticIntentContractGuard.Fingerprint(
            registryFingerprint,
            nameof(registryFingerprint));
        BatchFingerprint = SemanticIntentContractGuard.Fingerprint(
            batchFingerprint,
            nameof(batchFingerprint));
        ProducerOrigin = SemanticIntentContractGuard.Enum(
            producerOrigin,
            nameof(producerOrigin));
        ProducerProfileFingerprint = SemanticIntentContractGuard.Fingerprint(
            producerProfileFingerprint,
            nameof(producerProfileFingerprint));
        Admission = SemanticIntentContractGuard.Enum(
            admission,
            nameof(admission));
        var items = (projections
                ?? throw new ArgumentNullException(nameof(projections)))
            .Take(SemanticIntentProposalBatch.MaximumProposals + 1)
            .ToArray();
        if (items.Any(item => item == null)
            || items.Select(item => item.ProposalFingerprint)
                .Distinct(StringComparer.Ordinal)
                .Count() != items.Length
            || ((Admission is SemanticIntentShadowAdmission.Abstained
                    or SemanticIntentShadowAdmission.Expired)
                && items.Length != 0)
            || (Admission is SemanticIntentShadowAdmission.Projected
                    or SemanticIntentShadowAdmission.Ambiguous)
                && items.Length == 0)
        {
            throw new ArgumentException(
                "Projection count must match the Shadow admission.",
                nameof(projections));
        }
        var canonical = items.OrderBy(item => item.Rank).ToArray();
        if (canonical
                .Select((item, index) => item.Rank == index + 1)
                .Any(matches => !matches)
            || (Admission == SemanticIntentShadowAdmission.Projected
                && canonical.Length != 1))
        {
            throw new ArgumentException(
                "Outcome projections must be contiguous and unambiguous.",
                nameof(projections));
        }
        Projections = new ReadOnlyCollection<SemanticIntentShadowProjection>(
            canonical);
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            InputFingerprint,
            ScenarioFingerprint,
            RegistryFingerprint,
            BatchFingerprint,
            ProducerOrigin,
            ProducerProfileFingerprint,
            Admission,
            SemanticIntentFingerprint.CanonicalSequence(
                canonical.Select(item => item.Fingerprint)),
            Authority);
    }

    internal static SemanticIntentShadowOutcome CreateValidated(
        string inputFingerprint,
        string scenarioFingerprint,
        string registryFingerprint,
        string batchFingerprint,
        SemanticIntentProducerOrigin producerOrigin,
        string producerProfileFingerprint,
        SemanticIntentShadowAdmission admission,
        IEnumerable<SemanticIntentShadowProjection> projections)
        => new(
            inputFingerprint,
            scenarioFingerprint,
            registryFingerprint,
            batchFingerprint,
            producerOrigin,
            producerProfileFingerprint,
            admission,
            projections);
}

internal sealed class SemanticIntentOracleProjection
{
    internal int Rank { get; }
    internal string PrimaryDescriptorId { get; }
    internal int PrimaryDescriptorVersion { get; }
    internal IReadOnlyList<string> DescriptorKeys { get; }
    internal IReadOnlyList<string> DescriptorReferenceFingerprints { get; }
    internal SemanticPurposeAxis Purpose { get; }
    internal SemanticFreshnessAxis Freshness { get; }
    internal SemanticEffectAxis Effect { get; }
    internal SemanticTargetAxis Target { get; }
    internal SemanticAmbiguityKind Ambiguity { get; }
    internal SemanticIntentConfidenceBand Confidence { get; }
    internal SemanticTargetReferenceKind TargetReferenceKind { get; }
    internal string TargetReferenceFingerprint { get; }
    internal string Fingerprint { get; }

    internal SemanticIntentOracleProjection(
        int rank,
        IEnumerable<SemanticDescriptorReference> descriptors,
        SemanticPurposeAxis purpose,
        SemanticFreshnessAxis freshness,
        SemanticEffectAxis effect,
        SemanticTargetAxis target,
        SemanticAmbiguityKind ambiguity,
        SemanticIntentConfidenceBand confidence,
        SemanticTargetReference targetReference)
    {
        if (rank <= 0 || rank > SemanticIntentProposalBatch.MaximumProposals)
            throw new ArgumentOutOfRangeException(nameof(rank));
        Rank = rank;
        var descriptorItems = (descriptors
                ?? throw new ArgumentNullException(nameof(descriptors)))
            .Take(SemanticIntentProposal.MaximumDescriptors + 1)
            .ToArray();
        if (descriptorItems.Length == 0
            || descriptorItems.Length > SemanticIntentProposal.MaximumDescriptors
            || descriptorItems.Any(item => item == null)
            || descriptorItems.Select(item => item.DescriptorId)
                .Distinct(StringComparer.Ordinal)
                .Count() != descriptorItems.Length)
        {
            throw new ArgumentException(
                "Oracle descriptor references must be bounded and unique.",
                nameof(descriptors));
        }
        var canonicalDescriptors = descriptorItems
            .OrderBy(item => item.Rank)
            .ToArray();
        if (canonicalDescriptors
            .Select((item, index) => item.Rank == index + 1)
            .Any(matches => !matches))
        {
            throw new ArgumentException(
                "Oracle descriptor ranks must be contiguous.",
                nameof(descriptors));
        }
        var primary = canonicalDescriptors[0];
        PrimaryDescriptorId = primary.DescriptorId;
        PrimaryDescriptorVersion = primary.DescriptorVersion;
        DescriptorKeys = new ReadOnlyCollection<string>(
            canonicalDescriptors.Select(item => item.Key).ToArray());
        DescriptorReferenceFingerprints = new ReadOnlyCollection<string>(
            canonicalDescriptors.Select(item => item.Fingerprint).ToArray());
        Purpose = SemanticIntentContractGuard.Enum(purpose, nameof(purpose));
        Freshness = SemanticIntentContractGuard.Enum(
            freshness,
            nameof(freshness));
        Effect = SemanticIntentContractGuard.Enum(effect, nameof(effect));
        Target = SemanticIntentContractGuard.Enum(target, nameof(target));
        Ambiguity = SemanticIntentContractGuard.Enum(
            ambiguity,
            nameof(ambiguity));
        Confidence = SemanticIntentContractGuard.Enum(
            confidence,
            nameof(confidence));
        var exactTargetReference = targetReference
            ?? throw new ArgumentNullException(nameof(targetReference));
        TargetReferenceKind = exactTargetReference.Kind;
        TargetReferenceFingerprint = exactTargetReference.Fingerprint;
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            Rank,
            PrimaryDescriptorId,
            PrimaryDescriptorVersion,
            SemanticIntentFingerprint.CanonicalSequence(DescriptorKeys),
            SemanticIntentFingerprint.CanonicalSequence(
                DescriptorReferenceFingerprints),
            Purpose,
            Freshness,
            Effect,
            Target,
            Ambiguity,
            Confidence,
            TargetReferenceKind,
            TargetReferenceFingerprint);
    }
}

internal sealed class SemanticIntentDesignOracle
{
    internal string CorpusFingerprint { get; }
    internal string ScenarioFingerprint { get; }
    internal SemanticIntentShadowAdmission ExpectedAdmission { get; }
    internal IReadOnlyList<SemanticIntentOracleProjection> Projections { get; }
    internal bool WasProductionObserved => false;
    internal string Fingerprint { get; }

    internal SemanticIntentDesignOracle(
        string corpusFingerprint,
        string scenarioFingerprint,
        SemanticIntentShadowAdmission expectedAdmission,
        IEnumerable<SemanticIntentOracleProjection> projections)
    {
        CorpusFingerprint = SemanticIntentContractGuard.Fingerprint(
            corpusFingerprint,
            nameof(corpusFingerprint));
        ScenarioFingerprint = SemanticIntentContractGuard.Fingerprint(
            scenarioFingerprint,
            nameof(scenarioFingerprint));
        ExpectedAdmission = SemanticIntentContractGuard.Enum(
            expectedAdmission,
            nameof(expectedAdmission));
        var items = (projections
                ?? throw new ArgumentNullException(nameof(projections)))
            .Take(SemanticIntentProposalBatch.MaximumProposals + 1)
            .ToArray();
        if (items.Length > SemanticIntentProposalBatch.MaximumProposals
            || items.Any(item => item == null)
            || items.Select(item => item.Rank).Distinct().Count()
                != items.Length
            || ((ExpectedAdmission is SemanticIntentShadowAdmission.Abstained
                    or SemanticIntentShadowAdmission.Expired)
                && items.Length != 0)
            || ((ExpectedAdmission is SemanticIntentShadowAdmission.Projected
                    or SemanticIntentShadowAdmission.Ambiguous)
                && items.Length == 0))
        {
            throw new ArgumentException(
                "Oracle projections must match the expected admission.",
                nameof(projections));
        }
        var canonical = items.OrderBy(item => item.Rank).ToArray();
        if (canonical
            .Select((item, index) => item.Rank == index + 1)
            .Any(matches => !matches))
        {
            throw new ArgumentException(
                "Oracle projection ranks must be contiguous.",
                nameof(projections));
        }
        Projections = new ReadOnlyCollection<SemanticIntentOracleProjection>(
            canonical);
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            CorpusFingerprint,
            ScenarioFingerprint,
            ExpectedAdmission,
            SemanticIntentFingerprint.CanonicalSequence(
                canonical.Select(item => item.Fingerprint)),
            WasProductionObserved);
    }
}

internal sealed class SemanticIntentShadowComparisonRecord
{
    internal string CorpusFingerprint { get; }
    internal string ScenarioFingerprint { get; }
    internal string OracleFingerprint { get; }
    internal string InputFingerprint { get; }
    internal string RegistryFingerprint { get; }
    internal string BatchFingerprint { get; }
    internal string OutcomeFingerprint { get; }
    internal SemanticIntentProducerOrigin ProducerOrigin { get; }
    internal string ProducerProfileFingerprint { get; }
    internal IReadOnlyList<string> OracleDescriptorKeys { get; }
    internal SemanticIntentShadowAdmission OracleAdmission { get; }
    internal SemanticIntentShadowAdmission CandidateAdmission { get; }
    internal IReadOnlyList<SemanticIntentComparisonDifference> Differences {
        get;
    }
    internal IReadOnlyList<string> MismatchFingerprints { get; }
    internal bool ExactMatch => MismatchFingerprints.Count == 0;
    internal bool WouldOverPromoteAmbiguity =>
        (OracleAdmission is SemanticIntentShadowAdmission.Ambiguous
            or SemanticIntentShadowAdmission.Abstained
            or SemanticIntentShadowAdmission.Expired)
        && CandidateAdmission == SemanticIntentShadowAdmission.Projected;
    internal string Fingerprint { get; }

    private SemanticIntentShadowComparisonRecord(
        SemanticIntentDesignOracle oracle,
        SemanticIntentInputSnapshot input,
        SemanticIntentShadowOutcome outcome,
        IEnumerable<SemanticIntentComparisonMismatch> mismatches)
    {
        CorpusFingerprint = oracle.CorpusFingerprint;
        ScenarioFingerprint = oracle.ScenarioFingerprint;
        OracleFingerprint = oracle.Fingerprint;
        InputFingerprint = input.Fingerprint;
        RegistryFingerprint = outcome.RegistryFingerprint;
        BatchFingerprint = outcome.BatchFingerprint;
        OutcomeFingerprint = outcome.Fingerprint;
        ProducerOrigin = outcome.ProducerOrigin;
        ProducerProfileFingerprint = outcome.ProducerProfileFingerprint;
        OracleDescriptorKeys = new ReadOnlyCollection<string>(
            oracle.Projections
                .SelectMany(item => item.DescriptorKeys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
        OracleAdmission = oracle.ExpectedAdmission;
        CandidateAdmission = outcome.Admission;
        var mismatchItems = (mismatches
                ?? throw new ArgumentNullException(nameof(mismatches)))
            .ToArray();
        if (mismatchItems.Any(item => item == null)
            || mismatchItems.Select(item => item.Fingerprint)
                .Distinct(StringComparer.Ordinal)
                .Count() != mismatchItems.Length)
        {
            throw new ArgumentException(
                "Comparison mismatches must be non-null and unique.",
                nameof(mismatches));
        }
        var canonicalMismatches = mismatchItems
            .OrderBy(value => value.Fingerprint, StringComparer.Ordinal)
            .ToArray();
        var canonical = canonicalMismatches
            .Select(item => item.Kind)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        Differences = new ReadOnlyCollection<
            SemanticIntentComparisonDifference>(canonical);
        MismatchFingerprints = new ReadOnlyCollection<string>(
            canonicalMismatches.Select(item => item.Fingerprint).ToArray());
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            CorpusFingerprint,
            ScenarioFingerprint,
            OracleFingerprint,
            InputFingerprint,
            RegistryFingerprint,
            BatchFingerprint,
            OutcomeFingerprint,
            ProducerOrigin,
            ProducerProfileFingerprint,
            SemanticIntentFingerprint.CanonicalSequence(
                OracleDescriptorKeys),
            OracleAdmission,
            CandidateAdmission,
            SemanticIntentFingerprint.CanonicalSequence(
                canonical.Select(value => value.ToString())),
            SemanticIntentFingerprint.CanonicalSequence(
                MismatchFingerprints),
            ExactMatch,
            WouldOverPromoteAmbiguity);
    }

    internal static SemanticIntentShadowComparisonRecord Compare(
        SemanticIntentDesignOracle oracle,
        SemanticIntentInputSnapshot input,
        SemanticIntentRegistrySnapshot registry,
        SemanticIntentProposalBatch batch,
        long nowMonotonicTicks)
    {
        if (oracle == null)
            throw new ArgumentNullException(nameof(oracle));
        if (input == null)
            throw new ArgumentNullException(nameof(input));
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));
        if (batch == null)
            throw new ArgumentNullException(nameof(batch));
        var outcome = SemanticIntentShadowReplay.Evaluate(
            input,
            registry,
            batch,
            nowMonotonicTicks);
        if (input.Origin != SemanticIntentInputOrigin.FixedCorpusReplay
            || oracle.ScenarioFingerprint != input.ScenarioFingerprint
            || outcome.ScenarioFingerprint != input.ScenarioFingerprint
            || outcome.InputFingerprint != input.Fingerprint
            || outcome.RegistryFingerprint != input.RegistryFingerprint
            || outcome.ProducerOrigin
                != SemanticIntentProducerOrigin.FixedCorpusReplay)
        {
            throw new InvalidOperationException(
                "Oracle, input and outcome must describe the exact scenario.");
        }

        var mismatches = new List<SemanticIntentComparisonMismatch>();
        if (oracle.ExpectedAdmission != outcome.Admission)
        {
            mismatches.Add(new SemanticIntentComparisonMismatch(
                SemanticIntentComparisonDifference.Admission,
                oracle.ExpectedAdmission,
                outcome.Admission));
        }
        if (oracle.Projections.Count != outcome.Projections.Count)
        {
            mismatches.Add(new SemanticIntentComparisonMismatch(
                SemanticIntentComparisonDifference.ProjectionCount,
                oracle.Projections.Count,
                outcome.Projections.Count));
        }
        var sharedCount = Math.Min(
            oracle.Projections.Count,
            outcome.Projections.Count);
        for (var index = 0; index < sharedCount; index++)
        {
            var expected = oracle.Projections[index];
            var actual = outcome.Projections[index];
            if (expected.PrimaryDescriptorId != actual.PrimaryDescriptorId
                || expected.PrimaryDescriptorVersion
                    != actual.PrimaryDescriptorVersion
                || !expected.DescriptorReferenceFingerprints.SequenceEqual(
                    actual.DescriptorReferenceFingerprints,
                    StringComparer.Ordinal))
            {
                mismatches.Add(new SemanticIntentComparisonMismatch(
                    SemanticIntentComparisonDifference.Descriptor,
                    expected.PrimaryDescriptorId,
                    expected.PrimaryDescriptorVersion,
                    SemanticIntentFingerprint.CanonicalSequence(
                        expected.DescriptorReferenceFingerprints),
                    actual.PrimaryDescriptorId,
                    actual.PrimaryDescriptorVersion,
                    SemanticIntentFingerprint.CanonicalSequence(
                        actual.DescriptorReferenceFingerprints)));
            }
            if (expected.Purpose != actual.Purpose)
            {
                mismatches.Add(new SemanticIntentComparisonMismatch(
                    SemanticIntentComparisonDifference.Purpose,
                    expected.Purpose,
                    actual.Purpose));
            }
            if (expected.Freshness != actual.Freshness)
            {
                mismatches.Add(new SemanticIntentComparisonMismatch(
                    SemanticIntentComparisonDifference.Freshness,
                    expected.Freshness,
                    actual.Freshness));
            }
            if (expected.Effect != actual.Effect)
            {
                mismatches.Add(new SemanticIntentComparisonMismatch(
                    SemanticIntentComparisonDifference.Effect,
                    expected.Effect,
                    actual.Effect));
            }
            if (expected.Target != actual.Target)
            {
                mismatches.Add(new SemanticIntentComparisonMismatch(
                    SemanticIntentComparisonDifference.Target,
                    expected.Target,
                    actual.Target));
            }
            if (expected.Ambiguity != actual.Ambiguity)
            {
                mismatches.Add(new SemanticIntentComparisonMismatch(
                    SemanticIntentComparisonDifference.Ambiguity,
                    expected.Ambiguity,
                    actual.Ambiguity));
            }
            if (expected.Confidence != actual.Confidence)
            {
                mismatches.Add(new SemanticIntentComparisonMismatch(
                    SemanticIntentComparisonDifference.Confidence,
                    expected.Confidence,
                    actual.Confidence));
            }
            if (expected.TargetReferenceKind != actual.TargetReferenceKind
                || expected.TargetReferenceFingerprint
                    != actual.TargetReferenceFingerprint)
            {
                mismatches.Add(new SemanticIntentComparisonMismatch(
                    SemanticIntentComparisonDifference.TargetReference,
                    expected.TargetReferenceKind,
                    expected.TargetReferenceFingerprint,
                    actual.TargetReferenceKind,
                    actual.TargetReferenceFingerprint));
            }
        }
        return new SemanticIntentShadowComparisonRecord(
            oracle,
            input,
            outcome,
            mismatches);
    }
}

internal sealed class SemanticIntentExperienceEvidence
{
    internal string ExperienceId { get; }
    internal long RegistryGeneration { get; }
    internal string RegistryFingerprint { get; }
    internal string CorpusFingerprint { get; }
    internal string ScenarioFingerprint { get; }
    internal string InputFingerprint { get; }
    internal string BatchFingerprint { get; }
    internal string OutcomeFingerprint { get; }
    internal string ProducerProfileFingerprint { get; }
    internal SemanticExperienceSignalKind SignalKind { get; }
    internal SemanticExperienceAssessment Assessment { get; }
    internal string SourceProofFingerprint { get; }
    internal string Fingerprint { get; }

    private SemanticIntentExperienceEvidence(
        string experienceId,
        SemanticIntentRegistrySnapshot registry,
        SemanticIntentShadowComparisonRecord comparison)
    {
        ExperienceId = SemanticIntentContractGuard.Identifier(
            experienceId,
            nameof(experienceId));
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));
        if (comparison == null)
            throw new ArgumentNullException(nameof(comparison));
        if (comparison.RegistryFingerprint != registry.Fingerprint)
        {
            throw new InvalidOperationException(
                "Fixed comparison and registry identities do not match.");
        }
        RegistryGeneration = registry.Generation;
        RegistryFingerprint = registry.Fingerprint;
        CorpusFingerprint = comparison.CorpusFingerprint;
        ScenarioFingerprint = comparison.ScenarioFingerprint;
        InputFingerprint = comparison.InputFingerprint;
        BatchFingerprint = comparison.BatchFingerprint;
        OutcomeFingerprint = comparison.OutcomeFingerprint;
        ProducerProfileFingerprint = comparison.ProducerProfileFingerprint;
        SignalKind = SemanticExperienceSignalKind.FixedCorpusComparison;
        Assessment = comparison.ExactMatch
            ? SemanticExperienceAssessment.Supports
            : SemanticExperienceAssessment.Contradicts;
        SourceProofFingerprint = comparison.Fingerprint;
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            ExperienceId,
            RegistryGeneration,
            RegistryFingerprint,
            CorpusFingerprint,
            ScenarioFingerprint,
            InputFingerprint,
            BatchFingerprint,
            OutcomeFingerprint,
            ProducerProfileFingerprint,
            SignalKind,
            Assessment,
            SourceProofFingerprint);
    }

    internal static SemanticIntentExperienceEvidence FromFixedComparison(
        string experienceId,
        SemanticIntentRegistrySnapshot registry,
        SemanticIntentShadowComparisonRecord comparison)
        => new(experienceId, registry, comparison);
}

internal sealed class SemanticAdaptationDelta
{
    internal SemanticAdaptationDimension Dimension { get; }
    internal string DescriptorId { get; }
    internal int DescriptorVersion { get; }
    internal int AdjustmentBasisPoints { get; }
    internal string Fingerprint { get; }
    internal string TargetKey { get; }

    internal SemanticAdaptationDelta(
        SemanticAdaptationDimension dimension,
        string descriptorId,
        int descriptorVersion,
        int adjustmentBasisPoints)
    {
        Dimension = SemanticIntentContractGuard.Enum(
            dimension,
            nameof(dimension));
        DescriptorId = SemanticIntentContractGuard.Identifier(
            descriptorId,
            nameof(descriptorId));
        DescriptorVersion = SemanticIntentContractGuard.Positive(
            descriptorVersion,
            nameof(descriptorVersion));
        AdjustmentBasisPoints = SemanticIntentContractGuard.Adjustment(
            adjustmentBasisPoints,
            nameof(adjustmentBasisPoints));
        TargetKey = BuildTargetKey();
        Fingerprint = BuildFingerprint();
    }

    private string BuildTargetKey()
        => SemanticIntentFingerprint.Canonical(
            Dimension,
            DescriptorId,
            DescriptorVersion);

    private string BuildFingerprint()
        => SemanticIntentFingerprint.HashCanonical(
            Dimension,
            DescriptorId,
            DescriptorVersion,
            AdjustmentBasisPoints);
}

internal sealed class SemanticAdaptationCandidate
{
    internal const int MaximumExperienceReferences = 32;
    internal const int MaximumDeltas = 16;

    internal string CandidateId { get; }
    internal int BaseProfileVersion { get; }
    internal int CandidateProfileVersion { get; }
    internal string BaseProfileFingerprint { get; }
    internal string CandidateProfileFingerprint { get; }
    internal long RegistryGeneration { get; }
    internal string RegistryFingerprint { get; }
    internal string CorpusFingerprint { get; }
    internal IReadOnlyList<string> ExperienceIds { get; }
    internal IReadOnlyList<SemanticAdaptationDelta> Deltas { get; }
    internal string RollbackIdentity { get; }
    internal string Fingerprint { get; }

    internal SemanticAdaptationCandidate(
        string candidateId,
        int baseProfileVersion,
        int candidateProfileVersion,
        string baseProfileFingerprint,
        long registryGeneration,
        string registryFingerprint,
        string corpusFingerprint,
        IEnumerable<string> experienceIds,
        IEnumerable<SemanticAdaptationDelta> deltas)
    {
        CandidateId = SemanticIntentContractGuard.Identifier(
            candidateId,
            nameof(candidateId));
        BaseProfileVersion = SemanticIntentContractGuard.Positive(
            baseProfileVersion,
            nameof(baseProfileVersion));
        CandidateProfileVersion = SemanticIntentContractGuard.Positive(
            candidateProfileVersion,
            nameof(candidateProfileVersion));
        if (CandidateProfileVersion != BaseProfileVersion + 1)
        {
            throw new ArgumentException(
                "An adaptation candidate must advance exactly one profile version.",
                nameof(candidateProfileVersion));
        }
        BaseProfileFingerprint = SemanticIntentContractGuard.Fingerprint(
            baseProfileFingerprint,
            nameof(baseProfileFingerprint));
        RegistryGeneration = SemanticIntentContractGuard.Positive(
            registryGeneration,
            nameof(registryGeneration));
        RegistryFingerprint = SemanticIntentContractGuard.Fingerprint(
            registryFingerprint,
            nameof(registryFingerprint));
        CorpusFingerprint = SemanticIntentContractGuard.Fingerprint(
            corpusFingerprint,
            nameof(corpusFingerprint));

        var evidenceIds = (experienceIds
                ?? throw new ArgumentNullException(nameof(experienceIds)))
            .Select(value => SemanticIntentContractGuard.Identifier(
                value,
                nameof(experienceIds)))
            .ToArray();
        if (evidenceIds.Length == 0
            || evidenceIds.Length > MaximumExperienceReferences
            || evidenceIds.Distinct(StringComparer.Ordinal).Count()
                != evidenceIds.Length)
        {
            throw new ArgumentException(
                "Experience references must be non-empty, bounded and unique.",
                nameof(experienceIds));
        }
        var orderedEvidenceIds = evidenceIds
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        ExperienceIds = new ReadOnlyCollection<string>(orderedEvidenceIds);

        var deltaItems = (deltas
                ?? throw new ArgumentNullException(nameof(deltas)))
            .ToArray();
        if (deltaItems.Length == 0
            || deltaItems.Length > MaximumDeltas
            || deltaItems.Any(item => item == null)
            || deltaItems.Select(item => item.TargetKey)
                .Distinct(StringComparer.Ordinal)
                .Count() != deltaItems.Length)
        {
            throw new ArgumentException(
                "Adaptation deltas must be non-empty, bounded and unique.",
                nameof(deltas));
        }
        var orderedDeltas = deltaItems
            .OrderBy(item => item.Fingerprint, StringComparer.Ordinal)
            .ToArray();
        Deltas = new ReadOnlyCollection<SemanticAdaptationDelta>(
            orderedDeltas);
        CandidateProfileFingerprint = SemanticIntentFingerprint.HashCanonical(
            "semantic-profile",
            BaseProfileVersion,
            CandidateProfileVersion,
            BaseProfileFingerprint,
            RegistryGeneration,
            RegistryFingerprint,
            SemanticIntentFingerprint.CanonicalSequence(
                orderedDeltas.Select(item => item.Fingerprint)));
        RollbackIdentity = SemanticIntentFingerprint.HashCanonical(
            "rollback",
            BaseProfileVersion,
            BaseProfileFingerprint,
            RegistryGeneration,
            RegistryFingerprint,
            CandidateId,
            CandidateProfileFingerprint);
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            CandidateId,
            BaseProfileVersion,
            CandidateProfileVersion,
            BaseProfileFingerprint,
            CandidateProfileFingerprint,
            RegistryGeneration,
            RegistryFingerprint,
            CorpusFingerprint,
            SemanticIntentFingerprint.CanonicalSequence(orderedEvidenceIds),
            SemanticIntentFingerprint.CanonicalSequence(
                orderedDeltas.Select(item => item.Fingerprint)),
            RollbackIdentity);
    }
}

internal sealed class SemanticAdaptationReplayEvidence
{
    internal const int MaximumScenarios = 64;

    internal string CandidateFingerprint { get; }
    internal string CorpusFingerprint { get; }
    internal int ScenarioCount { get; }
    internal int ImprovedScenarioCount { get; }
    internal int RegressionCount { get; }
    internal int SafetyRegressionCount { get; }
    internal string BaselineOutcomeFingerprint { get; }
    internal string CandidateOutcomeFingerprint { get; }
    internal IReadOnlyList<string> ScenarioFingerprints { get; }
    internal IReadOnlyList<string> ImprovedScenarioFingerprints { get; }
    internal IReadOnlyList<string>
        ImprovedScenarioDescriptorBindingFingerprints { get; }
    internal IReadOnlyList<string> BaselineComparisonFingerprints { get; }
    internal IReadOnlyList<string> CandidateComparisonFingerprints { get; }
    internal string Fingerprint { get; }
    internal bool ProvesProducerAppliedProfile => false;
    internal bool ProvesCausalImprovement => false;

    private SemanticAdaptationReplayEvidence(
        SemanticAdaptationCandidate candidate,
        IReadOnlyList<SemanticIntentShadowComparisonRecord> baseline,
        IReadOnlyList<SemanticIntentShadowComparisonRecord> replayCandidate)
    {
        CandidateFingerprint = candidate.Fingerprint;
        CorpusFingerprint = candidate.CorpusFingerprint;
        ScenarioCount = baseline.Count;

        var improved = new List<string>();
        var improvedBindings = new HashSet<string>(StringComparer.Ordinal);
        var regressions = 0;
        var safetyRegressions = 0;
        for (var index = 0; index < baseline.Count; index++)
        {
            var before = baseline[index];
            var after = replayCandidate[index];
            var resolved = before.MismatchFingerprints.Except(
                after.MismatchFingerprints,
                StringComparer.Ordinal);
            var introduced = after.MismatchFingerprints.Except(
                before.MismatchFingerprints,
                StringComparer.Ordinal);
            if (resolved.Any())
            {
                improved.Add(before.ScenarioFingerprint);
                foreach (var descriptorKey in before.OracleDescriptorKeys)
                {
                    improvedBindings.Add(
                        BuildImprovementBindingFingerprint(
                            before.ScenarioFingerprint,
                            descriptorKey));
                }
            }
            if (introduced.Any())
                regressions++;
            if (after.WouldOverPromoteAmbiguity
                && !before.WouldOverPromoteAmbiguity)
            {
                safetyRegressions++;
            }
        }
        ImprovedScenarioCount = improved.Count;
        RegressionCount = regressions;
        SafetyRegressionCount = safetyRegressions;
        ScenarioFingerprints = new ReadOnlyCollection<string>(
            baseline.Select(item => item.ScenarioFingerprint).ToArray());
        ImprovedScenarioFingerprints = new ReadOnlyCollection<string>(
            improved.ToArray());
        ImprovedScenarioDescriptorBindingFingerprints =
            new ReadOnlyCollection<string>(
                improvedBindings.OrderBy(
                    value => value,
                    StringComparer.Ordinal).ToArray());
        BaselineComparisonFingerprints = new ReadOnlyCollection<string>(
            baseline.Select(item => item.Fingerprint).ToArray());
        CandidateComparisonFingerprints = new ReadOnlyCollection<string>(
            replayCandidate.Select(item => item.Fingerprint).ToArray());
        BaselineOutcomeFingerprint = SemanticIntentFingerprint.Hash(
            SemanticIntentFingerprint.CanonicalSequence(
                BaselineComparisonFingerprints));
        CandidateOutcomeFingerprint = SemanticIntentFingerprint.Hash(
            SemanticIntentFingerprint.CanonicalSequence(
                CandidateComparisonFingerprints));
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            CandidateFingerprint,
            CorpusFingerprint,
            ScenarioCount,
            ImprovedScenarioCount,
            RegressionCount,
            SafetyRegressionCount,
            BaselineOutcomeFingerprint,
            CandidateOutcomeFingerprint,
            SemanticIntentFingerprint.CanonicalSequence(
                ScenarioFingerprints),
            SemanticIntentFingerprint.CanonicalSequence(
                ImprovedScenarioFingerprints),
            SemanticIntentFingerprint.CanonicalSequence(
                ImprovedScenarioDescriptorBindingFingerprints));
    }

    internal static SemanticAdaptationReplayEvidence FromFixedReplay(
        SemanticAdaptationCandidate candidate,
        IEnumerable<SemanticIntentShadowComparisonRecord> baseline,
        IEnumerable<SemanticIntentShadowComparisonRecord> replayCandidate)
    {
        if (candidate == null)
            throw new ArgumentNullException(nameof(candidate));
        var baselineItems = Materialize(baseline, nameof(baseline));
        var candidateItems = Materialize(
            replayCandidate,
            nameof(replayCandidate));
        if (baselineItems.Length == 0
            || baselineItems.Length != candidateItems.Length)
        {
            throw new ArgumentException(
                "Replay sets must be non-empty and exactly paired.",
                nameof(replayCandidate));
        }

        var orderedBaseline = baselineItems
            .OrderBy(item => item.ScenarioFingerprint, StringComparer.Ordinal)
            .ToArray();
        var orderedCandidate = candidateItems
            .OrderBy(item => item.ScenarioFingerprint, StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < orderedBaseline.Length; index++)
        {
            var before = orderedBaseline[index];
            var after = orderedCandidate[index];
            if (before.ScenarioFingerprint != after.ScenarioFingerprint
                || before.OracleFingerprint != after.OracleFingerprint
                || before.InputFingerprint != after.InputFingerprint
                || before.CorpusFingerprint != candidate.CorpusFingerprint
                || after.CorpusFingerprint != candidate.CorpusFingerprint
                || before.RegistryFingerprint
                    != candidate.RegistryFingerprint
                || after.RegistryFingerprint
                    != candidate.RegistryFingerprint
                || before.ProducerProfileFingerprint
                    != candidate.BaseProfileFingerprint
                || after.ProducerProfileFingerprint
                    != candidate.CandidateProfileFingerprint)
            {
                throw new InvalidOperationException(
                    "Replay comparisons do not bind the exact candidate profiles.");
            }
        }
        return new SemanticAdaptationReplayEvidence(
            candidate,
            orderedBaseline,
            orderedCandidate);
    }

    internal bool SupportsImprovement(
        string scenarioFingerprint,
        string descriptorId,
        int descriptorVersion)
    {
        var scenario = SemanticIntentContractGuard.Fingerprint(
            scenarioFingerprint,
            nameof(scenarioFingerprint));
        var descriptorKey = SemanticIntentContractGuard.Identifier(
                descriptorId,
                nameof(descriptorId))
            + "@"
            + SemanticIntentContractGuard.Positive(
                descriptorVersion,
                nameof(descriptorVersion))
                .ToString(CultureInfo.InvariantCulture);
        return ImprovedScenarioDescriptorBindingFingerprints.Contains(
            BuildImprovementBindingFingerprint(scenario, descriptorKey),
            StringComparer.Ordinal);
    }

    private static string BuildImprovementBindingFingerprint(
        string scenarioFingerprint,
        string descriptorKey)
        => SemanticIntentFingerprint.HashCanonical(
            scenarioFingerprint,
            descriptorKey);

    private static SemanticIntentShadowComparisonRecord[] Materialize(
        IEnumerable<SemanticIntentShadowComparisonRecord> source,
        string parameterName)
    {
        if (source == null)
            throw new ArgumentNullException(parameterName);
        var items = source.Take(MaximumScenarios + 1).ToArray();
        if (items.Length > MaximumScenarios
            || items.Any(item => item == null)
            || items.Select(item => item.ScenarioFingerprint)
                .Distinct(StringComparer.Ordinal)
                .Count() != items.Length)
        {
            throw new ArgumentException(
                "Replay comparisons must be bounded, non-null and unique.",
                parameterName);
        }
        return items;
    }
}

internal sealed class SemanticAdaptationShadowOutcome
{
    internal SemanticAdaptationShadowAdmission Admission { get; }
    internal string CandidateFingerprint { get; }
    internal string RollbackIdentity { get; }
    internal string RetainedEffectiveProfileFingerprint { get; }
    internal string ReplayEvidenceFingerprint { get; }
    internal string Fingerprint { get; }
    internal SemanticIntentAuthority Authority =>
        SemanticIntentAuthority.ShadowOnly;
    internal bool EligibleForShadowReview =>
        Admission == SemanticAdaptationShadowAdmission.EligibleForShadowReview;
    internal bool MayApplyAutomatically => false;
    internal bool ProducerProfileApplicationVerified => false;
    internal bool CausalImprovementProven => false;
    internal bool CanChangeCode => false;
    internal bool CanAddCapability => false;
    internal bool CanChangePermission => false;
    internal bool CanChangeSafetyPolicy => false;
    internal bool CanChangeBudget => false;
    internal bool CanChangeVerifier => false;
    internal bool CanChangeSuccessCriteria => false;
    internal long ProfileWrites => 0;
    internal long RegistryWrites => 0;
    internal long RuntimePromotions => 0;
    internal long PersistenceWrites => 0;

    internal SemanticAdaptationShadowOutcome(
        SemanticAdaptationShadowAdmission admission,
        string candidateFingerprint,
        string rollbackIdentity,
        string retainedEffectiveProfileFingerprint,
        string replayEvidenceFingerprint)
    {
        Admission = SemanticIntentContractGuard.Enum(
            admission,
            nameof(admission));
        CandidateFingerprint = SemanticIntentContractGuard.Fingerprint(
            candidateFingerprint,
            nameof(candidateFingerprint));
        RollbackIdentity = SemanticIntentContractGuard.Fingerprint(
            rollbackIdentity,
            nameof(rollbackIdentity));
        RetainedEffectiveProfileFingerprint =
            SemanticIntentContractGuard.Fingerprint(
                retainedEffectiveProfileFingerprint,
                nameof(retainedEffectiveProfileFingerprint));
        ReplayEvidenceFingerprint = SemanticIntentContractGuard.Fingerprint(
            replayEvidenceFingerprint,
            nameof(replayEvidenceFingerprint));
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            Admission,
            CandidateFingerprint,
            RollbackIdentity,
            RetainedEffectiveProfileFingerprint,
            ReplayEvidenceFingerprint,
            Authority);
    }
}

internal static class SemanticIntentFingerprint
{
    internal static string Hash(string value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    internal static string HashCanonical(params object?[] values)
        => Hash(Canonical(values));

    internal static string Canonical(params object?[] values)
    {
        if (values == null)
            throw new ArgumentNullException(nameof(values));
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            var formatted = value switch
            {
                null => "NULL",
                string text => text,
                IFormattable formattable => formattable.ToString(
                    null,
                    CultureInfo.InvariantCulture) ?? string.Empty,
                _ => value.ToString() ?? string.Empty
            };
            builder.Append(
                formatted.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(formatted);
        }
        return builder.ToString();
    }

    internal static string CanonicalSequence(IEnumerable<string> values)
    {
        if (values == null)
            throw new ArgumentNullException(nameof(values));
        return Canonical(values.Cast<object?>().ToArray());
    }
}

internal static class SemanticIntentContractGuard
{
    internal const int MaximumIdentifierLength = 128;

    internal static string Identifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumIdentifierLength
            || value.Any(character =>
                character > 127
                || !(character is >= 'A' and <= 'Z'
                    or >= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '.' or '_' or ':' or '-' or '/')))
        {
            throw new ArgumentException(
                "Identifier must be a bounded ASCII token.",
                parameterName);
        }
        return value;
    }

    internal static string Fingerprint(string value, string parameterName)
    {
        if (value == null
            || value.Length != 64
            || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Fingerprint must be a 64-character hexadecimal SHA-256.",
                parameterName);
        }
        return value.ToUpperInvariant();
    }

    internal static T Enum<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!System.Enum.IsDefined(typeof(T), value))
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static int Positive(int value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static long Positive(long value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static string Text(
        string value,
        int maximumUnicodeScalars,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || HasInvalidSurrogateSequence(value)
            || value.EnumerateRunes().Count() > maximumUnicodeScalars
            || value.Any(character =>
                char.IsControl(character)
                && character is not '\r' and not '\n' and not '\t'))
        {
            throw new ArgumentException(
                "Text is empty, contains forbidden controls or exceeds bounds.",
                parameterName);
        }
        return value;
    }

    private static bool HasInvalidSurrogateSequence(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length
                    || !char.IsLowSurrogate(value[index + 1]))
                {
                    return true;
                }
                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                return true;
            }
        }
        return false;
    }

    internal static void Lifetime(long observedAt, long expiresAt)
    {
        if (observedAt < 0 || expiresAt <= observedAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
    }

    internal static int Adjustment(int value, string parameterName)
    {
        if (value == 0 || value < -1000 || value > 1000)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}
