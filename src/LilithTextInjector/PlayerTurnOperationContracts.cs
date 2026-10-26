using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum PlayerTurnInputSource
{
    Invalid = 0,
    TextEntry = 1,
    PushToTalkTranscript = 2
}

internal enum PlayerTurnOperationPhase
{
    Began = 1,
    Admitted = 2,
    ProviderStarted = 3,
    ProviderCompleted = 4,
    Published = 5,
    Terminal = 6
}

internal enum PlayerTurnPublicationKind
{
    AssistantReply = 1,
    FailureNotice = 2
}

internal enum PlayerTurnTerminalDisposition
{
    None = 0,
    Completed = 1,
    Failed = 2,
    Cancelled = 3
}

internal enum PlayerTurnFailureReason
{
    None = 0,
    CredentialUnavailable = 1,
    RouteNotAdmitted = 2,
    MemoryOrPromptPreparationRejected = 3,
    ProviderFaulted = 4,
    ProviderResponseRejected = 5,
    EmptyReply = 6,
    ToolExecutionFailed = 7,
    PublicationFailed = 8,
    PreDispatchFaulted = 9
}

internal enum PlayerTurnCancellationReason
{
    None = 0,
    ConfigurationChanged = 1,
    ProviderConfigurationChanged = 2,
    Expired = 3,
    SourceDisabled = 4,
    RuntimeGenerationChanged = 5,
    NativeDialogueTookControl = 6,
    Superseded = 7,
    Shutdown = 8,
    PlayerRequested = 9
}

internal enum PlayerTurnBeginAdmission
{
    Accepted = 1,
    ActiveOperation = 2,
    TimestampReversal = 3,
    GenerationExhausted = 4
}

internal enum PlayerTurnPublicationAdmission
{
    Admitted = 1,
    NoActiveOperation = 2,
    StaleOperation = 3,
    RuntimeGenerationChanged = 4,
    PhaseRejected = 5
}

internal sealed class PlayerTurnOperationHandle
{
    internal string OperationId { get; }
    internal long OperationGeneration { get; }
    internal string ProcessEpochId { get; }
    internal long RuntimeGeneration { get; }
    internal PlayerTurnInputSource InputSource { get; }

    internal PlayerTurnOperationHandle(
        string operationId,
        long operationGeneration,
        string processEpochId,
        long runtimeGeneration,
        PlayerTurnInputSource inputSource)
    {
        OperationId = PlayerTurnOperationGuard.Metadata(
            operationId,
            nameof(operationId));
        ProcessEpochId = PlayerTurnOperationGuard.Metadata(
            processEpochId,
            nameof(processEpochId));
        if (operationGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(operationGeneration));
        if (runtimeGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(runtimeGeneration));
        if (!Enum.IsDefined(typeof(PlayerTurnInputSource), inputSource)
            || inputSource == PlayerTurnInputSource.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(inputSource));
        }
        OperationGeneration = operationGeneration;
        RuntimeGeneration = runtimeGeneration;
        InputSource = inputSource;
    }

    internal bool Matches(PlayerTurnOperationHandle? other)
        => other != null
            && OperationGeneration == other.OperationGeneration
            && RuntimeGeneration == other.RuntimeGeneration
            && InputSource == other.InputSource
            && string.Equals(
                OperationId,
                other.OperationId,
                StringComparison.Ordinal)
            && string.Equals(
                ProcessEpochId,
                other.ProcessEpochId,
                StringComparison.Ordinal);
}

internal sealed class PlayerTurnBeginResult
{
    internal PlayerTurnBeginAdmission Admission { get; }
    internal PlayerTurnOperationHandle? Handle { get; }
    internal bool Accepted => Admission == PlayerTurnBeginAdmission.Accepted;

    internal PlayerTurnBeginResult(
        PlayerTurnBeginAdmission admission,
        PlayerTurnOperationHandle? handle)
    {
        if (!Enum.IsDefined(typeof(PlayerTurnBeginAdmission), admission))
            throw new ArgumentOutOfRangeException(nameof(admission));
        if ((admission == PlayerTurnBeginAdmission.Accepted) != (handle != null))
            throw new ArgumentException("Accepted begin requires exactly one handle.");
        Admission = admission;
        Handle = handle;
    }
}

