using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LilithTextInjector;

internal static class LilithSpeechTextNormalizer
{
    // Presentation-only Markdown list markers are not part of the spoken text.
    // Require a line boundary and following whitespace: -5 and well-known stay intact.
    private static readonly Regex UnorderedListMarkerPattern = new(
        @"^[ \t]*-[ \t]+(?=\S)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex MarkdownLinkPattern = new(
        @"\[([^\]]+)\]\(https?://[^\s\)]+\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WebAddressPattern = new(
        @"https?://\S+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex EmptySourcesHeadingPattern = new(
        @"(?:來源|资料来源|資料來源|出典|Sources?)\s*[:：]\s*$",
        RegexOptions.IgnoreCase
        | RegexOptions.CultureInvariant
        | RegexOptions.Multiline);

    private static readonly Regex ParentheticalStageDirectionPattern = new(
        @"[\(（](?<direction>[^\(\)（）\r\n]{1,24})[\)）]",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> NonSpokenStageDirections = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "笑",
        "笑著",
        "笑着",
        "微笑",
        "輕笑",
        "轻笑",
        "苦笑",
        "偷笑",
        "大笑",
        "笑う",
        "微笑む",
        "laugh",
        "laughs",
        "laughing",
        "chuckle",
        "chuckles",
        "chuckling",
        "giggle",
        "giggles",
        "giggling",
        "smile",
        "smiles",
        "smiling"
    };

    internal static string Prepare(string text)
    {
        if (text == null)
            throw new ArgumentNullException(nameof(text));

        var cleaned = UnorderedListMarkerPattern.Replace(text, string.Empty);
        cleaned = MarkdownLinkPattern.Replace(cleaned, "$1");
        cleaned = WebAddressPattern.Replace(cleaned, string.Empty);
        cleaned = EmptySourcesHeadingPattern.Replace(cleaned, string.Empty);
        var stageDirectionSource = cleaned;
        cleaned = ParentheticalStageDirectionPattern.Replace(
            stageDirectionSource,
            match => ShouldRemoveStageDirection(
                stageDirectionSource,
                match)
                    ? string.Empty
                    : match.Value);
        cleaned = Regex.Replace(cleaned, @"[ \t]+", " ");
        cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n").Trim();
        return cleaned;
    }

    private static bool ShouldRemoveStageDirection(
        string source,
        Match match)
    {
        if (!NonSpokenStageDirections.Contains(
                match.Groups["direction"].Value.Trim()))
        {
            return false;
        }

        var previous = match.Index > 0
            ? source[match.Index - 1]
            : (char?)null;
        var nextIndex = match.Index + match.Length;
        var next = nextIndex < source.Length
            ? source[nextIndex]
            : (char?)null;

        if ((previous.HasValue && IsQuotationMark(previous.Value))
            || (next.HasValue && IsQuotationMark(next.Value)))
        {
            return false;
        }

        return !previous.HasValue
            || !next.HasValue
            || IsSpeechBoundary(previous.Value)
            || IsSpeechBoundary(next.Value);
    }

    private static bool IsSpeechBoundary(char value)
        => char.IsWhiteSpace(value)
            || char.IsPunctuation(value);

    private static bool IsQuotationMark(char value)
        => value is '\''
            or '"'
            or '「'
            or '」'
            or '『'
            or '』'
            or '“'
            or '”'
            or '‘'
            or '’';
}
