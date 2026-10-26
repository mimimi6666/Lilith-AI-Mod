using System.Collections.Immutable;
using System.Text.Json;

namespace LilithRuntime;

internal enum LilithLetterMaterialRole { Main, Backdrop }
// This is a host-projected, already redacted excerpt, never an arbitrary raw tool log.
// Future native integration must prove ownership, success, purpose and privacy.
internal sealed record LilithLetterToolFact(Guid Id, Guid TurnKey, string Excerpt,
    DateTimeOffset ObservedAtUtc, DateTimeOffset ExpiresAtUtc, bool ConfirmedSuccessful);
internal sealed record LilithLetterWritingTurn(Guid Key, LilithLetterMaterialRole Role,
    string PlayerText, string LilithText, bool WithinRecentTwelveHours);

internal sealed class LilithLettersWritingPacket
{
    internal Guid Id { get; }
    internal string StateFingerprint { get; }
    internal long StateRevision { get; }
    internal string Locale { get; }
    internal DateTimeOffset CreatedAtUtc { get; }
    internal DateTimeOffset ExpiresAtUtc { get; }
    internal ImmutableArray<LilithLetterWritingTurn> Turns { get; }
    internal ImmutableArray<LilithLetterToolFact> Tools { get; }
    internal const int MaximumTurns = 32;
    internal const int MaximumTools = 8;

    private LilithLettersWritingPacket(LilithLettersSnapshot state, string locale, DateTimeOffset now,
        ImmutableArray<LilithLetterWritingTurn> turns, ImmutableArray<LilithLetterToolFact> tools, DateTimeOffset expires)
    {
        Id = Guid.NewGuid(); StateFingerprint = Fingerprint(state); StateRevision = state.Revision;
        Locale = locale; CreatedAtUtc = now; ExpiresAtUtc = expires; Turns = turns; Tools = tools;
    }

    internal static string Fingerprint(LilithLettersSnapshot state) => LilithLettersHistory.Hash(JsonSerializer.Serialize(state));

    internal static LilithLettersWritingPacket? Create(LilithLettersSnapshot state,
        IEnumerable<LilithLetterMaterial> currentMaterial, IEnumerable<LilithLetterToolFact> hostToolFacts,
        string locale, DateTimeOffset mainFromUtc, DateTimeOffset mainToUtc, DateTimeOffset now)
    {
        LilithLettersState.Validate(state); LilithLettersPolicy.RequireUtc(now);
        if (!state.Settings.Enabled) return null; // Don't even enumerate private material while Off.
        LilithLettersPolicy.RequireUtc(mainFromUtc); LilithLettersPolicy.RequireUtc(mainToUtc);
        if (now < state.HighWaterUtc || mainToUtc > now || mainFromUtc > mainToUtc
            || mainToUtc - mainFromUtc > LilithLettersPolicy.MaximumCollection
            || now - mainFromUtc > LilithLettersPolicy.MaximumContextAge)
            throw new InvalidDataException("Letter context time bounds rejected.");
        if (locale is not ("zh-Hant-TW" or "zh-Hans-CN" or "en" or "ja-JP"))
            throw new ArgumentException("Letter output locale rejected.");
        var materials = currentMaterial.Take(MaximumTurns + 1).ToArray();
        if (materials.Length > MaximumTurns || materials.Any(m => m is null)
            || materials.Select(m => m.Key).Distinct().Count() != materials.Length)
            throw new InvalidDataException("Letter material count or identities rejected.");
        var turns = ImmutableArray.CreateBuilder<LilithLetterWritingTurn>();
        var characters = 0; var expires = now.AddMinutes(10);
        foreach (var material in materials)
        {
            var item = state.Turns.FirstOrDefault(t => t.Source.Key == material.Key);
            if (item is null || item.Used || item.Withdrawn || item.Kind == LilithLetterConversationKind.ToolOnly)
                throw new InvalidDataException("Letter source is not current.");
            var source = item.Source;
            if (source.CompletedAtUtc > now || now - source.CompletedAtUtc >= LilithLettersPolicy.MaximumContextAge)
                throw new InvalidDataException("Letter material expired.");
            if (string.IsNullOrWhiteSpace(material.PlayerText) || string.IsNullOrWhiteSpace(material.LilithText)
                || material.PlayerText.Length > LilithLettersHistory.MaximumMaterialCharacters
                || material.LilithText.Length > LilithLettersHistory.MaximumMaterialCharacters)
                throw new InvalidDataException("Letter text bound rejected.");
            characters = checked(characters + material.PlayerText.Length + material.LilithText.Length);
            if (characters > LilithLettersHistory.MaximumMaterialCharacters
                || LilithLettersHistory.Hash(material.PlayerText) != source.UserSha256
                || LilithLettersHistory.Hash(material.LilithText) != source.AssistantSha256)
                throw new InvalidDataException("Letter text does not match its source.");
            var role = source.CompletedAtUtc >= mainFromUtc && source.CompletedAtUtc <= mainToUtc
                ? LilithLetterMaterialRole.Main : LilithLetterMaterialRole.Backdrop;
            // Later material must not leak into a previously sealed context.
            if (source.CompletedAtUtc > mainToUtc) continue;
            turns.Add(new(material.Key, role, material.PlayerText, material.LilithText,
                now - source.CompletedAtUtc <= TimeSpan.FromHours(12)));
            var sourceExpiry = source.CompletedAtUtc + LilithLettersPolicy.MaximumContextAge;
            if (sourceExpiry < expires) expires = sourceExpiry;
        }
        if (!turns.Any(t => t.Role == LilithLetterMaterialRole.Main)) return null;
        var suppliedTools = hostToolFacts.Take(MaximumTools + 1).ToArray();
        if (suppliedTools.Length > MaximumTools || suppliedTools.Any(t => t is null)
            || suppliedTools.Select(t => t.Id).Distinct().Count() != suppliedTools.Length)
            throw new InvalidDataException("Letter tool facts exceeded their bound.");
        var tools = ImmutableArray.CreateBuilder<LilithLetterToolFact>();
        foreach (var fact in suppliedTools)
        {
            LilithLettersPolicy.RequireUtc(fact.ObservedAtUtc); LilithLettersPolicy.RequireUtc(fact.ExpiresAtUtc);
            if (fact.Id == Guid.Empty || fact.TurnKey == Guid.Empty || string.IsNullOrWhiteSpace(fact.Excerpt)
                || fact.Excerpt.Length > 1500) throw new InvalidDataException("Letter tool fact rejected.");
            if (!fact.ConfirmedSuccessful || fact.ObservedAtUtc > mainToUtc || fact.ExpiresAtUtc <= now
                || fact.ObservedAtUtc > fact.ExpiresAtUtc || now - fact.ObservedAtUtc >= LilithLettersPolicy.MaximumContextAge
                || !turns.Any(t => t.Key == fact.TurnKey)) continue;
            var source = state.Turns.Single(t => t.Source.Key == fact.TurnKey).Source;
            if (fact.ObservedAtUtc > source.CompletedAtUtc) continue;
            tools.Add(fact);
            if (fact.ExpiresAtUtc < expires) expires = fact.ExpiresAtUtc;
        }
        return new(state, locale, now, turns.ToImmutable(), tools.ToImmutable(), expires);
    }
}
