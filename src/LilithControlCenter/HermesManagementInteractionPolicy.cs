namespace LilithControlCenter;

// Presentation only. Readable rows and an editable draft never authorize a
// mutation; the native request, current generation and receipt remain required.
internal sealed record HermesManagementInteractionPolicy(
    bool ReadingCredentials,
    bool ReadingTelegram,
    bool ReadingModels,
    bool MutationOrRecoveryInProgress,
    bool CredentialCatalogCurrent,
    bool TelegramStatusCurrent,
    bool ShuttingDown)
{
    internal bool AnyOperationInProgress => ReadingCredentials || ReadingTelegram
        || ReadingModels || MutationOrRecoveryInProgress || ShuttingDown;

    internal bool CanPrepareCredential => CredentialCatalogCurrent
        && !ReadingCredentials && !MutationOrRecoveryInProgress && !ShuttingDown;

    internal bool CanMutateCredential => CredentialCatalogCurrent && !AnyOperationInProgress;
    internal bool CanMutateTelegram => TelegramStatusCurrent && !AnyOperationInProgress;
}
