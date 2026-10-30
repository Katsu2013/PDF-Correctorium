using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PdfCorrectorium.App.Services;

/// <summary>Bounded, display-only grapheme diff. No replacement text or OCR offsets are modified.</summary>
internal static class WordDifference
{
    public static readonly DependencyProperty OriginalProperty = DependencyProperty.RegisterAttached(
        "Original", typeof(string), typeof(WordDifference), new PropertyMetadata("", Update));
    public static readonly DependencyProperty SuggestedProperty = DependencyProperty.RegisterAttached(
        "Suggested", typeof(string), typeof(WordDifference), new PropertyMetadata("", Update));
    public static readonly DependencyProperty ShowSuggestedProperty = DependencyProperty.RegisterAttached(
        "ShowSuggested", typeof(bool), typeof(WordDifference), new PropertyMetadata(false, Update));
    public static readonly DependencyProperty MaximumTextElementsProperty = DependencyProperty.RegisterAttached(
        "MaximumTextElements", typeof(int), typeof(WordDifference), new PropertyMetadata(64, Update));
    public static void SetMaximumTextElements(DependencyObject target, int value) => target.SetValue(MaximumTextElementsProperty, value);
    public static int GetMaximumTextElements(DependencyObject target) => (int)target.GetValue(MaximumTextElementsProperty);
    public static void SetOriginal(DependencyObject target, string value) => target.SetValue(OriginalProperty, value);
    public static string GetOriginal(DependencyObject target) => (string)target.GetValue(OriginalProperty);
    public static void SetSuggested(DependencyObject target, string value) => target.SetValue(SuggestedProperty, value);
    public static string GetSuggested(DependencyObject target) => (string)target.GetValue(SuggestedProperty);
    public static void SetShowSuggested(DependencyObject target, bool value) => target.SetValue(ShowSuggestedProperty, value);
    public static bool GetShowSuggested(DependencyObject target) => (bool)target.GetValue(ShowSuggestedProperty);

    private static void Update(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not TextBlock text) return;
        var original = GetOriginal(text) ?? "";
        var suggested = GetSuggested(text) ?? "";
        var useSuggested = GetShowSuggested(text);
        var (left, right) = Compare(original, suggested, GetMaximumTextElements(text));
        var value = useSuggested ? suggested : original;
        var changed = useSuggested ? right : left;
        text.Inlines.Clear();
        // Runs retain the exact displayed string, including combining and surrogate characters.
        for (var start = 0; start < value.Length;)
        {
            var end = start + 1;
            while (end < value.Length && changed[end] == changed[start]) end++;
            var run = new Run(value[start..end]);
            if (changed[start]) { run.SetResourceReference(TextElement.ForegroundProperty, "AccentTextBrush"); run.FontWeight = FontWeights.SemiBold; run.TextDecorations = TextDecorations.Underline; }
            text.Inlines.Add(run); start = end;
        }
    }

    internal static (bool[] Original, bool[] Suggested) Compare(string original, string suggested, int maximumElements = 64)
    {
        var left = Enumerable.Repeat(true, original.Length).ToArray();
        var right = Enumerable.Repeat(true, suggested.Length).ToArray();
        maximumElements = Math.Clamp(maximumElements, 1, 512);
        if (original.Length > maximumElements * 4 || suggested.Length > maximumElements * 4) return (left, right);
        var a = StringInfo.ParseCombiningCharacters(original);
        var b = StringInfo.ParseCombiningCharacters(suggested);
        if (a.Length > maximumElements || b.Length > maximumElements) return (left, right);
        static int Length(string text, int[] starts, int index) => (index + 1 < starts.Length ? starts[index + 1] : text.Length) - starts[index];
        bool EqualElements(int i, int j) => original.AsSpan(a[i], Length(original, a, i))
            .SequenceEqual(suggested.AsSpan(b[j], Length(suggested, b, j)));
        var lengths = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        for (var j = b.Length - 1; j >= 0; j--)
            lengths[i, j] = EqualElements(i, j)
                ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        var x = 0; var y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (EqualElements(x, y))
            {
                Array.Fill(left, false, a[x], Length(original, a, x));
                Array.Fill(right, false, b[y], Length(suggested, b, y));
                x++; y++;
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1]) x++;
            else y++;
        }
        return (left, right);
    }
}
