using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

internal enum SemanticIntentInferenceDisposition
{
    Proposed = 1,
    Abstained = 2,
    Unavailable = 3
}

internal enum SemanticIntentInferenceMaterialAdmission
{
    Created = 1,
    InputRejected = 2,
    BindingRejected = 3
}

internal enum SemanticIntentSuppliedContextAdmissionDisposition
{
    ExplicitlyAdmitted = 1,
    Denied = 2
}

/// <summary>
/// An invocation-scoped claim supplied by the upstream projection owner.
/// Stage B binds and enforces this claim, but does not claim that a live owner
/// has been connected or independently verified.
/// </summary>
internal sealed class SemanticIntentSuppliedContextAdmission
{
    internal SemanticIntentSuppliedContextAdmissionDisposition Disposition {
        get;
    }
    internal string AdmissionIdentityFingerprint { get; }
    internal string ContextId { get; }
    internal SemanticContextKind Kind { get; }
    internal string ContentFingerprint { get; }
    internal string ProvenanceFingerprint { get; }
    internal long ObservedAtMonotonicTicks { get; }
    internal long ExpiresAtMonotonicTicks { get; }
    internal string Fingerprint { get; }
    internal bool LiveOwnerAuthorizationVerified => false;

    internal SemanticIntentSuppliedContextAdmission(
        SemanticIntentSuppliedContextAdmissionDisposition disposition,
        string admissionIdentityFingerprint,
        string contextId,
        SemanticContextKind kind,
        string contentFingerprint,
        string provenanceFingerprint,
        long observedAtMonotonicTicks,
        long expiresAtMonotonicTicks)
    {
        Disposition = SemanticIntentContractGuard.Enum(
            disposition,
            nameof(disposition));
        AdmissionIdentityFingerprint =
            SemanticIntentContractGuard.Fingerprint(
                admissionIdentityFingerprint,
                nameof(admissionIdentityFingerprint));
        ContextId = SemanticIntentContractGuard.Identifier(
            contextId,
            nameof(contextId));
        Kind = SemanticIntentContractGuard.Enum(kind, nameof(kind));
        ContentFingerprint = SemanticIntentContractGuard.Fingerprint(
            contentFingerprint,
            nameof(contentFingerprint));
        ProvenanceFingerprint = SemanticIntentContractGuard.Fingerprint(
            provenanceFingerprint,
            nameof(provenanceFingerprint));
        SemanticIntentContractGuard.Lifetime(
            observedAtMonotonicTicks,
            expiresAtMonotonicTicks);
        ObservedAtMonotonicTicks = observedAtMonotonicTicks;
        ExpiresAtMonotonicTicks = expiresAtMonotonicTicks;
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            Disposition,
            AdmissionIdentityFingerprint,
            ContextId,
            Kind,
            ContentFingerprint,
            ProvenanceFingerprint,
            ObservedAtMonotonicTicks,
            ExpiresAtMonotonicTicks);
    }
}

internal sealed class SemanticIntentInferenceContextMaterialCreation
{
    internal SemanticIntentInferenceMaterialAdmission Admission { get; }
    internal SemanticIntentInferenceContextMaterial? Material { get; }
    internal bool Created =>
        Admission == SemanticIntentInferenceMaterialAdmission.Created;
    internal bool ContainsEphemeralPrivateContent => Material != null;
    internal bool MayPersist => false;

    private SemanticIntentInferenceContextMaterialCreation(
        SemanticIntentInferenceMaterialAdmission admission,
        SemanticIntentInferenceContextMaterial? material)
    {
        Admission = SemanticIntentContractGuard.Enum(
            admission,
            nameof(admission));
        if ((Admission == SemanticIntentInferenceMaterialAdmission.Created)
            != (material != null))
        {
            throw new ArgumentException(
                "Context material presence must match its creation admission.",
                nameof(material));
        }
        Material = material;
    }

    internal static SemanticIntentInferenceContextMaterialCreation Create(
        SemanticIntentInferenceContextMaterial material)
        => new(
            SemanticIntentInferenceMaterialAdmission.Created,
            material ?? throw new ArgumentNullException(nameof(material)));

    internal static SemanticIntentInferenceContextMaterialCreation Reject(
        SemanticIntentInferenceMaterialAdmission admission)
    {
        if (admission == SemanticIntentInferenceMaterialAdmission.Created)
            throw new ArgumentOutOfRangeException(nameof(admission));
        return new SemanticIntentInferenceContextMaterialCreation(
            admission,
            null);
    }
}

