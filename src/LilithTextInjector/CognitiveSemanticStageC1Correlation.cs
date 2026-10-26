using System;
using System.Globalization;

namespace LilithTextInjector;

internal enum CognitiveSameCallAttestationAdmission
{
    Accepted = 0,
    SnapshotExpired = 1,
    PlayerTurnMismatch = 2,
    ProviderLifecycleMismatch = 3,
    RequestMismatch = 4,
    RouteMismatch = 5,
    PromptMismatch = 6,
    CompletionMismatch = 7,
    SituationMismatch = 8,
    RuntimeMismatch = 9,
    ModelMismatch = 10,
    ProtocolMismatch = 11,
    ResponseMismatch = 12
}

internal sealed class CognitiveSameCallSourceCapture
{
    internal CognitiveInputSnapshot Snapshot { get; }
    internal PlayerTurnOperationHandle PlayerTurnHandle { get; }
    internal string PreparedRequestId { get; }
    internal string PreparedRequestFingerprint { get; }
    internal string PreparedCurrentTurnHash { get; }
    internal string ProviderRouteId { get; }
    internal string PreparedSystemPromptHash { get; }
    internal string ProviderExecutionFingerprint { get; }
    internal string ProviderRouteFingerprint { get; }
    internal string ProviderPromptFingerprint { get; }
    internal string CompositionId { get; }
    internal string EnvelopeId { get; }
    internal string AuthorizationFingerprint { get; }
    internal string SessionId { get; }
    internal Guid SituationId { get; }
    internal string RuntimeFingerprint { get; }
    internal string EffectiveModelFingerprint { get; }
    internal string ProtocolInstructionFingerprint { get; }
    internal long ProviderRegistryGeneration { get; }
    internal long ProviderConfigurationGeneration { get; }
    internal string CaptureFingerprint { get; }

