using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum LivingPromptCompositionMode
{
    Off = 0,
    Prepared = 1
}

public enum LivingPromptSourceKind
{
    PlayerTriggered = 0,
    BrainAuthorizedProactive = 1,
    ContextualProactiveChat = 2
}

internal enum LivingPromptCharacterSlotOwner
{
    None = 0,
    Legacy = 1,
    CharacterRuntime = 2
}

internal enum LivingPromptLegacyCharacterSlotAdmission
{
    BaselineOnly = 0,
    LegacyOwned = 1,
    BaselineMismatch = 2,
    AuthorizationRejected = 3
}

internal enum LivingPromptTurnRole
{
    User = 0,
    Assistant = 1
}

internal enum LivingPromptTurnOrigin
{
    SourceOwned = 0,
    MemoryWorkingSet = 1
}

internal enum LivingPromptCompositionAdmission
{
    Disabled = 0,
    RootOwnershipAcquired = 1,
    RootOwnershipRejected = 2,
    AuthorizationMismatch = 3,
    RuntimeMismatch = 4,
    SessionMismatch = 5,
    SituationMismatch = 6,
    ProviderMismatch = 7,
    Expired = 8,
    Duplicate = 9,
    RootOwnershipMismatch = 10,
    CharacterSlotRejected = 11,
    MemoryPacketRejected = 12,
    Composed = 13,
    Cancelled = 14,
    DispatchAccepted = 15,
    DispatchRejected = 16,
    CompletionRecorded = 17,
    CompletionRejected = 18,
    Faulted = 19,
    WorkingContextRejected = 20,
    TurnContentRejected = 21,
    NotYetValid = 22
}

internal sealed class LivingPromptCompositionPolicy
{
    internal const int MaximumWorkingContextTurns = 32;

    internal static LivingPromptCompositionPolicy Off { get; } =
        new(LivingPromptCompositionMode.Off);

    internal LivingPromptCompositionMode Mode { get; }
    internal bool Enabled => Mode == LivingPromptCompositionMode.Prepared;