internal sealed class PlayerTurnOperationSnapshot
{
    internal PlayerTurnOperationHandle Handle { get; }
    internal PlayerTurnOperationPhase Phase { get; }
    internal DateTimeOffset BeganAt { get; }
    internal DateTimeOffset? AdmittedAt { get; }
    internal DateTimeOffset? ProviderStartedAt { get; }
    internal DateTimeOffset? ProviderCompletedAt { get; }
    internal DateTimeOffset? PublishedAt { get; }
    internal DateTimeOffset? TerminalAt { get; }
    internal string ProviderRouteId { get; }
    internal string ProviderRequestId { get; }
    internal PlayerTurnPublicationKind? PublicationKind { get; }
    internal PlayerTurnTerminalDisposition TerminalDisposition { get; }
    internal PlayerTurnFailureReason FailureReason { get; }
    internal PlayerTurnCancellationReason CancellationReason { get; }
    internal int ProviderCallCount { get; }
    internal int ProviderCompletionCount { get; }

    internal PlayerTurnOperationSnapshot(
        PlayerTurnOperationHandle handle,
        PlayerTurnOperationPhase phase,
        DateTimeOffset beganAt,
        DateTimeOffset? admittedAt,
        DateTimeOffset? providerStartedAt,
        DateTimeOffset? providerCompletedAt,
        DateTimeOffset? publishedAt,
        DateTimeOffset? terminalAt,
        string providerRouteId,
        string providerRequestId,
        PlayerTurnPublicationKind? publicationKind,
        PlayerTurnTerminalDisposition terminalDisposition,
        PlayerTurnFailureReason failureReason,
        PlayerTurnCancellationReason cancellationReason,
        int providerCallCount,
        int providerCompletionCount)
    {
        Handle = handle ?? throw new ArgumentNullException(nameof(handle));
        Phase = phase;
        BeganAt = beganAt;
        AdmittedAt = admittedAt;
        ProviderStartedAt = providerStartedAt;
        ProviderCompletedAt = providerCompletedAt;
        PublishedAt = publishedAt;
        TerminalAt = terminalAt;
        ProviderRouteId = providerRouteId;
        ProviderRequestId = providerRequestId;
        PublicationKind = publicationKind;
        TerminalDisposition = terminalDisposition;
        FailureReason = failureReason;
        CancellationReason = cancellationReason;
        if (providerCallCount < 0
            || providerCompletionCount < 0
            || providerCompletionCount > providerCallCount)
        {
            throw new ArgumentOutOfRangeException(nameof(providerCallCount));
        }
        ProviderCallCount = providerCallCount;
        ProviderCompletionCount = providerCompletionCount;
    }
}

internal sealed class PlayerTurnProviderCompletionReceipt
{
    internal string ReceiptId { get; }
    internal PlayerTurnOperationHandle Handle { get; }
    internal PlayerTurnInputSource InputSource => Handle.InputSource;
    internal string ProviderRouteId { get; }
    internal string TransportRequestId { get; }
    internal string ResponseFingerprint { get; }
    internal int ProviderCallOrdinal { get; }
    internal int ProviderCompletionOrdinal { get; }
    internal DateTimeOffset CompletedAt { get; }
    internal bool ContainsRawContent => false;

