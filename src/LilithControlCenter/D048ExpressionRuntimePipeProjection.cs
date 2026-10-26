namespace LilithControlCenter;

public sealed partial class D046HermesWorkbenchRuntimePipeProjection : ID048ExpressionProjection
{
    public Task<D048ExpressionCatalog> GetExpressionAsync(CancellationToken cancellationToken)
        => RunExpressionAsync(p => p.BindExpressionGet(), (p, r, e, b) => p.DecodeExpressionCatalog(r, e, b), cancellationToken);
    public Task<D048ExpressionVersion> GetExpressionVersionAsync(int revision, CancellationToken cancellationToken)
        => RunExpressionAsync(p => p.BindExpressionVersion(revision), (p, r, e, b) => p.DecodeExpressionVersion(r, e, b), cancellationToken);
    public Task<D048ExpressionCatalog> SaveExpressionAsync(string operationId, int expectedRevision, string content, string reason, CancellationToken cancellationToken)
        => RunExpressionAsync(p => p.BindExpressionSave(operationId, expectedRevision, content, reason), (p, r, e, b) => p.DecodeExpressionCatalog(r, e, b), cancellationToken);
    public Task<D048ExpressionCatalog> RevertExpressionAsync(string operationId, int expectedRevision, int targetRevision, string reason, CancellationToken cancellationToken)
        => RunExpressionAsync(p => p.BindExpressionRevert(operationId, expectedRevision, targetRevision, reason), (p, r, e, b) => p.DecodeExpressionCatalog(r, e, b), cancellationToken);
    private async Task<T> RunExpressionAsync<T>(Func<D046CanonicalHermesWorkbenchRuntimeProtocol, D046HermesRuntimeRequestBinding> bind,
        Func<D046CanonicalHermesWorkbenchRuntimeProtocol, LilithRuntimeContracts.LilithRuntimeResponse,
            D046HermesRuntimeEnvelopeContext, D046HermesRuntimeRequestBinding, T> decode, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var protocol = _protocol as D046CanonicalHermesWorkbenchRuntimeProtocol ?? throw new NotSupportedException();
            var binding = bind(protocol);
            return await SendRequestAsync(binding, (r, e) => decode(protocol, r, e, binding), cancellationToken,
                isDecodeFailureIsolated: true).ConfigureAwait(false);
        }
        finally { _operationGate.Release(); }
    }
}
