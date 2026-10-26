using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LilithProviderControlPlane;

internal enum ProviderControlPlaneStoreAdmission
{
    Invalid = 0,
    Applied = 1,
    DuplicateTransaction = 2,
    TransactionNotYetValid = 3,
    TransactionExpired = 4,
    StoreRevisionMismatch = 5,
    TargetIdentityMismatch = 6,
    InstanceNotFound = 7,
    ProviderMismatch = 8,
    ConfigurationGenerationMismatch = 9,
    ConfigurationGenerationNotMonotonic = 10,
    CatalogProfileMismatch = 11,
    CatalogGenerationNotMonotonic = 12,
    CatalogNotCurrent = 13,
    CatalogExpired = 14,
    CatalogObservationMismatch = 15,
    ModelSelectionMismatch = 16,
    CapabilityEvidenceMismatch = 17,
    FunctionAssignmentMismatch = 18
}

internal enum ProviderHealthStoreAdmission
{
    Invalid = 0,
    Applied = 1,
    SchedulerRejected = 2,
    DuplicateResult = 3,
    InstanceRemoved = 4,
    ProviderMismatch = 5,
    ConfigurationGenerationMismatch = 6,
    ResultNotYetValid = 7,
    ResultExpired = 8
}

internal sealed class ProviderControlPlaneStoreReceipt
{
    internal ProviderControlPlaneStoreAdmission Admission { get; }
    internal ProviderControlPlaneTransactionId TransactionId { get; }
    internal long RevisionBefore { get; }
    internal long RevisionAfter { get; }
    internal bool Applied => Admission == ProviderControlPlaneStoreAdmission.Applied;
    internal bool GrantsCapability => false;
    internal bool GrantsExecution => false;
    internal ProviderControlPlaneAuthorityCounters AuthorityCounters =>
        ProviderControlPlaneAuthorityCounters.Zero;

    internal ProviderControlPlaneStoreReceipt(
        ProviderControlPlaneStoreAdmission admission,
        ProviderControlPlaneTransactionId transactionId,
        long revisionBefore,
        long revisionAfter)
    {
        Admission = ProviderControlPlaneCoreGuard.Defined(
            admission,
            nameof(admission));
        TransactionId = transactionId
            ?? throw new ArgumentNullException(nameof(transactionId));
        RevisionBefore = ProviderControlPlaneCoreGuard.NonNegative(
            revisionBefore,
            nameof(revisionBefore));
        RevisionAfter = ProviderControlPlaneCoreGuard.NonNegative(
            revisionAfter,
            nameof(revisionAfter));
        if (Applied != (RevisionAfter == RevisionBefore + 1)
            || (!Applied && RevisionAfter != RevisionBefore))
        {
            throw new ArgumentException(
                "Store receipts must preserve exact revision semantics.");
        }
    }
}

internal sealed class ProviderHealthStoreReceipt
{
    internal ProviderHealthStoreAdmission Admission { get; }
    internal ProviderStartupHealthAttemptId AttemptId { get; }
    internal long RevisionBefore { get; }
    internal long RevisionAfter { get; }
    internal bool Applied => Admission == ProviderHealthStoreAdmission.Applied;
    internal bool GrantsCapability => false;
    internal bool GrantsExecution => false;
    internal bool SelectsModel => false;
    internal ProviderControlPlaneAuthorityCounters AuthorityCounters =>
        ProviderControlPlaneAuthorityCounters.Zero;

    internal ProviderHealthStoreReceipt(
        ProviderHealthStoreAdmission admission,
        ProviderStartupHealthAttemptId attemptId,
        long revisionBefore,
        long revisionAfter)
    {
        Admission = ProviderControlPlaneCoreGuard.Defined(
            admission,
            nameof(admission));
        AttemptId = attemptId ?? throw new ArgumentNullException(nameof(attemptId));
        RevisionBefore = ProviderControlPlaneCoreGuard.NonNegative(
            revisionBefore,
            nameof(revisionBefore));
        RevisionAfter = ProviderControlPlaneCoreGuard.NonNegative(
            revisionAfter,
            nameof(revisionAfter));
        if (Applied != (RevisionAfter == RevisionBefore + 1)
            || (!Applied && RevisionAfter != RevisionBefore))
        {
            throw new ArgumentException(
                "Health receipts must preserve exact revision semantics.");
        }
    }
}