    internal LivingPromptCompositionPolicy(LivingPromptCompositionMode mode)
    {
        if (!Enum.IsDefined(typeof(LivingPromptCompositionMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
    }
}

internal sealed class LivingPromptSourceAuthorization
{
    internal LivingPromptSourceKind SourceKind { get; }
    internal string AuthorizationId { get; }
    internal string RequestIdentity { get; }
    internal string SessionId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal Guid? DecisionId { get; }
    internal string? GoalOrCandidateId { get; }
    internal string SemanticSourceHash { get; }
    internal ProactiveRespondPurpose? ProactivePurpose { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }

    private LivingPromptSourceAuthorization(
        LivingPromptSourceKind sourceKind,
        string authorizationId,
        string requestIdentity,
        string sessionId,
        Guid situationId,
        long situationRevision,
        CharacterRuntimeIdentity runtimeIdentity,
        Guid? decisionId,
        string? goalOrCandidateId,
        string semanticSourceHash,
        ProactiveRespondPurpose? proactivePurpose,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        if (!Enum.IsDefined(typeof(LivingPromptSourceKind), sourceKind))
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        if (situationId == Guid.Empty)
            throw new ArgumentException(
                "Situation identity cannot be empty.",
                nameof(situationId));
        if (situationRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(situationRevision));
        if (decisionId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(decisionId));

        SourceKind = sourceKind;
        AuthorizationId = LivingContinuityContractGuard.Identifier(
            authorizationId,
            nameof(authorizationId));
        RequestIdentity = LivingContinuityContractGuard.Identifier(
            requestIdentity,
            nameof(requestIdentity));
        SessionId = LivingContinuityContractGuard.Identifier(
            sessionId,
            nameof(sessionId));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        if (!string.Equals(
                SessionId,
                RuntimeIdentity.OrchestratorSessionId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Authorization and runtime must share a session.",
                nameof(sessionId));
        }
        SituationId = situationId;
        SituationRevision = situationRevision;
        DecisionId = decisionId;
        GoalOrCandidateId = goalOrCandidateId == null
            ? null
            : LivingContinuityContractGuard.Identifier(
                goalOrCandidateId,
                nameof(goalOrCandidateId));
        SemanticSourceHash = LivingContinuityContractGuard.Sha256(
            semanticSourceHash,
            nameof(semanticSourceHash));
        ProactivePurpose = proactivePurpose;
        IssuedAt = LivingContinuityContractGuard.Timestamp(
            issuedAt,
            nameof(issuedAt));
        ExpiresAt = LivingContinuityContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (ExpiresAt <= IssuedAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));

        if (sourceKind == LivingPromptSourceKind.PlayerTriggered
            && proactivePurpose.HasValue)
        {
            throw new ArgumentException(
                "Player-triggered authorization cannot carry proactive purpose.",
                nameof(proactivePurpose));
        }
        if (sourceKind == LivingPromptSourceKind.BrainAuthorizedProactive
            && (!proactivePurpose.HasValue
                || !decisionId.HasValue
                || GoalOrCandidateId == null))
        {
            throw new ArgumentException(
                "Brain-authorized proactive source requires exact purpose, "
                + "Decision and Candidate.",
                nameof(proactivePurpose));
        }
    }

    internal static LivingPromptSourceAuthorization FromPlayer(
        PlayerTriggeredRespondAuthorization authorization,
        string requestContentHash)
    {
        if (authorization == null)
            throw new ArgumentNullException(nameof(authorization));
        return new LivingPromptSourceAuthorization(
            LivingPromptSourceKind.PlayerTriggered,
            authorization.AuthorizationId.ToString("N"),
            authorization.TurnId.ToString("N"),
            authorization.SessionId,
            authorization.SituationId,
            authorization.SituationRevision,
            authorization.RuntimeIdentity,
            authorization.DecisionId,
            authorization.GoalId,
            requestContentHash,
            null,
            authorization.IssuedAt,
            authorization.ValidUntil);
    }

    internal static LivingPromptSourceAuthorization FromProactive(
        ProactiveRespondAuthorization authorization)
    {
        if (authorization == null)
            throw new ArgumentNullException(nameof(authorization));
        return new LivingPromptSourceAuthorization(
            LivingPromptSourceKind.BrainAuthorizedProactive,
            authorization.AuthorizationId,
            authorization.OccurrenceId.ToString("N"),
            authorization.RuntimeIdentity.OrchestratorSessionId,
            authorization.SnapshotId,
            authorization.SituationRevision,
            authorization.RuntimeIdentity,
            authorization.DecisionId,
            authorization.CandidateId,
            authorization.SourceContentHash,
            authorization.Purpose,
            authorization.IssuedAt,
            authorization.ExpiresAt);
    }

    internal string Fingerprint()
        => LivingPromptHash.Of(
            string.Join(
                "|",
                ((int)SourceKind).ToString(CultureInfo.InvariantCulture),
                AuthorizationId,
                RequestIdentity,
                SessionId,
                SituationId.ToString("N"),
                SituationRevision.ToString(CultureInfo.InvariantCulture),
                RuntimeIdentity.RuntimeInstanceId,
                RuntimeIdentity.Generation.ToString(
                    CultureInfo.InvariantCulture),
                DecisionId?.ToString("N") ?? "-",
                GoalOrCandidateId ?? "-",
                SemanticSourceHash,
                ProactivePurpose.HasValue
                    ? ((int)ProactivePurpose.Value).ToString(
                        CultureInfo.InvariantCulture)
                    : "-",
                IssuedAt.UtcDateTime.Ticks.ToString(
                    CultureInfo.InvariantCulture),
                ExpiresAt.UtcDateTime.Ticks.ToString(
                    CultureInfo.InvariantCulture)));
}

internal sealed class LivingPromptTurn
{
    internal string TurnId { get; }
    internal LivingPromptTurnRole Role { get; }
    internal LivingPromptTurnOrigin Origin { get; }
    internal string ContentHash { get; }
    internal string Text { get; }

    internal LivingPromptTurn(
        string turnId,
        LivingPromptTurnRole role,
        string contentHash,
        string text,
        LivingPromptTurnOrigin origin =
            LivingPromptTurnOrigin.SourceOwned)
    {
        TurnId = LivingContinuityContractGuard.Identifier(
            turnId,
            nameof(turnId));
        if (!Enum.IsDefined(typeof(LivingPromptTurnRole), role))
            throw new ArgumentOutOfRangeException(nameof(role));
        if (!Enum.IsDefined(typeof(LivingPromptTurnOrigin), origin))
            throw new ArgumentOutOfRangeException(nameof(origin));
        Role = role;
        Origin = origin;
        ContentHash = LivingContinuityContractGuard.Sha256(
            contentHash,
            nameof(contentHash));
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }
}

internal sealed class LivingPromptCharacterSlot
{
    internal LivingPromptCharacterSlotOwner Owner { get; }
    internal string SlotId { get; }
    internal string Fragment { get; }
    internal string FragmentHash { get; }
    internal Guid? PromptOwnerLeaseId { get; }
    internal Guid? AuthorizationId { get; }
    internal Guid? TurnId { get; }
    internal string? ProviderRouteId { get; }
    internal string? PromptBaselineSha256 { get; }
    internal DateTimeOffset? ValidUntil { get; }
    internal LivingPromptSourceKind? SourceKind { get; }
    internal string? SourceAuthorizationId { get; }
    internal string? SourceRequestIdentity { get; }

    private LivingPromptCharacterSlot(
        LivingPromptCharacterSlotOwner owner,
        string slotId,
        string fragment,
        Guid? promptOwnerLeaseId,
        Guid? authorizationId,
        Guid? turnId,
        string? providerRouteId,
        string? promptBaselineSha256,
        DateTimeOffset? validUntil,
        LivingPromptSourceKind? sourceKind = null,
        string? sourceAuthorizationId = null,
        string? sourceRequestIdentity = null)
    {
        if (!Enum.IsDefined(typeof(LivingPromptCharacterSlotOwner), owner))
            throw new ArgumentOutOfRangeException(nameof(owner));
        if (owner == LivingPromptCharacterSlotOwner.None
            && (!string.IsNullOrEmpty(slotId)
                || !string.IsNullOrEmpty(fragment)))
        {
            throw new ArgumentException(
                "Empty Character slot cannot carry content.");
        }
        if (owner != LivingPromptCharacterSlotOwner.None
            && (string.IsNullOrWhiteSpace(slotId)
                || string.IsNullOrWhiteSpace(fragment)))
        {
            throw new ArgumentException(
                "Owned Character slot requires one exact fragment.");
        }
        Owner = owner;
        SlotId = owner == LivingPromptCharacterSlotOwner.None
            ? string.Empty
            : LivingContinuityContractGuard.Identifier(
                slotId,
                nameof(slotId));
        Fragment = fragment;
        FragmentHash = LivingPromptHash.Of(fragment);
        PromptOwnerLeaseId = promptOwnerLeaseId;
        AuthorizationId = authorizationId;
        TurnId = turnId;
        ProviderRouteId = providerRouteId;
        PromptBaselineSha256 = promptBaselineSha256;
        ValidUntil = validUntil;
        SourceKind = sourceKind;
        SourceAuthorizationId = sourceAuthorizationId;
        SourceRequestIdentity = sourceRequestIdentity;
        if (owner == LivingPromptCharacterSlotOwner.CharacterRuntime
            && (!promptOwnerLeaseId.HasValue
                || providerRouteId == null
                || promptBaselineSha256 == null
                || !validUntil.HasValue
                || !HasExactlyOneOwnershipProof(
                    authorizationId,
                    turnId,
                    sourceKind,
                    sourceAuthorizationId,
                    sourceRequestIdentity)))
        {
            throw new ArgumentException(
                "CharacterRuntime slot requires an exact consumed "
                + "Prompt-ownership proof.");
        }
        if (owner != LivingPromptCharacterSlotOwner.CharacterRuntime
            && (sourceKind.HasValue
                || sourceAuthorizationId != null
                || sourceRequestIdentity != null))
        {
            throw new ArgumentException(
                "Only a CharacterRuntime slot may carry typed source proof.");
        }
    }

    private static bool HasExactlyOneOwnershipProof(
        Guid? authorizationId,
        Guid? turnId,
        LivingPromptSourceKind? sourceKind,
        string? sourceAuthorizationId,
        string? sourceRequestIdentity)
    {
        var playerProof = authorizationId.HasValue && turnId.HasValue;
        var contextualProof = sourceKind
                == LivingPromptSourceKind.ContextualProactiveChat
            && !string.IsNullOrWhiteSpace(sourceAuthorizationId)
            && !string.IsNullOrWhiteSpace(sourceRequestIdentity);
        return playerProof != contextualProof;
    }

    internal static LivingPromptCharacterSlot Empty { get; } =
        new(
            LivingPromptCharacterSlotOwner.None,
            string.Empty,
            string.Empty,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

    internal static LivingPromptCharacterSlot FromCharacterRuntime(
        CharacterPromptInfluenceOwnershipLease promptLease,
        CharacterLanguageStyleDirective directive)
    {
        if (promptLease == null)
            throw new ArgumentNullException(nameof(promptLease));
        if (directive == null)
            throw new ArgumentNullException(nameof(directive));
        if (promptLease.Owner
                != LanguageStylePromptOwner.CharacterRuntimeOwned
            || directive.OwnerLeaseId != promptLease.LeaseId
            || directive.AuthorizationId != promptLease.AuthorizationId
            || directive.ValidUntil > promptLease.ValidUntil)
        {
            throw new ArgumentException(
                "Character slot requires the exact CharacterRuntime "
                + "Prompt lease and directive.",
                nameof(directive));
        }
        return new LivingPromptCharacterSlot(
            LivingPromptCharacterSlotOwner.CharacterRuntime,
            directive.DirectiveId,
            directive.ExactSuffixText,
            promptLease.LeaseId,
            promptLease.AuthorizationId,
            promptLease.TurnId,
            promptLease.ProviderRouteId,
            promptLease.PromptBaselineSha256,
            directive.ValidUntil);
    }

    internal static LivingPromptLegacyCharacterSlotAdmission
        TryFromLegacyRenderedPrompt(
            string slotId,
            LivingPromptSourceAuthorization authorization,
            string providerRouteId,
            string baselinePrompt,
            string legacyRenderedPrompt,
            DateTimeOffset validUntil,
            out LivingPromptCharacterSlot slot)
    {
        if (authorization == null)
            throw new ArgumentNullException(nameof(authorization));
        providerRouteId = LivingContinuityContractGuard.Identifier(
            providerRouteId,
            nameof(providerRouteId));
        baselinePrompt = baselinePrompt
            ?? throw new ArgumentNullException(nameof(baselinePrompt));
        legacyRenderedPrompt = legacyRenderedPrompt
            ?? throw new ArgumentNullException(
                nameof(legacyRenderedPrompt));
        validUntil = LivingContinuityContractGuard.Timestamp(
            validUntil,
            nameof(validUntil));
        slot = Empty;

        if (string.Equals(
                baselinePrompt,
                legacyRenderedPrompt,
                StringComparison.Ordinal))
        {
            return LivingPromptLegacyCharacterSlotAdmission.BaselineOnly;
        }
        var prefix = baselinePrompt + "\n\n";
        if (!legacyRenderedPrompt.StartsWith(
                prefix,
                StringComparison.Ordinal)
            || legacyRenderedPrompt.Length <= prefix.Length)
        {
            return LivingPromptLegacyCharacterSlotAdmission
                .BaselineMismatch;
        }
        if (authorization.SourceKind
                != LivingPromptSourceKind.PlayerTriggered
            || !Guid.TryParseExact(
                authorization.AuthorizationId,
                "N",
                out var authorizationId)
            || !Guid.TryParseExact(
                authorization.RequestIdentity,
                "N",
                out var turnId)
            || validUntil <= authorization.IssuedAt
            || validUntil > authorization.ExpiresAt)
        {
            return LivingPromptLegacyCharacterSlotAdmission
                .AuthorizationRejected;
        }

        slot = new LivingPromptCharacterSlot(
            LivingPromptCharacterSlotOwner.Legacy,
            slotId,
            legacyRenderedPrompt[prefix.Length..],
            null,
            authorizationId,
            turnId,
            providerRouteId,
            LivingPromptHash.Of(baselinePrompt),
            validUntil);
        return LivingPromptLegacyCharacterSlotAdmission.LegacyOwned;
    }
}

internal sealed class LivingPromptFrozenParts
{
    internal string BaselinePrompt { get; }
    internal string BaselinePromptHash { get; }
    internal LivingPromptCharacterSlot CharacterSlot { get; }
    internal string? MemoryPacketId { get; }
    internal string? MemoryPacketHash { get; }
    internal string MemoryFragment { get; }
    internal string ProactiveInstruction { get; }
    internal string RenderedPrompt { get; }
    internal string RenderedPromptHash { get; }

    internal LivingPromptFrozenParts(
        LivingPromptCompositionRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        BaselinePrompt = request.BaselinePrompt;
        BaselinePromptHash = LivingPromptHash.Of(BaselinePrompt);
        CharacterSlot = request.CharacterSlot;
        MemoryPacketId = request.MemoryPacket?.PacketId;
        MemoryPacketHash = request.MemoryPacket?.PacketHash;
        MemoryFragment = request.MemoryPacket?.PromptFragment
            ?? string.Empty;
        ProactiveInstruction = request.Authorization.SourceKind switch
        {
            LivingPromptSourceKind.BrainAuthorizedProactive =>
                "[Proactive response authorization]\n"
                + "Purpose: "
                + request.Authorization.ProactivePurpose
                + "\nRespond only for this authorized occurrence. "
                + "Do not create another proactive task.",
            _ => string.Empty
        };

        var fragments = new List<string> { BaselinePrompt };
        if (CharacterSlot.Owner
            != LivingPromptCharacterSlotOwner.None)
        {
            fragments.Add(CharacterSlot.Fragment);
        }
        if (MemoryFragment.Length > 0)
            fragments.Add(MemoryFragment);
        if (ProactiveInstruction.Length > 0)
            fragments.Add(ProactiveInstruction);
        RenderedPrompt = string.Join("\n\n", fragments);
        RenderedPromptHash = LivingPromptHash.Of(RenderedPrompt);
    }
}

internal sealed class LivingPromptRootOwnership
{
    internal ContinuityOwnershipRequest Request { get; }
    internal ContinuityOwnershipRecord Record { get; }

    internal LivingPromptRootOwnership(
        ContinuityOwnershipRequest request,
        ContinuityOwnershipRecord record)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Record = record ?? throw new ArgumentNullException(nameof(record));
    }
}

internal sealed class LivingPromptCompositionRequest
{
    internal string CompositionId { get; }
    internal LivingPromptSourceAuthorization Authorization { get; }
    internal LivingPromptRootOwnership RootOwnership { get; }
    internal string ProviderRouteId { get; }
    internal string LivingSnapshotFingerprint { get; }
    internal string BaselinePrompt { get; }
    internal LivingPromptCharacterSlot CharacterSlot { get; }
    internal LongTermMemoryPromptPacket? MemoryPacket { get; }
    internal IReadOnlyList<LivingPromptTurn> WorkingContext { get; }
    internal LivingMemoryWorkingContextAuthorization?
        WorkingContextAuthorization { get; }
    internal LivingPromptTurn CurrentRequest { get; }
    internal DateTimeOffset RequestedAt { get; }

    internal LivingPromptCompositionRequest(
        string compositionId,
        LivingPromptSourceAuthorization authorization,
        LivingPromptRootOwnership rootOwnership,
        string providerRouteId,
        string livingSnapshotFingerprint,
        string baselinePrompt,
        LivingPromptCharacterSlot characterSlot,
        LongTermMemoryPromptPacket? memoryPacket,
        IEnumerable<LivingPromptTurn>? workingContext,
        LivingPromptTurn currentRequest,
        DateTimeOffset requestedAt,
        LivingMemoryWorkingContextAuthorization?
            workingContextAuthorization = null)
    {
        CompositionId = LivingContinuityContractGuard.Identifier(
            compositionId,
            nameof(compositionId));
        Authorization = authorization
            ?? throw new ArgumentNullException(nameof(authorization));
        RootOwnership = rootOwnership
            ?? throw new ArgumentNullException(nameof(rootOwnership));
        ProviderRouteId = LivingContinuityContractGuard.Identifier(
            providerRouteId,
            nameof(providerRouteId));
        LivingSnapshotFingerprint = LivingContinuityContractGuard.Sha256(
            livingSnapshotFingerprint,
            nameof(livingSnapshotFingerprint));
        BaselinePrompt = baselinePrompt
            ?? throw new ArgumentNullException(nameof(baselinePrompt));
        CharacterSlot = characterSlot
            ?? throw new ArgumentNullException(nameof(characterSlot));
        MemoryPacket = memoryPacket;
        WorkingContext = Array.AsReadOnly(
            (workingContext ?? Array.Empty<LivingPromptTurn>())
                .Select(
                    item => item
                        ?? throw new ArgumentException(
                            "Working context cannot contain null.",
                            nameof(workingContext)))
                .ToArray());
        WorkingContextAuthorization = workingContextAuthorization;
        CurrentRequest = currentRequest
            ?? throw new ArgumentNullException(nameof(currentRequest));
        RequestedAt = LivingContinuityContractGuard.Timestamp(
            requestedAt,
            nameof(requestedAt));
    }
}

internal sealed class LivingPromptEnvelope
{
    internal string CompositionId { get; }
    internal string EnvelopeId { get; }
    internal LivingPromptSourceKind SourceKind { get; }
    internal string AuthorizationFingerprint { get; }
    internal string ProviderRouteId { get; }
    internal string SessionId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string LivingSnapshotFingerprint { get; }
    internal LivingPromptFrozenParts FrozenParts { get; }
    internal string SystemPrompt { get; }
    internal string SystemPromptHash { get; }
    internal LivingPromptCharacterSlotOwner CharacterOwner { get; }
    internal string? MemoryPacketId { get; }
    internal string? WorkingContextAuthorizationFingerprint { get; }
    internal IReadOnlyList<LivingPromptTurn> WorkingContext { get; }
    internal LivingPromptTurn CurrentRequest { get; }
    internal DateTimeOffset CreatedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }

    internal LivingPromptEnvelope(
        LivingPromptCompositionRequest request,
        IReadOnlyList<LivingPromptTurn> workingContext,
        LivingPromptFrozenParts frozenParts)
    {
        CompositionId = request.CompositionId;
        SourceKind = request.Authorization.SourceKind;
        AuthorizationFingerprint = request.Authorization.Fingerprint();
        ProviderRouteId = request.ProviderRouteId;
        SessionId = request.Authorization.SessionId;
        SituationId = request.Authorization.SituationId;
        SituationRevision = request.Authorization.SituationRevision;
        RuntimeIdentity = request.Authorization.RuntimeIdentity;
        LivingSnapshotFingerprint = request.LivingSnapshotFingerprint;
        FrozenParts = frozenParts
            ?? throw new ArgumentNullException(nameof(frozenParts));
        SystemPrompt = FrozenParts.RenderedPrompt;
        SystemPromptHash = FrozenParts.RenderedPromptHash;
        CharacterOwner = request.CharacterSlot.Owner;
        MemoryPacketId = request.MemoryPacket?.PacketId;
        WorkingContextAuthorizationFingerprint = request
            .WorkingContextAuthorization?.Fingerprint;
        WorkingContext = workingContext;
        CurrentRequest = request.CurrentRequest;
        CreatedAt = request.RequestedAt;
        ExpiresAt = request.WorkingContextAuthorization == null
            || request.WorkingContextAuthorization.ExpiresAt
                >= request.Authorization.ExpiresAt
            ? request.Authorization.ExpiresAt
            : request.WorkingContextAuthorization.ExpiresAt;
        EnvelopeId = "d025-envelope-" + LivingPromptHash.Of(
            string.Join(
                "|",
                CompositionId,
                AuthorizationFingerprint,
                ProviderRouteId,
                LivingSnapshotFingerprint,
                SystemPromptHash,
                CharacterOwner.ToString(),
                MemoryPacketId ?? "-",
                WorkingContextAuthorizationFingerprint ?? "-",
                string.Join(
                    ",",
                    WorkingContext.Select(
                        item => item.TurnId + ":" + item.ContentHash)),
                CurrentRequest.TurnId,
                CurrentRequest.ContentHash))[..32];
    }
}

internal sealed class LivingPromptCompositionResult
{
    internal LivingPromptCompositionAdmission Admission { get; }
    internal LivingPromptRootOwnership? RootOwnership { get; }
    internal LivingPromptEnvelope? Envelope { get; }
    internal bool LegacyMayFallback { get; }

