using System.IO;
using LilithRuntimeContracts;

namespace LilithControlCenter;

public sealed partial class D046HermesWorkbenchRuntimePipeProjection
{
    // These exact no-effect reads do not own the mutation/answer queue.
    // One outstanding request per method also bounds retained late replies:
    // a timeout never creates unlimited orphaned requests or automatic retries.
    private readonly Dictionary<string, SemaphoreSlim> _catalogReadSlots = new(StringComparer.Ordinal)
    {
        [HermesCapabilityCatalogContract.Method] = new(1, 1),
        [HermesCommandCatalogContract.Method] = new(1, 1),
        ["tools.catalog"] = new(1, 1),
        ["tools.settings.get"] = new(1, 1),
        [HermesUsageBudgetContract.GetMethod] = new(1, 1),
        [HermesUsageBudgetPreviewContract.PreviewMethod] = new(1, 1),
        [HermesLatestUsageContract.GetMethod] = new(1, 1)
    };

    internal async Task<T> SendCatalogReadAsync<T>(D046HermesRuntimeRequestBinding binding,
        Func<LilithRuntimeResponse, D046HermesRuntimeEnvelopeContext, T> decode,
        CancellationToken cancellationToken, TimeSpan? responseTimeout = null,
        Action? validateStateLocked = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(decode);
        if (!_catalogReadSlots.TryGetValue(binding.Method, out var slot))
            throw new InvalidOperationException("Only the closed catalog read surface may use this lane.");
        using var admission = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        // Queue admission is bounded independently from the response budget.
        if (!await slot.WaitAsync(responseTimeout ?? _responseTimeout, admission.Token).ConfigureAwait(false))
            throw new TimeoutException("A preceding native catalog read is still pending.");

        var retainSlot = false;
        var requestId = Guid.NewGuid();
        D046PendingRuntimeResponse? pending = null;
        var sent = false;
        try
        {
            await EnsureReadyAsync(admission.Token).ConfigureAwait(false);
            Guid generation;
            lock (_stateGate)
            {
                ThrowIfUnavailableLocked();
                generation = _runtimeGeneration;
            }
            pending = new D046PendingRuntimeResponse(binding.Method, (response, envelope) =>
            {
                if (envelope.GenerationId != generation)
                    throw new InvalidDataException("Native catalog generation changed during the read.");
                return decode(response, envelope)!;
            }, isDecodeFailureIsolated: true);
            if (!_pending.TryAdd(requestId, pending))
                throw new InvalidOperationException("Runtime request identity collided.");
            await WriteSerializedRequestAsync(binding, requestId,
                new LilithRuntimeRequest(binding.Method, binding.HermesPayload), validateStateLocked, null, admission.Token)
                .ConfigureAwait(false);
            sent = true;
            var result = await pending.Completion.Task.WaitAsync(responseTimeout ?? _responseTimeout,
                admission.Token).ConfigureAwait(false);
            return (T)result;
        }
        catch (Exception exception) when (sent && pending is not null
            && exception is TimeoutException or OperationCanceledException)
        {
            // No-effect caller timeout/cancellation retires adoption, not the
            // exact decoder. The receive loop still validates sequence, request,
            // method, generation and shape before discarding its late result.
            // Retain this method's slot until receipt/fault/disposal, so even a
            // peer that never replies can retain at most one tiny request per method.
            retainSlot = true;
            _ = RetireCatalogReadAsync(pending, slot);
            throw;
        }
        finally
        {
            if (!retainSlot)
            {
                _pending.TryRemove(requestId, out _);
                slot.Release();
            }
        }
    }

    private static async Task RetireCatalogReadAsync(D046PendingRuntimeResponse pending, SemaphoreSlim slot)
    {
        try { await pending.Completion.Task.ConfigureAwait(false); }
        catch (Exception) { /* The original caller already received no result. */ }
        finally { slot.Release(); }
    }
}
