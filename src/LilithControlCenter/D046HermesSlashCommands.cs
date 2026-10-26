using LilithRuntimeContracts;

namespace LilithControlCenter;

internal enum D046HermesComposerAction { NewChat, Compress, Usage, Context, Help }

internal sealed record D046HermesSlashSuggestion(
    string Command,
    string CanonicalCommand,
    D046HermesComposerAction Action,
    string DescriptionKey);

/// <summary>
/// Presentation shortcuts for existing typed Workbench actions, intersected
/// with the native catalog. This is not a generic native command executor or
/// a second tool permission catalog. No generated or unimplemented command is
/// offered, and selecting a row only edits the composer.
/// </summary>
internal static class D046HermesSlashCommands
{
    private static readonly IReadOnlyDictionary<string, (D046HermesComposerAction Action, string Key)> Actions =
        new Dictionary<string, (D046HermesComposerAction, string)>(StringComparer.Ordinal)
        {
            ["/new"] = (D046HermesComposerAction.NewChat, "chat.slash.new"),
            ["/compress"] = (D046HermesComposerAction.Compress, "chat.slash.compress"),
            ["/usage"] = (D046HermesComposerAction.Usage, "chat.slash.usage"),
            ["/context"] = (D046HermesComposerAction.Context, "chat.slash.context"),
            ["/help"] = (D046HermesComposerAction.Help, "chat.slash.help")
        };

    internal static IReadOnlyList<D046HermesSlashSuggestion> Project(HermesCapabilityCommandCatalog? catalog)
    {
        if (catalog is null) return Array.Empty<D046HermesSlashSuggestion>();
        var rows = new List<D046HermesSlashSuggestion>();
        foreach (var command in catalog.Commands)
            if (Actions.TryGetValue(command, out var action))
                rows.Add(new(command, command, action.Action, action.Key));
        foreach (var alias in catalog.Aliases)
            if (Actions.TryGetValue(alias.Canonical, out var action)
                && !rows.Any(row => string.Equals(row.Command, alias.Alias, StringComparison.OrdinalIgnoreCase)))
                rows.Add(new(alias.Alias, alias.Canonical, action.Action, action.Key));
        return rows.OrderBy(row => row.Command, StringComparer.Ordinal).ToArray();
    }

    internal static bool TryGetQuery(string text, int caretIndex, int selectionLength, out string query)
    {
        query = string.Empty;
        if (selectionLength != 0 || caretIndex != text.Length || text.Length is 0 or > 128
            || text[0] != '/' || text.Any(char.IsWhiteSpace)) return false;
        query = text;
        return true;
    }

    internal static IReadOnlyList<D046HermesSlashSuggestion> Filter(
        IReadOnlyList<D046HermesSlashSuggestion> rows, string query)
        => rows.Where(row => row.Command.StartsWith(query, StringComparison.OrdinalIgnoreCase)).ToArray();

    internal static int MoveSelection(int index, int count, int direction)
        => count <= 0 ? -1 : ((index < 0 ? (direction > 0 ? -1 : 0) : index) + direction + count) % count;

    internal static bool TryParse(string text, out string command, out bool hasArguments)
    {
        command = string.Empty;
        hasArguments = false;
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal)) return false;
        var separator = 0;
        while (separator < trimmed.Length && !char.IsWhiteSpace(trimmed[separator])) ++separator;
        command = trimmed[..separator];
        hasArguments = separator < trimmed.Length;
        return true;
    }
}