internal sealed class ProviderFunctionAssignmentProjection
{
    internal ProviderControlPlaneFunction Function { get; }
    internal ProviderControlPlaneProviderIdentity Provider { get; }
    internal ProviderConfiguredInstanceId InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal long CatalogGeneration { get; }
    internal ProviderControlPlaneModelId ModelId { get; }
    internal ProviderCapabilityState CapabilityState { get; }
    internal ProviderCapabilityProvenance CapabilityProvenance { get; }

    internal ProviderFunctionAssignmentProjection(
        ProviderFunctionAssignment assignment,
        ProviderCapabilityEvidence evidence)
    {
        if (assignment == null)
            throw new ArgumentNullException(nameof(assignment));
        if (evidence == null)
            throw new ArgumentNullException(nameof(evidence));
        Function = assignment.Function;
        Provider = assignment.Provider;
        InstanceId = assignment.InstanceId;
        ConfigurationGeneration = assignment.ConfigurationGeneration;
        CatalogGeneration = assignment.CatalogGeneration;
        ModelId = assignment.ModelId;
        CapabilityState = evidence.State;
        CapabilityProvenance = evidence.Provenance;
    }
}

internal sealed class ProviderConfiguredInstanceProjection
{
    internal ProviderControlPlaneProviderIdentity Provider { get; }
    internal ProviderConfiguredInstanceId InstanceId { get; }
    internal long ConfigurationGeneration { get; }
    internal ProviderControlPlaneCredentialState CredentialState { get; }
    internal ProviderControlPlaneCatalogState CatalogState { get; }
    internal ProviderControlPlaneModelId? SelectedModelId { get; }
    internal ProviderCapabilityState CapabilityState { get; }
    internal ProviderCapabilityProvenance CapabilityProvenance { get; }
    internal ProviderHealthPresentation Health { get; }
    internal IReadOnlyList<ProviderFunctionAssignmentProjection> Assignments { get; }

    internal ProviderConfiguredInstanceProjection(
        ProviderConfiguredInstance instance,
        ProviderControlPlaneCatalogState catalogState,
        ProviderModelSelection? selection,
        ProviderCapabilityEvidence? evidence,
        ProviderHealthPresentation health,
        IEnumerable<ProviderFunctionAssignmentProjection>? assignments)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        Provider = instance.Provider;
        InstanceId = instance.InstanceId;
        ConfigurationGeneration = instance.ConfigurationGeneration;
        CredentialState = instance.CredentialState;
        CatalogState = ProviderControlPlaneCoreGuard.Defined(
            catalogState,
            nameof(catalogState));
        SelectedModelId = selection?.ModelId;
        CapabilityState = evidence?.State ?? ProviderCapabilityState.Unknown;
        CapabilityProvenance = evidence?.Provenance
            ?? ProviderCapabilityProvenance.None;
        Health = health ?? throw new ArgumentNullException(nameof(health));
        var frozen = (assignments
                ?? Array.Empty<ProviderFunctionAssignmentProjection>())
            .OrderBy(value => (int)value.Function)
            .ToArray();
        if (frozen.Any(value => value == null)
            || frozen.Select(value => value.Function).Distinct().Count()
                != frozen.Length)
        {
            throw new ArgumentException(
                "Projected assignments must be non-null and unique.",
                nameof(assignments));
        }
        Assignments = new ReadOnlyCollection<ProviderFunctionAssignmentProjection>(
            frozen);
    }
}

internal sealed class ProviderControlPlaneProjection
{
    internal long StoreRevision { get; }
    internal IReadOnlyList<ProviderConfiguredInstanceProjection> Instances { get; }

    internal ProviderControlPlaneProjection(
        long storeRevision,
        IEnumerable<ProviderConfiguredInstanceProjection> instances)
    {
        StoreRevision = ProviderControlPlaneCoreGuard.NonNegative(
            storeRevision,
            nameof(storeRevision));
        var frozen = (instances ?? throw new ArgumentNullException(nameof(instances)))
            .OrderBy(value => (int)value.Provider.Kind)
            .ThenBy(value => value.InstanceId.Value, StringComparer.Ordinal)
            .ToArray();
        if (frozen.Any(value => value == null)
            || frozen.Select(value => value.InstanceId.Value)
                .Distinct(StringComparer.Ordinal).Count() != frozen.Length)
        {
            throw new ArgumentException(
                "Projected configured instances must be non-null and unique.",
                nameof(instances));
        }
        Instances = new ReadOnlyCollection<ProviderConfiguredInstanceProjection>(
            frozen);
    }
}

