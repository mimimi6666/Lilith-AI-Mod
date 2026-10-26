using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LilithGameBridgeContracts;
using LilithRuntimeContracts;
using Microsoft.Win32.SafeHandles;

namespace LilithTextInjector;

/// <summary>
/// Thin game-side client for the dedicated Runtime game pipe. Every exchange
/// performs a fresh current-user generation handshake so a restarted Runtime
/// can never receive a request carrying the previous generation identity.
/// </summary>
internal sealed class LilithGameHermesClient
{
    internal static readonly TimeSpan ConnectionTimeout =
        TimeSpan.FromSeconds(5);
    private static readonly object RequestAdmissionPhaseMarker = new();

    private readonly Func<CancellationToken, Task<Stream>> _connectAsync;
    private readonly TimeSpan _handshakeTimeout;

    internal LilithGameHermesClient(
        Func<CancellationToken, Task<Stream>>? connectAsync = null,
        TimeSpan? handshakeTimeout = null)
    {
        _connectAsync = connectAsync ?? ConnectCurrentUserPipeAsync;
        _handshakeTimeout = handshakeTimeout ?? ConnectionTimeout;
        if (_handshakeTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(handshakeTimeout));
    }

    internal static bool ProvesRequestWasNotWritten(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Data[RequestAdmissionPhaseMarker] is false;
    }

    internal Task<LilithGameBridgePlayerNameResponse> SyncPlayerNameAsync(
        Guid requestId, Guid gameEpoch, long observationSequence, string displayName,
        CancellationToken cancellationToken)
        => ExchangeAsync(
            generationId => new LilithGameBridgePlayerNameRequest(generationId, requestId, 1,
                gameEpoch, observationSequence, displayName),
            frame => frame is LilithGameBridgePlayerNameResponse response
                    && response.GameEpoch == gameEpoch && response.ObservationSequence == observationSequence
                ? response : throw new InvalidDataException("Player-name response identity rejected."),
            requestId, cancellationToken);

