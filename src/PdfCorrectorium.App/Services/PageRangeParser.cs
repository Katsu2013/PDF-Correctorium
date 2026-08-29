namespace PdfCorrectorium.App.Services;

/// <summary>
/// 「1,3,5-10」のような利用者向けページ指定を、1始まりのページ番号一覧へ変換します。
/// </summary>
public static class PageRangeParser
{
    /// <summary>
    /// 単一ページと範囲を組み合わせた文字列を解析し、昇順かつ重複のないページ番号を返します。
    /// </summary>
    /// <param name="text">カンマ、空白または改行で区切ったページ指定。</param>
    /// <param name="pageCount">入力PDFの総ページ数。</param>
    /// <param name="pageNumbers">解析に成功したページ番号。</param>
    /// <returns>少なくとも1ページを正しく解析できた場合は <see langword="true"/>。</returns>
    public static bool TryParse(string? text, int pageCount, out IReadOnlyList<int> pageNumbers)
    {
        var result = new SortedSet<int>();
        pageNumbers = [];
        if (string.IsNullOrWhiteSpace(text) || pageCount <= 0) return false;

        var normalized = text
            .Replace('、', ',')
            .Replace('～', '-')
            .Replace('〜', '-')
            .Replace('–', '-')
            .Replace('—', '-')
            .Replace('－', '-');
        var segments = normalized.Split(
            [',', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var segment in segments)
        {
            var rangeParts = segment.Split('-', StringSplitOptions.TrimEntries);
            if (rangeParts.Length == 1)
            {
                if (!TryReadPageNumber(rangeParts[0], pageCount, out var pageNumber)) return false;
                result.Add(pageNumber);
                continue;
            }

            if (rangeParts.Length != 2 ||
                !TryReadPageNumber(rangeParts[0], pageCount, out var firstPage) ||
                !TryReadPageNumber(rangeParts[1], pageCount, out var lastPage))
                return false;

            var rangeStart = Math.Min(firstPage, lastPage);
            var rangeEnd = Math.Max(firstPage, lastPage);
            for (var pageNumber = rangeStart; pageNumber <= rangeEnd; pageNumber++)
                result.Add(pageNumber);
        }

        pageNumbers = result.ToArray();
        return pageNumbers.Count > 0;
    }

    /// <summary>昇順のページ番号を「1-3, 6, 9-11」のような短い範囲表現へ変換します。</summary>
    public static string Format(IEnumerable<int> pageNumbers)
    {
        var orderedPages = pageNumbers.Distinct().OrderBy(pageNumber => pageNumber).ToArray();
        if (orderedPages.Length == 0) return string.Empty;

        var ranges = new List<string>();
        var rangeStart = orderedPages[0];
        var previousPage = rangeStart;
        for (var index = 1; index <= orderedPages.Length; index++)
        {
            if (index < orderedPages.Length && orderedPages[index] == previousPage + 1)
            {
                previousPage = orderedPages[index];
                continue;
            }

            ranges.Add(rangeStart == previousPage ? $"{rangeStart}" : $"{rangeStart}-{previousPage}");
            if (index < orderedPages.Length)
                rangeStart = previousPage = orderedPages[index];
        }

        return string.Join(", ", ranges);
    }

    /// <summary>1つのページ番号がPDFのページ範囲内にあるかを確認します。</summary>
    private static bool TryReadPageNumber(string value, int pageCount, out int pageNumber) =>
        int.TryParse(value, out pageNumber) && pageNumber >= 1 && pageNumber <= pageCount;
}
