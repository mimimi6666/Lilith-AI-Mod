using LilithTextInjector;

const long frequency = 1_000;
const long queuedAt = 10_000;

Assert(
    !AiVoiceTurnPolicy.ShouldDisplay(AiVoiceTurnPolicy.Generating, queuedAt, queuedAt + 6_999, frequency),
    "Generating voice must keep text hidden before seven seconds.");
Assert(
    AiVoiceTurnPolicy.ShouldDisplay(AiVoiceTurnPolicy.Generating, queuedAt, queuedAt + 7_000, frequency),
    "Generating voice must release text at seven seconds.");
Assert(
    AiVoiceTurnPolicy.ShouldDisplay(AiVoiceTurnPolicy.Ready, queuedAt, queuedAt + 250, frequency),
    "Ready voice must release text immediately.");
Assert(
    AiVoiceTurnPolicy.ShouldDisplay(AiVoiceTurnPolicy.Unavailable, queuedAt, queuedAt + 250, frequency),
    "Unavailable voice must not delay text.");

Console.WriteLine("Lilith voice wait policy tests passed (4 assertions).");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
