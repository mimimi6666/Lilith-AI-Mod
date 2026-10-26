namespace LilithTextInjector;

// A synchronous tool may own the current chat request; no other busy surface
// is exempted. These facts are captured again on the Unity thread before write.
internal readonly record struct NoteInboxToolFacts(
    bool SourceEnabled,
    bool MasterEnabled,
    bool ExactCurrentOwner,
    bool RuntimeMatches,
    bool Cancelled,
    bool BeforeDeadline,
    bool DirectControl,
    bool OtherConversationBusy,
    bool PendingExecution,
    bool QuietPeriod,
    bool OtherNoteInFlight);

internal static class NoteInboxToolPolicy
{
    internal static string Evaluate(NoteInboxToolFacts facts)
    {
        if (!facts.SourceEnabled || !facts.MasterEnabled) return "disabled";
        if (!facts.ExactCurrentOwner || !facts.RuntimeMatches) return "stale_owner";
        if (facts.Cancelled) return "cancelled";
        if (!facts.BeforeDeadline) return "expired";
        if (facts.DirectControl) return "direct_control";
        if (facts.OtherConversationBusy) return "busy";
        if (facts.PendingExecution) return "pending_execution";
        if (facts.QuietPeriod) return "quiet";
        if (facts.OtherNoteInFlight) return "note_busy";
        return "ok";
    }
}
