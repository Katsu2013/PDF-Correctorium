using System.Globalization;

namespace PdfCorrectorium.Core.Analysis;

/// <summary>Recognition evidence, not calibrated probabilities. Positions are normalized line coordinates.</summary>
public sealed record OcrLineReading(string Text, double[] Scores, double[] Positions, bool[] Ink);
public sealed record OcrLineCompletion(bool IsAutomatic, string Reason, int[] InsertedIndices);

/// <summary>Bounded, ordinal grapheme comparison. Never silently substitutes, deletes or normalizes text.</summary>
public static class OcrLineReconciler
{
    public const int MaximumElements = 512;
    public static string[] Elements(string text)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        return starts.Select((start, i) => text[start..(i + 1 < starts.Length ? starts[i + 1] : text.Length)]).ToArray();
    }

    public static OcrLineCompletion Evaluate(string current, OcrLineReading reading,
        bool locked, bool previouslyEdited, bool vertical)
    {
        OcrLineCompletion Review(string reason) => new(false, reason, []);
        if (current == reading.Text) return Review("既存の文字と一致しました。");
        if (locked) return Review("固定中の行・文字は変更しません。固定を解除してから再認識してください。");
        if (vertical) return Review("縦書きは原画像を確認して適用してください。");
        if (previouslyEdited) return Review("手修正済みの文字があるため、結果を確認してください。");
        if (current.Length > MaximumElements * 4 || reading.Text.Length > MaximumElements * 4)
            return Review("文字数が自動補完の対象外です。");
        var old = Elements(current); var next = Elements(reading.Text);
        if (old.Length < 3 || next.Length > MaximumElements || old.Length > MaximumElements)
            return Review("文字数が自動補完の対象外です。");
        if (next.Length <= old.Length) return Review("置換・削除の可能性があります。結果を確認してください。");
        if (next.Length - old.Length > Math.Min(8, Math.Max(1, old.Length / 4)))
            return Review("追加文字が多いため、結果を確認してください。");
        if (!Valid(reading, next.Length)) return Review("文字単位の認識根拠が不足しています。");
        var first = Match(old, next, false); var last = Match(old, next, true);
        if (first is null || last is null) return Review("置換を含むため、結果を確認してください。");
        if (!first.SequenceEqual(last)) return Review("繰り返し文字などで挿入位置が一意に決まりません。");
        var retained = first.ToHashSet();
        var added = Enumerable.Range(0, next.Length).Where(i => !retained.Contains(i)).ToArray();
        if (added.Any(i => string.IsNullOrWhiteSpace(next[i]) || next[i].Any(c => char.IsControl(c) ||
            char.GetUnicodeCategory(c) == UnicodeCategory.Format)))
            return Review("空白・制御文字の追加は確認が必要です。");
        if (reading.Scores.Average() < .95 ||
            added.Any(i => reading.Scores[i] < .98 || !reading.Ink[i]))
            return Review("自動補完に必要な信頼度・原画像の根拠に届きません。");
        return new(true, "既存文字を保持できる、一意で高信頼な文字抜けです。", added);
    }

    public static bool Valid(OcrLineReading reading, int count) => count > 0 && count <= MaximumElements &&
        reading.Text.Length <= MaximumElements * 4 && reading.Scores.Length == count && reading.Positions.Length == count &&
        reading.Ink.Length == count && reading.Scores.All(v => double.IsFinite(v) && v is >= 0 and <= 1) &&
        reading.Positions.All(v => double.IsFinite(v) && v is >= 0 and <= 1) &&
        reading.Positions.Zip(reading.Positions.Skip(1)).All(p => p.First < p.Second);

    private static int[]? Match(string[] old, string[] next, bool backwards)
    {
        var map = new int[old.Length]; var j = backwards ? next.Length - 1 : 0;
        for (var k = 0; k < old.Length; k++)
        {
            var i = backwards ? old.Length - 1 - k : k;
            while (j >= 0 && j < next.Length && old[i] != next[j]) j += backwards ? -1 : 1;
            if (j < 0 || j >= next.Length) return null;
            map[i] = j; j += backwards ? -1 : 1;
        }
        return map;
    }

    /// <summary>Only the changed span and its immediate anchors share space; distant edited widths stay intact.</summary>
    public static double[] ReconcileAdvances(string oldText, IReadOnlyList<double> oldWidths, OcrLineReading reading)
    {
        var old = Elements(oldText); var next = Elements(reading.Text);
        if (old.Length != oldWidths.Count || old.Length == 0 || next.Length == 0 || next.Length > MaximumElements ||
            oldWidths.Any(v => !double.IsFinite(v) || v <= 0)) throw new ArgumentException("Invalid character geometry.");
        var prefix = 0; var suffix = 0;
        while (prefix < old.Length && prefix < next.Length && old[prefix] == next[prefix]) prefix++;
        while (suffix < old.Length - prefix && suffix < next.Length - prefix && old[^(suffix + 1)] == next[^(suffix + 1)]) suffix++;
        if (old.Length == next.Length) return oldWidths.ToArray();
        // One anchor on each side gives inserted/deleted characters real room without changing the line bounds.
        prefix = Math.Max(0, prefix - 1); suffix = Math.Max(0, suffix - 1);
        var result = new double[next.Length];
        for (var i = 0; i < prefix; i++) result[i] = oldWidths[i];
        for (var i = 0; i < suffix; i++) result[next.Length - 1 - i] = oldWidths[old.Length - 1 - i];
        var extent = oldWidths.Skip(prefix).Take(old.Length - prefix - suffix).Sum();
        var end = next.Length - suffix; var weights = new double[end - prefix];
        var positionsValid = Valid(reading, next.Length);
        for (var i = prefix; i < end; i++)
        {
            var left = i == 0 ? 0 : (reading.Positions.ElementAtOrDefault(i - 1) + reading.Positions.ElementAtOrDefault(i)) / 2;
            var right = i == next.Length - 1 ? 1 : (reading.Positions.ElementAtOrDefault(i) + reading.Positions.ElementAtOrDefault(i + 1)) / 2;
            weights[i - prefix] = positionsValid ? Math.Max(.001, right - left) : 1;
        }
        var sum = weights.Sum();
        for (var i = prefix; i < end; i++) result[i] = extent * weights[i - prefix] / sum;
        return result;
    }
}
