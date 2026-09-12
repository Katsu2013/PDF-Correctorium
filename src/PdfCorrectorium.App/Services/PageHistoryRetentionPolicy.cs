namespace PdfCorrectorium.App.Services;

/// <summary>
/// ページ構成履歴が保持する作業PDFを、件数と合計容量の両方で制限します。
/// </summary>
/// <remarks>
/// 履歴は新しい項目から連続した範囲だけを残します。途中の項目だけを除去すると、
/// OCR編集とページ構成編集を共有するUndo/Redoの時系列が壊れるためです。
/// </remarks>
internal sealed class PageHistoryRetentionPolicy
{
    internal const int DefaultMaximumWorkingFileCount = 12;
    internal const long DefaultMaximumWorkingBytes = 1024L * 1024 * 1024;

    private readonly PageWorkingFileStore _workingFiles;

    public PageHistoryRetentionPolicy(
        PageWorkingFileStore workingFiles,
        int maximumWorkingFileCount = DefaultMaximumWorkingFileCount,
        long maximumWorkingBytes = DefaultMaximumWorkingBytes)
    {
        ArgumentNullException.ThrowIfNull(workingFiles);
        if (maximumWorkingFileCount < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumWorkingFileCount));
        if (maximumWorkingBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumWorkingBytes));

        _workingFiles = workingFiles;
        MaximumWorkingFileCount = maximumWorkingFileCount;
        MaximumWorkingBytes = maximumWorkingBytes;
    }

    public int MaximumWorkingFileCount { get; }
    public long MaximumWorkingBytes { get; }

    /// <summary>
    /// 新しい順の履歴から、安全に保持できる連続した項目数を返します。
    /// </summary>
    public PageHistoryRetentionDecision Evaluate<T>(
        IReadOnlyList<T> newestFirst,
        int maximumHistoryEntryCount,
        IEnumerable<string?> mandatoryPaths,
        Func<T, IEnumerable<string?>> referencedPathSelector)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);
        ArgumentNullException.ThrowIfNull(mandatoryPaths);
        ArgumentNullException.ThrowIfNull(referencedPathSelector);
        if (maximumHistoryEntryCount < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumHistoryEntryCount));

        var resources = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var unavailableMandatoryResource = !TryMeasureAdditionalResources(mandatoryPaths, resources, out var mandatory);
        foreach (var resource in mandatory) resources.Add(resource.Key, resource.Value);
        var retainedEntryCount = 0;
        var retainedBytes = resources.Values.Sum();
        // 現在表示中のPDFは必ず必要です。単体で上限を超える場合も保持し、
        // 追加の作業PDFを必要としないOCR履歴までは利用できるようにします。
        var storageLimitReached = unavailableMandatoryResource;
        var countLimitReached = newestFirst.Count > maximumHistoryEntryCount;

        foreach (var entry in newestFirst.Take(maximumHistoryEntryCount))
        {
            var pending = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (!TryMeasureAdditionalResources(referencedPathSelector(entry), resources, out pending))
            {
                storageLimitReached = true;
                break;
            }

            var candidateFileCount = resources.Count + pending.Count;
            var additionalBytes = pending.Values.Sum();
            var candidateBytes = retainedBytes + additionalBytes;
            if (pending.Count > 0 && ExceedsLimit(candidateFileCount, candidateBytes))
            {
                storageLimitReached = true;
                break;
            }

            foreach (var resource in pending) resources.Add(resource.Key, resource.Value);
            retainedBytes += additionalBytes;
            retainedEntryCount++;
        }

        return new PageHistoryRetentionDecision(
            retainedEntryCount,
            countLimitReached,
            storageLimitReached,
            resources.Count,
            retainedBytes);
    }

    private bool TryMeasureAdditionalResources(
        IEnumerable<string?> paths,
        IReadOnlyDictionary<string, long> retained,
        out Dictionary<string, long> additional)
    {
        additional = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var resource = _workingFiles.Inspect(path);
            if (resource is null || retained.ContainsKey(resource.Value.Path) || additional.ContainsKey(resource.Value.Path))
                continue;
            if (!resource.Value.IsAvailable) return false;
            additional.Add(resource.Value.Path, resource.Value.Length);
        }
        return true;
    }

    private bool ExceedsLimit(int fileCount, long byteCount) =>
        fileCount > MaximumWorkingFileCount || byteCount > MaximumWorkingBytes;
}

internal readonly record struct PageHistoryRetentionDecision(
    int RetainedEntryCount,
    bool CountLimitReached,
    bool StorageLimitReached,
    int RetainedWorkingFileCount,
    long RetainedWorkingBytes)
{
    public bool WasTrimmed(int originalEntryCount) => RetainedEntryCount < originalEntryCount;
}