    internal CognitiveSameCallSourceCapture(
        CognitiveInputSnapshot snapshot,
        PlayerTurnOperationHandle playerTurnHandle,
        string preparedRequestId,
        string preparedRequestFingerprint,
        string preparedCurrentTurnHash,
        string providerRouteId,
        string preparedSystemPromptHash,
        string providerExecutionFingerprint,
        string providerRouteFingerprint,
        string providerPromptFingerprint,
        string compositionId,
        string envelopeId,
        string authorizationFingerprint,
        string sessionId,
        Guid situationId,
        string runtimeFingerprint,
        string effectiveModelFingerprint,
        string protocolInstructionFingerprint,
        long providerRegistryGeneration,
        long providerConfigurationGeneration)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        if (Snapshot.InputSource != CognitiveInputSource.PlayerTurnOperation)
        {
            throw new ArgumentException(
                "A same-call capture requires a PlayerTurnOperation snapshot.",
                nameof(snapshot));
        }
        PlayerTurnHandle = playerTurnHandle
            ?? throw new ArgumentNullException(nameof(playerTurnHandle));
        if (!ReferenceEquals(
                Snapshot.PlayerTurnSourceHandle,
                PlayerTurnHandle)
            || !string.Equals(
                PlayerTurnHandle.ProcessEpochId,
                Snapshot.ProcessEpochId,
                StringComparison.Ordinal)
            || PlayerTurnHandle.RuntimeGeneration
                != Snapshot.RuntimeGeneration
            || !string.Equals(
                PlayerTurnHandle.OperationId,
                Snapshot.OperationId,
                StringComparison.Ordinal)
            || PlayerTurnHandle.OperationGeneration
                != Snapshot.OperationGeneration)
        {
            throw new ArgumentException(
                "The PlayerTurn handle must be the source of the snapshot.",
                nameof(playerTurnHandle));
        }
        PreparedRequestId = CognitiveShadowContractGuard.Identifier(
            preparedRequestId,
            nameof(preparedRequestId));
        PreparedRequestFingerprint = CognitiveShadowContractGuard.Fingerprint(
            preparedRequestFingerprint,
            nameof(preparedRequestFingerprint));
        PreparedCurrentTurnHash = CognitiveShadowContractGuard.Fingerprint(
            preparedCurrentTurnHash,
            nameof(preparedCurrentTurnHash));
        if (!string.Equals(
                PreparedCurrentTurnHash,
                Snapshot.TurnHash,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Prepared current turn must match the exact Cognitive snapshot.",
                nameof(preparedCurrentTurnHash));
        }
        ProviderRouteId = CognitiveShadowContractGuard.Identifier(
            providerRouteId,
            nameof(providerRouteId));
        PreparedSystemPromptHash = CognitiveShadowContractGuard.Fingerprint(
            preparedSystemPromptHash,
            nameof(preparedSystemPromptHash));
        ProviderExecutionFingerprint =
            CognitiveShadowContractGuard.Fingerprint(
                providerExecutionFingerprint,
                nameof(providerExecutionFingerprint));
        ProviderRouteFingerprint = CognitiveShadowContractGuard.Fingerprint(
            providerRouteFingerprint,
            nameof(providerRouteFingerprint));
        ProviderPromptFingerprint = CognitiveShadowContractGuard.Fingerprint(
            providerPromptFingerprint,
            nameof(providerPromptFingerprint));
        CompositionId = CognitiveShadowContractGuard.Identifier(
            compositionId,
            nameof(compositionId));
        EnvelopeId = CognitiveShadowContractGuard.Identifier(
            envelopeId,
            nameof(envelopeId));
        AuthorizationFingerprint = CognitiveShadowContractGuard.Fingerprint(
            authorizationFingerprint,
            nameof(authorizationFingerprint));
        SessionId = CognitiveShadowContractGuard.Identifier(
            sessionId,
            nameof(sessionId));
        if (situationId == Guid.Empty)
            throw new ArgumentException("Situation identity cannot be empty.", nameof(situationId));
        SituationId = situationId;
        RuntimeFingerprint = CognitiveShadowContractGuard.Fingerprint(
            runtimeFingerprint,
            nameof(runtimeFingerprint));
        EffectiveModelFingerprint = CognitiveShadowContractGuard.Fingerprint(
            effectiveModelFingerprint,
            nameof(effectiveModelFingerprint));
        ProtocolInstructionFingerprint = CognitiveShadowContractGuard.Fingerprint(
            protocolInstructionFingerprint,
            nameof(protocolInstructionFingerprint));
        ProviderRegistryGeneration = CognitiveShadowContractGuard.Positive(
            providerRegistryGeneration,
            nameof(providerRegistryGeneration));
        ProviderConfigurationGeneration = CognitiveShadowContractGuard.Positive(
            providerConfigurationGeneration,
            nameof(providerConfigurationGeneration));
        CaptureFingerprint = CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                Snapshot.SnapshotId.ToString("N"),
                Snapshot.SnapshotFingerprint,
                PreparedRequestId,
                PreparedRequestFingerprint,
                PreparedCurrentTurnHash,
                ProviderRouteId,
                PreparedSystemPromptHash,
                ProviderExecutionFingerprint,
                ProviderRouteFingerprint,
                ProviderPromptFingerprint,
                CompositionId,
                EnvelopeId,
                AuthorizationFingerprint,
                SessionId,
                SituationId.ToString("N"),
                Snapshot.SituationRevision.ToString(CultureInfo.InvariantCulture),
                Snapshot.SituationFingerprint,
                RuntimeFingerprint,
                EffectiveModelFingerprint,
                ProtocolInstructionFingerprint,
                ProviderRegistryGeneration.ToString(CultureInfo.InvariantCulture),
                ProviderConfigurationGeneration.ToString(
                    CultureInfo.InvariantCulture)));
    }
}

