namespace LilithTextInjector;

internal readonly record struct DailyNoteSafetyFacts(bool StateAvailable, bool DirectControl,
    bool ConversationOrSpeechBusy, bool PendingExecution, bool QuietPeriod, bool NoteInFlight);

internal static class DailyNoteSafetyPolicy
{
    internal static bool IsSafe(DailyNoteSafetyFacts facts) => facts.StateAvailable && !facts.DirectControl
        && !facts.ConversationOrSpeechBusy && !facts.PendingExecution && !facts.QuietPeriod && !facts.NoteInFlight;
}
