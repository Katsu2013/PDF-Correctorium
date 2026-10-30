namespace PdfCorrectorium.Core.Documents;

/// <summary>論理ページと不変の元PDFページとの対応を正規化します。</summary>
public static class ProjectPageSequence
{
    /// <summary>ページ配列を生成する前に適用する既定の資源上限です。</summary>
    public const int DefaultMaximumPageCount = 10_000;

    public static void ValidatePageBudget(IReadOnlyList<ProjectPageReference>? sequence,
        IReadOnlyList<OcrPage> pages, int sourcePageCount, int maximumPageCount = DefaultMaximumPageCount)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (maximumPageCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPageCount));
        if (sourcePageCount < 0 || sourcePageCount > maximumPageCount || pages.Count > maximumPageCount ||
            sequence?.Count > maximumPageCount || pages.Any(page => page.PageNumber < 1 || page.PageNumber > maximumPageCount) ||
            sequence is not null && sequence.Any(page => page.SourcePageNumber < 1 || page.SourcePageNumber > maximumPageCount))
            throw new InvalidDataException($"プロジェクトのページ数・ページ番号が上限 {maximumPageCount:N0} の範囲外です。");
    }

    /// <summary>ページ数を検査し、既存のOCRページIDを再利用して論理ページ列を生成します。</summary>
    public static IReadOnlyList<ProjectPageReference> Normalize(
        IReadOnlyList<ProjectPageReference>? sequence,
        IReadOnlyList<OcrPage> pages,
        int sourcePageCount,
        int maximumPageCount = DefaultMaximumPageCount,
        CancellationToken cancellationToken = default)
    {
        ValidatePageBudget(sequence, pages, sourcePageCount, maximumPageCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (sequence is { Count: > 0 }) return sequence.ToArray();
        var pageIds = pages.GroupBy(page => page.PageNumber)
            .ToDictionary(group => group.Key, group => group.First().Id);
        var result = new ProjectPageReference[sourcePageCount];
        for (var index = 0; index < result.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result[index] = new ProjectPageReference
            {
                PageId = pageIds.TryGetValue(index + 1, out var id) ? id : Guid.NewGuid(),
                SourcePageNumber = index + 1,
            };
        }
        return result;
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
