namespace LilithRuntimeContracts;

// Fixed product settings, never a grant to arbitrary Hermes tools.
public sealed record LilithLettersSettings
{
    public const int MaximumDailyLimit = 10;
    public bool Enabled { get; }
    public int DailyLimit { get; }

    public LilithLettersSettings(bool enabled = true, int dailyLimit = 1)
    {
        if (dailyLimit < 1 || dailyLimit > MaximumDailyLimit)
            throw new ArgumentOutOfRangeException(nameof(dailyLimit));
        Enabled = enabled;
        DailyLimit = dailyLimit;
    }

    // Missing is not equivalent to an explicit player opt-out.
    public static LilithLettersSettings FromExisting(bool? enabled, int? dailyLimit)
        => new(enabled ?? true, dailyLimit ?? 1);
}
