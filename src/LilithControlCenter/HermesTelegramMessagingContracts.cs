using System.Collections.ObjectModel;
using System.IO;

namespace LilithControlCenter;

public enum HermesTelegramConnectionState
{
    Unconfigured = 0,
    Starting = 1,
    Connected = 2,
    Attention = 3
}

public sealed record HermesTelegramStatus(
    bool Configured,
    bool GatewayRunning,
    HermesTelegramConnectionState State);

public sealed record HermesTelegramPendingPairing(
    string RequestId,
    string UserId,
    string UserName,
    double AgeMinutes)
{
    public string DisplayName => string.IsNullOrWhiteSpace(UserName)
        ? UserId
        : UserName;
}

public sealed record HermesTelegramApprovedUser(string UserId);

public sealed class HermesTelegramPairingCatalog
{
    public IReadOnlyList<HermesTelegramPendingPairing> Pending { get; }
    public IReadOnlyList<HermesTelegramApprovedUser> Approved { get; }

    public HermesTelegramPairingCatalog(
        IEnumerable<HermesTelegramPendingPairing> pending,
        IEnumerable<HermesTelegramApprovedUser> approved)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(approved);
        var pendingRows = pending.ToArray();
        var approvedRows = approved.ToArray();
        if (pendingRows.Length > 256
            || approvedRows.Length > 256
            || pendingRows.Any(static row => row is null)
            || approvedRows.Any(static row => row is null)
            || pendingRows.Select(static row => row.RequestId)
                .Distinct(StringComparer.Ordinal).Count() != pendingRows.Length
            || approvedRows.Select(static row => row.UserId)
                .Distinct(StringComparer.Ordinal).Count() != approvedRows.Length)
        {
            throw new ArgumentException(
                "Telegram pairing catalog was rejected.");
        }

        Pending = new ReadOnlyCollection<HermesTelegramPendingPairing>(
            pendingRows);
        Approved = new ReadOnlyCollection<HermesTelegramApprovedUser>(
            approvedRows);
    }
}

public sealed record HermesTelegramTokenMutationReceipt(
    string OperationId,
    bool Configured,
    bool Changed);

public sealed record HermesTelegramPairingApprovalReceipt(
    string RequestId,
    string UserId,
    bool Approved);

public sealed record HermesTelegramPairingRevokeReceipt(
    string UserId,
    bool Revoked);

public enum HermesTelegramVoiceMode { Off, VoiceOnly, All }

public enum HermesTelegramVoiceLocale { Unavailable, Zh, Ja }

// Configured is the validated voice configuration captured at process startup;
// it is not a synthesis health check or evidence that a listener heard audio.
public sealed record HermesTelegramVoiceStatus(
    string UserId,
    HermesTelegramVoiceMode Mode,
    string Revision,
    bool Configured,
    HermesTelegramVoiceLocale Locale);

public interface IHermesTelegramVoiceProjection
{
    Task<HermesTelegramVoiceStatus> GetTelegramVoiceStatusAsync(
        string userId, CancellationToken cancellationToken);

    Task<HermesTelegramVoiceStatus> SetTelegramVoiceModeAsync(
        string userId, HermesTelegramVoiceMode mode, string expectedRevision,
        CancellationToken cancellationToken);
}

internal interface IHermesTelegramVoiceRuntimeProtocol
{
    D046HermesRuntimeRequestBinding BindTelegramVoiceStatus(string userId);
    D046HermesRuntimeRequestBinding BindTelegramVoiceSet(
        string userId, HermesTelegramVoiceMode mode, string expectedRevision);
    HermesTelegramVoiceStatus DecodeTelegramVoiceStatus(
        LilithRuntimeContracts.LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);
}

internal static class HermesTelegramVoiceReceiptAdmission
{
    internal static HermesTelegramVoiceStatus RequireReadback(
        HermesTelegramVoiceStatus receipt, HermesTelegramVoiceStatus readback)
    {
        if (receipt != readback)
            throw new HermesTelegramResultUncertainException(
                "Telegram voice settings did not match the fresh readback.");
        return readback;
    }
}

public interface IHermesTelegramMessagingProjection
{
    Task<HermesTelegramStatus> GetTelegramStatusAsync(
        CancellationToken cancellationToken);

    Task<HermesTelegramTokenMutationReceipt> ReplaceTelegramTokenAsync(
        string botToken,
        CancellationToken cancellationToken);

    Task<HermesTelegramTokenMutationReceipt> RemoveTelegramTokenAsync(
        CancellationToken cancellationToken);

    Task<HermesTelegramPairingCatalog> GetTelegramPairingAsync(
        CancellationToken cancellationToken);

    Task<HermesTelegramPairingApprovalReceipt> ApproveTelegramPairingAsync(
        string requestId,
        CancellationToken cancellationToken);

    Task<HermesTelegramPairingRevokeReceipt> RevokeTelegramUserAsync(
        string userId,
        CancellationToken cancellationToken);
}

internal interface IHermesTelegramMessagingRuntimeProtocol
{
    D046HermesRuntimeRequestBinding BindTelegramStatus();

    D046HermesRuntimeRequestBinding BindTelegramTokenReplace(
        string botToken,
        string operationId);

    D046HermesRuntimeRequestBinding BindTelegramTokenRemove(string operationId);

    D046HermesRuntimeRequestBinding BindTelegramPairingList();

    D046HermesRuntimeRequestBinding BindTelegramPairingApprove(string requestId);

    D046HermesRuntimeRequestBinding BindTelegramPairingRevoke(string userId);

    HermesTelegramStatus DecodeTelegramStatus(
        LilithRuntimeContracts.LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    HermesTelegramTokenMutationReceipt DecodeTelegramTokenMutation(
        LilithRuntimeContracts.LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    HermesTelegramPairingCatalog DecodeTelegramPairing(
        LilithRuntimeContracts.LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    HermesTelegramPairingApprovalReceipt DecodeTelegramPairingApproval(
        LilithRuntimeContracts.LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    HermesTelegramPairingRevokeReceipt DecodeTelegramPairingRevoke(
        LilithRuntimeContracts.LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);
}

public sealed class HermesTelegramOperationException : Exception
{
    public HermesTelegramOperationException(string message)
        : base(message)
    {
    }
}

public sealed class HermesTelegramResultUncertainException : Exception
{
    public HermesTelegramResultUncertainException(string message)
        : base(message)
    {
    }
}

internal static class HermesTelegramGenerationAdmission
{
    internal static void RequireFresh(Guid retired, Guid candidate)
    {
        if (candidate == Guid.Empty)
        {
            throw new InvalidDataException(
                "Telegram reconnect omitted the Runtime generation.");
        }
        if (retired != Guid.Empty && retired == candidate)
        {
            throw new InvalidDataException(
                "Telegram reconnect reused the retired Runtime generation.");
        }
    }
}
