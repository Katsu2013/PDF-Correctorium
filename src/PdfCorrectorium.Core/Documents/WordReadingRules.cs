using System.Globalization;

namespace PdfCorrectorium.Core.Documents;

public sealed record WordReadingMatch(int Start, int Length, string SurfaceText, string ReadingText);

/// <summary>Shared exact/longest-match rules for speech, screen-only ruby and PDF reading alternatives.</summary>
public static class WordReadingRules
{
    public const int MaximumSharedReadings = 5000;
    public const int MaximumReadingLength = 256;

    public static IReadOnlyList<WordReading> Merge(IReadOnlyList<WordReading> common, IReadOnlyList<WordReading> local)
    {
        if (common.Count == 0) return local;
        if (local.Count == 0) return common;
        var overrides = local.Select(item => item.SurfaceText).ToHashSet(StringComparer.Ordinal);
        return common.Where(item => !overrides.Contains(item.SurfaceText)).Concat(local).ToArray();
    }

    public static bool IsValidShared(IReadOnlyList<WordReading>? readings) => readings is not null &&
        readings.Count <= MaximumSharedReadings && readings.All(item => item is not null &&
            !string.IsNullOrWhiteSpace(item.SurfaceText) && !string.IsNullOrWhiteSpace(item.ReadingText) &&
            item.SurfaceText.Length <= MaximumReadingLength && item.ReadingText.Length <= MaximumReadingLength &&
            !item.SurfaceText.Any(char.IsControl) && !item.ReadingText.Any(char.IsControl) && !item.SurfaceText.Contains('=')) &&
        readings.Select(item => item.SurfaceText).Distinct(StringComparer.Ordinal).Count() == readings.Count;
    public static bool TryParse(string text, out IReadOnlyList<WordReading> readings, out string error)
    {
        var result = new List<WordReading>();
        var number = 0;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            number++;
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1 ||
                string.IsNullOrWhiteSpace(line[..separator]) || string.IsNullOrWhiteSpace(line[(separator + 1)..]))
            {
                readings = []; error = $"{number}行目は「表記=よみ」で入力してください。"; return false;
            }
            result.Add(new() { SurfaceText = line[..separator].Trim(), ReadingText = line[(separator + 1)..].Trim() });
        }
        readings = result; error = string.Empty; return true;
    }

    public static string Format(IEnumerable<WordReading> readings) =>
        string.Join(Environment.NewLine, readings.Select(item => $"{item.SurfaceText}={item.ReadingText}"));

    public static IReadOnlyList<WordReadingMatch> FindMatches(string text, IReadOnlyList<WordReading> readings)
        => new WordReadingMatcher(readings).FindMatches(text, []);
}
