using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

[Flags]
internal enum AgentAdapterCapabilities
{
    None = 0,
    TextReply = 1 << 0,
    MotionSuggestions = 1 << 1,
    StreamingText = 1 << 2,
    Cancellation = 1 << 3,
    PersistentMemory = 1 << 4,
    ToolCalling = 1 << 5
}

internal sealed class AgentTurnRequest
{
    internal Guid Id { get; }
    internal string SessionId { get; }
    internal string UserText { get; }
    internal string Locale { get; }
    internal AgentAvatarMotionContext AvatarMotionContext { get; }
    internal DateTimeOffset RequestedAt { get; }

    private AgentTurnRequest(
        Guid id,
        string sessionId,
        string userText,
        string locale,
        AgentAvatarMotionContext avatarMotionContext,
        DateTimeOffset requestedAt)
    {
        Id = id;
        SessionId = sessionId;
        UserText = userText;
        Locale = locale;
        AvatarMotionContext = avatarMotionContext;
        RequestedAt = requestedAt;
    }

    internal static AgentTurnRequest Create(
        string sessionId,
        string userText,
        string locale,
        AgentAvatarMotionContext avatarMotionContext = default)
    {
        if (!TryNormalizeIdentifier(sessionId, 128, out var normalizedSessionId))
            throw new ArgumentException("Session ID must be a non-empty, control-free identifier.", nameof(sessionId));
        if (userText == null)
            throw new ArgumentNullException(nameof(userText));
        if (!TryNormalizeIdentifier(locale, 32, out var normalizedLocale))
            throw new ArgumentException("Locale must be a non-empty, control-free identifier.", nameof(locale));
        return new AgentTurnRequest(
            Guid.NewGuid(),
            normalizedSessionId,
            userText,
            normalizedLocale,
            avatarMotionContext,
            DateTimeOffset.UtcNow);
    }

    private static bool TryNormalizeIdentifier(
        string? value,
        int maximumLength,
        out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            normalized = string.Empty;
            return false;
        }
        foreach (var character in normalized)
        {
            if (!char.IsControl(character))
                continue;
            normalized = string.Empty;
            return false;
        }
        return true;
    }
}

internal readonly struct AgentMotionSuggestion
{
    internal MotionIntentKind Kind { get; }

    internal AgentMotionSuggestion(MotionIntentKind kind)
    {
        if (kind == MotionIntentKind.None)
            throw new ArgumentOutOfRangeException(nameof(kind));
        Kind = kind;
    }
}

internal sealed class AgentTurnResponse
{
    internal string ReplyText { get; }
    internal IReadOnlyList<AgentMotionSuggestion> MotionSuggestions { get; }

    internal AgentTurnResponse(
        string replyText,
        IReadOnlyList<AgentMotionSuggestion>? motionSuggestions = null)
    {
        ReplyText = replyText ?? throw new ArgumentNullException(nameof(replyText));
        if (motionSuggestions == null || motionSuggestions.Count == 0)
        {
            MotionSuggestions = Array.Empty<AgentMotionSuggestion>();
            return;
        }

        var copy = new AgentMotionSuggestion[motionSuggestions.Count];
        for (var index = 0; index < motionSuggestions.Count; index++)
            copy[index] = motionSuggestions[index];
        MotionSuggestions = Array.AsReadOnly(copy);
    }
}

internal interface IAgentAdapter
{
    string Id { get; }
    AgentAdapterCapabilities Capabilities { get; }

    ValueTask<AgentTurnResponse> ExecuteTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken);
}

internal sealed class MockAgentAdapter : IAgentAdapter
{
    internal const string AdapterId = "mock.agent.v1";

    private readonly object _gate = new();
    private readonly Queue<AgentTurnResponse> _scriptedResponses = new();
    private int _requestCount;
    private Guid _lastRequestId;

    public string Id => AdapterId;
    public AgentAdapterCapabilities Capabilities =>
        AgentAdapterCapabilities.TextReply
        | AgentAdapterCapabilities.MotionSuggestions
        | AgentAdapterCapabilities.Cancellation;

    internal int RequestCount
    {
        get
        {
            lock (_gate)
                return _requestCount;
        }
    }

    internal Guid LastRequestId
    {
        get
        {
            lock (_gate)
                return _lastRequestId;
        }
    }

    internal void EnqueueResponse(AgentTurnResponse response)
    {
        if (response == null)
            throw new ArgumentNullException(nameof(response));
        lock (_gate)
            _scriptedResponses.Enqueue(response);
    }

    public ValueTask<AgentTurnResponse> ExecuteTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _requestCount++;
            _lastRequestId = request.Id;
            var response = _scriptedResponses.Count > 0
                ? _scriptedResponses.Dequeue()
                : new AgentTurnResponse("mock:ok");
            return ValueTask.FromResult(response);
        }
    }
}

internal sealed class AgentMotionRoutingResult
{
    internal int SuggestedCount { get; }
    internal int RoutedCount { get; }
    internal int DroppedByTurnLimit { get; }
    internal IReadOnlyList<MotionProposalEnqueueResult> Results { get; }

    internal AgentMotionRoutingResult(
        int suggestedCount,
        int routedCount,
        int droppedByTurnLimit,
        IReadOnlyList<MotionProposalEnqueueResult> results)
    {
        SuggestedCount = suggestedCount;
        RoutedCount = routedCount;
        DroppedByTurnLimit = droppedByTurnLimit;
        Results = results;
    }
}

internal static class AgentMotionSuggestionRouter
{
    internal const int MaximumSuggestionsPerTurn = 4;

    internal static AgentMotionRoutingResult Route(
        IAgentAdapter adapter,
        AgentTurnResponse response,
        MotionIntentSource source,
        TimeSpan timeToLive,
        MotionProposalQueue queue)
    {
        if (adapter == null)
            throw new ArgumentNullException(nameof(adapter));
        if (response == null)
            throw new ArgumentNullException(nameof(response));
        if (queue == null)
            throw new ArgumentNullException(nameof(queue));
        if (!adapter.Capabilities.HasFlag(AgentAdapterCapabilities.MotionSuggestions))
            return new AgentMotionRoutingResult(response.MotionSuggestions.Count, 0, 0, Array.Empty<MotionProposalEnqueueResult>());

        var routedCount = Math.Min(
            response.MotionSuggestions.Count,
            MaximumSuggestionsPerTurn);
        var results = new MotionProposalEnqueueResult[routedCount];
        for (var index = 0; index < routedCount; index++)
        {
            results[index] = queue.Enqueue(
                response.MotionSuggestions[index].Kind,
                source,
                adapter.Id,
                timeToLive);
        }
        return new AgentMotionRoutingResult(
            response.MotionSuggestions.Count,
            routedCount,
            response.MotionSuggestions.Count - routedCount,
            Array.AsReadOnly(results));
    }
}