internal sealed class ProviderControlPlaneStoreSnapshot
{
    internal long Revision { get; }
    internal IReadOnlyList<ProviderConfiguredInstance> Instances { get; }
    internal IReadOnlyList<ProviderCatalogObservation> Catalogs { get; }
    internal IReadOnlyList<ProviderModelSelection> Selections { get; }
    internal IReadOnlyList<ProviderCapabilityEvidence> CapabilityEvidence { get; }
    internal IReadOnlyList<ProviderFunctionAssignment> Assignments { get; }
    internal IReadOnlyList<ProviderHealthObservation> HealthObservations { get; }
    internal ProviderControlPlaneAuthorityCounters AuthorityCounters =>
        ProviderControlPlaneAuthorityCounters.Zero;

    internal ProviderControlPlaneStoreSnapshot(
        long revision,
        IEnumerable<ProviderConfiguredInstance> instances,
        IEnumerable<ProviderCatalogObservation> catalogs,
        IEnumerable<ProviderModelSelection> selections,
        IEnumerable<ProviderCapabilityEvidence> capabilityEvidence,
        IEnumerable<ProviderFunctionAssignment> assignments,
        IEnumerable<ProviderHealthObservation> healthObservations)
    {
        Revision = ProviderControlPlaneCoreGuard.NonNegative(
            revision,
            nameof(revision));
        Instances = Freeze(
            instances,
            value => value.InstanceId.Value,
            nameof(instances));
        Catalogs = Freeze(
            catalogs,
            value => value.InstanceId.Value,
            nameof(catalogs));
        Selections = Freeze(
            selections,
            value => value.InstanceId.Value,
            nameof(selections));
        CapabilityEvidence = Freeze(
            capabilityEvidence,
            value => value.EvidenceId.Value,
            nameof(capabilityEvidence));
        Assignments = Freeze(
            assignments,
            value => ((int)value.Function).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            nameof(assignments));
        HealthObservations = Freeze(
            healthObservations,
            value => value.InstanceId.Value,
            nameof(healthObservations));
    }

    internal ProviderConfiguredInstance? FindInstance(
        ProviderConfiguredInstanceId instanceId)
        => Instances.SingleOrDefault(value => value.InstanceId.Equals(instanceId));

    internal ProviderCatalogObservation? FindCatalog(
        ProviderConfiguredInstanceId instanceId)
        => Catalogs.SingleOrDefault(value => value.InstanceId.Equals(instanceId));

    internal ProviderModelSelection? FindSelection(
        ProviderConfiguredInstanceId instanceId)
        => Selections.SingleOrDefault(value => value.InstanceId.Equals(instanceId));

    internal ProviderCapabilityEvidence? FindCapabilityEvidence(
        ProviderConfiguredInstanceId instanceId,
        ProviderControlPlaneCapability capability)
        => CapabilityEvidence.SingleOrDefault(value =>
            value.InstanceId.Equals(instanceId)
            && value.Capability == capability);

    internal ProviderHealthObservation? FindHealth(
        ProviderConfiguredInstanceId instanceId)
        => HealthObservations.SingleOrDefault(value =>
            value.InstanceId.Equals(instanceId));

