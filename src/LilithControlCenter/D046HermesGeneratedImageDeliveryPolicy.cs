using System.IO;
using System.Text;

namespace LilithControlCenter;

// Pure display policy: the caller must first establish an authenticated,
// completed assistant message in the current session/turn. Candidates are not
// receipts or permission to read a file. The existing confined reader and real
// decoder must succeed before a path is supplied to RemoveDeliveredDirectives.
internal static class D046HermesGeneratedImageDeliveryPolicy
{
    internal const int MaximumTextCharacters = 256 * 1024;
    internal const int MaximumCandidates = 8;
    private const int MaximumDirectiveCharacters = 1100;

    internal static IReadOnlyList<D046HermesGeneratedImageAttachment> GetCandidates(
        string text, string? imagesRoot = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumTextCharacters) return Array.Empty<D046HermesGeneratedImageAttachment>();
        var result = new List<D046HermesGeneratedImageAttachment>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        VisitDirectives(text, imagesRoot ?? D046HermesGeneratedImagePresentation.CurrentImagesRoot,
            (_, _, image) => { if (seen.Add(image.FilePath)) result.Add(image); });
        return result;
    }

    internal static string RemoveDeliveredDirectives(
        string text, IReadOnlyCollection<string> verifiedPaths)
        => RewriteDirectives(text, verifiedPaths, placeholder: null);

    // Pending is not successful delivery. The caller supplies a fixed localized
    // loading label, never model/stdout text or a filename; canonical text stays
    // untouched and can be restored verbatim if real decoding later fails.
    internal static string ReplacePendingDirectives(
        string text, IReadOnlyCollection<string> pendingPaths, string placeholder)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(pendingPaths);
        if (string.IsNullOrWhiteSpace(placeholder) || placeholder.Length > 160
            || placeholder.Any(char.IsControl) || placeholder.Contains('/') || placeholder.Contains('\\')
            || placeholder.Contains("MEDIA:", StringComparison.OrdinalIgnoreCase)) return text;
        return RewriteDirectives(text, pendingPaths, placeholder);
    }

    private static string RewriteDirectives(
        string text, IReadOnlyCollection<string> nominatedPaths, string? placeholder)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(nominatedPaths);
        if (text.Length > MaximumTextCharacters || nominatedPaths.Count == 0
            || nominatedPaths.Count > MaximumCandidates) return text;

        var root = D046HermesGeneratedImagePresentation.CurrentImagesRoot;
        var verified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var proposed in nominatedPaths)
        {
            // Bound enumeration as well as Count; this is a pure helper, not a
            // reason to enumerate an unbounded caller-supplied collection.
            if (++count > MaximumCandidates) return text;
            if (D046HermesGeneratedImagePresentation.TryResolveOwnedPath(proposed, root, out var path))
                verified.Add(path);
        }
        if (verified.Count == 0) return text;

        StringBuilder? output = null;
        var retainedFrom = 0;
        var exceeded = false;
        VisitDirectives(text, root, (start, length, image) =>
        {
            if (exceeded || !verified.Contains(image.FilePath)) return;
            var end = start + length;
            var newlineStart = end;
            if (newlineStart > start && text[newlineStart - 1] == '\n') newlineStart--;
            if (newlineStart > start && text[newlineStart - 1] == '\r') newlineStart--;
            var replacementLength = placeholder is null ? 0 : placeholder.Length + end - newlineStart;
            if ((output?.Length ?? 0) + start - retainedFrom + replacementLength > MaximumTextCharacters)
            { exceeded = true; return; }
            output ??= new StringBuilder(text.Length);
            output.Append(text, retainedFrom, start - retainedFrom);
            if (placeholder is not null)
            {
                output.Append(placeholder);
                output.Append(text, newlineStart, end - newlineStart);
            }
            retainedFrom = end;
        });
        if (output is null || exceeded || output.Length + text.Length - retainedFrom > MaximumTextCharacters) return text;
        output.Append(text, retainedFrom, text.Length - retainedFrom);
        return output.ToString();
    }

    private static void VisitDirectives(string text, string imagesRoot,
        Action<int, int, D046HermesGeneratedImageAttachment> visit)
    {
        var acceptedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fenceMarker = '\0';
        var fenceLength = 0;
        var offset = 0;
        while (offset < text.Length)
        {
            var start = offset;
            while (offset < text.Length && text[offset] is not ('\r' or '\n')) offset++;
            var contentEnd = offset;
            if (offset < text.Length && text[offset++] == '\r' && offset < text.Length && text[offset] == '\n') offset++;
            var line = text.AsSpan(start, contentEnd - start);
            // Four-space/tab indentation is a Markdown code example, not a
            // delivery request. Ordinary zero-to-three-space indentation is OK.
            var indent = 0;
            while (indent < line.Length && line[indent] == ' ') indent++;
            if (indent >= 4 || (indent < line.Length && line[indent] == '\t')) continue;
            line = line[indent..].TrimEnd(" \t".AsSpan());
            if (line.Length != 0 && line[0] is '`' or '~')
            {
                var marker = line[0];
                var run = 0;
                while (run < line.Length && line[run] == marker) run++;
                if (run >= 3)
                {
                    if (fenceMarker == '\0') { fenceMarker = marker; fenceLength = run; }
                    else if (marker == fenceMarker && run >= fenceLength
                        && line[run..].Trim(" \t".AsSpan()).IsEmpty) { fenceMarker = '\0'; fenceLength = 0; }
                    continue;
                }
            }
            if (fenceMarker != '\0' || line.Length > MaximumDirectiveCharacters
                || !line.StartsWith("MEDIA:", StringComparison.Ordinal)) continue;
            var proposed = line[6..].Trim(" \t".AsSpan()).ToString();
            if (!D046HermesGeneratedImagePresentation.TryResolveOwnedPath(proposed, imagesRoot, out var path)) continue;
            if (!acceptedPaths.Contains(path))
            {
                if (acceptedPaths.Count >= MaximumCandidates) continue;
                acceptedPaths.Add(path);
            }
            var mediaType = Path.GetExtension(path) switch
            {
                ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => string.Empty
            };
            visit(start, offset - start, new(path, Path.GetFileName(path), mediaType));
        }
    }
}
