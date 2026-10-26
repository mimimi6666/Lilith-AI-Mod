using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LilithTextInjector;

// Shared retrieval result contracts and text terms; the retired local search
// producer, scoring policy and renderer are intentionally absent.
internal sealed class RetrievedMemory
{
    internal MemoryEvent Event { get; }
    internal double Score { get; }
    internal int MatchedTermCount { get; }
    internal string ContextText { get; }

    internal RetrievedMemory(MemoryEvent memoryEvent, double score, int matchedTermCount, string contextText)
    {
        Event = memoryEvent ?? throw new ArgumentNullException(nameof(memoryEvent));
        Score = Math.Clamp(score, 0, 1);
        MatchedTermCount = Math.Max(0, matchedTermCount);
        ContextText = contextText ?? throw new ArgumentNullException(nameof(contextText));
    }
}

internal sealed class MemoryRetrievalResult
{
    internal IReadOnlyList<RetrievedMemory> Items { get; }
    internal int ContextCharacterCount { get; }
    internal int SensitiveItemsExcluded { get; }
    internal int BudgetItemsExcluded { get; }

    internal MemoryRetrievalResult(
        IEnumerable<RetrievedMemory> items,
        int contextCharacterCount,
        int sensitiveItemsExcluded,
        int budgetItemsExcluded)
    {
        Items = new ReadOnlyCollection<RetrievedMemory>(items.ToArray());
        ContextCharacterCount = Math.Max(0, contextCharacterCount);
        SensitiveItemsExcluded = Math.Max(0, sensitiveItemsExcluded);
        BudgetItemsExcluded = Math.Max(0, budgetItemsExcluded);
    }
}

internal static class MemoryTextTerms
{
    private static readonly Regex LatinWord = new("[\\p{L}\\p{N}_]+", RegexOptions.Compiled);

    internal static IReadOnlySet<string> Extract(string? text)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
            return terms;
        foreach (Match match in LatinWord.Matches(text.Normalize(NormalizationForm.FormKC)))
        {
            var value = match.Value.ToLowerInvariant();
            if (ContainsCjk(value))
                AddCjkTerms(value, terms);
            else if (value.Length >= 2)
                terms.Add(value);
        }
        return terms;
    }

    private static void AddCjkTerms(string value, HashSet<string> terms)
    {
        var cjk = new string(value.Where(IsCjk).ToArray());
        if (cjk.Length == 1)
        {
            terms.Add(cjk);
            return;
        }
        for (var index = 0; index < cjk.Length - 1; index++)
            terms.Add(cjk.Substring(index, 2));
    }

    private static bool ContainsCjk(string value)
        => value.Any(IsCjk);

    private static bool IsCjk(char value)
        => value is >= '\u3400' and <= '\u9fff'
            or >= '\uf900' and <= '\ufaff'
            or >= '\u3040' and <= '\u30ff'
            or >= '\uac00' and <= '\ud7af';
}