    private PlayerTurnProviderCompletionReceipt(
        PlayerTurnOperationHandle handle,
        string providerRouteId,
        string transportRequestId,
        string responseFingerprint,
        int providerCallOrdinal,
        int providerCompletionOrdinal,
        DateTimeOffset completedAt)
    {
        Handle = handle ?? throw new ArgumentNullException(nameof(handle));
        ProviderRouteId = PlayerTurnOperationGuard.Metadata(
            providerRouteId,
            nameof(providerRouteId));
        TransportRequestId = PlayerTurnOperationGuard.Metadata(
            transportRequestId,
            nameof(transportRequestId));
        ResponseFingerprint = PlayerTurnOperationGuard.Fingerprint(
            responseFingerprint,
            nameof(responseFingerprint));
        if (providerCallOrdinal <= 0
            || providerCompletionOrdinal <= 0
            || providerCompletionOrdinal > providerCallOrdinal)
        {
            throw new ArgumentOutOfRangeException(
                nameof(providerCompletionOrdinal));
        }
        ProviderCallOrdinal = providerCallOrdinal;
        ProviderCompletionOrdinal = providerCompletionOrdinal;
        CompletedAt = completedAt;
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    string.Join(
                        "|",
                        Handle.ProcessEpochId,
                        Handle.RuntimeGeneration.ToString(
                            CultureInfo.InvariantCulture),
                        Handle.OperationId,
                        Handle.OperationGeneration.ToString(
                            CultureInfo.InvariantCulture),
                        ((int)Handle.InputSource).ToString(
                            CultureInfo.InvariantCulture),
                        ProviderRouteId,
                        TransportRequestId,
                        ResponseFingerprint,
                        ProviderCallOrdinal.ToString(
                            CultureInfo.InvariantCulture),
                        ProviderCompletionOrdinal.ToString(
                            CultureInfo.InvariantCulture),
                        CompletedAt.UtcDateTime.Ticks.ToString(
                            CultureInfo.InvariantCulture)))));
        ReceiptId = "player-provider-completion-"
            + fingerprint.Substring(0, 32);
    }

    internal static PlayerTurnProviderCompletionReceipt Issue(
        object capability,
        PlayerTurnOperationHandle handle,
        string providerRouteId,
        string transportRequestId,
        string responseFingerprint,
        int providerCallOrdinal,
        int providerCompletionOrdinal,
        DateTimeOffset completedAt)
    {
        if (!PlayerTurnOperationRuntimeHost
                .AuthorizesCompletionReceiptIssuance(capability))
        {
            throw new InvalidOperationException(
                "Only the player-turn runtime host may issue a provider "
                + "completion receipt.");
        }
        return new PlayerTurnProviderCompletionReceipt(
            handle,
            providerRouteId,
            transportRequestId,
            responseFingerprint,
            providerCallOrdinal,
            providerCompletionOrdinal,
            completedAt);
    }
}

internal sealed class PlayerTurnOperationRuntimeSnapshot
{
    internal string ProcessEpochId { get; }
    internal PlayerTurnOperationSnapshot? Active { get; }
    internal IReadOnlyList<PlayerTurnOperationSnapshot> RecentTerminals { get; }
    internal long Began { get; }
    internal long Completed { get; }
    internal long Failed { get; }
    internal long Cancelled { get; }
    internal long RejectedTransitions { get; }

    internal PlayerTurnOperationRuntimeSnapshot(
        string processEpochId,
        PlayerTurnOperationSnapshot? active,
        IEnumerable<PlayerTurnOperationSnapshot> recentTerminals,
        long began,
        long completed,
        long failed,
        long cancelled,
        long rejectedTransitions)
    {
        ProcessEpochId = processEpochId;
        Active = active;
        RecentTerminals = new ReadOnlyCollection<PlayerTurnOperationSnapshot>(
            recentTerminals.ToArray());
        Began = began;
        Completed = completed;
        Failed = failed;
        Cancelled = cancelled;
        RejectedTransitions = rejectedTransitions;
    }
}

internal static class PlayerTurnOperationGuard
{
    internal static string Metadata(
        string? value,
        string parameterName,
        int maximumLength = 128)
    {
        var result = (value ?? string.Empty).Trim();
        if (result.Length == 0
            || result.Length > maximumLength
            || result.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Player-turn metadata must be bounded and control-free.",
                parameterName);
        }
        return result;
    }

    internal static string Fingerprint(
        string? value,
        string parameterName)
    {
        var result = value ?? string.Empty;
        if (result.Length != 64
            || result.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Fingerprint must be a 64-character hexadecimal SHA-256.",
                parameterName);
        }
        return result.ToUpperInvariant();
    }
}