    internal ProviderControlPlaneProjection Project(DateTimeOffset now)
    {
        ProviderControlPlaneCoreGuard.Timestamp(now, nameof(now));
        var projected = Instances.Select(instance =>
        {
            var catalog = FindCatalog(instance.InstanceId);
            var catalogState = catalog == null
                ? ProviderControlPlaneCatalogState.NotObserved
                : now >= catalog.ExpiresAt
                    ? ProviderControlPlaneCatalogState.Stale
                    : catalog.State;
            var selection = FindSelection(instance.InstanceId);
            var evidence = FindCapabilityEvidence(
                instance.InstanceId,
                ProviderControlPlaneCapability.TextGeneration);
            var healthObservation = FindHealth(instance.InstanceId);
            var health = healthObservation == null
                ? ProviderHealthPresentationPolicy.Map(
                    ProviderRuntimeHealthState.NotObserved)
                : healthObservation.IsCurrent(now)
                    ? healthObservation.Presentation
                    : ProviderHealthPresentationPolicy.Map(
                        ProviderRuntimeHealthState.Expired);
            var assignments = Assignments
                .Where(value => value.InstanceId.Equals(instance.InstanceId))
                .Select(assignment =>
                {
                    var assignmentEvidence = CapabilityEvidence.Single(value =>
                        value.EvidenceId.Equals(assignment.CapabilityEvidenceId));
                    return new ProviderFunctionAssignmentProjection(
                        assignment,
                        assignmentEvidence);
                });
            return new ProviderConfiguredInstanceProjection(
                instance,
                catalogState,
                selection,
                evidence,
                health,
                assignments);
        });
        return new ProviderControlPlaneProjection(Revision, projected);
    }

    private static IReadOnlyList<T> Freeze<T>(
        IEnumerable<T> values,
        Func<T, string> identity,
        string parameterName)
        where T : class
    {
        var source = (values ?? throw new ArgumentNullException(parameterName))
            .ToArray();
        if (source.Any(value => value == null))
        {
            throw new ArgumentException(
                "Snapshot members cannot be null.",
                parameterName);
        }
        var frozen = source
            .OrderBy(identity, StringComparer.Ordinal)
            .ToArray();
        if (frozen.Select(identity).Distinct(StringComparer.Ordinal).Count()
                != frozen.Length)
        {
            throw new ArgumentException(
                "Snapshot members must be non-null and uniquely identified.",
                parameterName);
        }
        return new ReadOnlyCollection<T>(frozen);
    }
}

internal sealed class InMemoryProviderControlPlaneStore
{
    private readonly Dictionary<string, ProviderConfiguredInstance> _instances =
        new(StringComparer.Ordinal);
    private readonly Dictionary<
        string,
        (ProviderControlPlaneProviderIdentity Provider,
            long ConfigurationGeneration)> _retiredInstances =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProviderCatalogObservation> _catalogs =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProviderModelSelection> _selections =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProviderCapabilityEvidence> _evidence =
        new(StringComparer.Ordinal);
    private readonly Dictionary<ProviderControlPlaneFunction, ProviderFunctionAssignment>
        _assignments = new();
    private readonly Dictionary<string, ProviderHealthObservation> _health =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _transactions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _healthResults = new(StringComparer.Ordinal);
    private long _revision;

    internal ProviderControlPlaneStoreSnapshot Snapshot()
        => new(
            _revision,
            _instances.Values,
            _catalogs.Values,
            _selections.Values,
            _evidence.Values,
            _assignments.Values,
            _health.Values);