internal sealed class CognitiveSameCallCompletionIdentity
{
    internal LivingPreparedCompletionDestination Destination { get; }
    internal OutcomeStatus Status { get; }
    internal string PromptCompositionCompletionId { get; }
    internal string ProviderReceiptId { get; }
    internal string PreparedRequestId { get; }
    internal string PreparedRequestFingerprint { get; }
    internal string ProviderRouteId { get; }
    internal string PreparedSystemPromptHash { get; }
    internal string ProviderExecutionCompletionId { get; }
    internal string ProviderExecutionFingerprint { get; }
    internal string ProviderRouteFingerprint { get; }
    internal string ProviderPromptFingerprint { get; }
    internal string CompositionId { get; }
    internal string EnvelopeId { get; }
    internal string AuthorizationFingerprint { get; }
    internal string SessionId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal string SituationFingerprint { get; }
    internal string RuntimeFingerprint { get; }
    internal string EffectiveModelFingerprint { get; }
    internal string ProtocolInstructionFingerprint { get; }
    internal long ProviderRegistryGeneration { get; }
    internal long ProviderConfigurationGeneration { get; }
    internal string ResponseFingerprint { get; }
    internal string ProviderNormalizedResponseFingerprint { get; }
    internal string CompletionFingerprint { get; }
    internal bool ContainsRawContent => false;

