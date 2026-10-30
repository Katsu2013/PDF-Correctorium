using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PdfCorrectorium.App.Services;

/// <summary>One budget per whole candidate search, not per page. Results are UTF-16 ranges.</summary>
internal sealed class DocumentRegexSearch
{
    private readonly Regex _regex;
    private long _characters;
    private int _matches;
    private TimeSpan _elapsed;
    internal DocumentRegexSearch(string pattern, bool matchCase)
    {
        if (pattern.Length is 0 or > 1024) throw new ArgumentException("正規表現は1〜1024文字で指定してください。");
        try
        {
            _regex = new Regex(pattern, RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase), TimeSpan.FromMilliseconds(200));
            if (_regex.IsMatch(string.Empty)) throw new ArgumentException("空文字に一致する正規表現は墨消し検索に使用できません。");
        }
        catch (RegexMatchTimeoutException error) { throw new InvalidOperationException("正規表現の評価が制限時間を超えました。式を単純にしてください。", error); }
        catch (ArgumentException error) { throw new ArgumentException("正規表現が正しくありません：" + error.Message, error); }
    }

    internal IEnumerable<(int Start, int Length)> Find(string text, CancellationToken token)
    {
        _characters += text.Length;
        if (_characters > 16_000_000) throw new InvalidOperationException("正規表現の検索文字量上限を超えました。対象ページを絞ってください。");
        Match? match = null;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var start = Stopwatch.GetTimestamp();
            try { match = match is null ? _regex.Match(text) : match.NextMatch(); }
            catch (RegexMatchTimeoutException error) { throw new InvalidOperationException("正規表現の評価が制限時間を超えました。式を単純にしてください。", error); }
            _elapsed += Stopwatch.GetElapsedTime(start);
            if (_elapsed > TimeSpan.FromSeconds(5)) throw new InvalidOperationException("正規表現の全体評価時間を超えました。対象ページを絞ってください。");
            if (!match.Success) yield break;
            if (match.Length == 0) throw new InvalidOperationException("空文字に一致する正規表現は墨消し検索に使用できません。");
            if (++_matches > 10000) throw new InvalidOperationException("一致箇所が10,000件を超えました。対象ページを絞ってください。");
            yield return (match.Index, match.Length);
        }
    }
}
