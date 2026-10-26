using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace LilithAgentWorkbench;

internal enum D045SkillCandidateFailure
{
    None = 0,
    CandidateIdentityMissing = 1,
    VersionInvalid = 2,
    TriggerShapeInvalid = 3,
    InstructionShapeInvalid = 4,
    CapabilityDeclarationInvalid = 5,
    CapabilityDeclarationOrderInvalid = 6,
    EvidenceInventoryInvalid = 7,
    DeterministicTestInventoryInvalid = 8,
    ProvenanceInventoryInvalid = 9,
    ReferenceOrderInvalid = 10,
    RollbackIdentityInvalid = 11
}

internal enum D045SkillRollbackKind
{
    ToAbsence = 1,
    ToPriorVersion = 2
}

internal sealed class D045SkillCandidateContractException : Exception
{
    internal D045SkillCandidateFailure Failure { get; }

    internal D045SkillCandidateContractException(
        D045SkillCandidateFailure failure,
        string field)
        : base(field)
    {
        Failure = failure;
    }
}

internal sealed class D045SkillRollbackIdentity
{
    internal D045SkillRollbackKind Kind { get; }
    internal Guid CandidateId { get; }
    internal long PriorVersion { get; }
    internal D045Sha256Fingerprint TargetFingerprint { get; }

    internal D045SkillRollbackIdentity(
        D045SkillRollbackKind kind,
        Guid candidateId,
        long priorVersion,
        D045Sha256Fingerprint targetFingerprint)
    {
        if (!Enum.IsDefined(typeof(D045SkillRollbackKind), kind)
            || candidateId == Guid.Empty
            || targetFingerprint is null
            || (kind == D045SkillRollbackKind.ToAbsence && priorVersion != 0)
            || (kind == D045SkillRollbackKind.ToPriorVersion
                && priorVersion <= 0))
        {
            throw new D045SkillCandidateContractException(
                D045SkillCandidateFailure.RollbackIdentityInvalid,
                nameof(kind));
        }

        Kind = kind;
        CandidateId = candidateId;
        PriorVersion = priorVersion;
        TargetFingerprint = targetFingerprint;
    }

    public override string ToString()
        => "D045SkillRollbackIdentity(content-free)";
}

internal readonly struct D045SkillCandidateAuthorityClaims
{
    internal bool InstallationAuthority => false;
    internal bool ActivationAuthority => false;
    internal bool ExecutionAuthority => false;
    internal bool PersistenceAuthority => false;
    internal bool SelfReviewAuthority => false;
    internal bool PolicyMutationAuthority => false;
    internal bool HiddenAuthority => false;
    internal bool ConsentInheritanceAuthority => false;
    internal bool ExternalServiceAuthority => false;
    internal bool SkillFileWriteAuthority => false;
    internal bool RequiredCapabilitiesAreGrants => false;
    internal bool RuntimeCallerPresent => false;
}

internal sealed class D045SkillCandidateAuthorityBoundary
{
    internal const int MaximumTriggerUtf8Bytes = 4_096;
    internal const int MaximumTriggerTextElements = 1_024;
    internal const int MaximumInstructionUtf8Bytes = 32_768;
    internal const int MaximumInstructionTextElements = 8_192;
    internal const int MaximumRequiredCapabilities = 17;
    internal const int MaximumEvidenceReferences = 16;
    internal const int MaximumDeterministicTestReferences = 32;
    internal const int MaximumProvenanceReferences = 16;

    private readonly ReadOnlyCollection<D045AuthorityCapability>
        _requiredCapabilities;
    private readonly ReadOnlyCollection<D045Sha256Fingerprint>
        _evidenceReferences;
    private readonly ReadOnlyCollection<D045Sha256Fingerprint>
        _deterministicTestReferences;
    private readonly ReadOnlyCollection<D045Sha256Fingerprint>
        _provenanceReferences;

    internal Guid CandidateId { get; }
    internal long Version { get; }
    internal D045Sha256Fingerprint TriggerDigest { get; }
    internal int TriggerUtf8ByteLength { get; }
    internal int TriggerTextElementCount { get; }
    internal D045Sha256Fingerprint InstructionDigest { get; }
    internal int InstructionUtf8ByteLength { get; }
    internal int InstructionTextElementCount { get; }
    internal IReadOnlyList<D045AuthorityCapability> RequiredCapabilities
        => _requiredCapabilities;
    internal IReadOnlyList<D045Sha256Fingerprint> EvidenceReferences
        => _evidenceReferences;
    internal IReadOnlyList<D045Sha256Fingerprint> DeterministicTestReferences
        => _deterministicTestReferences;
    internal IReadOnlyList<D045Sha256Fingerprint> ProvenanceReferences
        => _provenanceReferences;
    internal D045SkillRollbackIdentity Rollback { get; }
    internal D045SkillCandidateAuthorityClaims AuthorityClaims => default;

