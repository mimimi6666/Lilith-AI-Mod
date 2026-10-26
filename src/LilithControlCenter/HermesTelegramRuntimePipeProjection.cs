namespace LilithControlCenter;

public sealed partial class D046HermesWorkbenchRuntimePipeProjection
    : IHermesTelegramMessagingProjection, IHermesTelegramVoiceProjection
{
    public Task<HermesTelegramVoiceStatus> GetTelegramVoiceStatusAsync(
        string userId, CancellationToken cancellationToken)
        => RunTelegramOperationAsync(
            protocol => RequireTelegramVoiceProtocol(protocol).BindTelegramVoiceStatus(userId),
            (protocol, response, envelope, binding) =>
                RequireTelegramVoiceProtocol(protocol).DecodeTelegramVoiceStatus(response, envelope, binding),
            cancellationToken, D046HermesReadOperation.TelegramVoiceStatus);

    public async Task<HermesTelegramVoiceStatus> SetTelegramVoiceModeAsync(
        string userId, HermesTelegramVoiceMode mode, string expectedRevision,
        CancellationToken cancellationToken)
    {
        var receipt = await RunTelegramOperationAsync(
            protocol => RequireTelegramVoiceProtocol(protocol).BindTelegramVoiceSet(userId, mode, expectedRevision),
            (protocol, response, envelope, binding) =>
                RequireTelegramVoiceProtocol(protocol).DecodeTelegramVoiceStatus(response, envelope, binding),
            cancellationToken).ConfigureAwait(false);
        var receiptGeneration = RuntimeGeneration;
        cancellationToken.ThrowIfCancellationRequested();
        var readback = await GetTelegramVoiceStatusAsync(userId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (receiptGeneration == Guid.Empty || RuntimeGeneration != receiptGeneration)
            throw new HermesTelegramResultUncertainException(
                "Telegram voice readback crossed a Runtime generation change.");
        return HermesTelegramVoiceReceiptAdmission.RequireReadback(receipt, readback);
    }

    private static IHermesTelegramVoiceRuntimeProtocol RequireTelegramVoiceProtocol(
        IHermesTelegramMessagingRuntimeProtocol protocol)
        => protocol as IHermesTelegramVoiceRuntimeProtocol
            ?? throw new NotSupportedException("目前無法設定 Telegram 語音。");

    public Task<HermesTelegramStatus> GetTelegramStatusAsync(
        CancellationToken cancellationToken)
        => RunTelegramOperationAsync(
            protocol => protocol.BindTelegramStatus(),
            (protocol, response, envelope, binding) =>
                protocol.DecodeTelegramStatus(response, envelope, binding),
            cancellationToken, D046HermesReadOperation.TelegramStatus);

    public Task<HermesTelegramTokenMutationReceipt> ReplaceTelegramTokenAsync(
        string botToken,
        CancellationToken cancellationToken)
        => RunTelegramOperationAsync(
            protocol => protocol.BindTelegramTokenReplace(
                botToken,
                "cc-" + Guid.NewGuid().ToString("N")),
            (protocol, response, envelope, binding) =>
                protocol.DecodeTelegramTokenMutation(
                    response,
                    envelope,
                    binding),
            cancellationToken);

    public Task<HermesTelegramTokenMutationReceipt> RemoveTelegramTokenAsync(
        CancellationToken cancellationToken)
        => RunTelegramOperationAsync(
            protocol => protocol.BindTelegramTokenRemove(
                "cc-" + Guid.NewGuid().ToString("N")),
            (protocol, response, envelope, binding) =>
                protocol.DecodeTelegramTokenMutation(
                    response,
                    envelope,
                    binding),
            cancellationToken);

    public Task<HermesTelegramPairingCatalog> GetTelegramPairingAsync(
        CancellationToken cancellationToken)
        => RunTelegramOperationAsync(
            protocol => protocol.BindTelegramPairingList(),
            (protocol, response, envelope, binding) =>
                protocol.DecodeTelegramPairing(response, envelope, binding),
            cancellationToken, D046HermesReadOperation.TelegramPairing);

    public Task<HermesTelegramPairingApprovalReceipt>
        ApproveTelegramPairingAsync(
            string requestId,
            CancellationToken cancellationToken)
        => RunTelegramOperationAsync(
            protocol => protocol.BindTelegramPairingApprove(requestId),
            (protocol, response, envelope, binding) =>
                protocol.DecodeTelegramPairingApproval(
                    response,
                    envelope,
                    binding),
            cancellationToken);

    public Task<HermesTelegramPairingRevokeReceipt> RevokeTelegramUserAsync(
        string userId,
        CancellationToken cancellationToken)
        => RunTelegramOperationAsync(
            protocol => protocol.BindTelegramPairingRevoke(userId),
            (protocol, response, envelope, binding) =>
                protocol.DecodeTelegramPairingRevoke(
                    response,
                    envelope,
                    binding),
            cancellationToken);

    private async Task<T> RunTelegramOperationAsync<T>(
        Func<IHermesTelegramMessagingRuntimeProtocol,
            D046HermesRuntimeRequestBinding> bind,
        Func<
            IHermesTelegramMessagingRuntimeProtocol,
            LilithRuntimeContracts.LilithRuntimeResponse,
            D046HermesRuntimeEnvelopeContext,
            D046HermesRuntimeRequestBinding,
            T> decode,
        CancellationToken cancellationToken,
        D046HermesReadOperation? readOperation = null)
    {
        // Observe only reads; preserve the existing gate and mutation authority.
        using var timing = readOperation is { } operation
            ? ReadDiagnostics.Begin(operation, cancellationToken) : null;
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            timing?.Advance(D046HermesReadPhase.Readiness);
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            var protocol = RequireTelegramProtocol();
            var binding = bind(protocol);
            timing?.Advance(D046HermesReadPhase.Response);
            var result = await SendRequestAsync(
                binding,
                (response, envelope) => decode(
                    protocol,
                    response,
                    envelope,
                    binding),
                cancellationToken,
                isDecodeFailureIsolated: true).ConfigureAwait(false);
            return timing is null ? result : timing.Success(result);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private IHermesTelegramMessagingRuntimeProtocol RequireTelegramProtocol()
        => _protocol as IHermesTelegramMessagingRuntimeProtocol
            ?? throw new NotSupportedException(
                "目前無法管理 Telegram 設定。");
}