    internal LivingPromptCompositionResult(
        LivingPromptCompositionAdmission admission,
        LivingPromptRootOwnership? rootOwnership = null,
        LivingPromptEnvelope? envelope = null,
        bool legacyMayFallback = false)
    {
        Admission = admission;
        RootOwnership = rootOwnership;
        Envelope = envelope;
        LegacyMayFallback = legacyMayFallback;
    }
}

internal interface ILivingPromptProviderPort
{
    LivingPromptProviderReceipt Dispatch(LivingPromptEnvelope envelope);
}

internal interface ILivingPromptAttemptProviderPort
    : ILivingPromptProviderPort
{
    LivingPromptProviderReceipt DispatchAttempt(
        LivingPromptDispatchAttempt attempt);
}

internal sealed class LivingPromptDispatchAttempt
{
    internal string AttemptId { get; }
    internal LivingPromptEnvelope Envelope { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity =>
        Envelope.RuntimeIdentity;
    internal string SessionId => Envelope.SessionId;
    internal Guid SituationId => Envelope.SituationId;
    internal long SituationRevision => Envelope.SituationRevision;
    internal string ProviderRouteId => Envelope.ProviderRouteId;
    internal DateTimeOffset AttemptedAt { get; }
    internal int Ordinal => 1;

    internal LivingPromptDispatchAttempt(
        LivingPromptEnvelope envelope,
        DateTimeOffset attemptedAt)
    {
        Envelope = envelope
            ?? throw new ArgumentNullException(nameof(envelope));
        AttemptedAt = LivingContinuityContractGuard.Timestamp(
            attemptedAt,
            nameof(attemptedAt));
        AttemptId = "d025-attempt-" + LivingPromptHash.Of(
            envelope.EnvelopeId
            + "|"
            + AttemptedAt.UtcDateTime.Ticks.ToString(
                CultureInfo.InvariantCulture))[..32];
    }
}

internal sealed class LivingPromptProviderReceipt
{
    internal string ProviderRequestId { get; }
    internal string AttemptId { get; }
    internal string EnvelopeId { get; }
    internal string ProviderRouteId { get; }
    internal DateTimeOffset AcceptedAt { get; }