    internal CognitiveSameCallCompletionIdentity(
        LivingPreparedCompletionDestination destination,
        OutcomeStatus status,
        string promptCompositionCompletionId,
        string providerReceiptId,
        string preparedRequestId,
        string preparedRequestFingerprint,
        string providerRouteId,
        string preparedSystemPromptHash,
        string providerExecutionCompletionId,
        string providerExecutionFingerprint,
        string providerRouteFingerprint,
        string providerPromptFingerprint,
        string compositionId,
        string envelopeId,
        string authorizationFingerprint,
        string sessionId,
        Guid situationId,
        long situationRevision,
        string situationFingerprint,
        string runtimeFingerprint,
        string effectiveModelFingerprint,
        string protocolInstructionFingerprint,
        long providerRegistryGeneration,
        long providerConfigurationGeneration,
        string responseFingerprint,
        string providerNormalizedResponseFingerprint)
    {
        Destination = CognitiveShadowContractGuard.Enum(
            destination,
            nameof(destination));
        Status = CognitiveShadowContractGuard.Enum(status, nameof(status));
        PromptCompositionCompletionId = CognitiveShadowContractGuard.Identifier(
            promptCompositionCompletionId,
            nameof(promptCompositionCompletionId));
        ProviderReceiptId = CognitiveShadowContractGuard.Identifier(
            providerReceiptId,
            nameof(providerReceiptId));
        PreparedRequestId = CognitiveShadowContractGuard.Identifier(
            preparedRequestId,
            nameof(preparedRequestId));
        PreparedRequestFingerprint = CognitiveShadowContractGuard.Fingerprint(
            preparedRequestFingerprint,
            nameof(preparedRequestFingerprint));
        ProviderRouteId = CognitiveShadowContractGuard.Identifier(
            providerRouteId,
            nameof(providerRouteId));
        PreparedSystemPromptHash = CognitiveShadowContractGuard.Fingerprint(
            preparedSystemPromptHash,
            nameof(preparedSystemPromptHash));
        ProviderExecutionCompletionId = CognitiveShadowContractGuard.Identifier(
            providerExecutionCompletionId,
            nameof(providerExecutionCompletionId));
        ProviderExecutionFingerprint = CognitiveShadowContractGuard.Fingerprint(
            providerExecutionFingerprint,
            nameof(providerExecutionFingerprint));
        ProviderRouteFingerprint = CognitiveShadowContractGuard.Fingerprint(
            providerRouteFingerprint,
            nameof(providerRouteFingerprint));
        ProviderPromptFingerprint = CognitiveShadowContractGuard.Fingerprint(
            providerPromptFingerprint,
            nameof(providerPromptFingerprint));
        CompositionId = CognitiveShadowContractGuard.Identifier(
            compositionId,
            nameof(compositionId));
        EnvelopeId = CognitiveShadowContractGuard.Identifier(
            envelopeId,
            nameof(envelopeId));
        AuthorizationFingerprint = CognitiveShadowContractGuard.Fingerprint(
            authorizationFingerprint,
            nameof(authorizationFingerprint));
        SessionId = CognitiveShadowContractGuard.Identifier(
            sessionId,
            nameof(sessionId));
        if (situationId == Guid.Empty)
            throw new ArgumentException("Situation identity cannot be empty.", nameof(situationId));
        SituationId = situationId;
        SituationRevision = CognitiveShadowContractGuard.Positive(
            situationRevision,
            nameof(situationRevision));
        SituationFingerprint = CognitiveShadowContractGuard.Fingerprint(
            situationFingerprint,
            nameof(situationFingerprint));
        RuntimeFingerprint = CognitiveShadowContractGuard.Fingerprint(
            runtimeFingerprint,
            nameof(runtimeFingerprint));
        EffectiveModelFingerprint = CognitiveShadowContractGuard.Fingerprint(
            effectiveModelFingerprint,
            nameof(effectiveModelFingerprint));
        ProtocolInstructionFingerprint = CognitiveShadowContractGuard.Fingerprint(
            protocolInstructionFingerprint,
            nameof(protocolInstructionFingerprint));
        ProviderRegistryGeneration = CognitiveShadowContractGuard.Positive(
            providerRegistryGeneration,
            nameof(providerRegistryGeneration));
        ProviderConfigurationGeneration = CognitiveShadowContractGuard.Positive(
            providerConfigurationGeneration,
            nameof(providerConfigurationGeneration));
        ResponseFingerprint = CognitiveShadowContractGuard.Fingerprint(
            responseFingerprint,
            nameof(responseFingerprint));
        ProviderNormalizedResponseFingerprint =
            CognitiveShadowContractGuard.Fingerprint(
                providerNormalizedResponseFingerprint,
                nameof(providerNormalizedResponseFingerprint));
        CompletionFingerprint = CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                ((int)Destination).ToString(CultureInfo.InvariantCulture),
                ((int)Status).ToString(CultureInfo.InvariantCulture),
                PromptCompositionCompletionId,
                ProviderReceiptId,
                PreparedRequestId,
                PreparedRequestFingerprint,
                ProviderRouteId,
                PreparedSystemPromptHash,
                ProviderExecutionCompletionId,
                ProviderExecutionFingerprint,
                ProviderRouteFingerprint,
                ProviderPromptFingerprint,
                CompositionId,
                EnvelopeId,
                AuthorizationFingerprint,
                SessionId,
                SituationId.ToString("N"),
                SituationRevision.ToString(CultureInfo.InvariantCulture),
                SituationFingerprint,
                RuntimeFingerprint,
                EffectiveModelFingerprint,
                ProtocolInstructionFingerprint,
                ProviderRegistryGeneration.ToString(
                    CultureInfo.InvariantCulture),
                ProviderConfigurationGeneration.ToString(
                    CultureInfo.InvariantCulture),
                ResponseFingerprint,
                ProviderNormalizedResponseFingerprint));
    }
}

internal sealed class CognitiveSameCallProviderAttestation
{
    internal Guid SnapshotId { get; }
    internal string SnapshotFingerprint { get; }
    internal PlayerTurnOperationHandle PlayerTurnSourceHandle { get; }
    internal string PlayerTurnReceiptId { get; }
    internal string PromptCompositionCompletionId { get; }
    internal string ProviderReceiptId { get; }
    internal string ProviderExecutionCompletionId { get; }
    internal string ProviderExecutionFingerprint { get; }
    internal string PreparedRequestId { get; }
    internal string PreparedRequestFingerprint { get; }
    internal string ProviderRouteId { get; }
    internal string EffectiveModelFingerprint { get; }
    internal string ProtocolInstructionFingerprint { get; }
    internal string ResponseFingerprint { get; }
    internal long ObservedAtMonotonicTicks { get; }
    internal string ReplayIdentityFingerprint { get; }
    internal string AttestationFingerprint { get; }
    internal bool ContainsRawContent => false;

