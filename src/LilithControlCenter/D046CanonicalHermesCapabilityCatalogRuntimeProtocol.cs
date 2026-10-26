using System.IO;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithControlCenter;

public sealed partial class D046CanonicalHermesWorkbenchRuntimeProtocol
    : ID046HermesCapabilityCatalogRuntimeProtocol
{
    public const string CapabilityCatalogMethod =
        HermesCapabilityCatalogContract.Method;

    public D046HermesRuntimeRequestBinding BindCapabilityCatalog()
        => Bind(CapabilityCatalogMethod, new { });

    internal D046HermesRuntimeRequestBinding BindCommandCatalog()
        => Bind(HermesCommandCatalogContract.Method, new { });

    internal HermesCommandCatalogSnapshot DecodeCommandCatalog(LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope, D046HermesRuntimeRequestBinding binding)
    {
        if (envelope.GenerationId == Guid.Empty || !response.Succeeded
            || response.Method != HermesCommandCatalogContract.Method || response.Method != binding.Method
            || binding.HermesPayload.ValueKind != JsonValueKind.Object
            || binding.HermesPayload.EnumerateObject().Any())
            throw new InvalidDataException("Hermes command catalog response was not correlated.");
        var catalog = HermesCommandCatalogContract.Decode(response.HermesPayload);
        if (catalog.Identity.HermesCommit != RequiredHermesCommit)
            throw new InvalidDataException("Hermes command catalog identity did not match Runtime hello.");
        return catalog;
    }

    public HermesCapabilityCatalog DecodeCapabilityCatalog(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(binding);
        if (envelope.GenerationId == Guid.Empty
            || !string.Equals(
                response.Method,
                CapabilityCatalogMethod,
                StringComparison.Ordinal)
            || !string.Equals(
                binding.Method,
                CapabilityCatalogMethod,
                StringComparison.Ordinal)
            || binding.HermesPayload.ValueKind != JsonValueKind.Object
            || binding.HermesPayload.EnumerateObject().Any())
        {
            throw new InvalidDataException(
                "Hermes capability catalog response was not correlated.");
        }
        if (!response.Succeeded)
        {
            throw new InvalidDataException(
                "Runtime did not provide an authoritative capability catalog.");
        }

        var catalog = HermesCapabilityCatalogContract.Decode(
            response.HermesPayload);
        if (!string.Equals(
                catalog.Identity.HermesCommit,
                RequiredHermesCommit,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes capability catalog identity did not match Runtime hello.");
        }
        return catalog;
    }
}
