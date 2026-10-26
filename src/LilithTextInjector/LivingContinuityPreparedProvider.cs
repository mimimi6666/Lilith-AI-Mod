using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace LilithTextInjector;

internal enum LivingPreparedProviderAdmission
{
    Prepared = 0,
    DispatchAccepted = 1,
    Duplicate = 2,
    Expired = 3,
    ProviderRejected = 4,
    Faulted = 5,
    CompletionRejected = 6,
    ResponseAccepted = 7,
    CompletionProjected = 8
}

internal enum LivingPreparedCompletionDestination
{
    PlayerReply = 0,
    ProactiveNote = 1,
    ProactiveChat = 2
}

internal sealed class FrozenLivingPromptTurn
{
    internal string TurnId { get; }
    internal LivingPromptTurnRole Role { get; }
    internal string ContentHash { get; }
    internal string Text { get; }

    internal FrozenLivingPromptTurn(LivingPromptTurn source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        TurnId = source.TurnId;
        Role = source.Role;
        ContentHash = source.ContentHash;
        Text = source.Text;
    }

    internal string Fingerprint()
        => LivingPromptHash.Of(
            string.Join(
                "|",
                TurnId,
                ((int)Role).ToString(CultureInfo.InvariantCulture),
                ContentHash,
                LivingPromptHash.Of(Text)));
}

internal sealed class LivingPreparedProviderRequest
{
    internal string RequestId { get; }
    internal string AttemptId { get; }
    internal string CompositionId { get; }
    internal string EnvelopeId { get; }
    internal LivingPromptSourceKind SourceKind { get; }
    internal LivingPreparedCompletionDestination Destination { get; }
    internal string AuthorizationFingerprint { get; }
    internal string ProviderRouteId { get; }
    internal string SessionId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string LivingSnapshotFingerprint { get; }
    internal string SystemPrompt { get; }
    internal string SystemPromptHash { get; }
    internal LivingPromptCharacterSlotOwner CharacterOwner { get; }
    internal string? MemoryPacketId { get; }
    internal IReadOnlyList<FrozenLivingPromptTurn> WorkingContext { get; }
    internal FrozenLivingPromptTurn CurrentRequest { get; }
    internal DateTimeOffset CreatedAt { get; }
    internal DateTimeOffset AttemptedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string RequestFingerprint { get; }

    internal LivingPreparedProviderRequest(
        LivingPromptDispatchAttempt attempt)
    {
        if (attempt == null)
            throw new ArgumentNullException(nameof(attempt));
        var envelope = attempt.Envelope;
        if (envelope == null)
            throw new ArgumentNullException(nameof(envelope));

        AttemptId = attempt.AttemptId;
        CompositionId = envelope.CompositionId;
        EnvelopeId = envelope.EnvelopeId;
        SourceKind = envelope.SourceKind;
        Destination = SourceKind switch
        {
            LivingPromptSourceKind.PlayerTriggered =>
                LivingPreparedCompletionDestination.PlayerReply,
            LivingPromptSourceKind.BrainAuthorizedProactive =>
                LivingPreparedCompletionDestination.ProactiveNote,
            LivingPromptSourceKind.ContextualProactiveChat =>
                LivingPreparedCompletionDestination.ProactiveChat,
            _ => throw new ArgumentOutOfRangeException(
                nameof(envelope),
                "Unsupported living prompt source.")
        };
        AuthorizationFingerprint = envelope.AuthorizationFingerprint;
        ProviderRouteId = envelope.ProviderRouteId;
        SessionId = envelope.SessionId;
        SituationId = envelope.SituationId;
        SituationRevision = envelope.SituationRevision;
        RuntimeIdentity = CopyRuntimeIdentity(envelope.RuntimeIdentity);
        LivingSnapshotFingerprint = envelope.LivingSnapshotFingerprint;
        SystemPrompt = envelope.SystemPrompt;
        SystemPromptHash = envelope.SystemPromptHash;
        CharacterOwner = envelope.CharacterOwner;
        MemoryPacketId = envelope.MemoryPacketId;
        WorkingContext = new ReadOnlyCollection<FrozenLivingPromptTurn>(
            envelope.WorkingContext
                .Select(item => new FrozenLivingPromptTurn(item))
                .ToArray());
        CurrentRequest = new FrozenLivingPromptTurn(
            envelope.CurrentRequest);
        CreatedAt = envelope.CreatedAt;
        AttemptedAt = attempt.AttemptedAt;
        ExpiresAt = envelope.ExpiresAt;
        RequestFingerprint = LivingPromptHash.Of(
            string.Join(
                "|",
                AttemptId,
                CompositionId,
                EnvelopeId,
                ((int)SourceKind).ToString(CultureInfo.InvariantCulture),
                ((int)Destination).ToString(CultureInfo.InvariantCulture),
                AuthorizationFingerprint,
                ProviderRouteId,
                SessionId,
                SituationId.ToString("N"),
                SituationRevision.ToString(CultureInfo.InvariantCulture),
                RuntimeFingerprint(RuntimeIdentity),
                LivingSnapshotFingerprint,
                SystemPromptHash,
                ((int)CharacterOwner).ToString(
                    CultureInfo.InvariantCulture),
                MemoryPacketId ?? "-",
                string.Join(
                    ",",
                    WorkingContext.Select(item => item.Fingerprint())),
                CurrentRequest.Fingerprint(),
                CreatedAt.UtcDateTime.Ticks.ToString(
                    CultureInfo.InvariantCulture),
                AttemptedAt.UtcDateTime.Ticks.ToString(
                    CultureInfo.InvariantCulture),
                ExpiresAt.UtcDateTime.Ticks.ToString(
                    CultureInfo.InvariantCulture)));
        RequestId = "d025-provider-" + RequestFingerprint[..32];
    }