    internal D045SkillCandidateAuthorityBoundary(
        Guid candidateId,
        long version,
        D045Sha256Fingerprint triggerDigest,
        int triggerUtf8ByteLength,
        int triggerTextElementCount,
        D045Sha256Fingerprint instructionDigest,
        int instructionUtf8ByteLength,
        int instructionTextElementCount,
        IReadOnlyList<D045AuthorityCapability>? requiredCapabilities,
        IReadOnlyList<D045Sha256Fingerprint>? evidenceReferences,
        IReadOnlyList<D045Sha256Fingerprint>? deterministicTestReferences,
        IReadOnlyList<D045Sha256Fingerprint>? provenanceReferences,
        D045SkillRollbackIdentity rollback)
    {
        if (candidateId == Guid.Empty)
        {
            Reject(
                D045SkillCandidateFailure.CandidateIdentityMissing,
                nameof(candidateId));
        }
        if (version <= 0)
        {
            Reject(D045SkillCandidateFailure.VersionInvalid, nameof(version));
        }

        RequireTextShape(
            triggerDigest,
            triggerUtf8ByteLength,
            triggerTextElementCount,
            MaximumTriggerUtf8Bytes,
            MaximumTriggerTextElements,
            D045SkillCandidateFailure.TriggerShapeInvalid,
            nameof(triggerDigest));
        RequireTextShape(
            instructionDigest,
            instructionUtf8ByteLength,
            instructionTextElementCount,
            MaximumInstructionUtf8Bytes,
            MaximumInstructionTextElements,
            D045SkillCandidateFailure.InstructionShapeInvalid,
            nameof(instructionDigest));

        CandidateId = candidateId;
        Version = version;
        TriggerDigest = triggerDigest;
        TriggerUtf8ByteLength = triggerUtf8ByteLength;
        TriggerTextElementCount = triggerTextElementCount;
        InstructionDigest = instructionDigest;
        InstructionUtf8ByteLength = instructionUtf8ByteLength;
        InstructionTextElementCount = instructionTextElementCount;

        _requiredCapabilities = FreezeCapabilities(requiredCapabilities);
        _evidenceReferences = FreezeReferences(
            evidenceReferences,
            minimum: 0,
            MaximumEvidenceReferences,
            D045SkillCandidateFailure.EvidenceInventoryInvalid,
            nameof(evidenceReferences));
        _deterministicTestReferences = FreezeReferences(
            deterministicTestReferences,
            minimum: 1,
            MaximumDeterministicTestReferences,
            D045SkillCandidateFailure.DeterministicTestInventoryInvalid,
            nameof(deterministicTestReferences));
        _provenanceReferences = FreezeReferences(
            provenanceReferences,
            minimum: 1,
            MaximumProvenanceReferences,
            D045SkillCandidateFailure.ProvenanceInventoryInvalid,
            nameof(provenanceReferences));

        Rollback = rollback
            ?? throw new D045SkillCandidateContractException(
                D045SkillCandidateFailure.RollbackIdentityInvalid,
                nameof(rollback));
        if (Rollback.CandidateId != CandidateId
            || (Rollback.Kind == D045SkillRollbackKind.ToPriorVersion
                && Rollback.PriorVersion >= Version))
        {
            Reject(
                D045SkillCandidateFailure.RollbackIdentityInvalid,
                nameof(rollback));
        }
    }

    public override string ToString()
        => "D045SkillCandidateAuthorityBoundary(content-free; no-authority)";

    private static void RequireTextShape(
        D045Sha256Fingerprint digest,
        int utf8ByteLength,
        int textElementCount,
        int maximumUtf8Bytes,
        int maximumTextElements,
        D045SkillCandidateFailure failure,
        string field)
    {
        if (digest is null
            || utf8ByteLength <= 0
            || utf8ByteLength > maximumUtf8Bytes
            || textElementCount <= 0
            || textElementCount > maximumTextElements
            || utf8ByteLength < textElementCount)
        {
            Reject(failure, field);
        }
    }

    private static ReadOnlyCollection<D045AuthorityCapability>
        FreezeCapabilities(
            IReadOnlyList<D045AuthorityCapability>? source)
    {
        if (source is null || source.Count > MaximumRequiredCapabilities)
        {
            Reject(
                D045SkillCandidateFailure.CapabilityDeclarationInvalid,
                nameof(source));
        }

        var copy = new D045AuthorityCapability[source.Count];
        var previous = 0;
        for (var index = 0; index < source.Count; index++)
        {
            var value = source[index];
            var ordinal = (int)value;
            if (!Enum.IsDefined(typeof(D045AuthorityCapability), value))
            {
                Reject(
                    D045SkillCandidateFailure.CapabilityDeclarationInvalid,
                    nameof(source));
            }
            if (ordinal <= previous)
            {
                Reject(
                    D045SkillCandidateFailure.CapabilityDeclarationOrderInvalid,
                    nameof(source));
            }

            previous = ordinal;
            copy[index] = value;
        }

        return Array.AsReadOnly(copy);
    }

    private static ReadOnlyCollection<D045Sha256Fingerprint> FreezeReferences(
        IReadOnlyList<D045Sha256Fingerprint>? source,
        int minimum,
        int maximum,
        D045SkillCandidateFailure failure,
        string field)
    {
        if (source is null || source.Count < minimum || source.Count > maximum)
        {
            Reject(failure, field);
        }

        var copy = new D045Sha256Fingerprint[source.Count];
        string? previous = null;
        for (var index = 0; index < source.Count; index++)
        {
            var value = source[index];
            if (value is null)
                Reject(failure, field);
            if (previous is not null
                && string.CompareOrdinal(previous, value.CanonicalHex) >= 0)
            {
                Reject(
                    D045SkillCandidateFailure.ReferenceOrderInvalid,
                    field);
            }

            previous = value.CanonicalHex;
            copy[index] = value;
        }

        return Array.AsReadOnly(copy);
    }

    [DoesNotReturn]
    private static void Reject(
        D045SkillCandidateFailure failure,
        string field)
        => throw new D045SkillCandidateContractException(failure, field);
}
