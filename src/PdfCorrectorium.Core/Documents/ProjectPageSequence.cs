namespace PdfCorrectorium.Core.Documents;

/// <summary>論理ページと不変の元PDFページとの対応を正規化します。</summary>
public static class ProjectPageSequence
{
    /// <summary>
    /// 保存済み対応がなければ物理ページと同じ順の対応を作り、既存OCRページIDを再利用します。
    /// </summary>
    public static IReadOnlyList<ProjectPageReference> Normalize(
        IReadOnlyList<ProjectPageReference>? sequence,
        IReadOnlyList<OcrPage> pages,
        int sourcePageCount)
    {
        if (sourcePageCount < 0) throw new ArgumentOutOfRangeException(nameof(sourcePageCount));
        ArgumentNullException.ThrowIfNull(pages);
        if (sequence is { Count: > 0 }) return sequence.ToArray();

        var pageIds = pages
            .Where(page => page.PageNumber > 0)
            .GroupBy(page => page.PageNumber)
            .ToDictionary(group => group.Key, group => group.First().Id);
        return Enumerable.Range(1, sourcePageCount)
            .Select(number => new ProjectPageReference
            {
                PageId = pageIds.GetValueOrDefault(number, Guid.NewGuid()),
                SourcePageNumber = number,
            })
            .ToArray();
    }

    /// <summary>0、90、180、270度のいずれかへ正規化します。</summary>
    public static int NormalizeRotation(int degrees)
    {
        var normalized = degrees % 360;
        if (normalized < 0) normalized += 360;
        return normalized;
    }

    /// <summary>論理ページ構成が元PDFの物理ページ構成と同一かを返します。</summary>
    public static bool IsPhysicalIdentity(
        IReadOnlyList<ProjectPageReference> sequence,
        int sourcePageCount) =>
        sourcePageCount == sequence.Count &&
        sequence.Select((page, index) =>
                page.SourcePageNumber == index + 1 && NormalizeRotation(page.RotationDegrees) == 0)
            .All(matches => matches);

    /// <summary>実体化済みPDFに対応する同一順・無回転のページ対応を作ります。</summary>
    public static IReadOnlyList<ProjectPageReference> AsMaterialized(
        IReadOnlyList<ProjectPageReference> sequence) =>
        sequence.Select((page, index) => page with
        {
            SourcePageNumber = index + 1,
            RotationDegrees = 0,
        }).ToArray();
}