    private static CharacterRuntimeIdentity CopyRuntimeIdentity(
        CharacterRuntimeIdentity source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        return new CharacterRuntimeIdentity(
            source.RuntimeProfileId,
            source.RuntimeInstanceId,
            source.Generation,
            source.OrchestratorSessionId,
            source.OwnerIdentity);
    }

    private static string RuntimeFingerprint(
        CharacterRuntimeIdentity runtime)
        => string.Join(
            ":",
            runtime.RuntimeProfileId,
            runtime.RuntimeInstanceId,
            runtime.Generation.ToString(CultureInfo.InvariantCulture),
            runtime.OrchestratorSessionId,
            runtime.OwnerIdentity);
}

internal sealed class LivingPreparedProviderAcceptance
{
    internal string ProviderRequestId { get; }
    internal string RequestId { get; }
    internal string RequestFingerprint { get; }
    internal string ProviderRouteId { get; }
    internal DateTimeOffset AcceptedAt { get; }

    internal LivingPreparedProviderAcceptance(
        string providerRequestId,
        LivingPreparedProviderRequest request,
        DateTimeOffset acceptedAt)
    {
        ProviderRequestId = LivingContinuityContractGuard.Identifier(
            providerRequestId,
            nameof(providerRequestId));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        RequestId = request.RequestId;
        RequestFingerprint = request.RequestFingerprint;
        ProviderRouteId = request.ProviderRouteId;
        AcceptedAt = LivingContinuityContractGuard.Timestamp(
            acceptedAt,
            nameof(acceptedAt));
        if (AcceptedAt < request.CreatedAt
            || AcceptedAt < request.AttemptedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(acceptedAt));
        }
    }
}

internal sealed class LivingPreparedProviderResponse
{
    internal string ProviderRequestId { get; }
    internal string RequestId { get; }
    internal string RequestFingerprint { get; }
    internal string ProviderRouteId { get; }
    internal string ResponseText { get; }
    internal DateTimeOffset CompletedAt { get; }

