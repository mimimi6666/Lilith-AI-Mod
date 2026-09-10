namespace LilithTextInjector;

internal static class AiVoiceTurnPolicy
{
    internal const double WaitSeconds = 7d;
    internal const int Generating = 0;
    internal const int Ready = 1;
    internal const int Unavailable = 2;

    internal static double ElapsedSeconds(long queuedAtTimestamp, long nowTimestamp, long frequency)
        => (nowTimestamp - queuedAtTimestamp) / (double)frequency;

    internal static bool ShouldDisplay(int voiceState, long queuedAtTimestamp, long nowTimestamp, long frequency)
        => voiceState != Generating
            || ElapsedSeconds(queuedAtTimestamp, nowTimestamp, frequency) >= WaitSeconds;
}
