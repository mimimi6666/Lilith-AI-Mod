using System.Text.Json;

namespace LilithRuntime;

// Host-local, fixed-duration opt-in. Never included in package defaults or model data.
internal sealed record LilithLettersTiming(DateTimeOffset? TestExpiresAtUtc = null, string? ExtraEvaluationDay = null, int ExtraEvaluations = 3, bool OperatorTest = false)
{
    internal const string FileName = "letters-test-timing.v1.json";
    internal static readonly LilithLettersTiming Production = new();
    internal bool IsTest(DateTimeOffset now) => TestExpiresAtUtc is { } end && now < end;
    internal bool AllowsPresentTest(DateTimeOffset now) => OperatorTest && IsTest(now);
    internal TimeSpan Age(DateTimeOffset now) => IsTest(now) ? TimeSpan.FromMinutes(5) : LilithLettersPolicy.MinimumAge;
    internal TimeSpan Collection(DateTimeOffset now) => IsTest(now) ? TimeSpan.FromMinutes(10) : LilithLettersPolicy.MaximumCollection;
    internal TimeSpan Absence(DateTimeOffset now) => IsTest(now) ? TimeSpan.FromMinutes(1) : LilithLettersPolicy.MinimumAbsence;
    internal TimeSpan Retry(DateTimeOffset now) => IsTest(now) ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(1);
    internal TimeSpan PreparationRetry(DateTimeOffset now) => IsTest(now) ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(5);
    internal int EvaluationLimit(int dailyLettersLimit, string day, DateTimeOffset now)
        => Math.Min(30, dailyLettersLimit * 3 + (IsTest(now) && day == ExtraEvaluationDay ? Math.Clamp(ExtraEvaluations, 0, 6) : 0));

    internal static LilithLettersTiming Load(string home, DateTimeOffset now)
    {
        var path = Path.Combine(home, FileName);
        LilithRuntimeWorkspacePathGuard.EnsureSafe(path);
        if (!File.Exists(path)) return Production;
        if (new FileInfo(path).Length > 1024) throw new InvalidDataException("Letters test settings invalid.");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        var hasExtra = root.TryGetProperty("extra_evaluation_day", out var extraDay);
        var hasOperatorTest = root.TryGetProperty("operator_test", out var operatorTest);
        var fields = new List<string> { "enabled", "expires_at_utc" };
        if (hasExtra) fields.AddRange(["extra_evaluation_day", "extra_evaluations"]);
        if (hasOperatorTest) fields.Add("operator_test");
        LilithRuntimeContracts.LilithNoteInboxProtocol.Exact(root, fields.ToArray());
        string? day = null;
        var extraCount = 0;
        if (hasExtra)
        {
            day = extraDay.GetString();
            extraCount = root.GetProperty("extra_evaluations").GetInt32();
            if (!DateOnly.TryParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _) || extraCount is < 1 or > 6)
                throw new InvalidDataException("Letters test evaluation grant invalid.");
        }
        var enabled = root.GetProperty("enabled").GetBoolean();
        var end = root.GetProperty("expires_at_utc").GetDateTimeOffset();
        LilithLettersPolicy.RequireUtc(end);
        if (end > now.AddHours(24)) throw new InvalidDataException("Letters test duration exceeds one day.");
        return enabled && end > now ? new(end, day, extraCount, hasOperatorTest && operatorTest.GetBoolean()) : Production;
    }
}

// Codes are host-authored literals only, not provider exception messages or content.
internal sealed class LilithLettersValidationException(string code) : Exception
{
    internal string Code { get; } = code;
}
