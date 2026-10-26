using System;
using System.Collections.Generic;

namespace LilithTextInjector;

// Presentation only. Do not widen the native chat/IPC four-locale contract.
internal static class MiniGameDisplayLanguage
{
    private static readonly string[] AdditionalPrefixes = { "ko", "ru", "id", "th", "tr", "vi" };
    internal static IReadOnlyList<string> Supported { get; } = Array.AsReadOnly(new[]
        { "zh-TW", "zh-CN", "ja", "en", "ko", "ru", "es-419", "pt-BR", "id", "th", "tr", "vi" });

    internal static string Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "zh-TW";
        foreach (var c in value) if (char.IsControl(c)) return "zh-TW";
        var name = value.Trim().Replace('_', '-');
        if (name.Length > 32) return "zh-TW";
        if (Is(name, "zh-Hans") || Is(name, "zh-CN") || Is(name, "zh-SG")
            || name.Equals("simplified-chinese", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
        if (name.Equals("zh", StringComparison.OrdinalIgnoreCase) || Is(name, "zh-Hant")
            || Is(name, "zh-TW") || Is(name, "zh-HK") || Is(name, "zh-MO")
            || name.Equals("traditional-chinese", StringComparison.OrdinalIgnoreCase)) return "zh-TW";
        if (Is(name, "ja") || name.Equals("jp", StringComparison.OrdinalIgnoreCase)
            || name.Equals("japanese", StringComparison.OrdinalIgnoreCase)) return "ja";
        if (Is(name, "en") || name.Equals("english", StringComparison.OrdinalIgnoreCase)) return "en";
        foreach (var language in AdditionalPrefixes)
            if (Is(name, language)) return language;
        if (Is(name, "es")) return "es-419";
        if (Is(name, "pt")) return "pt-BR";
        // Same safe English default as the existing display projection for an
        // unknown nonempty language. Supported locales never use this fallback.
        return "en";
    }

    private static bool Is(string value, string prefix) =>
        value.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || value.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase);
}
