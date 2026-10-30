using System.Globalization;

namespace PdfCorrectorium.Core.Documents;

/// <summary>Immutable project-owned prefix index; local exceptions remain cheap per-row inputs.</summary>
public sealed class WordReadingMatcher
{
    public static readonly WordReadingMatcher Empty = new([]);
    private readonly Dictionary<char, WordReading[]> _prefixes;
    public WordReadingMatcher(IReadOnlyList<WordReading> readings) => _prefixes = Index(readings);
    private static Dictionary<char, WordReading[]> Index(IReadOnlyList<WordReading> readings) => readings
        .Where(rule => !string.IsNullOrEmpty(rule.SurfaceText) && !string.IsNullOrWhiteSpace(rule.ReadingText))
        .GroupBy(rule => rule.SurfaceText, StringComparer.Ordinal)
        .Where(group => group.Select(rule => rule.ReadingText).Distinct(StringComparer.Ordinal).Count() == 1)
        .Select(group => group.First()).GroupBy(rule => rule.SurfaceText[0])
        .ToDictionary(group => group.Key, group => group.OrderByDescending(rule => rule.SurfaceText.Length).ToArray());

    public IReadOnlyList<WordReadingMatch> FindMatches(string text, IReadOnlyList<WordReading> local)
    {
        if (text.Length == 0 || _prefixes.Count == 0 && local.Count == 0) return [];
        var exceptions = local.Count == 0 ? null : local.Select(rule => rule.SurfaceText).ToHashSet(StringComparer.Ordinal);
        var localIndex = local.Count == 0 ? null : Index(local);
        var boundaries = new bool[text.Length + 1];
        foreach (var start in StringInfo.ParseCombiningCharacters(text)) boundaries[start] = true;
        boundaries[text.Length] = true;
        var result = new List<WordReadingMatch>();
        for (var index = 0; index < text.Length;)
        {
            WordReading? best = null;
            void Search(WordReading[]? candidates, bool common)
            {
                if (candidates is null) return;
                foreach (var rule in candidates)
                {
                    if (common && exceptions?.Contains(rule.SurfaceText) == true) continue;
                    var length = rule.SurfaceText.Length;
                    if (best is not null && length < best.SurfaceText.Length) break;
                    if (index + length <= text.Length && boundaries[index + length] && text.AsSpan(index).StartsWith(rule.SurfaceText, StringComparison.Ordinal))
                    { best = rule; break; }
                }
            }
            if (boundaries[index])
            {
                Search(_prefixes.GetValueOrDefault(text[index]), true);
                Search(localIndex?.GetValueOrDefault(text[index]), false);
            }
            if (best is null) { index++; continue; }
            result.Add(new(index, best.SurfaceText.Length, best.SurfaceText, best.ReadingText)); index += best.SurfaceText.Length;
        }
        return result;
    }
}