internal sealed class SemanticIntentInferenceContextMaterial
{
    internal const int MaximumContentUnicodeScalars = 512;

    internal SemanticContextReference Reference { get; }
    internal string ExactContent { get; }
    internal int UnicodeScalarCount { get; }
    internal string ContentFingerprint { get; }
    internal string ProvenanceFingerprint { get; }
    internal SemanticIntentSuppliedContextAdmission SuppliedAdmission { get; }
    internal string AdmissionFingerprint => SuppliedAdmission.Fingerprint;
    internal string Fingerprint { get; }
    internal bool ContainsEphemeralPrivateContent => true;
    internal bool MayPersist => false;
    internal bool MayLogContent => false;
    internal bool MayPublishContent => false;

    private SemanticIntentInferenceContextMaterial(
        SemanticContextReference reference,
        string exactContent,
        SemanticIntentSuppliedContextAdmission suppliedAdmission)
    {
        Reference = reference
            ?? throw new ArgumentNullException(nameof(reference));
        if (Reference.Kind is SemanticContextKind.ActiveBoundary
            or SemanticContextKind.ActiveOpenLoop)
        {
            throw new ArgumentException(
                "Boundary and open-loop policy state cannot become inference content.",
                nameof(reference));
        }
        ExactContent = SemanticIntentContractGuard.Text(
            exactContent,
            MaximumContentUnicodeScalars,
            nameof(exactContent));
        UnicodeScalarCount = ExactContent.EnumerateRunes().Count();
        ContentFingerprint = SemanticIntentFingerprint.Hash(ExactContent);
        SuppliedAdmission = suppliedAdmission
            ?? throw new ArgumentNullException(nameof(suppliedAdmission));
        ProvenanceFingerprint = SuppliedAdmission.ProvenanceFingerprint;
        if (SuppliedAdmission.Disposition
                != SemanticIntentSuppliedContextAdmissionDisposition
                    .ExplicitlyAdmitted
            || SuppliedAdmission.ContextId != Reference.ContextId
            || SuppliedAdmission.Kind != Reference.Kind
            || SuppliedAdmission.ContentFingerprint != ContentFingerprint
            || SuppliedAdmission.ObservedAtMonotonicTicks
                != Reference.ObservedAtMonotonicTicks
            || SuppliedAdmission.ExpiresAtMonotonicTicks
                != Reference.ExpiresAtMonotonicTicks)
        {
            throw new InvalidOperationException(
                "Context material did not carry one exact admitted projection claim.");
        }

        var expectedSourceFingerprint = SourceFingerprint(
            Reference.ContextId,
            Reference.Kind,
            ContentFingerprint,
            ProvenanceFingerprint,
            SuppliedAdmission.Fingerprint,
            Reference.ObservedAtMonotonicTicks,
            Reference.ExpiresAtMonotonicTicks);
        if (Reference.SourceFingerprint != expectedSourceFingerprint)
        {
            throw new InvalidOperationException(
                "Context content did not match its exact source binding.");
        }

        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            Reference.Fingerprint,
            ContentFingerprint,
            ProvenanceFingerprint,
            SuppliedAdmission.Fingerprint,
            UnicodeScalarCount);
    }

    internal static SemanticIntentInferenceContextMaterialCreation
        TryCreateBound(
        SemanticIntentSuppliedContextAdmission? suppliedAdmission,
        string exactContent,
        long? expectedObservedAtMonotonicTicks = null,
        long? expectedExpiresAtMonotonicTicks = null)
    {
        if (suppliedAdmission == null
            || suppliedAdmission.Disposition
                != SemanticIntentSuppliedContextAdmissionDisposition
                    .ExplicitlyAdmitted)
        {
            return SemanticIntentInferenceContextMaterialCreation.Reject(
                SemanticIntentInferenceMaterialAdmission.InputRejected);
        }
        try
        {
            var content = SemanticIntentContractGuard.Text(
                exactContent,
                MaximumContentUnicodeScalars,
                nameof(exactContent));
            if ((expectedObservedAtMonotonicTicks.HasValue
                    && suppliedAdmission.ObservedAtMonotonicTicks
                        != expectedObservedAtMonotonicTicks.Value)
                || (expectedExpiresAtMonotonicTicks.HasValue
                    && suppliedAdmission.ExpiresAtMonotonicTicks
                        != expectedExpiresAtMonotonicTicks.Value)
                || SemanticIntentFingerprint.Hash(content)
                    != suppliedAdmission.ContentFingerprint)
            {
                return SemanticIntentInferenceContextMaterialCreation.Reject(
                    SemanticIntentInferenceMaterialAdmission.BindingRejected);
            }
            var sourceFingerprint = SourceFingerprint(
                suppliedAdmission.ContextId,
                suppliedAdmission.Kind,
                suppliedAdmission.ContentFingerprint,
                suppliedAdmission.ProvenanceFingerprint,
                suppliedAdmission.Fingerprint,
                suppliedAdmission.ObservedAtMonotonicTicks,
                suppliedAdmission.ExpiresAtMonotonicTicks);
            var reference = new SemanticContextReference(
                suppliedAdmission.ContextId,
                suppliedAdmission.Kind,
                sourceFingerprint,
                suppliedAdmission.ObservedAtMonotonicTicks,
                suppliedAdmission.ExpiresAtMonotonicTicks);
            return SemanticIntentInferenceContextMaterialCreation.Create(
                new SemanticIntentInferenceContextMaterial(
                    reference,
                    content,
                    suppliedAdmission));
        }
        catch (ArgumentException)
        {
            return SemanticIntentInferenceContextMaterialCreation.Reject(
                SemanticIntentInferenceMaterialAdmission.InputRejected);
        }
    }

    internal static SemanticIntentInferenceContextMaterialCreation TryBindExact(
        SemanticContextReference reference,
        string exactContent,
        SemanticIntentSuppliedContextAdmission? suppliedAdmission)
    {
        if (suppliedAdmission == null
            || suppliedAdmission.Disposition
                != SemanticIntentSuppliedContextAdmissionDisposition
                    .ExplicitlyAdmitted)
        {
            return SemanticIntentInferenceContextMaterialCreation.Reject(
                SemanticIntentInferenceMaterialAdmission.InputRejected);
        }
        try
        {
            return SemanticIntentInferenceContextMaterialCreation.Create(
                new SemanticIntentInferenceContextMaterial(
                    reference,
                    exactContent,
                    suppliedAdmission));
        }
        catch (InvalidOperationException)
        {
            return SemanticIntentInferenceContextMaterialCreation.Reject(
                SemanticIntentInferenceMaterialAdmission.BindingRejected);
        }
        catch (ArgumentException)
        {
            return SemanticIntentInferenceContextMaterialCreation.Reject(
                SemanticIntentInferenceMaterialAdmission.InputRejected);
        }
    }

    private static string SourceFingerprint(
        string contextId,
        SemanticContextKind kind,
        string contentFingerprint,
        string provenanceFingerprint,
        string admissionFingerprint,
        long observedAtMonotonicTicks,
        long expiresAtMonotonicTicks)
    {
        SemanticIntentContractGuard.Lifetime(
            observedAtMonotonicTicks,
            expiresAtMonotonicTicks);
        return SemanticIntentFingerprint.HashCanonical(
            "semantic-inference-context-v1",
            contextId,
            kind,
            contentFingerprint,
            provenanceFingerprint,
            admissionFingerprint,
            observedAtMonotonicTicks,
            expiresAtMonotonicTicks);
    }
}