    internal LivingPreparedProviderResponse(
        string providerRequestId,
        LivingPreparedProviderRequest request,
        string responseText,
        DateTimeOffset completedAt)
    {
        ProviderRequestId = LivingContinuityContractGuard.Identifier(
            providerRequestId,
            nameof(providerRequestId));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        RequestId = request.RequestId;
        RequestFingerprint = request.RequestFingerprint;
        ProviderRouteId = request.ProviderRouteId;
        if (responseText == null)
            throw new ArgumentNullException(nameof(responseText));
        if (string.IsNullOrWhiteSpace(responseText))
        {
            throw new ArgumentException(
                "A provider response cannot be empty.",
                nameof(responseText));
        }
        ResponseText = responseText;
        CompletedAt = LivingContinuityContractGuard.Timestamp(
            completedAt,
            nameof(completedAt));
    }
}

internal interface ILivingPreparedProviderTransport
{
    LivingPreparedProviderAcceptance Dispatch(
        LivingPreparedProviderRequest request);
}

internal sealed class LivingPreparedProviderExchange
{
    internal LivingPreparedProviderRequest Request { get; }
    internal LivingPreparedProviderResponse Response { get; }
    internal LivingPromptDispatchAttempt Attempt { get; }
    internal LivingPromptProviderReceipt Receipt { get; }

    internal LivingPreparedProviderExchange(
        LivingPreparedProviderRequest request,
        LivingPreparedProviderResponse response,
        LivingPromptDispatchAttempt attempt,
        LivingPromptProviderReceipt receipt)
    {
        Request = request
            ?? throw new ArgumentNullException(nameof(request));
        Response = response
            ?? throw new ArgumentNullException(nameof(response));
        Attempt = attempt
            ?? throw new ArgumentNullException(nameof(attempt));
        Receipt = receipt
            ?? throw new ArgumentNullException(nameof(receipt));
    }
}

internal sealed class LivingPreparedProviderCounters
{
    internal int ProviderCalls { get; private set; }
    internal int ProviderRetries => 0;
    internal int LegacyFallbacks => 0;

    internal void RecordProviderCall() => ProviderCalls++;
}

