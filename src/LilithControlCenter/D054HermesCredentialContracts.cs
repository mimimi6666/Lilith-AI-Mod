using System.Collections.ObjectModel;
using System.IO;

namespace LilithControlCenter;

public sealed record D054HermesCredentialProvider(
    string ProviderId,
    string DisplayName,
    bool Configured,
    bool LegacySourcesPresent,
    LilithRuntimeContracts.HermesCredentialRegionMetadata? Region = null)
{
    public string FamilyId => Region?.FamilyId ?? ProviderId;
    public string FamilyName => Region?.FamilyName ?? DisplayName;
    public string VariantLabel => Region is { VariantId: not "default" } region
        ? ControlCenterLocalization.Current["credential.region." + region.VariantId] : DisplayName;
    public string BaseUrl => Region?.BaseUrl ?? string.Empty;
}

public sealed class D054HermesCredentialCatalog
{
    private readonly IReadOnlyList<D054HermesCredentialProvider> _providers;

    public IReadOnlyList<D054HermesCredentialProvider> Providers => _providers;

    public D054HermesCredentialCatalog(
        IEnumerable<D054HermesCredentialProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var frozen = providers.ToArray();
        if (frozen.Length > 256
            || frozen.Any(static value => value is null)
            || frozen.Select(static value => value.ProviderId)
                .Distinct(StringComparer.Ordinal)
                .Count() != frozen.Length)
        {
            throw new ArgumentException(
                "Hermes credential providers were rejected.",
                nameof(providers));
        }
        _providers = new ReadOnlyCollection<D054HermesCredentialProvider>(frozen);
    }
}

public sealed record D054HermesCredentialMutationReceipt(
    string ProviderId,
    string DisplayName,
    bool Configured,
    bool Changed,
    bool ManagedLegacySourcesCleared);

public interface ID054HermesCredentialProjection
{
    Task<D054HermesCredentialCatalog> GetCredentialStatusAsync(
        CancellationToken cancellationToken);

    Task<D054HermesCredentialMutationReceipt> ReplaceCredentialAsync(
        string providerId,
        string apiKey,
        CancellationToken cancellationToken);

    Task<D054HermesCredentialMutationReceipt> RemoveCredentialAsync(
        string providerId,
        CancellationToken cancellationToken);
}

internal interface ID054HermesCredentialRuntimeProtocol
{
    D046HermesRuntimeRequestBinding BindCredentialStatus();

    D046HermesRuntimeRequestBinding BindCredentialReplace(
        string providerId,
        string apiKey,
        string operationId);

    D046HermesRuntimeRequestBinding BindCredentialRemove(
        string providerId,
        string operationId);

    D054HermesCredentialCatalog DecodeCredentialStatus(
        LilithRuntimeContracts.LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D054HermesCredentialMutationReceipt DecodeCredentialMutation(
        LilithRuntimeContracts.LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);
}

public sealed class D054HermesCredentialOperationException : Exception
{
    public string Code { get; }

    internal D054HermesCredentialOperationException(
        string code,
        string message)
        : base(message)
        => Code = string.IsNullOrWhiteSpace(code)
            ? "credential.operation_failed"
            : code;
}

public sealed class D054HermesCredentialResultUncertainException : Exception
{
    public string Code { get; }

    internal D054HermesCredentialResultUncertainException(
        string code,
        string message)
        : base(message)
        => Code = string.IsNullOrWhiteSpace(code)
            ? "credential.result_uncertain"
            : code;
}

internal static class D054HermesCredentialGenerationAdmission
{
    internal static void RequireFresh(Guid retired, Guid candidate)
    {
        if (candidate == Guid.Empty)
        {
            throw new InvalidDataException(
                "Hermes credential reconnect omitted the Runtime generation.");
        }
        if (retired != Guid.Empty && retired == candidate)
        {
            throw new InvalidDataException(
                "Hermes credential reconnect reused the retired Runtime generation.");
        }
    }
}

internal static class D054HermesCredentialMutationPostcondition
{
    internal static void Require(
        D054HermesCredentialCatalog catalog,
        string providerId,
        bool expectedConfigured)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException(
                "Hermes credential provider must not be empty.",
                nameof(providerId));

        var provider = catalog.Providers.SingleOrDefault(value =>
            string.Equals(
                value.ProviderId,
                providerId,
                StringComparison.Ordinal));
        if (provider is null)
        {
            throw new InvalidDataException(
                "The fresh Hermes generation omitted the mutated credential provider.");
        }
        if (provider.Configured != expectedConfigured)
        {
            throw new InvalidDataException(
                "The fresh Hermes generation did not prove the credential mutation postcondition.");
        }
    }
}