internal sealed class SemanticIntentInferenceDescriptorDefinition
{
    internal const int MaximumDefinitionUnicodeScalars = 512;

    internal string DescriptorId { get; }
    internal int DescriptorVersion { get; }
    internal string DescriptorFingerprint { get; }
    internal string ExactDefinition { get; }
    internal int UnicodeScalarCount { get; }
    internal string DefinitionFingerprint { get; }
    internal string Fingerprint { get; }

    internal SemanticIntentInferenceDescriptorDefinition(
        SemanticRegistryDescriptor descriptor,
        string exactDefinition)
    {
        if (descriptor == null)
            throw new ArgumentNullException(nameof(descriptor));
        DescriptorId = descriptor.DescriptorId;
        DescriptorVersion = descriptor.DescriptorVersion;
        DescriptorFingerprint = descriptor.Fingerprint;
        ExactDefinition = SemanticIntentContractGuard.Text(
            exactDefinition,
            MaximumDefinitionUnicodeScalars,
            nameof(exactDefinition));
        UnicodeScalarCount = ExactDefinition.EnumerateRunes().Count();
        DefinitionFingerprint = SemanticIntentFingerprint.Hash(ExactDefinition);
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            DescriptorId,
            DescriptorVersion,
            DescriptorFingerprint,
            DefinitionFingerprint,
            UnicodeScalarCount);
    }

    internal string Key => DescriptorId + "@"
        + DescriptorVersion.ToString(CultureInfo.InvariantCulture);
}

