using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace LilithTextInjector;

// Text-only supplements: the reviewed bilingual audio catalog is unchanged.
internal static class MiniGameDialogueSubtitles
{
    internal const string SourceCatalogSha256 = "4104C75610381C89A2619A2A600D822C479CB429333C8E05825226BFB85A6187";
    internal const int MaximumBytes = 256 * 1024;
    private static readonly Lazy<bool> SourceMatches = new(() =>
    {
        using var source = typeof(MiniGameDialogueSubtitles).Assembly.GetManifestResourceStream(
            "LilithTextInjector.MiniGameDialogue.catalog.json")
            ?? throw new InvalidDataException("Mini-game source catalog is missing.");
        using var sha = SHA256.Create();
        return source.Length <= MiniGameDialogueCatalog.MaximumCatalogBytes
            && Convert.ToHexString(sha.ComputeHash(source)) == SourceCatalogSha256;
    });
    private static readonly IReadOnlyDictionary<string, Lazy<Translation>> Translations =
        new ReadOnlyDictionary<string, Lazy<Translation>>(MiniGameDisplayLanguage.Supported
            .Where(locale => locale is not ("zh-TW" or "ja"))
            .ToDictionary(locale => locale, locale => new Lazy<Translation>(() => Load(locale)), StringComparer.Ordinal));

    internal sealed class Translation
    {
        internal IReadOnlyDictionary<string, string> Lines { get; }
        internal IReadOnlyDictionary<string, string> Ui { get; }
        internal Translation(Dictionary<string, string> lines, Dictionary<string, string> ui)
        { Lines = new ReadOnlyDictionary<string, string>(lines); Ui = new ReadOnlyDictionary<string, string>(ui); }
    }

    internal static string Text(MiniGameDialogueLine line, string? language)
    {
        if (line == null) throw new ArgumentNullException(nameof(line));
        var locale = MiniGameDisplayLanguage.Resolve(language);
        return locale switch { "zh-TW" => line.Zh, "ja" => line.Ja, _ => Translations[locale].Value.Lines[line.Id] };
    }

    internal static string UiText(string key, string? language)
    {
        var locale = MiniGameDisplayLanguage.Resolve(language);
        if (locale == "zh-TW") return MiniGameUiText.Text(key, LilithGameDisplayLocale.TraditionalChinese);
        if (locale == "ja") return MiniGameUiText.Text(key, LilithGameDisplayLocale.Japanese);
        if (key == null || !Translations[locale].Value.Ui.TryGetValue(key, out var text))
            throw new ArgumentException("Unknown mini-game UI text key.", nameof(key));
        return text;
    }

    private static Translation Load(string locale)
    {
        if (!SourceMatches.Value) throw new InvalidDataException("Mini-game subtitle source catalog differs.");
        using var stream = typeof(MiniGameDialogueSubtitles).Assembly.GetManifestResourceStream(
            "LilithTextInjector.MiniGameDialogue.Subtitles." + locale + ".json")
            ?? throw new InvalidDataException("Mini-game subtitle resource is missing.");
        if (stream.Length <= 0 || stream.Length > MaximumBytes)
            throw new InvalidDataException("Mini-game subtitle resource size rejected.");
        var bytes = new byte[(int)stream.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        return Parse(bytes, locale);
    }

    internal static Translation Parse(byte[] bytes, string locale)
    {
        if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumBytes
            || !Translations.ContainsKey(locale)) throw new InvalidDataException("Mini-game subtitle input rejected.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        var root = Fields(document.RootElement);
        if (root.Count != 5 || !root.TryGetValue("schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1
            || !root.TryGetValue("locale", out var tag) || tag.ValueKind != JsonValueKind.String || tag.GetString() != locale
            || !root.TryGetValue("sourceCatalogSha256", out var hash) || hash.ValueKind != JsonValueKind.String
            || hash.GetString() != SourceCatalogSha256 || !root.TryGetValue("lines", out var lines)
            || !root.TryGetValue("ui", out var ui)) throw new InvalidDataException("Mini-game subtitle identity differs.");
        return new Translation(ReadText(lines, MiniGameDialogueCatalog.All.Select(line => line.Id), 512),
            ReadText(ui, MiniGameUiText.Keys, 256));
    }

    private static Dictionary<string, JsonElement> Fields(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Subtitle object required.");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!fields.TryAdd(property.Name, property.Value)) throw new InvalidDataException("Duplicate subtitle field.");
        return fields;
    }

    private static Dictionary<string, string> ReadText(JsonElement element, IEnumerable<string> expected, int maximum)
    {
        var fields = Fields(element);
        var keys = new HashSet<string>(expected, StringComparer.Ordinal);
        if (!keys.SetEquals(fields.Keys)) throw new InvalidDataException("Subtitle coverage differs.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (field.Value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Subtitle string required.");
            var text = field.Value.GetString()!;
            if (string.IsNullOrWhiteSpace(text) || text.Length > maximum || text.Any(char.IsControl)
                || text != text.Trim()) throw new InvalidDataException("Subtitle text rejected.");
            result.Add(field.Key, text);
        }
        return result;
    }
}
