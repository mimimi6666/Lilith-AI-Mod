using System;
using System.Text.RegularExpressions;
using LilithGameBridgeContracts;

namespace LilithTextInjector;

// Pure, bounded performance metadata. No file paths, calls or mutable global tone.
internal sealed record JapaneseSpeechDelivery(string Emotion, string ReferenceCue, bool AllowNativeCue = true)
{
    internal static readonly JapaneseSpeechDelivery None = new(string.Empty, string.Empty);
}

internal static class JapaneseSpeechDeliveryPolicy
{
    private const string Separator = @"[\s、。，,.!！?？…⋯~〜～ー―—]";
    private static readonly (string Cue, string Emotion, Regex Pattern)[] Patterns =
    {
        Entry("breath_cycle", "calm", "す[ぅう]" + Separator + "*は[ぁあ]"),
        Entry("laugh_ehehe", "affectionate", "えへへっ?"),
        Entry("laugh_fufu", "playful", "ふふっ?"),
        Entry("reveal", "excited", "(?:ジャジャ|じゃじゃ)[ー~〜～―—]{0,3}[ンん]"),
        Entry("exhale_soft", "relieved", "ふ[ぅう]"),
        Entry("exhale_long", "calm", "は[ぁあ]"),
        Entry("inhale", "calm", "す[ぅう]"),
        Entry("response", "gentle", "ん"),
        Entry("surprise", "surprised", "えっ")
    };
    private static readonly Regex Separators = new("^" + Separator + "*",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private static (string, string, Regex) Entry(string cue, string emotion, string pattern)
        => (cue, emotion, new Regex("^" + pattern + "(?=" + Separator + "|$)",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)));

    internal static bool TryLeadingCue(string text, out JapaneseSpeechDelivery delivery, out string body)
    {
        delivery = JapaneseSpeechDelivery.None;
        body = text;
        if (text == null || text.Length == 0 || text.Length > 4000) return false;
        var trimmed = text.TrimStart();
        foreach (var entry in Patterns)
        {
            var match = entry.Pattern.Match(trimmed);
            if (!match.Success) continue;
            var rest = trimmed.Substring(match.Length);
            var remaining = Separators.Replace(rest, string.Empty, 1);
            if (entry.Cue == "response"
                && (rest.StartsWith("ー", StringComparison.Ordinal)
                    || rest.StartsWith("〜", StringComparison.Ordinal)
                    || rest.StartsWith("~", StringComparison.Ordinal)
                    || rest.StartsWith("～", StringComparison.Ordinal)
                    || remaining.StartsWith("と", StringComparison.Ordinal))) return false;
            if (remaining.StartsWith("とは", StringComparison.Ordinal)
                || remaining.StartsWith("という", StringComparison.Ordinal)
                || remaining.StartsWith("っていう", StringComparison.Ordinal)) return false;
            foreach (var other in Patterns)
                if (other.Pattern.IsMatch(remaining)) return false;
            delivery = new JapaneseSpeechDelivery(entry.Emotion, entry.Cue);
            body = remaining;
            return true;
        }
        return false;
    }

    internal static bool IsCueOnly(string text)
        => TryLeadingCue(text, out _, out var body) && body.Length == 0;

    internal static JapaneseSpeechDelivery[] Plan(
        System.Collections.Generic.IReadOnlyList<D043AdaptiveTtsSegment> segments,
        string baseEmotion)
    {
        if (!LilithJapaneseSpeechEmotion.IsValid(baseEmotion))
            throw new ArgumentException("Unknown Japanese delivery hint.", nameof(baseEmotion));
        var baseline = new JapaneseSpeechDelivery(baseEmotion, string.Empty);
        var current = baseline;
        var result = new JapaneseSpeechDelivery[segments.Count];
        var quoteDepth = 0;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (quoteDepth == 0 && TryLeadingCue(segment.Text, out var cue, out _)) current = cue;
            result[index] = current with { AllowNativeCue = quoteDepth == 0 };
            foreach (var character in segment.Text)
            {
                if (character is '「' or '『' or '“' or '‘') quoteDepth++;
                else if (character is '」' or '』' or '”' or '’') quoteDepth = Math.Max(0, quoteDepth - 1);
                else if (character == '"') quoteDepth = quoteDepth == 0 ? 1 : 0;
            }
            if (segment.BoundaryAfter == D043AdaptiveTtsBoundaryKind.Paragraph) current = baseline;
        }
        return result;
    }
}