internal sealed class SemanticIntentInferenceProfile
{
    internal const int MaximumTotalDefinitionUnicodeScalars = 8192;

    private readonly IReadOnlyDictionary<string,
        SemanticIntentInferenceDescriptorDefinition> _definitionsByKey;

    internal string ProducerId { get; }
    internal int ProducerVersion { get; }
    internal string ProfileId { get; }
    internal int ProfileVersion { get; }
    internal SemanticIntentRegistrySnapshot Registry { get; }
    internal IReadOnlyList<SemanticIntentInferenceDescriptorDefinition>
        Definitions { get; }
    internal string Fingerprint { get; }

    internal SemanticIntentInferenceProfile(
        string producerId,
        int producerVersion,
        string profileId,
        int profileVersion,
        SemanticIntentRegistrySnapshot registry,
        IEnumerable<SemanticIntentInferenceDescriptorDefinition> definitions)
    {
        ProducerId = SemanticIntentContractGuard.Identifier(
            producerId,
            nameof(producerId));
        ProducerVersion = SemanticIntentContractGuard.Positive(
            producerVersion,
            nameof(producerVersion));
        ProfileId = SemanticIntentContractGuard.Identifier(
            profileId,
            nameof(profileId));
        ProfileVersion = SemanticIntentContractGuard.Positive(
            profileVersion,
            nameof(profileVersion));
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));

        var items = (definitions
                ?? throw new ArgumentNullException(nameof(definitions)))
            .Take(SemanticIntentRegistrySnapshot.MaximumDescriptors + 1)
            .ToArray();
        if (items.Length != Registry.Descriptors.Count
            || items.Any(item => item == null)
            || items.Select(item => item.Key)
                .Distinct(StringComparer.Ordinal)
                .Count() != items.Length
            || items.Sum(item => item.UnicodeScalarCount)
                > MaximumTotalDefinitionUnicodeScalars)
        {
            throw new ArgumentException(
                "Inference definitions must be bounded and exactly cover the registry.",
                nameof(definitions));
        }

        var ordered = items
            .OrderBy(item => item.DescriptorId, StringComparer.Ordinal)
            .ThenBy(item => item.DescriptorVersion)
            .ToArray();
        for (var index = 0; index < Registry.Descriptors.Count; index++)
        {
            var descriptor = Registry.Descriptors[index];
            var definition = ordered[index];
            if (definition.DescriptorId != descriptor.DescriptorId
                || definition.DescriptorVersion != descriptor.DescriptorVersion
                || definition.DescriptorFingerprint != descriptor.Fingerprint)
            {
                throw new InvalidOperationException(
                    "Inference definitions crossed their exact registry binding.");
            }
        }

        Definitions = new ReadOnlyCollection<
            SemanticIntentInferenceDescriptorDefinition>(ordered);
        _definitionsByKey = new ReadOnlyDictionary<string,
            SemanticIntentInferenceDescriptorDefinition>(
                ordered.ToDictionary(
                    item => item.Key,
                    item => item,
                    StringComparer.Ordinal));
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            ProducerId,
            ProducerVersion,
            ProfileId,
            ProfileVersion,
            Registry.Generation,
            Registry.Fingerprint,
            SemanticIntentFingerprint.CanonicalSequence(
                ordered.Select(item => item.Fingerprint)));
    }

    internal SemanticIntentInferenceDescriptorDefinition? FindDefinition(
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
        return _definitionsByKey.TryGetValue(key, out var definition)
            ? definition
            : null;
    }
}

internal sealed class SemanticIntentInferenceRequest
{
    internal const int MaximumTotalContextUnicodeScalars = 2048;

    private readonly IReadOnlyDictionary<string,
        SemanticIntentInferenceContextMaterial> _materialsById;
    private int _inferenceInvocationClaimed;

    internal string RequestId { get; }
    internal SemanticIntentInputSnapshot Input { get; }
    internal SemanticIntentRegistrySnapshot Registry { get; }
    internal SemanticIntentInferenceProfile Profile { get; }
    internal IReadOnlyList<SemanticIntentInferenceContextMaterial>
        ContextMaterials { get; }
    internal long RequestedAtMonotonicTicks { get; }
    internal string Fingerprint { get; }
    internal SemanticIntentInferenceRequestWitness IdentityWitness { get; }
    internal bool ContainsEphemeralPrivateContent => true;
    internal bool MayPersist => false;
    internal bool MayLogContent => false;
    internal bool MayPublishContent => false;
    internal bool IsInferenceInvocationClaimed =>
        Volatile.Read(ref _inferenceInvocationClaimed) != 0;

