using System.Security.Cryptography;
using System.Text;
using LilithGameBridgeContracts;

namespace LilithRuntime;

// Created only at the host's successfully attested player-turn completion point.
// Contains no transcript. Neither this reference nor a classifier grants delivery.
internal sealed record LilithLetterTurnReference(Guid Key, string ProfileId, string StoredSessionId,
    string NativeTurnId, DateTimeOffset CompletedAtUtc, string UserSha256, string AssistantSha256);

internal sealed record LilithLetterMaterial(Guid Key, string PlayerText, string LilithText);

internal static class LilithLettersHistory
{
    internal const int MaximumTurns = 512;
    internal const int MaximumMaterialCharacters = 24_000;
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static IReadOnlyList<LilithLetterMaterial> Assemble(LilithLettersSnapshot state, string storedSession,
        IEnumerable<LilithGameBridgeHistoryEntry> currentRows, DateTimeOffset now)
    {
        LilithLettersState.Validate(state); LilithLettersPolicy.RequireUtc(now);
        if (now < state.HighWaterUtc) throw new InvalidDataException("Letter clock regressed.");
        if (!state.Settings.Enabled) return Array.Empty<LilithLetterMaterial>();
        return Assemble(state.ProfileId, storedSession, state.Turns.Where(t => !t.Used && !t.Withdrawn
            && t.Kind != LilithLetterConversationKind.ToolOnly && t.Source.StoredSessionId == storedSession)
            .Select(t => t.Source), currentRows, now);
    }

    internal static LilithLetterTurnReference Completed(string profile, string storedSession,
        string nativeTurn, DateTimeOffset now, string playerText, string assistantText)
    {
        Identity(profile); Identity(storedSession); Identity(nativeTurn); LilithLettersPolicy.RequireUtc(now);
        if (string.IsNullOrWhiteSpace(playerText) || string.IsNullOrWhiteSpace(assistantText)
            || playerText.Length > 128_000 || assistantText.Length > 128_000)
            throw new ArgumentException("Letter source text is not bounded.");
        var key = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(profile + "\n" + storedSession + "\n" + nativeTurn)).AsSpan(0, 16));
        return new(key, profile, storedSession, nativeTurn, now, Hash(playerText), Hash(assistantText));
    }

    // The caller must obtain these rows from the exact, attested stored game
    // session. F10 rows alone are insufficient: a completed reference is required.
    // Re-read current text immediately before use; deleted/edited rows fail closed.
    internal static IReadOnlyList<LilithLetterMaterial> Assemble(string profile, string storedSession,
        IEnumerable<LilithLetterTurnReference> references, IEnumerable<LilithGameBridgeHistoryEntry> currentRows,
        DateTimeOffset now)
    {
        Identity(profile); Identity(storedSession); LilithLettersPolicy.RequireUtc(now);
        var refs = references.Take(MaximumTurns + 1).ToArray();
        var rows = currentRows.Take(MaximumTurns * 2 + 1).ToArray();
        if (refs.Length > MaximumTurns || rows.Length > MaximumTurns * 2
            || refs.Select(r => r.Key).Distinct().Count() != refs.Length)
            throw new InvalidDataException("Letter history bound exceeded.");
        var result = new List<LilithLetterMaterial>();
        var characters = 0;
        foreach (var reference in refs.OrderBy(r => r.CompletedAtUtc).ThenBy(r => r.Key))
        {
            Validate(reference);
            if (reference.ProfileId != profile || reference.StoredSessionId != storedSession)
                throw new InvalidDataException("Letter history binding mismatch.");
            if (reference.CompletedAtUtc > now || now - reference.CompletedAtUtc >= LilithLettersPolicy.MaximumContextAge)
                continue;
            var matched = rows.Where(r => r.TurnId == reference.NativeTurnId).ToArray();
            var users = matched.Where(r => r.Role == "user" && r.MessageKind == "user").ToArray();
            var replies = matched.Where(r => r.Role == "assistant" && r.MessageKind == "primary").ToArray();
            if (users.Length != 1 || replies.Length != 1
                || Hash(users[0].Text) != reference.UserSha256 || Hash(replies[0].Text) != reference.AssistantSha256)
                continue;
            var earliest = (now - LilithLettersPolicy.MaximumContextAge).ToUnixTimeMilliseconds();
            var latest = reference.CompletedAtUtc.ToUnixTimeMilliseconds();
            if (users[0].OccurredAtUnixTimeMilliseconds < earliest || replies[0].OccurredAtUnixTimeMilliseconds < earliest
                || users[0].OccurredAtUnixTimeMilliseconds > latest || replies[0].OccurredAtUnixTimeMilliseconds > latest
                || users[0].OccurredAtUnixTimeMilliseconds > replies[0].OccurredAtUnixTimeMilliseconds)
                continue;
            var size = users[0].Text.Length + replies[0].Text.Length;
            if (size > MaximumMaterialCharacters - characters) continue; // Never split a fact mid-turn.
            characters += size;
            result.Add(new(reference.Key, users[0].Text, replies[0].Text));
        }
        return result.AsReadOnly();
    }

    internal static void Validate(LilithLetterTurnReference reference)
    {
        Identity(reference.ProfileId); Identity(reference.StoredSessionId); Identity(reference.NativeTurnId);
        LilithLettersPolicy.RequireUtc(reference.CompletedAtUtc);
        if (reference.Key == Guid.Empty || !Digest(reference.UserSha256) || !Digest(reference.AssistantSha256)
            || reference.Key != new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(reference.ProfileId + "\n"
                + reference.StoredSessionId + "\n" + reference.NativeTurnId)).AsSpan(0, 16)))
            throw new ArgumentException("Invalid letter source reference.");
    }

    internal static void Identity(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
            throw new ArgumentException("Invalid letter source identity.");
    }
    private static bool Digest(string value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
