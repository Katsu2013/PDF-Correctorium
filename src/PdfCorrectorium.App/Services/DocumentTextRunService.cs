namespace PdfCorrectorium.App.Services;

/// <summary>字形と文字列の対応を維持し、実際に一致した文字だけを返します。正規表現検索は実行しません。</summary>
internal static class DocumentTextRunService
{
    internal sealed record Run(string Text, IReadOnlyList<PdfSelectableTextCharacter> Characters)
    {
        public IReadOnlyList<PdfSelectableTextCharacter> Slice(int start, int length)
        {
            var offset = 0;
            var result = new List<PdfSelectableTextCharacter>();
            foreach (var character in Characters)
            {
                if (offset < start + length && offset + character.Text.Length > start && character.Width > 0 && character.Height > 0)
                    result.Add(character);
                offset += character.Text.Length;
            }
            return result;
        }
    }

    internal static IReadOnlyList<Run> Build(IReadOnlyList<PdfSelectableTextCharacter> characters)
    {
        var runs = new List<Run>();
        var current = new List<PdfSelectableTextCharacter>();
        void Flush()
        {
            if (current.Count > 0) runs.Add(new(string.Concat(current.Select(c => c.Text)), current.ToArray()));
            current.Clear();
        }
        foreach (var character in characters)
        {
            if (character.Text.Contains('\n') || character.Text.Contains('\r')) { Flush(); continue; }
            if (character.Width <= 0 || character.Height <= 0 || !double.IsFinite(character.Left) || !double.IsFinite(character.Top))
            {
                // Some PDF fonts give spaces no ink box; preserve them in the text,
                // but never create a redaction rectangle for a box without area.
                if (string.IsNullOrWhiteSpace(character.Text) && current.Count > 0) current.Add(character);
                else Flush();
                continue;
            }
            if (current.LastOrDefault(item => item.Width > 0 && item.Height > 0) is { } previous)
            {
                var sameRow = Math.Abs(previous.Top + previous.Height / 2 - character.Top - character.Height / 2) <= Math.Max(previous.Height, character.Height) * .6;
                var sameColumn = Math.Abs(previous.Left + previous.Width / 2 - character.Left - character.Width / 2) <= Math.Max(previous.Width, character.Width) * .6;
                var nearby = Math.Abs(previous.Left - character.Left) <= Math.Max(previous.Width, character.Width) * 5 ||
                    Math.Abs(previous.Top - character.Top) <= Math.Max(previous.Height, character.Height) * 5;
                if ((!sameRow && !sameColumn) || !nearby ||
                    sameRow && Math.Abs(previous.Left - character.Left) > Math.Max(previous.Width, character.Width) * 6 ||
                    !sameRow && sameColumn && Math.Abs(previous.Top - character.Top) > Math.Max(previous.Height, character.Height) * 6) Flush();
            }
            current.Add(character);
        }
        Flush();
        return runs;
    }

    internal static IEnumerable<(Run Run, int Start, int Length)> Find(IReadOnlyList<Run> runs, string text, bool matchCase)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        foreach (var run in runs)
            for (var start = 0; start <= run.Text.Length - text.Length;)
            {
                var index = run.Text.IndexOf(text, start, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
                if (index < 0) break;
                yield return (run, index, text.Length);
                start = index + text.Length;
            }
    }

    internal static IEnumerable<(Run Run, int Start, int Length)> Find(IReadOnlyList<Run> runs,
        DocumentRegexSearch pattern, CancellationToken token)
    {
        foreach (var run in runs)
            foreach (var match in pattern.Find(run.Text, token)) yield return (run, match.Start, match.Length);
    }
}