    private CognitiveSameCallProviderAttestation(
        CognitiveSameCallSourceCapture capture,
        PlayerTurnProviderCompletionReceipt playerTurnReceipt,
        CognitiveSameCallCompletionIdentity completion,
        long observedAtMonotonicTicks)
    {
        SnapshotId = capture.Snapshot.SnapshotId;
        SnapshotFingerprint = capture.Snapshot.SnapshotFingerprint;
        PlayerTurnSourceHandle = capture.PlayerTurnHandle;
        PlayerTurnReceiptId = playerTurnReceipt.ReceiptId;
        PromptCompositionCompletionId =
            completion.PromptCompositionCompletionId;
        ProviderReceiptId = completion.ProviderReceiptId;
        ProviderExecutionCompletionId =
            completion.ProviderExecutionCompletionId;
        ProviderExecutionFingerprint =
            completion.ProviderExecutionFingerprint;
        PreparedRequestId = capture.PreparedRequestId;
        PreparedRequestFingerprint = capture.PreparedRequestFingerprint;
        ProviderRouteId = capture.ProviderRouteId;
        EffectiveModelFingerprint = capture.EffectiveModelFingerprint;
        ProtocolInstructionFingerprint =
            capture.ProtocolInstructionFingerprint;
        ResponseFingerprint = completion.ResponseFingerprint;
        ObservedAtMonotonicTicks = observedAtMonotonicTicks;
        ReplayIdentityFingerprint = CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                SnapshotId.ToString("N"),
                PlayerTurnReceiptId));
        AttestationFingerprint = CognitiveShadowFingerprint.Hash(
            string.Join(
                "|",
                ReplayIdentityFingerprint,
                capture.CaptureFingerprint,
                completion.CompletionFingerprint,
                ObservedAtMonotonicTicks.ToString(
                    CultureInfo.InvariantCulture)));
    }

    internal static CognitiveSameCallAttestationResult TryCreate(
        CognitiveSameCallSourceCapture capture,
        PlayerTurnProviderCompletionReceipt playerTurnReceipt,
        CognitiveSameCallCompletionIdentity completion,
        long observedAtMonotonicTicks)
    {
        var admission = CognitiveSameCallAttestor.Validate(
            capture,
            playerTurnReceipt,
            completion,
            observedAtMonotonicTicks);
        return admission == CognitiveSameCallAttestationAdmission.Accepted
            ? new CognitiveSameCallAttestationResult(
                admission,
                new CognitiveSameCallProviderAttestation(
                    capture,
                    playerTurnReceipt,
                    completion,
                    observedAtMonotonicTicks))
            : new CognitiveSameCallAttestationResult(admission, null);
    }

    internal bool Matches(CognitiveInputSnapshot snapshot)
        => snapshot != null
            && snapshot.SnapshotId == SnapshotId
            && ReferenceEquals(
                snapshot.PlayerTurnSourceHandle,
                PlayerTurnSourceHandle)
            && string.Equals(
                snapshot.SnapshotFingerprint,
                SnapshotFingerprint,
                StringComparison.Ordinal);
}

internal sealed class CognitiveSameCallAttestationResult
{
    internal CognitiveSameCallAttestationAdmission Admission { get; }
    internal CognitiveSameCallProviderAttestation? Attestation { get; }
    internal bool Accepted =>
        Admission == CognitiveSameCallAttestationAdmission.Accepted;