    private SemanticIntentInferenceRequest(
        string requestId,
        SemanticIntentInputSnapshot input,
        SemanticIntentRegistrySnapshot registry,
        SemanticIntentInferenceProfile profile,
        IEnumerable<SemanticIntentInferenceContextMaterial> contextMaterials,
        long requestedAtMonotonicTicks)
    {
        RequestId = SemanticIntentContractGuard.Identifier(
            requestId,
            nameof(requestId));
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        if (requestedAtMonotonicTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(requestedAtMonotonicTicks));
        RequestedAtMonotonicTicks = requestedAtMonotonicTicks;

        if (Input.Origin != SemanticIntentInputOrigin.SemanticInference
            || Input.RegistryGeneration != Registry.Generation
            || Input.RegistryFingerprint != Registry.Fingerprint
            || !ReferenceEquals(Profile.Registry, Registry))
        {
            throw new InvalidOperationException(
                "Inference request identities did not share one exact registry boundary.");
        }

        var items = (contextMaterials
                ?? throw new ArgumentNullException(nameof(contextMaterials)))
            .Take(SemanticIntentInputSnapshot.MaximumContexts + 1)
            .ToArray();
        if (items.Length != Input.Contexts.Count
            || items.Any(item => item == null)
            || items.Select(item => item.Reference.ContextId)
                .Distinct(StringComparer.Ordinal)
                .Count() != items.Length
            || items.Sum(item => item.UnicodeScalarCount)
                > MaximumTotalContextUnicodeScalars)
        {
            throw new ArgumentException(
                "Inference context must be bounded and exactly cover the input references.",
                nameof(contextMaterials));
        }

        var ordered = items
            .OrderBy(item => item.Reference.ContextId, StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < Input.Contexts.Count; index++)
        {
            var expected = Input.Contexts[index];
            var material = ordered[index];
            if (!ReferenceEquals(material.Reference, expected)
                || material.Reference.Fingerprint != expected.Fingerprint)
            {
                throw new InvalidOperationException(
                    "Inference context material crossed its exact input reference.");
            }
        }

        ContextMaterials = new ReadOnlyCollection<
            SemanticIntentInferenceContextMaterial>(ordered);
        _materialsById = new ReadOnlyDictionary<string,
            SemanticIntentInferenceContextMaterial>(
                ordered.ToDictionary(
                    item => item.Reference.ContextId,
                    item => item,
                    StringComparer.Ordinal));
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            RequestId,
            Input.Fingerprint,
            Registry.Generation,
            Registry.Fingerprint,
            Profile.Fingerprint,
            RequestedAtMonotonicTicks,
            SemanticIntentFingerprint.CanonicalSequence(
                ordered.Select(item => item.Fingerprint)));
        IdentityWitness = new SemanticIntentInferenceRequestWitness(
            Fingerprint);
    }

    internal SemanticIntentInferenceContextMaterial? FindContextMaterial(
        string contextId)
        => _materialsById.TryGetValue(contextId, out var material)
            ? material
            : null;

    internal bool TryClaimInferenceInvocation()
        => Interlocked.CompareExchange(
            ref _inferenceInvocationClaimed,
            1,
            0) == 0;

    internal static SemanticIntentInferenceRequestCreation TryCreate(
        string requestId,
        SemanticIntentInputSnapshot? input,
        SemanticIntentRegistrySnapshot? registry,
        SemanticIntentInferenceProfile? profile,
        IEnumerable<SemanticIntentInferenceContextMaterial>? contextMaterials,
        long requestedAtMonotonicTicks)
    {
        if (input == null
            || registry == null
            || profile == null
            || contextMaterials == null
            || requestedAtMonotonicTicks < 0)
        {
            return SemanticIntentInferenceRequestCreation.Reject(
                SemanticIntentInferenceRequestAdmission.Malformed);
        }
        try
        {
            SemanticIntentContractGuard.Identifier(
                requestId,
                nameof(requestId));
        }
        catch (ArgumentException)
        {
            return SemanticIntentInferenceRequestCreation.Reject(
                SemanticIntentInferenceRequestAdmission.Malformed);
        }

        SemanticIntentInferenceContextMaterial[] materials;
        try
        {
            materials = contextMaterials
                .Take(SemanticIntentInputSnapshot.MaximumContexts + 1)
                .ToArray();
        }
        catch (Exception)
        {
            return SemanticIntentInferenceRequestCreation.Reject(
                SemanticIntentInferenceRequestAdmission.Malformed);
        }
        if (materials.Length != input.Contexts.Count
            || materials.Any(item => item == null)
            || materials.Select(item => item.Reference.ContextId)
                .Distinct(StringComparer.Ordinal)
                .Count() != materials.Length
            || materials.Sum(item => item.UnicodeScalarCount)
                > MaximumTotalContextUnicodeScalars)
        {
            return SemanticIntentInferenceRequestCreation.Reject(
                SemanticIntentInferenceRequestAdmission.MaterialRejected);
        }
        if (input.Origin != SemanticIntentInputOrigin.SemanticInference
            || input.RegistryGeneration != registry.Generation
            || input.RegistryFingerprint != registry.Fingerprint
            || !ReferenceEquals(profile.Registry, registry))
        {
            return SemanticIntentInferenceRequestCreation.Reject(
                SemanticIntentInferenceRequestAdmission.BindingRejected);
        }
        var ordered = materials
            .OrderBy(item => item.Reference.ContextId, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Select((material, index) =>
                ReferenceEquals(material.Reference, input.Contexts[index])
                && material.Reference.Fingerprint
                    == input.Contexts[index].Fingerprint)
            .Any(matches => !matches))
        {
            return SemanticIntentInferenceRequestCreation.Reject(
                SemanticIntentInferenceRequestAdmission.BindingRejected);
        }

        try
        {
            return SemanticIntentInferenceRequestCreation.Create(
                new SemanticIntentInferenceRequest(
                    requestId,
                    input,
                    registry,
                    profile,
                    ordered,
                    requestedAtMonotonicTicks));
        }
        catch (InvalidOperationException)
        {
            return SemanticIntentInferenceRequestCreation.Reject(
                SemanticIntentInferenceRequestAdmission.BindingRejected);
        }
        catch (ArgumentException)
        {
            return SemanticIntentInferenceRequestCreation.Reject(
                SemanticIntentInferenceRequestAdmission.Malformed);
        }
    }
}

