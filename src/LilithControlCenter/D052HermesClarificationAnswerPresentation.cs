namespace LilithControlCenter;

internal sealed partial class D046HermesTimelineItemViewModel
{
    public bool IsClarificationAnswer => IsUserMessage && Kind == D046HermesEventKind.Clarification;
}

internal sealed partial class D052HermesTurnPresentationCollection
{
    // A receipt-backed UI echo, not a fabricated native user turn. The native
    // clarification tool remains the durable owner of this answer.
    internal void ShowAcceptedClarificationAnswer(
        string eventId, string requestId, string answer, DateTimeOffset occurredUtc)
    {
        var key = "clarification-answer:" + requestId;
        if (Turns.SelectMany(turn => turn.UserMessages).Any(row => row.Key == key))
            return;
        _activityByEventId.TryGetValue(eventId, out var question);
        var turn = question is null ? null : Turns.FirstOrDefault(candidate =>
            candidate.ActivityRows.Contains(question));
        turn ??= GetOrCreateStandaloneSessionRow(key);
        turn.AddUserMessage(new D046HermesTimelineItemViewModel(
            key, D046HermesEventKind.Clarification, D046HermesMessageRole.User,
            string.Empty, answer, string.Empty, occurredUtc, isStreaming: false,
            headingKey: "chat.speakerUser"));
    }
}