    internal LivingPromptProviderReceipt(
        string providerRequestId,
        LivingPromptDispatchAttempt attempt,
        DateTimeOffset acceptedAt)
    {
        ProviderRequestId = LivingContinuityContractGuard.Identifier(
            providerRequestId,
            nameof(providerRequestId));
        if (attempt == null)
            throw new ArgumentNullException(nameof(attempt));
        AttemptId = attempt.AttemptId;
        EnvelopeId = attempt.Envelope.EnvelopeId;
        ProviderRouteId = attempt.ProviderRouteId;
        AcceptedAt = LivingContinuityContractGuard.Timestamp(
            acceptedAt,
            nameof(acceptedAt));
    }

    internal LivingPromptProviderReceipt(
        string providerRequestId,
        LivingPromptEnvelope envelope,
        DateTimeOffset acceptedAt)
    {
        ProviderRequestId = LivingContinuityContractGuard.Identifier(
            providerRequestId,
            nameof(providerRequestId));
        if (envelope == null)
            throw new ArgumentNullException(nameof(envelope));
        AttemptId = string.Empty;
        EnvelopeId = envelope.EnvelopeId;
        ProviderRouteId = envelope.ProviderRouteId;
        AcceptedAt = LivingContinuityContractGuard.Timestamp(
            acceptedAt,
            nameof(acceptedAt));
    }
}

internal sealed class LivingPromptCompletionRecord
{
    internal string CompletionId { get; }
    internal string ProviderRequestId { get; }
    internal string EnvelopeId { get; }
    internal string AuthorizationFingerprint { get; }
    internal LivingPromptSourceKind SourceKind { get; }
    internal OutcomeStatus Status { get; }
    internal DateTimeOffset CompletedAt { get; }
    internal bool RecordOnly => true;
    internal bool CreatesProactiveCandidate => false;
    internal bool Learns => false;