internal sealed class SemanticIntentInferenceRequestWitness
{
    internal string RequestFingerprint { get; }

    internal SemanticIntentInferenceRequestWitness(string requestFingerprint)
    {
        RequestFingerprint = SemanticIntentContractGuard.Fingerprint(
            requestFingerprint,
            nameof(requestFingerprint));
    }
}

internal enum SemanticIntentInferenceRequestAdmission
{
    Created = 1,
    Malformed = 2,
    MaterialRejected = 3,
    BindingRejected = 4
}

internal sealed class SemanticIntentInferenceRequestCreation
{
    internal SemanticIntentInferenceRequestAdmission Admission { get; }
    internal SemanticIntentInferenceRequest? Request { get; }
    internal bool Created =>
        Admission == SemanticIntentInferenceRequestAdmission.Created;
    internal bool ContainsEphemeralPrivateContent => Request != null;
    internal bool MayPersist => false;

    private SemanticIntentInferenceRequestCreation(
        SemanticIntentInferenceRequestAdmission admission,
        SemanticIntentInferenceRequest? request)
    {
        Admission = SemanticIntentContractGuard.Enum(
            admission,
            nameof(admission));
        if ((Admission == SemanticIntentInferenceRequestAdmission.Created)
            != (request != null))
        {
            throw new ArgumentException(
                "Inference request presence must match its creation admission.",
                nameof(request));
        }
        Request = request;
    }

    internal static SemanticIntentInferenceRequestCreation Create(
        SemanticIntentInferenceRequest request)
        => new(
            SemanticIntentInferenceRequestAdmission.Created,
            request ?? throw new ArgumentNullException(nameof(request)));

    internal static SemanticIntentInferenceRequestCreation Reject(
        SemanticIntentInferenceRequestAdmission admission)
    {
        if (admission == SemanticIntentInferenceRequestAdmission.Created)
            throw new ArgumentOutOfRangeException(nameof(admission));
        return new SemanticIntentInferenceRequestCreation(admission, null);
    }
}

internal sealed class SemanticIntentInferenceDescriptorReference
{
    internal string DescriptorId { get; }
    internal int DescriptorVersion { get; }
    internal int Rank { get; }
    internal string Fingerprint { get; }

    internal SemanticIntentInferenceDescriptorReference(
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
}

internal sealed class SemanticIntentInferenceEvidenceReference
{
    internal string TurnId { get; }
    internal int ScalarStart { get; }
    internal int ScalarLength { get; }
    internal SemanticEvidenceRole Role { get; }
    internal string Fingerprint { get; }

