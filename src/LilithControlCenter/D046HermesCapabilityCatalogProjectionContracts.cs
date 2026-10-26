using LilithRuntimeContracts;

namespace LilithControlCenter;

internal interface ID046HermesCapabilityCatalogProjection
{
    Task<HermesCapabilityCatalog> GetCapabilityCatalogAsync(
        CancellationToken cancellationToken);
}

internal interface ID046HermesCommandCatalogProjection
{
    Task<HermesCommandCatalogSnapshot> GetCommandCatalogAsync(CancellationToken cancellationToken);
}

internal interface ID046HermesCapabilityCatalogRuntimeProtocol
{
    D046HermesRuntimeRequestBinding BindCapabilityCatalog();

    HermesCapabilityCatalog DecodeCapabilityCatalog(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);
}