    internal Task<LilithGameBridgeResponse> SubmitAsync(
        Guid requestId,
        Guid turnId,
        LilithGameBridgeInputSurface surface,
        string text,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        CancellationToken cancellationToken,
        LilithMiniGamePublicContext? miniGameContext = null,
        LilithMiniGameDiagnosticRequest? miniGameDiagnostic = null,
        LilithMiniGameAnswerRepair? miniGameAnswerRepair = null)
    {
        LilithMiniGameDiagnosticRequest? admittedDiagnostic = null;
        LilithMiniGamePublicContext? admittedContext = null;
        return ExchangeAsync(
            generationId =>
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                admittedContext = miniGameContext?.IsFresh(now) == true ? miniGameContext : null;
                admittedDiagnostic = miniGameDiagnostic?.IsActive(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) == true
                    ? miniGameDiagnostic : null;
                return new LilithGameBridgeRequest(
                generationId,
                requestId,
                sequence: 1,
                turnId,
                surface,
                text,
                displayLocale,
                speechLocale,
                admittedContext,
                admittedDiagnostic,
                // Keep the optional argument for source/binary compatibility;
                // old callers cannot re-enable mini-game answer repair.
                miniGameAnswerRepair: null);
            },
            frame => frame is LilithGameBridgeResponse response
                    && response.TurnId == turnId
                    && (response.MiniGameDiagnostic == null
                        || response.MiniGameDiagnostic.TraceId == admittedDiagnostic?.TraceId)
                    && (response.MiniGameEventReceipt == null
                        || response.MiniGameEventReceipt.Matches(admittedContext))
                ? response
                : throw new InvalidDataException(
                    "Game bridge submit response identity was rejected."),
            requestId,
            cancellationToken);
    }

    internal Task<LilithGameBridgeResponse> SubmitAsync(
        Guid requestId,
        Guid turnId,
        LilithGameBridgeInputSurface surface,
        string text,
        CancellationToken cancellationToken)
        => SubmitAsync(
            requestId,
            turnId,
            surface,
            text,
            LilithGameBridgeDisplayLocale.TraditionalChinese,
            LilithGameBridgeSpeechLocale.Chinese,
            cancellationToken);

    internal Task<LilithGameBridgeHistoryResponse> ReadHistoryAsync(
        Guid requestId,
        string cursor,
        int pageSize,
        CancellationToken cancellationToken)
        => ExchangeAsync(
            generationId => new LilithGameBridgeHistoryRequest(
                generationId,
                requestId,
                sequence: 1,
                cursor,
                pageSize),
            frame => frame as LilithGameBridgeHistoryResponse
                ?? throw new InvalidDataException(
                    "Game bridge history response type was rejected."),
            requestId,
            cancellationToken);

    internal Task<LilithGameBridgeNotificationResponse> ReadNotificationsAsync(
        Guid requestId,
        long afterSequence,
        int waitMilliseconds,
        CancellationToken cancellationToken)
        => ReadNotificationsAsync(
            requestId,
            afterSequence,
            waitMilliseconds,
            LilithGameBridgeDisplayLocale.TraditionalChinese,
            LilithGameBridgeSpeechLocale.Chinese,
            cancellationToken);

    internal Task<LilithGameBridgeNotificationResponse> ReadNotificationsAsync(
        Guid requestId,
        long afterSequence,
        int waitMilliseconds,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        CancellationToken cancellationToken)
        => ReadNotificationsAsync(
            requestId,
            afterSequence,
            waitMilliseconds,
            displayLocale,
            speechLocale,
            tailOnly: false,
            cancellationToken);

    internal Task<LilithGameBridgeNotificationResponse> ReadNotificationsAsync(
        Guid requestId,
        long afterSequence,
        int waitMilliseconds,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        bool tailOnly,
        CancellationToken cancellationToken)
        => ExchangeAsync(
            generationId => new LilithGameBridgeNotificationRequest(
                generationId,
                requestId,
                sequence: 1,
                afterSequence,
                waitMilliseconds,
                displayLocale,
                speechLocale,
                tailOnly),
            frame => frame as LilithGameBridgeNotificationResponse
                ?? throw new InvalidDataException(
                    "Game bridge notification response type was rejected."),
            requestId,
            cancellationToken);

    internal Task<LilithGameBridgeVoiceStartResponse> StartVoiceAsync(
        Guid requestId,
        Guid captureId,
        LilithGameBridgeVoiceLanguage language,
        int maximumDurationMilliseconds,
        CancellationToken cancellationToken)
        => ExchangeAsync(
            generationId => new LilithGameBridgeVoiceStartRequest(
                generationId,
                requestId,
                sequence: 1,
                captureId,
                language,
                maximumDurationMilliseconds),
            frame => frame is LilithGameBridgeVoiceStartResponse response
                    && response.CaptureId == captureId
                ? response
                : throw new InvalidDataException(
                    "Game bridge voice-start response identity was rejected."),
            requestId,
            cancellationToken);

    internal Task<LilithGameBridgeVoiceStopResponse> StopVoiceAsync(
        Guid requestId,
        Guid captureId,
        LilithGameBridgeVoiceStopDisposition disposition,
        CancellationToken cancellationToken)
        => ExchangeAsync(
            generationId => new LilithGameBridgeVoiceStopRequest(
                generationId,
                requestId,
                sequence: 1,
                captureId,
                disposition),
            frame => frame is LilithGameBridgeVoiceStopResponse response
                    && response.CaptureId == captureId
                ? response
                : throw new InvalidDataException(
                    "Game bridge voice-stop response identity was rejected."),
            requestId,
            cancellationToken);

    internal Task<LilithGameBridgeInterruptResponse> InterruptAsync(
        Guid requestId,
        Guid turnId,
        CancellationToken cancellationToken)
        => ExchangeAsync(
            generationId => new LilithGameBridgeInterruptRequest(
                generationId,
                requestId,
                sequence: 1,
                turnId),
            frame => frame is LilithGameBridgeInterruptResponse response
                    && response.TurnId == turnId
                ? response
                : throw new InvalidDataException(
                    "Game bridge interrupt response identity was rejected."),
            requestId,
            cancellationToken);

    internal Task<LilithGameBridgeNativeAssistantCommitResponse>
        CommitNativeAssistantAsync(
            Guid requestId,
            Guid eventId,
            string text,
            long occurredAtUnixMilliseconds,
            int nodeId,
            int lineId,
            string trigger,
            string locale,
            CancellationToken cancellationToken)
        => CommitNativeAssistantAsync(
            requestId,
            eventId,
            text,
            occurredAtUnixMilliseconds,
            nodeId,
            lineId,
            trigger,
            locale,
            proactiveDialogueEnabled: false,
            cancellationToken);

    internal Task<LilithGameBridgeNativeAssistantCommitResponse>
        CommitNativeAssistantAsync(
            Guid requestId,
            Guid eventId,
            string text,
            long occurredAtUnixMilliseconds,
            int nodeId,
            int lineId,
            string trigger,
            string locale,
            bool proactiveDialogueEnabled,
            CancellationToken cancellationToken)
        => CommitNativeAssistantAsync(requestId, eventId, text, occurredAtUnixMilliseconds,
            nodeId, lineId, trigger, locale, proactiveDialogueEnabled, null, cancellationToken);

    internal Task<LilithGameBridgeNativeAssistantCommitResponse>
        CommitNativeAssistantAsync(Guid requestId, Guid eventId, string text,
            long occurredAtUnixMilliseconds, int nodeId, int lineId, string trigger, string locale,
            bool proactiveDialogueEnabled, LilithGameBridgeNativeApplicationObservation? application,
            CancellationToken cancellationToken, LilithMiniGamePublicContext? miniGameContext = null)
        => ExchangeAsync(
            generationId =>
                new LilithGameBridgeNativeAssistantCommitRequest(
                    generationId,
                    requestId,
                    sequence: 1,
                    eventId,
                    proactiveDialogueEnabled,
                    new LilithGameBridgeNativePerceptionSnapshot(
                        LilithGameBridgeNativePerceptionSnapshot.CurrentSchemaVersion,
                        LilithGameBridgeNativePerceptionSnapshot.OfficialNativeDialogueSource,
                        text,
                        occurredAtUnixMilliseconds,
                        nodeId.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        lineId.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        trigger,
                        locale,
                        application), miniGameContext),
            frame =>
                frame is LilithGameBridgeNativeAssistantCommitResponse response
                    && response.EventId == eventId
                    && (!response.MiniGameEventsPending || miniGameContext?.Events is not null)
                    && (response.MiniGameEventReceipt is null || response.MiniGameEventReceipt.Matches(miniGameContext))
                    ? response
                    : throw new InvalidDataException(
                        "Game bridge native-assistant commit response identity was rejected."),
            requestId,
            cancellationToken);

    internal async Task<LilithGameBridgeNoteDraftResponse> GenerateNoteDraftAsync(
        Guid requestId, Guid eventId, string category, string topic, string context,
        LilithGameBridgeDisplayLocale displayLocale, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(LilithGameBridgeProtocol.MaximumNoteDraftMilliseconds);
        var response = await ExchangeAsync(
            generationId => new LilithGameBridgeNoteDraftRequest(
                generationId, requestId, 1, eventId, category, topic, context, displayLocale),
            frame => frame is LilithGameBridgeNoteDraftResponse draft && draft.EventId == eventId
                ? draft
                : throw new InvalidDataException("Game bridge note draft identity was rejected."),
            requestId, deadline.Token).ConfigureAwait(false);
        deadline.Token.ThrowIfCancellationRequested();
        return response;
    }

    internal Task<LilithGameBridgeForegroundProactiveResponse>
        RequestForegroundProactiveAsync(
            Guid requestId,
            Guid operationId,
            long observedAtUnixMilliseconds,
            string applicationId,
            string applicationDisplayName,
            string applicationCategory,
            LilithGameBridgeDisplayLocale displayLocale,
            CancellationToken cancellationToken,
            LilithMiniGamePublicContext? miniGameContext = null)
        => ExchangeAsync(
            generationId =>
                new LilithGameBridgeForegroundProactiveRequest(
                    generationId,
                    requestId,
                    sequence: 1,
                    operationId,
                    observedAtUnixMilliseconds,
                    applicationId,
                    applicationDisplayName,
                    applicationCategory,
                    displayLocale,
                    miniGameContext),
            frame =>
                frame is LilithGameBridgeForegroundProactiveResponse response
                    && response.OperationId == operationId
                    && (response.MiniGameEventReceipt == null
                        || response.MiniGameEventReceipt.Matches(miniGameContext))
                    ? response
                    : throw new InvalidDataException(
                        "Game bridge foreground-proactive response identity was rejected."),
            requestId,
            cancellationToken);

    internal Task<LilithGameBridgeMemoPollResponse> PollMemosAsync(
        Guid requestId,
        int waitMilliseconds,
        CancellationToken cancellationToken)
        => ExchangeAsync(
            generationId => new LilithGameBridgeMemoPollRequest(
                generationId,
                requestId,
                sequence: 1,
                waitMilliseconds),
            frame => frame as LilithGameBridgeMemoPollResponse
                ?? throw new InvalidDataException(
                    "Game bridge memo-poll response type was rejected."),
            requestId,
            cancellationToken);

    internal Task<LilithGameBridgeMemoCompleteResponse> CompleteMemoAsync(
        Guid requestId,
        LilithMemoResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        return ExchangeAsync(
            generationId => new LilithGameBridgeMemoCompleteRequest(
                generationId,
                requestId,
                sequence: 1,
                result),
            frame => frame is LilithGameBridgeMemoCompleteResponse response
                    && response.OperationId == result.OperationId
                ? response
                : throw new InvalidDataException(
                    "Game bridge memo-complete response identity was rejected."),
            requestId,
            cancellationToken);
    }

    internal Task<LilithGameBridgeNoteInboxPollResponse> PollNoteInboxAsync(
        Guid requestId, int waitMilliseconds, CancellationToken cancellationToken)
        => ExchangeAsync(generation => new LilithGameBridgeNoteInboxPollRequest(generation, requestId, 1, waitMilliseconds),
            frame => frame as LilithGameBridgeNoteInboxPollResponse
                ?? throw new InvalidDataException("NoteInbox poll response rejected."), requestId, cancellationToken);

    internal Task<LilithGameBridgeDailyNoteOpportunityResponse> PublishDailyNoteOpportunityAsync(
        Guid requestId, LilithDailyNoteOpportunity opportunity, CancellationToken cancellationToken)
        => ExchangeAsync(generation => new LilithGameBridgeDailyNoteOpportunityRequest(generation, requestId, 1, opportunity),
            frame => frame as LilithGameBridgeDailyNoteOpportunityResponse
                ?? throw new InvalidDataException("Daily opportunity response rejected."), requestId, cancellationToken);

    internal Task<LilithGameBridgeDailyNotePollResponse> PollDailyNoteAsync(
        Guid requestId, string profileId, Guid gameEpoch, int waitMilliseconds, CancellationToken cancellationToken)
        => ExchangeAsync(generation => new LilithGameBridgeDailyNotePollRequest(generation, requestId, 1, profileId, gameEpoch, waitMilliseconds),
            frame => frame is LilithGameBridgeDailyNotePollResponse response
                && (response.Command is null || response.Command.Scope.ProfileId == profileId && response.Command.Scope.GameEpoch == gameEpoch)
                    ? response : throw new InvalidDataException("Daily poll response identity rejected."), requestId, cancellationToken);

    internal async Task<LilithDailyNotePolledCommand> PollDailyNoteLeaseAsync(
        Guid requestId, string profileId, Guid gameEpoch, CancellationToken cancellationToken)
    {
        Stream? stream = null;
        try
        {
            using var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestDeadline.CancelAfter(_handshakeTimeout < TimeSpan.FromSeconds(3) ? _handshakeTimeout : TimeSpan.FromSeconds(3));
            stream = await _connectAsync(requestDeadline.Token).ConfigureAwait(false);
            var hello = await LilithGameBridgeProtocol.ReadAsync(stream, requestDeadline.Token).ConfigureAwait(false)
                as LilithGameBridgeHello ?? throw new InvalidDataException("Daily lease hello rejected.");
            if (!hello.Ready || hello.Sequence != 1) throw new InvalidDataException("Daily lease generation is not ready.");
            await LilithGameBridgeProtocol.WriteAsync(stream,
                new LilithGameBridgeDailyNotePollRequest(hello.GenerationId, requestId, 1, profileId, gameEpoch, 1000), requestDeadline.Token).ConfigureAwait(false);
            var response = await LilithGameBridgeProtocol.ReadAsync(stream, requestDeadline.Token).ConfigureAwait(false)
                as LilithGameBridgeDailyNotePollResponse ?? throw new InvalidDataException("Daily lease response rejected.");
            if (response.GenerationId != hello.GenerationId || response.RequestId != requestId || response.Sequence != 2
                || response.Command is { } command && (command.Scope.ProfileId != profileId || command.Scope.GameEpoch != gameEpoch))
                throw new InvalidDataException("Daily lease response identity rejected.");
            if (response.Command is null) return new(response, null);
            var expires = DateTimeOffset.UtcNow.AddSeconds(5);
            if (response.Command.Scope.ExpiresAtUtc < expires) expires = response.Command.Scope.ExpiresAtUtc;
            var lease = new LilithDailyNotePipeLease(stream, expires, cancellationToken);
            stream = null; // Ownership transfers only after the exact reply was validated.
            return new(response, lease);
        }
        finally { stream?.Dispose(); }
    }

    internal Task<LilithGameBridgeDailyNoteCompleteResponse> CompleteDailyNoteAsync(
        Guid requestId, LilithDailyNoteScope scope, LilithNoteInboxResult result, CancellationToken cancellationToken)
        => ExchangeAsync(generation => new LilithGameBridgeDailyNoteCompleteRequest(generation, requestId, 1, scope, result),
            frame => frame is LilithGameBridgeDailyNoteCompleteResponse response && response.Scope == scope
                ? response : throw new InvalidDataException("Daily completion scope rejected."), requestId, cancellationToken);

    internal Task<LilithGameBridgeNoteInboxCompleteResponse> CompleteNoteInboxAsync(
        Guid requestId, LilithNoteInboxResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        return ExchangeAsync(generation => new LilithGameBridgeNoteInboxCompleteRequest(generation, requestId, 1, result),
            frame => frame is LilithGameBridgeNoteInboxCompleteResponse response
                && response.OperationId == result.OperationId && response.DeliveryId == result.DeliveryId
                    ? response : throw new InvalidDataException("NoteInbox completion identity rejected."), requestId, cancellationToken);
    }

    private async Task<TResponse> ExchangeAsync<TResponse>(
        Func<Guid, LilithGameBridgeClientRequest> createRequest,
        Func<LilithGameBridgeFrame, TResponse> selectResponse,
        Guid expectedRequestId,
        CancellationToken cancellationToken)
        where TResponse : LilithGameBridgeFrame
    {
        if (expectedRequestId == Guid.Empty)
            throw new ArgumentException(
                "Game bridge request identity is required.",
                nameof(expectedRequestId));

        var requestMayHaveBeenWritten = false;
        try
        {
            using var handshake = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            handshake.CancelAfter(_handshakeTimeout);
            using var stream = await _connectAsync(handshake.Token)
                .ConfigureAwait(false);
            var helloFrame = await LilithGameBridgeProtocol.ReadAsync(
                    stream,
                    handshake.Token)
                .ConfigureAwait(false);
            if (helloFrame is not LilithGameBridgeHello hello
                || hello.Sequence != 1
                || !hello.Ready)
            {
                throw new InvalidDataException(
                    "Game bridge Runtime hello was rejected.");
            }
            handshake.CancelAfter(Timeout.InfiniteTimeSpan);

            var request = createRequest(hello.GenerationId);
            if (request.GenerationId != hello.GenerationId
                || request.RequestId != expectedRequestId
                || request.Sequence != 1)
            {
                throw new InvalidDataException(
                    "Game bridge request identity was rejected before send.");
            }

            // A partial pipe write is already ambiguous. Mark the request
            // before entering the write so callers may clear local state only
            // when failure was proven to occur before any request bytes could
            // reach Runtime.
            requestMayHaveBeenWritten = true;
            await LilithGameBridgeProtocol.WriteAsync(
                    stream,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            var response = selectResponse(
                await LilithGameBridgeProtocol.ReadAsync(
                        stream,
                        cancellationToken)
                    .ConfigureAwait(false));
            if (response.GenerationId != hello.GenerationId
                || response.Sequence != 2
                || ResponseRequestId(response) != expectedRequestId)
            {
                throw new InvalidDataException(
                    "Game bridge response identity was rejected.");
            }
            return response;
        }
        catch (Exception exception)
        {
            try
            {
                exception.Data[RequestAdmissionPhaseMarker] =
                    requestMayHaveBeenWritten;
            }
            catch
            {
                // Missing phase evidence is interpreted conservatively as an
                // ambiguous request by ProvesRequestWasNotWritten.
            }
            throw;
        }
    }

    private static Guid ResponseRequestId(LilithGameBridgeFrame response)
        => response switch
        {
            LilithGameBridgePlayerNameResponse playerName => playerName.RequestId,
            LilithGameBridgeResponse submit => submit.RequestId,
            LilithGameBridgeNoteDraftResponse draft => draft.RequestId,
            LilithGameBridgeNoteInboxPollResponse notePoll => notePoll.RequestId,
            LilithGameBridgeNoteInboxCompleteResponse noteComplete => noteComplete.RequestId,
            LilithGameBridgeDailyNoteOpportunityResponse dailyOpportunity => dailyOpportunity.RequestId,
            LilithGameBridgeDailyNotePollResponse dailyPoll => dailyPoll.RequestId,
            LilithGameBridgeDailyNoteCompleteResponse dailyComplete => dailyComplete.RequestId,
            LilithGameBridgeHistoryResponse history => history.RequestId,
            LilithGameBridgeNotificationResponse notifications =>
                notifications.RequestId,
            LilithGameBridgeVoiceStartResponse voiceStart =>
                voiceStart.RequestId,
            LilithGameBridgeVoiceStopResponse voiceStop =>
                voiceStop.RequestId,
            LilithGameBridgeInterruptResponse interrupt => interrupt.RequestId,
            LilithGameBridgeNativeAssistantCommitResponse nativeCommit =>
                nativeCommit.RequestId,
            LilithGameBridgeForegroundProactiveResponse foregroundProactive =>
                foregroundProactive.RequestId,
            LilithGameBridgeMemoPollResponse memoPoll => memoPoll.RequestId,
            LilithGameBridgeMemoCompleteResponse memoComplete =>
                memoComplete.RequestId,
            _ => throw new InvalidDataException(
                "Game bridge response shape was rejected.")
        };

    private static async Task<Stream> ConnectCurrentUserPipeAsync(
        CancellationToken cancellationToken)
    {
        var pipe = LilithGameBridgeEndpoint.CreateCurrentUserClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(ConnectionTimeout);
        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            return pipe;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }
}