    internal SemanticIntentInferenceEvidenceReference(
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

internal sealed class SemanticIntentInferenceCandidate
{
    internal int Rank { get; }
    internal IReadOnlyList<SemanticIntentInferenceDescriptorReference>
        Descriptors { get; }
    internal SemanticAmbiguityKind Ambiguity { get; }
    internal SemanticIntentConfidenceBand Confidence { get; }
    internal IReadOnlyList<string> ContextReferenceIds { get; }
    internal IReadOnlyList<SemanticIntentInferenceEvidenceReference>
        Evidence { get; }
    internal string? TargetCueEvidenceFingerprint { get; }
    internal string Fingerprint { get; }

    internal SemanticIntentInferenceCandidate(
        int rank,
        IEnumerable<SemanticIntentInferenceDescriptorReference> descriptors,
        SemanticAmbiguityKind ambiguity,
        SemanticIntentConfidenceBand confidence,
        IEnumerable<string> contextReferenceIds,
        IEnumerable<SemanticIntentInferenceEvidenceReference> evidence,
        string? targetCueEvidenceFingerprint)
    {
        if (rank <= 0 || rank > SemanticIntentProposalBatch.MaximumProposals)
            throw new ArgumentOutOfRangeException(nameof(rank));
        Rank = rank;
        Ambiguity = SemanticIntentContractGuard.Enum(
            ambiguity,
            nameof(ambiguity));
        Confidence = SemanticIntentContractGuard.Enum(
            confidence,
            nameof(confidence));

        var descriptorItems = (descriptors
                ?? throw new ArgumentNullException(nameof(descriptors)))
            .Take(SemanticIntentProposal.MaximumDescriptors + 1)
            .ToArray();
        if (descriptorItems.Length == 0
            || descriptorItems.Length > SemanticIntentProposal.MaximumDescriptors
            || descriptorItems.Any(item => item == null))
        {
            throw new ArgumentException(
                "Inference descriptors must be non-empty and bounded.",
                nameof(descriptors));
        }
        var orderedDescriptors = descriptorItems
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.DescriptorId, StringComparer.Ordinal)
            .ThenBy(item => item.DescriptorVersion)
            .ToArray();
        Descriptors = new ReadOnlyCollection<
            SemanticIntentInferenceDescriptorReference>(orderedDescriptors);

        var contextItems = (contextReferenceIds
                ?? throw new ArgumentNullException(nameof(contextReferenceIds)))
            .Select(value => SemanticIntentContractGuard.Identifier(
                value,
                nameof(contextReferenceIds)))
            .Take(SemanticIntentProposal.MaximumContextReferences + 1)
            .ToArray();
        if (contextItems.Length > SemanticIntentProposal.MaximumContextReferences)
        {
            throw new ArgumentException(
                "Inference context citations exceeded the proposal bound.",
                nameof(contextReferenceIds));
        }
        var orderedContexts = contextItems
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        ContextReferenceIds = new ReadOnlyCollection<string>(orderedContexts);

        var evidenceItems = (evidence
                ?? throw new ArgumentNullException(nameof(evidence)))
            .Take(SemanticIntentProposal.MaximumEvidenceSpans + 1)
            .ToArray();
        if (evidenceItems.Length == 0
            || evidenceItems.Length > SemanticIntentProposal.MaximumEvidenceSpans
            || evidenceItems.Any(item => item == null))
        {
            throw new ArgumentException(
                "Inference evidence must be non-empty and bounded.",
                nameof(evidence));
        }
        var orderedEvidence = evidenceItems
            .OrderBy(item => item.TurnId, StringComparer.Ordinal)
            .ThenBy(item => item.ScalarStart)
            .ThenBy(item => item.ScalarLength)
            .ThenBy(item => item.Role)
            .ToArray();
        Evidence = new ReadOnlyCollection<
            SemanticIntentInferenceEvidenceReference>(orderedEvidence);

        TargetCueEvidenceFingerprint = targetCueEvidenceFingerprint == null
            ? null
            : SemanticIntentContractGuard.Fingerprint(
                targetCueEvidenceFingerprint,
                nameof(targetCueEvidenceFingerprint));
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            Rank,
            Ambiguity,
            Confidence,
            SemanticIntentFingerprint.CanonicalSequence(
                orderedDescriptors.Select(item => item.Fingerprint)),
            SemanticIntentFingerprint.CanonicalSequence(orderedContexts),
            SemanticIntentFingerprint.CanonicalSequence(
                orderedEvidence.Select(item => item.Fingerprint)),
            TargetCueEvidenceFingerprint);
    }
}

internal sealed class SemanticIntentInferenceResponse
{
    internal string CompletionId { get; }
    internal string RequestFingerprint { get; }
    internal string InputFingerprint { get; }
    internal long RegistryGeneration { get; }
    internal string RegistryFingerprint { get; }
    internal string ProducerId { get; }
    internal int ProducerVersion { get; }
    internal string ProfileFingerprint { get; }
    internal long CompletedAtMonotonicTicks { get; }
    internal SemanticIntentInferenceDisposition Disposition { get; }
    internal IReadOnlyList<SemanticIntentInferenceCandidate> Candidates { get; }
    internal string Fingerprint { get; }
    internal bool ContainsRawContent => false;
    internal SemanticIntentInferenceRequestWitness RequestIdentityWitness {
        get;
    }