    internal LivingPromptCompletionRecord(
        string completionId,
        string providerRequestId,
        LivingPromptEnvelope envelope,
        OutcomeStatus status,
        DateTimeOffset completedAt)
    {
        CompletionId = LivingContinuityContractGuard.Identifier(
            completionId,
            nameof(completionId));
        ProviderRequestId = LivingContinuityContractGuard.Identifier(
            providerRequestId,
            nameof(providerRequestId));
        if (envelope == null)
            throw new ArgumentNullException(nameof(envelope));
        if (!Enum.IsDefined(typeof(OutcomeStatus), status)
            || status is OutcomeStatus.Unknown
                or OutcomeStatus.Started
                or OutcomeStatus.ConsentPending)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        EnvelopeId = envelope.EnvelopeId;
        AuthorizationFingerprint = envelope.AuthorizationFingerprint;
        SourceKind = envelope.SourceKind;
        Status = status;
        CompletedAt = LivingContinuityContractGuard.Timestamp(
            completedAt,
            nameof(completedAt));
    }
}

internal static class LivingPromptCompletionProjector
{
    internal static LivingPromptCompletionRecord Project(
        string completionId,
        LivingPromptDispatchAttempt attempt,
        LivingPromptProviderReceipt receipt,
        OutcomeStatus status,
        DateTimeOffset completedAt)
    {
        if (attempt == null)
            throw new ArgumentNullException(nameof(attempt));
        if (receipt == null)
            throw new ArgumentNullException(nameof(receipt));
        if (!string.Equals(
                receipt.AttemptId,
                attempt.AttemptId,
                StringComparison.Ordinal)
            || !string.Equals(
                receipt.EnvelopeId,
                attempt.Envelope.EnvelopeId,
                StringComparison.Ordinal)
            || !string.Equals(
                receipt.ProviderRouteId,
                attempt.ProviderRouteId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Completion receipt must belong to the exact immutable "
                + "dispatch attempt.",
                nameof(receipt));
        }
        return new LivingPromptCompletionRecord(
            completionId,
            receipt.ProviderRequestId,
            attempt.Envelope,
            status,
            completedAt);
    }
}

internal sealed class LivingPromptCompositionCounters
{
    internal int PromptCompositions { get; private set; }
    internal int ProviderDispatches { get; private set; }
    internal int ProviderRetries => 0;
    internal int LegacyFallbacks => 0;
    internal int DecisionWrites => 0;
    internal int CharacterWrites => 0;
    internal int RelationshipWrites => 0;
    internal int MemoryWrites => 0;
    internal int EvidenceCalls => 0;
    internal int ReasoningCalls => 0;
    internal int QueueWrites => 0;
    internal int MotionExecutions => 0;
    internal int CapabilityExecutions => 0;
    internal int ProactiveCandidatesCreated => 0;
    internal bool UnauthorizedOutputsAreZero =>
        ProviderRetries == 0
        && LegacyFallbacks == 0
        && DecisionWrites == 0
        && CharacterWrites == 0
        && RelationshipWrites == 0
        && MemoryWrites == 0
        && EvidenceCalls == 0
        && ReasoningCalls == 0
        && QueueWrites == 0
        && MotionExecutions == 0
        && CapabilityExecutions == 0
        && ProactiveCandidatesCreated == 0;

    internal void RecordComposition() => PromptCompositions++;
    internal void RecordDispatch() => ProviderDispatches++;
}

internal sealed class LivingPromptCompositionHost
{
    private static readonly ConditionalWeakTable<
        ContinuityOwnershipRegistry,
        object> OwnershipLocks = new();

    private sealed class Transaction
    {
        internal LivingPromptRootOwnership Root { get; }
        internal string AuthorizationFingerprint { get; }
        internal LivingPromptEnvelope? Envelope { get; set; }
        internal LivingPromptDispatchAttempt? DispatchAttempt { get; set; }
        internal LivingPromptProviderReceipt? Receipt { get; set; }
        internal LivingPromptCompletionRecord? Completion { get; set; }
        internal bool DispatchAttempted { get; set; }
        internal bool Terminal { get; set; }

        internal Transaction(
            LivingPromptRootOwnership root,
            string authorizationFingerprint)
        {
            Root = root;
            AuthorizationFingerprint = authorizationFingerprint
                ?? throw new ArgumentNullException(
                    nameof(authorizationFingerprint));
        }
    }

    private readonly object _sync = new();
    private readonly LivingPromptCompositionPolicy _policy;
    private readonly CharacterRuntimeIdentity _runtimeIdentity;
    private readonly ContinuityOwnershipRegistry _ownership;
    private readonly object _ownershipSync;
    private readonly Dictionary<string, Transaction> _transactions =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _claimedAuthorizations =
        new(StringComparer.Ordinal);
    private readonly LivingPromptCompositionCounters _counters = new();

    internal LivingPromptCompositionHost(
        LivingPromptCompositionPolicy policy,
        CharacterRuntimeIdentity runtimeIdentity,
        ContinuityOwnershipRegistry ownership)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _runtimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        _ownership = ownership
            ?? throw new ArgumentNullException(nameof(ownership));
        _ownershipSync = OwnershipLocks.GetValue(
            _ownership,
            static _ => new object());
    }

    internal int ActiveCount
    {
        get
        {
            lock (_sync)
                return _transactions.Values.Count(item => !item.Terminal);
        }
    }

    internal LivingPromptCompositionCounters Counters => _counters;

    internal LivingPromptCompositionResult TryBegin(
        string compositionId,
        LivingPromptSourceAuthorization authorization,
        CharacterRuntimeIdentity activeRuntime,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        DateTimeOffset now)
    {
        compositionId = LivingContinuityContractGuard.Identifier(
            compositionId,
            nameof(compositionId));
        if (authorization == null)
            throw new ArgumentNullException(nameof(authorization));
        ValidateActiveArguments(
            activeRuntime,
            activeSessionId,
            activeSituationId,
            activeSituationRevision);
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));