internal sealed class LivingPreparedProviderAdapter
    : ILivingPromptAttemptProviderPort
{
    private readonly object _sync = new();
    private readonly ILivingPreparedProviderTransport _transport;
    private bool _attempted;
    private bool _responseAttempted;

    internal LivingPreparedProviderRequest? FrozenRequest { get; private set; }
    internal LivingPromptDispatchAttempt? Attempt { get; private set; }
    internal LivingPromptProviderReceipt? Receipt { get; private set; }
    internal LivingPreparedProviderExchange? Exchange { get; private set; }
    internal LivingPreparedProviderCounters Counters { get; } = new();

    internal LivingPreparedProviderAdapter(
        ILivingPreparedProviderTransport transport)
    {
        _transport = transport
            ?? throw new ArgumentNullException(nameof(transport));
    }

    public LivingPromptProviderReceipt Dispatch(
        LivingPromptEnvelope envelope)
        => throw new InvalidOperationException(
            "Prepared provider requires the transaction-owned "
            + "immutable dispatch attempt.");

    public LivingPromptProviderReceipt DispatchAttempt(
        LivingPromptDispatchAttempt attempt)
    {
        if (attempt == null)
            throw new ArgumentNullException(nameof(attempt));

        lock (_sync)
        {
            if (_attempted)
            {
                throw new InvalidOperationException(
                    "Prepared provider attempt was already consumed.");
            }
            _attempted = true;
            FrozenRequest = new LivingPreparedProviderRequest(attempt);
            if (attempt.AttemptedAt < FrozenRequest.CreatedAt
                || attempt.AttemptedAt > FrozenRequest.ExpiresAt)
            {
                throw new InvalidOperationException(
                    "Prepared provider attempt is outside its lifetime.");
            }

            Counters.RecordProviderCall();
            var acceptance = _transport.Dispatch(FrozenRequest)
                ?? throw new InvalidOperationException(
                    "Prepared provider returned no acceptance.");
            if (!Matches(acceptance))
            {
                throw new InvalidOperationException(
                    "Prepared provider acceptance does not match the "
                    + "single immutable dispatch attempt.");
            }

            var receipt = new LivingPromptProviderReceipt(
                acceptance.ProviderRequestId,
                attempt,
                acceptance.AcceptedAt);
            Attempt = attempt;
            Receipt = receipt;
            return receipt;
        }
    }

    internal LivingPreparedProviderAdmission TryAcceptResponse(
        LivingPreparedProviderResponse response,
        DateTimeOffset now,
        out LivingPreparedProviderExchange? exchange)
    {
        if (response == null)
            throw new ArgumentNullException(nameof(response));
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));
        exchange = null;

        lock (_sync)
        {
            if (!_attempted
                || FrozenRequest == null
                || Attempt == null
                || Receipt == null)
            {
                return LivingPreparedProviderAdmission.CompletionRejected;
            }
            if (_responseAttempted)
                return LivingPreparedProviderAdmission.Duplicate;
            _responseAttempted = true;

            var completionDeadline =
                FrozenRequest.ExpiresAt.AddMinutes(30);
            if (!Matches(response)
                || response.CompletedAt < Receipt.AcceptedAt
                || response.CompletedAt > completionDeadline
                || now < response.CompletedAt
                || now > completionDeadline)
            {
                return LivingPreparedProviderAdmission.CompletionRejected;
            }

            Exchange = new LivingPreparedProviderExchange(
                FrozenRequest,
                response,
                Attempt,
                Receipt);
            exchange = Exchange;
            return LivingPreparedProviderAdmission.ResponseAccepted;
        }
    }

    private bool Matches(LivingPreparedProviderAcceptance acceptance)
        => string.Equals(
                acceptance.RequestId,
                FrozenRequest!.RequestId,
                StringComparison.Ordinal)
            && string.Equals(
                acceptance.RequestFingerprint,
                FrozenRequest.RequestFingerprint,
                StringComparison.Ordinal)
            && string.Equals(
                acceptance.ProviderRouteId,
                FrozenRequest.ProviderRouteId,
                StringComparison.Ordinal)
            && acceptance.AcceptedAt == FrozenRequest.AttemptedAt;

    private bool Matches(LivingPreparedProviderResponse response)
        => string.Equals(
                response.ProviderRequestId,
                Receipt!.ProviderRequestId,
                StringComparison.Ordinal)
            && string.Equals(
                response.RequestId,
                FrozenRequest!.RequestId,
                StringComparison.Ordinal)
            && string.Equals(
                response.RequestFingerprint,
                FrozenRequest.RequestFingerprint,
                StringComparison.Ordinal)
            && string.Equals(
                response.ProviderRouteId,
                FrozenRequest.ProviderRouteId,
                StringComparison.Ordinal);
}

internal abstract class LivingPreparedCompletionProjection
{
    internal LivingPreparedCompletionDestination Destination { get; }
    internal string CompletionId { get; }
    internal string ProviderRequestId { get; }
    internal string RequestId { get; }
    internal string RequestFingerprint { get; }
    internal string CompositionId { get; }
    internal string EnvelopeId { get; }
    internal string AuthorizationFingerprint { get; }
    internal string ProviderRouteId { get; }
    internal string SessionId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string ResponseText { get; }
    internal OutcomeStatus Status { get; }
    internal DateTimeOffset CompletedAt { get; }
    internal bool CreatesCandidate => false;
    internal bool Learns => false;

    protected LivingPreparedCompletionProjection(
        LivingPreparedCompletionDestination destination,
        LivingPreparedProviderExchange exchange,
        LivingPromptCompletionRecord completion)
    {
        Destination = destination;
        CompletionId = completion.CompletionId;
        ProviderRequestId = completion.ProviderRequestId;
        RequestId = exchange.Request.RequestId;
        RequestFingerprint = exchange.Request.RequestFingerprint;
        CompositionId = exchange.Request.CompositionId;
        EnvelopeId = exchange.Request.EnvelopeId;
        AuthorizationFingerprint =
            exchange.Request.AuthorizationFingerprint;
        ProviderRouteId = exchange.Request.ProviderRouteId;
        SessionId = exchange.Request.SessionId;
        SituationId = exchange.Request.SituationId;
        SituationRevision = exchange.Request.SituationRevision;
        RuntimeIdentity = new CharacterRuntimeIdentity(
            exchange.Request.RuntimeIdentity.RuntimeProfileId,
            exchange.Request.RuntimeIdentity.RuntimeInstanceId,
            exchange.Request.RuntimeIdentity.Generation,
            exchange.Request.RuntimeIdentity.OrchestratorSessionId,
            exchange.Request.RuntimeIdentity.OwnerIdentity);
        ResponseText = exchange.Response.ResponseText;
        Status = completion.Status;
        CompletedAt = completion.CompletedAt;
    }
}