    internal SemanticIntentInferenceResponse(
        string completionId,
        string requestFingerprint,
        string inputFingerprint,
        long registryGeneration,
        string registryFingerprint,
        string producerId,
        int producerVersion,
        string profileFingerprint,
        long completedAtMonotonicTicks,
        SemanticIntentInferenceDisposition disposition,
        IEnumerable<SemanticIntentInferenceCandidate> candidates,
        SemanticIntentInferenceRequestWitness requestIdentityWitness)
    {
        CompletionId = SemanticIntentContractGuard.Identifier(
            completionId,
            nameof(completionId));
        RequestFingerprint = SemanticIntentContractGuard.Fingerprint(
            requestFingerprint,
            nameof(requestFingerprint));
        InputFingerprint = SemanticIntentContractGuard.Fingerprint(
            inputFingerprint,
            nameof(inputFingerprint));
        RegistryGeneration = SemanticIntentContractGuard.Positive(
            registryGeneration,
            nameof(registryGeneration));
        RegistryFingerprint = SemanticIntentContractGuard.Fingerprint(
            registryFingerprint,
            nameof(registryFingerprint));
        ProducerId = SemanticIntentContractGuard.Identifier(
            producerId,
            nameof(producerId));
        ProducerVersion = SemanticIntentContractGuard.Positive(
            producerVersion,
            nameof(producerVersion));
        ProfileFingerprint = SemanticIntentContractGuard.Fingerprint(
            profileFingerprint,
            nameof(profileFingerprint));
        if (completedAtMonotonicTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(completedAtMonotonicTicks));
        CompletedAtMonotonicTicks = completedAtMonotonicTicks;
        Disposition = SemanticIntentContractGuard.Enum(
            disposition,
            nameof(disposition));
        RequestIdentityWitness = requestIdentityWitness
            ?? throw new ArgumentNullException(nameof(requestIdentityWitness));
        if (RequestIdentityWitness.RequestFingerprint != RequestFingerprint)
        {
            throw new InvalidOperationException(
                "Inference response request witness crossed its fingerprint binding.");
        }

        var items = (candidates
                ?? throw new ArgumentNullException(nameof(candidates)))
            .Take(SemanticIntentProposalBatch.MaximumProposals + 1)
            .ToArray();
        if (items.Length > SemanticIntentProposalBatch.MaximumProposals
            || items.Any(item => item == null))
        {
            throw new ArgumentException(
                "Inference candidates exceeded their bounded schema.",
                nameof(candidates));
        }
        var ordered = items
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Fingerprint, StringComparer.Ordinal)
            .ToArray();
        Candidates = new ReadOnlyCollection<SemanticIntentInferenceCandidate>(
            ordered);
        Fingerprint = SemanticIntentFingerprint.HashCanonical(
            CompletionId,
            RequestFingerprint,
            InputFingerprint,
            RegistryGeneration,
            RegistryFingerprint,
            ProducerId,
            ProducerVersion,
            ProfileFingerprint,
            CompletedAtMonotonicTicks,
            Disposition,
            SemanticIntentFingerprint.CanonicalSequence(
                ordered.Select(item => item.Fingerprint)));
    }
}

internal interface ISemanticIntentInferencePort
{
    ValueTask<SemanticIntentInferenceResponse> InferAsync(
        SemanticIntentInferenceRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Trusted invocation-local monotonic observations. Stage B supplies no
/// concrete clock implementation or runtime registration.
/// </summary>
internal interface ISemanticIntentInferenceMonotonicClock
{
    long Frequency { get; }

    long ObserveTicks();
}