        lock (_sync)
        {
            if (!_policy.Enabled)
            {
                return Stop(
                    LivingPromptCompositionAdmission.Disabled,
                    legacyMayFallback: true);
            }
            if (!_runtimeIdentity.Matches(activeRuntime))
            {
                return Stop(
                    LivingPromptCompositionAdmission.RuntimeMismatch);
            }
            var validation = ValidateAuthorization(
                authorization,
                activeRuntime,
                activeSessionId,
                activeSituationId,
                activeSituationRevision,
                now);
            if (validation.HasValue)
                return Stop(validation.Value);
            if (_transactions.ContainsKey(compositionId))
                return Stop(LivingPromptCompositionAdmission.Duplicate);
            var authorizationFingerprint = authorization.Fingerprint();
            if (_claimedAuthorizations.Contains(authorizationFingerprint))
                return Stop(LivingPromptCompositionAdmission.Duplicate);

            var expiry = authorization.ExpiresAt;
            var request = new ContinuityOwnershipRequest(
                "d025-prompt-" + compositionId,
                ContinuityOwnershipDomain.PromptAssembly,
                ContinuityOwner.LivingContinuityOwned,
                authorization.RequestIdentity,
                authorization.SessionId,
                authorization.SituationId,
                authorization.SituationRevision,
                authorization.RuntimeIdentity,
                now,
                expiry);
            ContinuityOwnershipRecord record;
            lock (_ownershipSync)
                record = _ownership.Acquire(request, activeRuntime, now);
            if (record.Disposition
                    != ContinuityOwnershipDisposition.Acquired
                || record.IsTerminal)
            {
                return Stop(
                    LivingPromptCompositionAdmission.RootOwnershipRejected,
                    new LivingPromptRootOwnership(request, record));
            }
            var root = new LivingPromptRootOwnership(request, record);
            _claimedAuthorizations.Add(authorizationFingerprint);
            _transactions.Add(
                compositionId,
                new Transaction(root, authorizationFingerprint));
            return Stop(
                LivingPromptCompositionAdmission.RootOwnershipAcquired,
                root);
        }
    }

    internal LivingPromptCompositionResult TryCompose(
        LivingPromptCompositionRequest request,
        CharacterRuntimeIdentity activeRuntime,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        string activeProviderRouteId,
        DateTimeOffset now)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        ValidateActiveArguments(
            activeRuntime,
            activeSessionId,
            activeSituationId,
            activeSituationRevision);
        activeProviderRouteId = LivingContinuityContractGuard.Identifier(
            activeProviderRouteId,
            nameof(activeProviderRouteId));
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));

        lock (_sync)
        {
            if (!_policy.Enabled)
                return Stop(LivingPromptCompositionAdmission.Disabled);
            if (!_runtimeIdentity.Matches(activeRuntime))
            {
                return Stop(
                    LivingPromptCompositionAdmission.RuntimeMismatch);
            }
            if (!_transactions.TryGetValue(
                    request.CompositionId,
                    out var transaction))
            {
                return Stop(
                    LivingPromptCompositionAdmission.RootOwnershipMismatch);
            }
            if (transaction.Terminal
                || transaction.Envelope != null)
            {
                return Stop(LivingPromptCompositionAdmission.Duplicate);
            }
            if (!string.Equals(
                    transaction.AuthorizationFingerprint,
                    request.Authorization.Fingerprint(),
                    StringComparison.Ordinal))
            {
                return Retire(
                    transaction,
                    LivingPromptCompositionAdmission.AuthorizationMismatch,
                    now);
            }
            if (!string.Equals(
                    request.CurrentRequest.TurnId,
                    request.Authorization.RequestIdentity,
                    StringComparison.Ordinal)
                || (request.Authorization.SourceKind
                        == LivingPromptSourceKind.PlayerTriggered
                    && !string.Equals(
                        request.CurrentRequest.ContentHash,
                        request.Authorization.SemanticSourceHash,
                        StringComparison.Ordinal)))
            {
                return Retire(
                    transaction,
                    LivingPromptCompositionAdmission.AuthorizationMismatch,
                    now);
            }
            var validation = ValidateAuthorization(
                request.Authorization,
                activeRuntime,
                activeSessionId,
                activeSituationId,
                activeSituationRevision,
                now);
            if (validation.HasValue)
                return Retire(transaction, validation.Value, now);
            if (!string.Equals(
                    request.ProviderRouteId,
                    activeProviderRouteId,
                    StringComparison.Ordinal))
            {
                return Retire(
                    transaction,
                    LivingPromptCompositionAdmission.ProviderMismatch,
                    now);
            }
            if (!RootMatches(request, transaction.Root))
            {
                return Retire(
                    transaction,
                    LivingPromptCompositionAdmission.RootOwnershipMismatch,
                    now);
            }
            if (request.WorkingContext.Count
                    > LivingPromptCompositionPolicy
                        .MaximumWorkingContextTurns)
            {
                return Retire(
                    transaction,
                    LivingPromptCompositionAdmission
                        .WorkingContextRejected,
                    now);
            }
            if (!TurnContentMatches(request))
            {
                return Retire(
                    transaction,
                    LivingPromptCompositionAdmission.TurnContentRejected,
                    now);
            }
            if (!WorkingContextAuthorizationMatches(request))
            {
                return Retire(
                    transaction,
                    LivingPromptCompositionAdmission
                        .WorkingContextRejected,
                    now);
            }
            if (!CharacterSlotMatches(request, now))
            {
                return Retire(
                    transaction,
                    LivingPromptCompositionAdmission.CharacterSlotRejected,
                    now);
            }
            if (!MemoryPacketMatches(request))
            {
                return Retire(
                    transaction,
                    LivingPromptCompositionAdmission.MemoryPacketRejected,
                    now);
            }

            ContinuityOwnershipRecord consumed;
            lock (_ownershipSync)
            {
                consumed = _ownership.Consume(
                    transaction.Root.Request,
                    activeRuntime,
                    activeSituationId,
                    activeSituationRevision,
                    now);
            }
            if (!consumed.WasConsumed)
            {
                transaction.Terminal = true;
                return Stop(
                    LivingPromptCompositionAdmission.RootOwnershipRejected,
                    new LivingPromptRootOwnership(
                        transaction.Root.Request,
                        consumed));
            }

            var working = DeduplicateWorking(
                request.WorkingContext,
                request.CurrentRequest);
            var frozenParts = new LivingPromptFrozenParts(request);
            var envelope = new LivingPromptEnvelope(
                request,
                working,
                frozenParts);
            transaction.Envelope = envelope;
            _counters.RecordComposition();
            return Stop(
                LivingPromptCompositionAdmission.Composed,
                transaction.Root,
                envelope);
        }
    }

    internal LivingPromptCompositionAdmission TryDispatch(
        LivingPromptEnvelope envelope,
        CharacterRuntimeIdentity activeRuntime,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        string activeProviderRouteId,
        DateTimeOffset now,
        ILivingPromptProviderPort providerPort,
        out LivingPromptProviderReceipt? receipt)
    {
        if (envelope == null)
            throw new ArgumentNullException(nameof(envelope));
        if (providerPort == null)
            throw new ArgumentNullException(nameof(providerPort));
        ValidateActiveArguments(
            activeRuntime,
            activeSessionId,
            activeSituationId,
            activeSituationRevision);
        activeProviderRouteId = LivingContinuityContractGuard.Identifier(
            activeProviderRouteId,
            nameof(activeProviderRouteId));
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));
        receipt = null;

        lock (_sync)
        {
            if (!_policy.Enabled)
                return LivingPromptCompositionAdmission.Disabled;
            if (!_runtimeIdentity.Matches(activeRuntime))
                return LivingPromptCompositionAdmission.RuntimeMismatch;
            if (!_transactions.TryGetValue(
                    envelope.CompositionId,
                    out var transaction)
                || transaction.Envelope == null
                || !string.Equals(
                    transaction.Envelope.EnvelopeId,
                    envelope.EnvelopeId,
                    StringComparison.Ordinal))
            {
                return LivingPromptCompositionAdmission.AuthorizationMismatch;
            }
            if (transaction.Terminal || transaction.DispatchAttempted)
                return LivingPromptCompositionAdmission.Duplicate;
            var validation = ValidateEnvelope(
                envelope,
                activeRuntime,
                activeSessionId,
                activeSituationId,
                activeSituationRevision,
                activeProviderRouteId,
                now);
            if (validation.HasValue)
            {
                transaction.Terminal = true;
                return validation.Value;
            }

            transaction.DispatchAttempted = true;
            var attempt = new LivingPromptDispatchAttempt(envelope, now);
            transaction.DispatchAttempt = attempt;
            try
            {
                var returned = providerPort
                        is ILivingPromptAttemptProviderPort attemptPort
                    ? attemptPort.DispatchAttempt(attempt)
                    : providerPort.Dispatch(envelope);
                var accepted = returned
                    ?? throw new InvalidOperationException(
                        "Provider port returned no receipt.");
                if ((accepted.AttemptId.Length > 0
                        && !string.Equals(
                            accepted.AttemptId,
                            attempt.AttemptId,
                            StringComparison.Ordinal))
                    || !string.Equals(
                        accepted.EnvelopeId,
                        envelope.EnvelopeId,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        accepted.ProviderRouteId,
                        envelope.ProviderRouteId,
                        StringComparison.Ordinal)
                    || accepted.AcceptedAt < envelope.CreatedAt
                    || accepted.AcceptedAt > now)
                {
                    transaction.Terminal = true;
                    return LivingPromptCompositionAdmission.DispatchRejected;
                }
                var boundReceipt = new LivingPromptProviderReceipt(
                    accepted.ProviderRequestId,
                    attempt,
                    accepted.AcceptedAt);
                transaction.Receipt = boundReceipt;
                _counters.RecordDispatch();
                receipt = boundReceipt;
                return LivingPromptCompositionAdmission.DispatchAccepted;
            }
            catch
            {
                transaction.Terminal = true;
                return LivingPromptCompositionAdmission.Faulted;
            }
        }
    }

    internal LivingPromptCompositionAdmission RecordCompletion(
        LivingPromptEnvelope envelope,
        LivingPromptProviderReceipt receipt,
        string completionId,
        OutcomeStatus status,
        CharacterRuntimeIdentity activeRuntime,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        DateTimeOffset now,
        out LivingPromptCompletionRecord? completion)
    {
        if (envelope == null)
            throw new ArgumentNullException(nameof(envelope));
        if (receipt == null)
            throw new ArgumentNullException(nameof(receipt));
        ValidateActiveArguments(
            activeRuntime,
            activeSessionId,
            activeSituationId,
            activeSituationRevision);
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));
        completion = null;

        lock (_sync)
        {
            if (!_runtimeIdentity.Matches(activeRuntime))
            {
                return LivingPromptCompositionAdmission
                    .CompletionRejected;
            }
            if (!_transactions.TryGetValue(
                    envelope.CompositionId,
                    out var transaction)
                || transaction.Envelope == null
                || transaction.DispatchAttempt == null
                || transaction.Receipt == null)
            {
                return LivingPromptCompositionAdmission.CompletionRejected;
            }
            if (transaction.Terminal
                || transaction.Completion != null
                || !string.Equals(
                    transaction.Envelope.EnvelopeId,
                    envelope.EnvelopeId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    transaction.DispatchAttempt.AttemptId,
                    receipt.AttemptId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    transaction.Receipt.ProviderRequestId,
                    receipt.ProviderRequestId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    transaction.Receipt.ProviderRouteId,
                    receipt.ProviderRouteId,
                    StringComparison.Ordinal)
                || transaction.Receipt.AcceptedAt != receipt.AcceptedAt
                || !string.Equals(
                    receipt.EnvelopeId,
                    envelope.EnvelopeId,
                    StringComparison.Ordinal))
            {
                return LivingPromptCompositionAdmission.CompletionRejected;
            }
            var validation = ValidateEnvelope(
                envelope,
                activeRuntime,
                activeSessionId,
                activeSituationId,
                activeSituationRevision,
                envelope.ProviderRouteId,
                now,
                allowAfterAuthorizationExpiry: true);
            if (validation.HasValue)
            {
                transaction.Terminal = true;
                return LivingPromptCompositionAdmission.CompletionRejected;
            }
            if (now > envelope.ExpiresAt.AddMinutes(30))
            {
                transaction.Terminal = true;
                return LivingPromptCompositionAdmission.CompletionRejected;
            }

            completion = LivingPromptCompletionProjector.Project(
                completionId,
                transaction.DispatchAttempt,
                receipt,
                status,
                now);
            transaction.Completion = completion;
            transaction.Terminal = true;
            return LivingPromptCompositionAdmission.CompletionRecorded;
        }
    }

    internal LivingPromptCompositionAdmission Cancel(
        string compositionId,
        ContinuityRetirementReason reason,
        DateTimeOffset now)
    {
        compositionId = LivingContinuityContractGuard.Identifier(
            compositionId,
            nameof(compositionId));
        if (!Enum.IsDefined(typeof(ContinuityRetirementReason), reason)
            || reason == ContinuityRetirementReason.Consumed)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));
        lock (_sync)
        {
            if (!_transactions.TryGetValue(compositionId, out var transaction)
                || transaction.Terminal)
            {
                return LivingPromptCompositionAdmission.CompletionRejected;
            }
            if (!transaction.Root.Record.IsTerminal)
            {
                lock (_ownershipSync)
                {
                    _ownership.Retire(
                        transaction.Root.Request,
                        reason,
                        now);
                }
            }
            transaction.Terminal = true;
            return LivingPromptCompositionAdmission.Cancelled;
        }
    }

    private LivingPromptCompositionResult Retire(
        Transaction transaction,
        LivingPromptCompositionAdmission admission,
        DateTimeOffset now)
    {
        if (!transaction.Root.Record.IsTerminal)
        {
            lock (_ownershipSync)
            {
                _ownership.Retire(
                    transaction.Root.Request,
                    RetirementReason(admission),
                    now);
            }
        }
        transaction.Terminal = true;
        return Stop(admission, transaction.Root);
    }

    private static LivingPromptCompositionAdmission?
        ValidateAuthorization(
            LivingPromptSourceAuthorization authorization,
            CharacterRuntimeIdentity activeRuntime,
            string activeSessionId,
            Guid activeSituationId,
            long activeSituationRevision,
            DateTimeOffset now)
    {
        if (!authorization.RuntimeIdentity.Matches(activeRuntime))
            return LivingPromptCompositionAdmission.RuntimeMismatch;
        if (!string.Equals(
                authorization.SessionId,
                activeSessionId,
                StringComparison.Ordinal))
        {
            return LivingPromptCompositionAdmission.SessionMismatch;
        }
        if (authorization.SituationId != activeSituationId
            || authorization.SituationRevision != activeSituationRevision)
        {
            return LivingPromptCompositionAdmission.SituationMismatch;
        }
        if (now < authorization.IssuedAt)
            return LivingPromptCompositionAdmission.NotYetValid;
        if (now >= authorization.ExpiresAt)
            return LivingPromptCompositionAdmission.Expired;
        return null;
    }

    private static LivingPromptCompositionAdmission? ValidateEnvelope(
        LivingPromptEnvelope envelope,
        CharacterRuntimeIdentity activeRuntime,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        string activeProviderRouteId,
        DateTimeOffset now,
        bool allowAfterAuthorizationExpiry = false)
    {
        if (!envelope.RuntimeIdentity.Matches(activeRuntime))
            return LivingPromptCompositionAdmission.RuntimeMismatch;
        if (!string.Equals(
                envelope.SessionId,
                activeSessionId,
                StringComparison.Ordinal))
        {
            return LivingPromptCompositionAdmission.SessionMismatch;
        }
        if (envelope.SituationId != activeSituationId
            || envelope.SituationRevision != activeSituationRevision)
        {
            return LivingPromptCompositionAdmission.SituationMismatch;
        }
        if (!string.Equals(
                envelope.ProviderRouteId,
                activeProviderRouteId,
                StringComparison.Ordinal))
        {
            return LivingPromptCompositionAdmission.ProviderMismatch;
        }
        if (now < envelope.CreatedAt)
            return LivingPromptCompositionAdmission.NotYetValid;
        if (!allowAfterAuthorizationExpiry && now >= envelope.ExpiresAt)
            return LivingPromptCompositionAdmission.Expired;
        return null;
    }

    private static bool RootMatches(
        LivingPromptCompositionRequest request,
        LivingPromptRootOwnership expected)
        => ReferenceEquals(
                request.RootOwnership.Request,
                expected.Request)
            && ReferenceEquals(
                request.RootOwnership.Record,
                expected.Record)
            && expected.Record.Disposition
                == ContinuityOwnershipDisposition.Acquired
            && !expected.Record.IsTerminal
            && expected.Request.Domain
                == ContinuityOwnershipDomain.PromptAssembly
            && expected.Request.RequestedOwner
                == ContinuityOwner.LivingContinuityOwned
            && string.Equals(
                expected.Request.OccurrenceId,
                request.Authorization.RequestIdentity,
                StringComparison.Ordinal)
            && string.Equals(
                expected.Request.SessionId,
                request.Authorization.SessionId,
                StringComparison.Ordinal)
            && expected.Request.SituationId
                == request.Authorization.SituationId
            && expected.Request.SituationRevision
                == request.Authorization.SituationRevision
            && expected.Request.RuntimeIdentity.Matches(
                request.Authorization.RuntimeIdentity);

    private static bool MemoryPacketMatches(
        LivingPromptCompositionRequest request)
    {
        var packet = request.MemoryPacket;
        if (packet == null)
            return true;
        if (!packet.HasValidIntegrity()
            || !ReferenceEquals(
                packet.PromptOwnership,
                request.RootOwnership.Record)
            || !string.Equals(
                packet.PromptOwnershipLeaseId,
                request.RootOwnership.Request.LeaseId,
                StringComparison.Ordinal)
            || !string.Equals(
                packet.PromptOwnershipOccurrenceId,
                request.Authorization.RequestIdentity,
                StringComparison.Ordinal)
            || !string.Equals(
                packet.RequestId,
                request.Authorization.RequestIdentity,
                StringComparison.Ordinal)
            || !string.Equals(
                packet.ProviderRouteId,
                request.ProviderRouteId,
                StringComparison.Ordinal)
            || !packet.RuntimeIdentity.Matches(
                request.Authorization.RuntimeIdentity)
            || !string.Equals(
                packet.LivingSnapshotFingerprint,
                request.LivingSnapshotFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                packet.QueryFingerprint,
                request.CurrentRequest.ContentHash,
                StringComparison.OrdinalIgnoreCase)
            || packet.CreatedAt > request.RequestedAt)
        {
            return false;
        }

        var seenIds = new HashSet<string>(
            request.WorkingContext.Select(item => item.TurnId),
            StringComparer.Ordinal);
        seenIds.Add(request.CurrentRequest.TurnId);
        var seenHashes = new HashSet<string>(
            request.WorkingContext.Select(item => item.ContentHash),
            StringComparer.OrdinalIgnoreCase);
        seenHashes.Add(request.CurrentRequest.ContentHash);
        return packet.Items.All(
            item => !seenIds.Contains(item.EventId.ToString("N"))
                && !seenHashes.Contains(item.ContentHash));
    }

    private static bool TurnContentMatches(
        LivingPromptCompositionRequest request)
    {
        if (!string.Equals(
                LivingPromptHash.Of(request.CurrentRequest.Text),
                request.CurrentRequest.ContentHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        foreach (var turn in request.WorkingContext)
        {
            if (!string.Equals(
                    LivingPromptHash.Of(turn.Text),
                    turn.ContentHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    private static bool WorkingContextAuthorizationMatches(
        LivingPromptCompositionRequest request)
    {
        var memoryTurns = request.WorkingContext
            .Where(
                item => item.Origin
                    == LivingPromptTurnOrigin.MemoryWorkingSet)
            .ToArray();
        if (memoryTurns.Length == 0)
            return request.WorkingContextAuthorization == null;
        if (memoryTurns.Length != request.WorkingContext.Count
            || request.WorkingContextAuthorization == null)
        {
            return false;
        }
        return request.WorkingContextAuthorization.Matches(
            request.CompositionId,
            request.ProviderRouteId,
            request.Authorization.RuntimeIdentity,
            request.WorkingContext,
            request.RequestedAt);
    }

    private static bool CharacterSlotMatches(
        LivingPromptCompositionRequest request,
        DateTimeOffset now)
    {
        var slot = request.CharacterSlot;
        if (slot.Owner == LivingPromptCharacterSlotOwner.None)
        {
            return slot.Fragment.Length == 0
                && !slot.PromptOwnerLeaseId.HasValue
                && !slot.AuthorizationId.HasValue
                && !slot.TurnId.HasValue
                && slot.ProviderRouteId == null
                && slot.PromptBaselineSha256 == null
                && !slot.ValidUntil.HasValue
                && !slot.SourceKind.HasValue
                && slot.SourceAuthorizationId == null
                && slot.SourceRequestIdentity == null;
        }
        if (slot.Owner != LivingPromptCharacterSlotOwner.Legacy
            && slot.Owner != LivingPromptCharacterSlotOwner.CharacterRuntime)
        {
            return false;
        }
        var common = string.Equals(
                slot.ProviderRouteId,
                request.ProviderRouteId,
                StringComparison.Ordinal)
            && string.Equals(
                slot.PromptBaselineSha256,
                LivingPromptHash.Of(request.BaselinePrompt),
                StringComparison.OrdinalIgnoreCase)
            && slot.ValidUntil.HasValue
            && slot.ValidUntil.Value > now
            && slot.ValidUntil.Value <= request.Authorization.ExpiresAt;
        if (!common)
            return false;

        if (request.Authorization.SourceKind
            == LivingPromptSourceKind.ContextualProactiveChat)
        {
            return slot.Owner == LivingPromptCharacterSlotOwner.CharacterRuntime
                && slot.PromptOwnerLeaseId.HasValue
                && !slot.AuthorizationId.HasValue
                && !slot.TurnId.HasValue
                && slot.SourceKind
                    == LivingPromptSourceKind.ContextualProactiveChat
                && string.Equals(
                    slot.SourceAuthorizationId,
                    request.Authorization.AuthorizationId,
                    StringComparison.Ordinal)
                && string.Equals(
                    slot.SourceRequestIdentity,
                    request.Authorization.RequestIdentity,
                    StringComparison.Ordinal);
        }

        if (request.Authorization.SourceKind
                != LivingPromptSourceKind.PlayerTriggered
            || !Guid.TryParseExact(
                request.Authorization.AuthorizationId,
                "N",
                out var authorizationId)
            || !Guid.TryParseExact(
                request.Authorization.RequestIdentity,
                "N",
                out var turnId))
        {
            return false;
        }
        return slot.AuthorizationId == authorizationId
            && slot.TurnId == turnId
            && string.Equals(
                slot.ProviderRouteId,
                request.ProviderRouteId,
                StringComparison.Ordinal)
            && !slot.SourceKind.HasValue
            && slot.SourceAuthorizationId == null
            && slot.SourceRequestIdentity == null
            && (slot.Owner
                    == LivingPromptCharacterSlotOwner.CharacterRuntime
                ? slot.PromptOwnerLeaseId.HasValue
                : !slot.PromptOwnerLeaseId.HasValue);
    }

    private static IReadOnlyList<LivingPromptTurn> DeduplicateWorking(
        IReadOnlyList<LivingPromptTurn> working,
        LivingPromptTurn current)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal)
        {
            current.TurnId
        };
        var hashes = new HashSet<string>(StringComparer.Ordinal)
        {
            current.ContentHash
        };
        var result = new List<LivingPromptTurn>();
        foreach (var item in working)
        {
            if (!ids.Add(item.TurnId))
                continue;
            if (!hashes.Add(item.ContentHash))
                continue;
            result.Add(item);
        }
        return new ReadOnlyCollection<LivingPromptTurn>(result);
    }

    private static void ValidateActiveArguments(
        CharacterRuntimeIdentity activeRuntime,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision)
    {
        if (activeRuntime == null)
            throw new ArgumentNullException(nameof(activeRuntime));
        LivingContinuityContractGuard.Identifier(
            activeSessionId,
            nameof(activeSessionId));
        if (activeSituationId == Guid.Empty)
            throw new ArgumentException(
                "Situation identity cannot be empty.",
                nameof(activeSituationId));
        if (activeSituationRevision <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(activeSituationRevision));
    }

    private static ContinuityRetirementReason RetirementReason(
        LivingPromptCompositionAdmission admission)
        => admission switch
        {
            LivingPromptCompositionAdmission.RuntimeMismatch =>
                ContinuityRetirementReason.RuntimeGenerationChanged,
            LivingPromptCompositionAdmission.SessionMismatch =>
                ContinuityRetirementReason.SessionClosed,
            LivingPromptCompositionAdmission.SituationMismatch =>
                ContinuityRetirementReason.SituationRevisionChanged,
            LivingPromptCompositionAdmission.Expired =>
                ContinuityRetirementReason.Expired,
            _ => ContinuityRetirementReason.Faulted
        };

    private static LivingPromptCompositionResult Stop(
        LivingPromptCompositionAdmission admission,
        LivingPromptRootOwnership? root = null,
        LivingPromptEnvelope? envelope = null,
        bool legacyMayFallback = false)
        => new(
            admission,
            root,
            envelope,
            legacyMayFallback);
}

internal static class LivingPromptHash
{
    internal static string Of(string value)
        => Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        value ?? throw new ArgumentNullException(
                            nameof(value)))))
            .ToLowerInvariant();
}