internal interface ILilithDailyNoteExecutionLease : IDisposable
{
    bool IsAlive { get; }
}

internal sealed record LilithDailyNotePolledCommand(LilithGameBridgeDailyNotePollResponse Response, ILilithDailyNoteExecutionLease? Lease);

// A real generation/scope lifetime connection. No cached generation is a lease.
internal sealed class LilithDailyNotePipeLease : ILilithDailyNoteExecutionLease
{
    private readonly Stream _stream;
    private readonly DateTimeOffset _expires;
    private readonly CancellationTokenSource _stop;
    private readonly Task _watch;
    private int _alive = 1;
    private int _disposed;
    internal LilithDailyNotePipeLease(Stream stream, DateTimeOffset expires, CancellationToken cancellation)
    {
        _stream = stream; _expires = expires;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var remaining = expires - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) _stop.Cancel(); else _stop.CancelAfter(remaining);
        _watch = WatchAsync();
    }
    public bool IsAlive
    {
        get
        {
            if (Volatile.Read(ref _alive) == 0 || _stop.IsCancellationRequested || DateTimeOffset.UtcNow >= _expires) return false;
            try
            {
                // Synchronize the last check with the real pipe, not just the
                // asynchronous EOF observer. A subsequent OS/sink race remains.
                return _stream is not PipeStream pipe || !OperatingSystem.IsWindows()
                    || PeekNamedPipe(pipe.SafePipeHandle, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            }
            catch { return false; }
        }
    }
    private async Task WatchAsync()
    {
        try { _ = await _stream.ReadAsync(new byte[1], 0, 1, _stop.Token).ConfigureAwait(false); }
        catch { }
        finally { Interlocked.Exchange(ref _alive, 0); }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Exchange(ref _alive, 0); _stop.Cancel(); _stream.Dispose();
        _ = _watch.ContinueWith(_ => _stop.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekNamedPipe(SafePipeHandle pipe, IntPtr buffer, uint size, IntPtr read, IntPtr available, IntPtr left);
}
