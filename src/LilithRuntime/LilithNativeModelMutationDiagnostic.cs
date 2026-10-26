namespace LilithRuntime;

// Closed, content-free failure metadata. Never accept exception.Message,
// provider/model IDs, paths, receipt values or request/response payload here.
internal enum LilithNativeModelMutationPhase
{
    CapturePrevious,
    ReadOptions,
    BeforeDispatch,
    ConfigSet,
    VerifyNativeReceipt,
    ReadBack,
    WriteReceipt,
    CommitAcknowledgement,
    PublishAuthority,
    ProviderSetupMutation,
    ProviderSetupMutationReceipt,
    ProviderSetupReadBack,
    ProviderSetupWriteReceipt,
    ProviderSetupCommitAcknowledgement,
    ProviderSetupVerifyDurable
}

internal sealed record LilithNativeModelMutationDiagnostic(
    LilithNativeModelMutationPhase Phase,
    string Reason)
{
    internal static LilithNativeModelMutationDiagnostic Create(
        LilithNativeModelMutationPhase phase, Exception? exception)
        => new(phase, exception switch
        {
            HermesRpcException rpc => rpc.Code switch
            {
                4090 => "native_busy",
                4094 => "selection_rejected",
                4096 => "resolution_rejected",
                5001 => "native_operation_failed",
                5035 => "native_authority_rejected",
                _ => "native_rpc_rejected"
            },
            HermesRequestNotSentException => "not_sent",
            OperationCanceledException => "cancelled",
            InvalidDataException => "evidence_rejected",
            IOException => "io_failed",
            null => "postcondition_rejected",
            _ => "operation_failed"
        });

    internal string SafeSummary => Phase >= LilithNativeModelMutationPhase.ProviderSetupMutation
        ? $"config_phase={Phase};reason={Reason}"
        : $"model_phase={Phase};reason={Reason}";
}