internal sealed class LivingPlayerReplyCompletionProjection
    : LivingPreparedCompletionProjection
{
    internal LivingPlayerReplyCompletionProjection(
        LivingPreparedProviderExchange exchange,
        LivingPromptCompletionRecord completion)
        : base(
            LivingPreparedCompletionDestination.PlayerReply,
            exchange,
            completion)
    {
    }
}

internal sealed class LivingProactiveNoteCompletionProjection
    : LivingPreparedCompletionProjection
{
    internal bool MayAppendConversationHistory => false;
    internal bool MayScheduleAnotherNote => false;
    internal bool MayEmitSpeech => false;
    internal bool MayEmitVisualBubble => false;

    internal LivingProactiveNoteCompletionProjection(
        LivingPreparedProviderExchange exchange,
        LivingPromptCompletionRecord completion)
        : base(
            LivingPreparedCompletionDestination.ProactiveNote,
            exchange,
            completion)
    {
    }
}

internal sealed class LivingProactiveChatCompletionProjection
    : LivingPreparedCompletionProjection
{
    internal bool MayAppendAssistantHistory => true;
    internal bool MayEmitAssistantBubble => true;
    internal bool MayCreateSyntheticPlayerTurn => false;
    internal bool MayEmitSpeech => false;
    internal bool MayInvokeTool => false;
    internal bool MayWriteCharacter => false;
    internal bool MayWriteRelationship => false;

    internal LivingProactiveChatCompletionProjection(
        LivingPreparedProviderExchange exchange,
        LivingPromptCompletionRecord completion)
        : base(
            LivingPreparedCompletionDestination.ProactiveChat,
            exchange,
            completion)
    {
    }
}

internal static class LivingPreparedCompletionProjector
{
    internal static LivingPreparedProviderAdmission TryProject(
        LivingPreparedProviderExchange exchange,
        LivingPromptCompletionRecord completion,
        out LivingPreparedCompletionProjection? projection)
    {
        if (exchange == null)
            throw new ArgumentNullException(nameof(exchange));
        if (completion == null)
            throw new ArgumentNullException(nameof(completion));
        projection = null;

        if (!Matches(exchange, completion))
            return LivingPreparedProviderAdmission.CompletionRejected;

        projection = exchange.Request.Destination switch
        {
            LivingPreparedCompletionDestination.PlayerReply =>
                new LivingPlayerReplyCompletionProjection(
                    exchange,
                    completion),
            LivingPreparedCompletionDestination.ProactiveNote =>
                new LivingProactiveNoteCompletionProjection(
                    exchange,
                    completion),
            LivingPreparedCompletionDestination.ProactiveChat =>
                new LivingProactiveChatCompletionProjection(
                    exchange,
                    completion),
            _ => null
        };
        return projection == null
            ? LivingPreparedProviderAdmission.CompletionRejected
            : LivingPreparedProviderAdmission.CompletionProjected;
    }

    private static bool Matches(
        LivingPreparedProviderExchange exchange,
        LivingPromptCompletionRecord completion)
        => string.Equals(
                completion.ProviderRequestId,
                exchange.Response.ProviderRequestId,
                StringComparison.Ordinal)
            && string.Equals(
                completion.EnvelopeId,
                exchange.Request.EnvelopeId,
                StringComparison.Ordinal)
            && string.Equals(
                completion.AuthorizationFingerprint,
                exchange.Request.AuthorizationFingerprint,
                StringComparison.Ordinal)
            && completion.SourceKind == exchange.Request.SourceKind
            && completion.CompletedAt >= exchange.Response.CompletedAt;
}
