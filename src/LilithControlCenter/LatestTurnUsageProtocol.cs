using System.IO;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithControlCenter;

public interface ILatestTurnUsageProjection
{
    Task<HermesLatestUsageSnapshot?> ReadLatestTurnUsageAsync(CancellationToken cancellationToken);
}

public sealed partial class D046CanonicalHermesWorkbenchRuntimeProtocol
{
    internal D046HermesRuntimeRequestBinding BindLatestTurnUsageGet()
        => new(HermesLatestUsageContract.GetMethod, HermesLatestUsageContract.GetRequest());
    internal HermesLatestUsageSnapshot DecodeLatestTurnUsage(LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope, D046HermesRuntimeRequestBinding binding)
    {
        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            if (binding.Method != HermesLatestUsageContract.GetMethod || response.Method != binding.Method || !response.Succeeded)
                throw new InvalidDataException("Latest usage response unavailable.");
            return HermesLatestUsageContract.Parse(response.HermesPayload);
        }
    }
}

public sealed partial class D046HermesWorkbenchRuntimePipeProjection : ILatestTurnUsageProjection
{
    private Guid _latestUsageCapabilityGeneration;
    private void CaptureLatestUsageHello(LilithRuntimeHello hello)
        => _latestUsageCapabilityGeneration = hello.Capabilities.Contains(HermesLatestUsageContract.GetMethod, StringComparer.Ordinal)
            ? _runtimeGeneration : Guid.Empty;
    public async Task<HermesLatestUsageSnapshot?> ReadLatestTurnUsageAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Guid generation;
        lock (_stateGate)
        {
            ThrowIfUnavailableLocked();
            if (_latestUsageCapabilityGeneration == Guid.Empty) return null;
            generation = _runtimeGeneration;
        }
        var protocol = _protocol as D046CanonicalHermesWorkbenchRuntimeProtocol ?? throw new NotSupportedException();
        return await SendCatalogReadAsync(protocol.BindLatestTurnUsageGet(), (response, envelope) =>
        {
            if (envelope.GenerationId != generation) throw new InvalidDataException("Latest usage generation changed.");
            return protocol.DecodeLatestTurnUsage(response, envelope, protocol.BindLatestTurnUsageGet());
        }, cancellationToken, responseTimeout: TimeSpan.FromSeconds(5), validateStateLocked: () =>
        {
            ThrowIfUnavailableLocked();
            if (_runtimeGeneration != generation || _latestUsageCapabilityGeneration != generation)
                throw new InvalidDataException("Latest usage generation changed.");
        }).ConfigureAwait(false);
    }
}