    internal CognitiveSameCallAttestationResult(
        CognitiveSameCallAttestationAdmission admission,
        CognitiveSameCallProviderAttestation? attestation)
    {
        Admission = CognitiveShadowContractGuard.Enum(
            admission,
            nameof(admission));
        if (Accepted != (attestation != null))
        {
            throw new ArgumentException(
                "Only an accepted result may carry an attestation.",
                nameof(attestation));
        }
        Attestation = attestation;
    }
}

internal static class CognitiveSameCallAttestor
{
    internal static CognitiveSameCallAttestationResult TryAttest(
        CognitiveSameCallSourceCapture capture,
        PlayerTurnProviderCompletionReceipt playerTurnReceipt,
        CognitiveSameCallCompletionIdentity completion,
        long observedAtMonotonicTicks)
        => CognitiveSameCallProviderAttestation.TryCreate(
            capture,
            playerTurnReceipt,
            completion,
            observedAtMonotonicTicks);

    internal static CognitiveSameCallAttestationAdmission Validate(
        CognitiveSameCallSourceCapture capture,
        PlayerTurnProviderCompletionReceipt playerTurnReceipt,
        CognitiveSameCallCompletionIdentity completion,
        long observedAtMonotonicTicks)
    {
        if (capture == null)
            throw new ArgumentNullException(nameof(capture));
        if (playerTurnReceipt == null)
            throw new ArgumentNullException(nameof(playerTurnReceipt));
        if (completion == null)
            throw new ArgumentNullException(nameof(completion));
        CognitiveShadowContractGuard.NonNegative(
            observedAtMonotonicTicks,
            nameof(observedAtMonotonicTicks));
        var snapshot = capture.Snapshot;
        if (observedAtMonotonicTicks < snapshot.CapturedAtMonotonicTicks
            || observedAtMonotonicTicks >= snapshot.ExpiresAtMonotonicTicks)
        {
            return CognitiveSameCallAttestationAdmission.SnapshotExpired;
        }
        var handle = playerTurnReceipt.Handle;
        if (!ReferenceEquals(capture.PlayerTurnHandle, handle)
            || playerTurnReceipt.InputSource == PlayerTurnInputSource.Invalid
            || !string.Equals(
                handle.ProcessEpochId,
                snapshot.ProcessEpochId,
                StringComparison.Ordinal)
            || handle.RuntimeGeneration != snapshot.RuntimeGeneration
            || !string.Equals(
                handle.OperationId,
                snapshot.OperationId,
                StringComparison.Ordinal)
            || handle.OperationGeneration != snapshot.OperationGeneration)
        {
            return CognitiveSameCallAttestationAdmission.PlayerTurnMismatch;
        }
        if (playerTurnReceipt.ProviderCallOrdinal != 1
            || playerTurnReceipt.ProviderCompletionOrdinal != 1)
        {
            return CognitiveSameCallAttestationAdmission
                .ProviderLifecycleMismatch;
        }
        if (!string.Equals(
                playerTurnReceipt.TransportRequestId,
                capture.PreparedRequestId,
                StringComparison.Ordinal)
            || !string.Equals(
                completion.PreparedRequestId,
                capture.PreparedRequestId,
                StringComparison.Ordinal)
            || !string.Equals(
                completion.PreparedRequestFingerprint,
                capture.PreparedRequestFingerprint,
                StringComparison.Ordinal))
        {
            return CognitiveSameCallAttestationAdmission.RequestMismatch;
        }
        if (!string.Equals(
                playerTurnReceipt.ResponseFingerprint,
                completion.ResponseFingerprint,
                StringComparison.Ordinal))
        {
            return CognitiveSameCallAttestationAdmission.ResponseMismatch;
        }
        if (!string.Equals(
                playerTurnReceipt.ProviderRouteId,
                capture.ProviderRouteId,
                StringComparison.Ordinal)
            || !string.Equals(
                completion.ProviderRouteId,
                capture.ProviderRouteId,
                StringComparison.Ordinal)
            || !string.Equals(
                completion.ProviderRouteFingerprint,
                capture.ProviderRouteFingerprint,
                StringComparison.Ordinal))
        {
            return CognitiveSameCallAttestationAdmission.RouteMismatch;
        }
        if (!string.Equals(
                completion.PreparedSystemPromptHash,
                capture.PreparedSystemPromptHash,
                StringComparison.Ordinal)
            || !string.Equals(
                completion.ProviderExecutionFingerprint,
                capture.ProviderExecutionFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                completion.ProviderPromptFingerprint,
                capture.ProviderPromptFingerprint,
                StringComparison.Ordinal))
        {
            return CognitiveSameCallAttestationAdmission.PromptMismatch;
        }
        if (completion.Destination
                != LivingPreparedCompletionDestination.PlayerReply
            || completion.Status != OutcomeStatus.Completed
            || string.Equals(
                completion.PromptCompositionCompletionId,
                completion.ProviderExecutionCompletionId,
                StringComparison.Ordinal)
            || string.Equals(
                completion.PromptCompositionCompletionId,
                completion.PreparedRequestId,
                StringComparison.Ordinal)
            || string.Equals(
                completion.ProviderExecutionCompletionId,
                completion.PreparedRequestId,
                StringComparison.Ordinal)
            || string.Equals(
                completion.ProviderReceiptId,
                completion.PreparedRequestId,
                StringComparison.Ordinal)
            || string.Equals(
                completion.ProviderReceiptId,
                completion.PromptCompositionCompletionId,
                StringComparison.Ordinal)
            || string.Equals(
                completion.ProviderReceiptId,
                completion.ProviderExecutionCompletionId,
                StringComparison.Ordinal))
        {
            return CognitiveSameCallAttestationAdmission.CompletionMismatch;
        }
        if (!string.Equals(
                completion.CompositionId,
                capture.CompositionId,
                StringComparison.Ordinal)
            || !string.Equals(
                completion.EnvelopeId,
                capture.EnvelopeId,
                StringComparison.Ordinal)
            || !string.Equals(
                completion.AuthorizationFingerprint,
                capture.AuthorizationFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                completion.SessionId,
                capture.SessionId,
                StringComparison.Ordinal))
        {
            return CognitiveSameCallAttestationAdmission.CompletionMismatch;
        }
        if (completion.SituationId != capture.SituationId
            || completion.SituationRevision != snapshot.SituationRevision
            || !string.Equals(
                completion.SituationFingerprint,
                snapshot.SituationFingerprint,
                StringComparison.Ordinal))
        {
            return CognitiveSameCallAttestationAdmission.SituationMismatch;
        }
        if (!string.Equals(
                completion.RuntimeFingerprint,
                capture.RuntimeFingerprint,
                StringComparison.Ordinal))
        {
            return CognitiveSameCallAttestationAdmission.RuntimeMismatch;
        }
        if (!string.Equals(
                completion.EffectiveModelFingerprint,
                capture.EffectiveModelFingerprint,
                StringComparison.Ordinal)
            || completion.ProviderRegistryGeneration
                != capture.ProviderRegistryGeneration
            || completion.ProviderConfigurationGeneration
                != capture.ProviderConfigurationGeneration)
        {
            return CognitiveSameCallAttestationAdmission.ModelMismatch;
        }
        if (!string.Equals(
                completion.ProtocolInstructionFingerprint,
                capture.ProtocolInstructionFingerprint,
                StringComparison.Ordinal))
        {
            return CognitiveSameCallAttestationAdmission.ProtocolMismatch;
        }
        if (!string.Equals(
                completion.ResponseFingerprint,
                completion.ProviderNormalizedResponseFingerprint,
                StringComparison.Ordinal))
        {
            return CognitiveSameCallAttestationAdmission.ResponseMismatch;
        }
        return CognitiveSameCallAttestationAdmission.Accepted;
    }
}