    internal ProviderControlPlaneStoreReceipt TryConfigure(
        ProviderControlPlaneTransactionIdentity transaction,
        ProviderConfiguredInstance desired,
        DateTimeOffset now)
    {
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));
        if (desired == null)
            throw new ArgumentNullException(nameof(desired));
        var admission = ValidateTransaction(transaction, now);
        if (admission != ProviderControlPlaneStoreAdmission.Applied)
            return Receipt(admission, transaction);
        if (!transaction.Provider.Equals(desired.Provider)
            || !transaction.InstanceId.Equals(desired.InstanceId)
            || transaction.ConfigurationGeneration
                != desired.ConfigurationGeneration)
        {
            return Receipt(
                ProviderControlPlaneStoreAdmission.TargetIdentityMismatch,
                transaction);
        }

        if (_instances.TryGetValue(desired.InstanceId.Value, out var current))
        {
            if (!current.Provider.Equals(desired.Provider))
            {
                return Receipt(
                    ProviderControlPlaneStoreAdmission.ProviderMismatch,
                    transaction);
            }
            if (desired.ConfigurationGeneration
                != current.ConfigurationGeneration + 1)
            {
                return Receipt(
                    ProviderControlPlaneStoreAdmission
                        .ConfigurationGenerationNotMonotonic,
                    transaction);
            }
        }
        else if (_retiredInstances.TryGetValue(
            desired.InstanceId.Value,
            out var retired))
        {
            if (!retired.Provider.Equals(desired.Provider))
            {
                return Receipt(
                    ProviderControlPlaneStoreAdmission.ProviderMismatch,
                    transaction);
            }
            if (desired.ConfigurationGeneration
                != retired.ConfigurationGeneration + 1)
            {
                return Receipt(
                    ProviderControlPlaneStoreAdmission
                        .ConfigurationGenerationNotMonotonic,
                    transaction);
            }
        }
        else if (desired.ConfigurationGeneration != 1)
        {
            return Receipt(
                ProviderControlPlaneStoreAdmission
                    .ConfigurationGenerationNotMonotonic,
                transaction);
        }

        var before = _revision;
        _instances[desired.InstanceId.Value] = desired;
        RetireDependentState(desired.InstanceId);
        CompleteTransaction(transaction);
        return new ProviderControlPlaneStoreReceipt(
            ProviderControlPlaneStoreAdmission.Applied,
            transaction.TransactionId,
            before,
            _revision);
    }

    internal ProviderControlPlaneStoreReceipt TryRemove(
        ProviderControlPlaneTransactionIdentity transaction,
        DateTimeOffset now)
    {
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));
        var admission = ValidateTransaction(transaction, now);
        if (admission != ProviderControlPlaneStoreAdmission.Applied)
            return Receipt(admission, transaction);
        if (!_instances.TryGetValue(transaction.InstanceId.Value, out var current))
            return Receipt(ProviderControlPlaneStoreAdmission.InstanceNotFound, transaction);
        var match = MatchTarget(transaction, current);
        if (match != ProviderControlPlaneStoreAdmission.Applied)
            return Receipt(match, transaction);

        var before = _revision;
        _retiredInstances[current.InstanceId.Value] = (
            current.Provider,
            current.ConfigurationGeneration);
        _instances.Remove(current.InstanceId.Value);
        RetireDependentState(current.InstanceId);
        CompleteTransaction(transaction);
        return new ProviderControlPlaneStoreReceipt(
            ProviderControlPlaneStoreAdmission.Applied,
            transaction.TransactionId,
            before,
            _revision);
    }

    internal ProviderControlPlaneStoreReceipt TryRecordCatalog(
        ProviderControlPlaneTransactionIdentity transaction,
        ProviderCatalogObservation observation,
        DateTimeOffset now)
    {
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));
        if (observation == null)
            throw new ArgumentNullException(nameof(observation));
        var admission = ValidateTransaction(transaction, now);
        if (admission != ProviderControlPlaneStoreAdmission.Applied)
            return Receipt(admission, transaction);
        var instanceResult = FindExactInstance(transaction);
        if (instanceResult.Admission != ProviderControlPlaneStoreAdmission.Applied)
            return Receipt(instanceResult.Admission, transaction);
        var instance = instanceResult.Instance!;
        if (!observation.Provider.Equals(instance.Provider)
            || !observation.InstanceId.Equals(instance.InstanceId)
            || observation.ConfigurationGeneration
                != instance.ConfigurationGeneration)
        {
            return Receipt(
                ProviderControlPlaneStoreAdmission.TargetIdentityMismatch,
                transaction);
        }
        if (!observation.CatalogProfileId.Equals(
                instance.Descriptor.CatalogProfileId))
        {
            return Receipt(
                ProviderControlPlaneStoreAdmission.CatalogProfileMismatch,
                transaction);
        }
        if (now < observation.ObservedAt || now >= observation.ExpiresAt)
            return Receipt(ProviderControlPlaneStoreAdmission.CatalogExpired, transaction);
        if (_catalogs.TryGetValue(instance.InstanceId.Value, out var previous))
        {
            if (observation.CatalogGeneration != previous.CatalogGeneration + 1)
            {
                return Receipt(
                    ProviderControlPlaneStoreAdmission
                        .CatalogGenerationNotMonotonic,
                    transaction);
            }
        }
        else if (observation.CatalogGeneration != 1)
        {
            return Receipt(
                ProviderControlPlaneStoreAdmission.CatalogGenerationNotMonotonic,
                transaction);
        }

        var before = _revision;
        _catalogs[instance.InstanceId.Value] = observation;
        RetireSelectionState(instance.InstanceId);
        CompleteTransaction(transaction);
        return new ProviderControlPlaneStoreReceipt(
            ProviderControlPlaneStoreAdmission.Applied,
            transaction.TransactionId,
            before,
            _revision);
    }

    internal ProviderControlPlaneStoreReceipt TrySelectModel(
        ProviderControlPlaneTransactionIdentity transaction,
        ProviderModelSelection selection,
        DateTimeOffset now)
    {
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));
        if (selection == null)
            throw new ArgumentNullException(nameof(selection));
        var admission = ValidateTransaction(transaction, now);
        if (admission != ProviderControlPlaneStoreAdmission.Applied)
            return Receipt(admission, transaction);
        var instanceResult = FindExactInstance(transaction);
        if (instanceResult.Admission != ProviderControlPlaneStoreAdmission.Applied)
            return Receipt(instanceResult.Admission, transaction);
        var instance = instanceResult.Instance!;
        if (!_catalogs.TryGetValue(instance.InstanceId.Value, out var catalog))
            return Receipt(ProviderControlPlaneStoreAdmission.CatalogNotCurrent, transaction);
        if (!catalog.IsSelectableAt(now))
            return Receipt(ProviderControlPlaneStoreAdmission.CatalogExpired, transaction);
        if (!selection.Provider.Equals(instance.Provider)
            || !selection.InstanceId.Equals(instance.InstanceId)
            || selection.ConfigurationGeneration != instance.ConfigurationGeneration)
        {
            return Receipt(
                ProviderControlPlaneStoreAdmission.TargetIdentityMismatch,
                transaction);
        }
        if (!selection.CatalogObservationId.Equals(catalog.ObservationId)
            || selection.CatalogGeneration != catalog.CatalogGeneration
            || !catalog.Models.Contains(selection.ModelId))
        {
            return Receipt(
                ProviderControlPlaneStoreAdmission.CatalogObservationMismatch,
                transaction);
        }

        var before = _revision;
        _selections[instance.InstanceId.Value] = selection;
        RetireCapabilityAndAssignments(instance.InstanceId);
        CompleteTransaction(transaction);
        return new ProviderControlPlaneStoreReceipt(
            ProviderControlPlaneStoreAdmission.Applied,
            transaction.TransactionId,
            before,
            _revision);
    }

    internal ProviderControlPlaneStoreReceipt TryRecordCapabilityEvidence(
        ProviderControlPlaneTransactionIdentity transaction,
        ProviderCapabilityEvidence evidence,
        DateTimeOffset now)
    {
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));
        if (evidence == null)
            throw new ArgumentNullException(nameof(evidence));
        var admission = ValidateTransaction(transaction, now);
        if (admission != ProviderControlPlaneStoreAdmission.Applied)
            return Receipt(admission, transaction);
        var instanceResult = FindExactInstance(transaction);
        if (instanceResult.Admission != ProviderControlPlaneStoreAdmission.Applied)
            return Receipt(instanceResult.Admission, transaction);
        var instance = instanceResult.Instance!;
        if (!_selections.TryGetValue(instance.InstanceId.Value, out var selection))
        {
            return Receipt(
                ProviderControlPlaneStoreAdmission.ModelSelectionMismatch,
                transaction);
        }
        if (now < evidence.ObservedAt || now >= evidence.ExpiresAt
            || !evidence.Provider.Equals(selection.Provider)
            || !evidence.InstanceId.Equals(selection.InstanceId)
            || evidence.ConfigurationGeneration
                != selection.ConfigurationGeneration
            || evidence.CatalogGeneration != selection.CatalogGeneration
            || !evidence.ModelSelectionId.Equals(selection.SelectionId)
            || !evidence.ModelId.Equals(selection.ModelId)
            || !evidence.TextExecutionProfileId.Equals(
                selection.TextExecutionProfileId))
        {
            return Receipt(
                ProviderControlPlaneStoreAdmission.CapabilityEvidenceMismatch,
                transaction);
        }

        var before = _revision;
        foreach (var existing in _evidence.Values
                     .Where(value =>
                         value.InstanceId.Equals(instance.InstanceId)
                         && value.Capability == evidence.Capability)
                     .ToArray())
        {
            _evidence.Remove(existing.EvidenceId.Value);
        }
        _evidence[evidence.EvidenceId.Value] = evidence;
        foreach (var assignment in _assignments
                     .Where(value =>
                         value.Value.InstanceId.Equals(instance.InstanceId))
                     .Select(value => value.Key)
                     .ToArray())
        {
            _assignments.Remove(assignment);
        }
        CompleteTransaction(transaction);
        return new ProviderControlPlaneStoreReceipt(
            ProviderControlPlaneStoreAdmission.Applied,
            transaction.TransactionId,
            before,
            _revision);
    }

    internal ProviderControlPlaneStoreReceipt TryAssignFunction(
        ProviderControlPlaneTransactionIdentity transaction,
        ProviderFunctionAssignment assignment,
        DateTimeOffset now)
    {
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));
        if (assignment == null)
            throw new ArgumentNullException(nameof(assignment));
        var admission = ValidateTransaction(transaction, now);
        if (admission != ProviderControlPlaneStoreAdmission.Applied)
            return Receipt(admission, transaction);
        var instanceResult = FindExactInstance(transaction);
        if (instanceResult.Admission != ProviderControlPlaneStoreAdmission.Applied)
            return Receipt(instanceResult.Admission, transaction);
        var instance = instanceResult.Instance!;
        if (!_selections.TryGetValue(instance.InstanceId.Value, out var selection)
            || !selection.SelectionId.Equals(assignment.ModelSelectionId)
            || !selection.ModelId.Equals(assignment.ModelId)
            || selection.CatalogGeneration != assignment.CatalogGeneration)
        {
            return Receipt(
                ProviderControlPlaneStoreAdmission.ModelSelectionMismatch,
                transaction);
        }
        if (!_evidence.TryGetValue(
                assignment.CapabilityEvidenceId.Value,
                out var evidence)
            || !evidence.CanAuthorize(assignment.Function, now)
            || !evidence.InstanceId.Equals(instance.InstanceId)
            || !evidence.ModelSelectionId.Equals(selection.SelectionId))
        {
            return Receipt(
                ProviderControlPlaneStoreAdmission.CapabilityEvidenceMismatch,
                transaction);
        }
        if (!assignment.Provider.Equals(instance.Provider)
            || !assignment.InstanceId.Equals(instance.InstanceId)
            || assignment.ConfigurationGeneration
                != instance.ConfigurationGeneration)
        {
            return Receipt(
                ProviderControlPlaneStoreAdmission.FunctionAssignmentMismatch,
                transaction);
        }

        var before = _revision;
        _assignments[assignment.Function] = assignment;
        CompleteTransaction(transaction);
        return new ProviderControlPlaneStoreReceipt(
            ProviderControlPlaneStoreAdmission.Applied,
            transaction.TransactionId,
            before,
            _revision);
    }

    internal ProviderHealthStoreReceipt TryApplyHealth(
        ProviderStartupHealthCompletionReceipt completion,
        DateTimeOffset now)
    {
        if (completion == null)
            throw new ArgumentNullException(nameof(completion));
        ProviderControlPlaneCoreGuard.Timestamp(now, nameof(now));
        var before = _revision;
        var attemptId = completion.AttemptId;
        if (!completion.Accepted || completion.Observation == null)
        {
            return new ProviderHealthStoreReceipt(
                ProviderHealthStoreAdmission.SchedulerRejected,
                attemptId,
                before,
                before);
        }
        var observation = completion.Observation;
        if (_healthResults.Contains(observation.Fingerprint))
        {
            return new ProviderHealthStoreReceipt(
                ProviderHealthStoreAdmission.DuplicateResult,
                attemptId,
                before,
                before);
        }
        if (!_instances.TryGetValue(observation.InstanceId.Value, out var instance))
        {
            return new ProviderHealthStoreReceipt(
                ProviderHealthStoreAdmission.InstanceRemoved,
                attemptId,
                before,
                before);
        }
        if (!observation.Provider.Equals(instance.Provider))
        {
            return new ProviderHealthStoreReceipt(
                ProviderHealthStoreAdmission.ProviderMismatch,
                attemptId,
                before,
                before);
        }
        if (observation.ConfigurationGeneration
            != instance.ConfigurationGeneration)
        {
            return new ProviderHealthStoreReceipt(
                ProviderHealthStoreAdmission.ConfigurationGenerationMismatch,
                attemptId,
                before,
                before);
        }
        if (now < observation.ObservedAt)
        {
            return new ProviderHealthStoreReceipt(
                ProviderHealthStoreAdmission.ResultNotYetValid,
                attemptId,
                before,
                before);
        }
        if (now >= observation.ExpiresAt)
        {
            return new ProviderHealthStoreReceipt(
                ProviderHealthStoreAdmission.ResultExpired,
                attemptId,
                before,
                before);
        }

        _health[instance.InstanceId.Value] = observation;
        _healthResults.Add(observation.Fingerprint);
        _revision++;
        return new ProviderHealthStoreReceipt(
            ProviderHealthStoreAdmission.Applied,
            attemptId,
            before,
            _revision);
    }

    private ProviderControlPlaneStoreAdmission ValidateTransaction(
        ProviderControlPlaneTransactionIdentity transaction,
        DateTimeOffset now)
    {
        ProviderControlPlaneCoreGuard.Timestamp(now, nameof(now));
        if (_transactions.Contains(transaction.TransactionId.Value))
            return ProviderControlPlaneStoreAdmission.DuplicateTransaction;
        if (now < transaction.CreatedAt)
            return ProviderControlPlaneStoreAdmission.TransactionNotYetValid;
        if (now >= transaction.ExpiresAt)
            return ProviderControlPlaneStoreAdmission.TransactionExpired;
        if (transaction.ExpectedStoreRevision != _revision)
            return ProviderControlPlaneStoreAdmission.StoreRevisionMismatch;
        return ProviderControlPlaneStoreAdmission.Applied;
    }

    private (ProviderControlPlaneStoreAdmission Admission,
        ProviderConfiguredInstance? Instance) FindExactInstance(
        ProviderControlPlaneTransactionIdentity transaction)
    {
        if (!_instances.TryGetValue(transaction.InstanceId.Value, out var instance))
        {
            return (
                ProviderControlPlaneStoreAdmission.InstanceNotFound,
                null);
        }
        var admission = MatchTarget(transaction, instance);
        return (admission, admission == ProviderControlPlaneStoreAdmission.Applied
            ? instance
            : null);
    }

    private static ProviderControlPlaneStoreAdmission MatchTarget(
        ProviderControlPlaneTransactionIdentity transaction,
        ProviderConfiguredInstance instance)
    {
        if (!transaction.Provider.Equals(instance.Provider))
            return ProviderControlPlaneStoreAdmission.ProviderMismatch;
        if (!transaction.InstanceId.Equals(instance.InstanceId))
            return ProviderControlPlaneStoreAdmission.TargetIdentityMismatch;
        if (transaction.ConfigurationGeneration
            != instance.ConfigurationGeneration)
        {
            return ProviderControlPlaneStoreAdmission
                .ConfigurationGenerationMismatch;
        }
        return ProviderControlPlaneStoreAdmission.Applied;
    }

    private ProviderControlPlaneStoreReceipt Receipt(
        ProviderControlPlaneStoreAdmission admission,
        ProviderControlPlaneTransactionIdentity transaction)
        => new(
            admission,
            transaction.TransactionId,
            _revision,
            _revision);

    private void CompleteTransaction(
        ProviderControlPlaneTransactionIdentity transaction)
    {
        _transactions.Add(transaction.TransactionId.Value);
        _revision++;
    }

    private void RetireDependentState(ProviderConfiguredInstanceId instanceId)
    {
        _catalogs.Remove(instanceId.Value);
        RetireSelectionState(instanceId);
        _health.Remove(instanceId.Value);
    }

    private void RetireSelectionState(ProviderConfiguredInstanceId instanceId)
    {
        _selections.Remove(instanceId.Value);
        RetireCapabilityAndAssignments(instanceId);
    }

    private void RetireCapabilityAndAssignments(
        ProviderConfiguredInstanceId instanceId)
    {
        foreach (var evidence in _evidence.Values
                     .Where(value => value.InstanceId.Equals(instanceId))
                     .ToArray())
        {
            _evidence.Remove(evidence.EvidenceId.Value);
        }
        foreach (var assignment in _assignments
                     .Where(value => value.Value.InstanceId.Equals(instanceId))
                     .Select(value => value.Key)
                     .ToArray())
        {
            _assignments.Remove(assignment);
        }
    }
}
