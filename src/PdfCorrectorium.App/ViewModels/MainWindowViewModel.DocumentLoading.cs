using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    // This task belongs to a document generation, not a page or a render session.
    // Replacing the document cancels its subprocesses; stale results are never applied.
    private CancellationTokenSource? _documentMetadataCancellation;
    private Task _documentMetadataTask = Task.CompletedTask;
    private Exception? _documentMetadataError;
    private int _documentMetadataGeneration;
    internal Func<string, IReadOnlyList<PdfCorrectorium.Core.Documents.ProjectPageReference>, CancellationToken, bool,
        Task<PdfSourceLinkService.ScanResult>>? DocumentMetadataScanOverride { get; set; }
    internal Func<string, IReadOnlyList<PdfCorrectorium.Core.Documents.ProjectPageReference>, IReadOnlyCollection<int>,
        CancellationToken, bool, Task<PdfSourceLinkService.ScanResult>>? DocumentPageScanOverride { get; set; }
    private readonly HashSet<int> _readyLinkPages = [];
    private readonly HashSet<int> _priorityLinkPages = [];
    private int _linkPageCount;
    private TaskCompletionSource _linkMetadataProgress = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isDocumentMetadataLoading;
    private bool _isDocumentInspectionLoading;
    public bool IsDocumentMetadataLoading => _isDocumentMetadataLoading;
    // Only an actionable failure belongs in the top notification area.
    public bool NeedsDocumentLoadingNotice => _documentMetadataError is not null;
    public bool IsDocumentLoadingProgressVisible => _documentMetadataError is null &&
        (_isDocumentMetadataLoading || _isDocumentInspectionLoading) && !IsBackgroundOperationVisible;
    public bool IsDocumentLoadingProgressIndeterminate => !_isDocumentMetadataLoading || _linkPageCount == 0;
    public double DocumentLoadingProgress => _linkPageCount == 0 ? 0 : 100d * _readyLinkPages.Count / _linkPageCount;
    public string DocumentLoadingStatusText => _isDocumentMetadataLoading
        ? $"{LocalizationService.Translate("リンク確認中")} ({_readyLinkPages.Count}/{_linkPageCount})"
        : LocalizationService.Translate("PDFの特性を確認中");
    public bool CanEditDocument => HasDocument && _documentMetadataError is null &&
        (!_isDocumentMetadataLoading || IsLinkPageReady(SelectedPage?.PageNumber ?? 0));
    public string DocumentLoadingSummary => _documentMetadataError is not null
        ? $"リンクの読み込みに失敗しました：{_documentMetadataError.Message[..Math.Min(_documentMetadataError.Message.Length, 500)]}\n文書を開き直してください。安全のため編集・保存は停止しています。"
        : _isDocumentMetadataLoading
        ? CanEditDocument
            ? $"表示ページのリンクを編集できます。残りのページを確認中です（{_readyLinkPages.Count}/{_linkPageCount}ページ）。保存・出力時は完了を待ちます。"
            : $"表示ページのリンクを読み込み中です（{_readyLinkPages.Count}/{_linkPageCount}ページ）。ページ移動・スクロール・拡大縮小は利用できます。"
        : _isDocumentInspectionLoading ? "PDFの特性をバックグラウンドで確認しています。編集操作は利用できます。"
        : "文書の読み込みが完了しました。";

    private bool IsLinkPageReady(int pageNumber) =>
        _project?.PageSequence.ElementAtOrDefault(pageNumber - 1) is { } page &&
        _readyLinkPages.Contains(page.SourcePageNumber);

    // Called by active/facing/realized continuous page surfaces. Never starts one subprocess per UI event.
    internal void RequestLinkPage(int pageNumber)
    {
        if (!_isDocumentMetadataLoading ||
            _project?.PageSequence.ElementAtOrDefault(pageNumber - 1) is not { } page ||
            _readyLinkPages.Contains(page.SourcePageNumber)) return;
        _priorityLinkPages.Add(page.SourcePageNumber);
    }

    private void NotifyDocumentLoading()
    {
        OnPropertyChanged(nameof(IsDocumentMetadataLoading));
        OnPropertyChanged(nameof(CanEditDocument));
        OnPropertyChanged(nameof(DocumentLoadingSummary));
        OnPropertyChanged(nameof(NeedsDocumentLoadingNotice));
        OnPropertyChanged(nameof(IsDocumentLoadingProgressVisible));
        OnPropertyChanged(nameof(IsDocumentLoadingProgressIndeterminate));
        OnPropertyChanged(nameof(DocumentLoadingProgress));
        OnPropertyChanged(nameof(DocumentLoadingStatusText));
        OnPropertyChanged(nameof(CanEditGeometry));
        OnPropertyChanged(nameof(CanAddOcrRegion));
        OnPropertyChanged(nameof(CanUseOcrEditControls));
        OnPropertyChanged(nameof(CanHitLinkAreas));
        OnPropertyChanged(nameof(IsLinkOverlayDisplayed));
        OnPropertyChanged(nameof(CanEditLinkSelection));
        NotifyLinkSelection();
        NotifyPreviewAvailability();
        NotifyHistoryState();
        RaiseMultiSelectionCommands();
        RaisePageManagementCommands();
        AddBookmarkCommand.RaiseCanExecuteChanged();
        AddChildBookmarkCommand.RaiseCanExecuteChanged();
        DeleteBookmarkCommand.RaiseCanExecuteChanged();
        MoveBookmarkUpCommand.RaiseCanExecuteChanged();
        MoveBookmarkDownCommand.RaiseCanExecuteChanged();
        ImportOcrDataCommand.RaiseCanExecuteChanged();
    }

    private void CancelDocumentMetadata()
    {
        _documentMetadataGeneration++;
        var cancellation = _documentMetadataCancellation;
        _documentMetadataCancellation = null;
        cancellation?.Cancel();
        SignalLinkMetadataProgress();
        // The running task disposes its token source after all child operations finish.
    }

    private void StartDocumentMetadata(string pdfPath)
    {
        CancelDocumentMetadata();
        _documentMetadataError = null;
        _readyLinkPages.Clear();
        _priorityLinkPages.Clear();
        _linkPageCount = _project!.PageSequence.Select(page => page.SourcePageNumber).Distinct().Count();
        _isDocumentMetadataLoading = true;
        _isDocumentInspectionLoading = true;
        InputPdfIssues.Clear();
        var cancellation = new CancellationTokenSource();
        _documentMetadataCancellation = cancellation;
        // Saved links exist before their crop/rotation metadata; clear any premature fallback frames.
        RefreshLinkAreaItems();
        NotifyDocumentLoading();
        _documentMetadataTask = LoadDocumentMetadataAsync(pdfPath, cancellation);
    }

    private async Task LoadDocumentMetadataAsync(string pdfPath, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var projectId = _project!.ProjectId;
        var sequence = _project.PageSequence;
        var coordinatesOnly = _project.SourceLinksInitialized && _project.SourceLinkImportVersion >= 2;
        bool IsCurrent() => ReferenceEquals(_documentMetadataCancellation, cancellation) &&
            !token.IsCancellationRequested && _project?.ProjectId == projectId && _resolvedPdfPath == pdfPath;
        try
        {
            // Parsing large qpdf responses also runs away from the UI thread.
            // Initialized projects need coordinate metadata, not another annotation import.
            var scanReader = DocumentMetadataScanOverride;
            if (scanReader is not null)
            {
                // Whole-document override retained for cancellation/error diagnostics.
                var scan = await Task.Run(() => scanReader(pdfPath, sequence, token, coordinatesOnly), token);
                if (!IsCurrent()) return;
                ApplyLinkPageScan(scan, coordinatesOnly);
            }
            else
            {
                var pageReader = DocumentPageScanOverride;
                var session = pageReader is null
                    ? await Task.Run(() => PdfSourceLinkService.CreateSessionAsync(pdfPath, sequence, token), token) : null;
                var remaining = sequence.Select(page => page.SourcePageNumber).Distinct().ToHashSet();
                while (remaining.Count > 0)
                {
                    if (!IsCurrent()) return;
                    RequestLinkPage(SelectedPage?.PageNumber ?? 0);
                    var selectedSource = sequence.ElementAtOrDefault((SelectedPage?.PageNumber ?? 0) - 1)?.SourcePageNumber;
                    var priority = (selectedSource is { } selectedNumber ? new[] { selectedNumber } : [])
                        .Concat(_priorityLinkPages).Where(remaining.Contains).Distinct().Take(4).ToArray();
                    // Small background batches bound the wait when the user scrolls/jumps elsewhere.
                    var batch = priority.Length > 0 ? priority : remaining.Take(8).ToArray();
                    foreach (var number in batch) _priorityLinkPages.Remove(number);
                    var scan = await Task.Run(() => pageReader is null
                        ? session!.ReadPagesAsync(batch, coordinatesOnly)
                        : pageReader(pdfPath, sequence, batch, token, coordinatesOnly), token);
                    if (!IsCurrent()) return;
                    if (batch.Any(number => !scan.PageCoordinates.ContainsKey(number)))
                        throw new System.IO.InvalidDataException("表示ページのリンク座標を取得できませんでした。");
                    ApplyLinkPageScan(scan, coordinatesOnly);
                    foreach (var number in batch) remaining.Remove(number);
                }
            }
            if (!IsCurrent()) return;
            _project = _project! with { SourceLinksInitialized = true, SourceLinkImportVersion = 2 };
            _unsupportedSourceLinkActions = _project.InternalLinks.Count(link => link.UnsupportedAction is not null);
            _isDocumentMetadataLoading = false;
            NotifyDocumentLoading();
            await InspectInputPdfAsync(pdfPath, token, IsCurrent);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            _documentMetadataError = ex;
            InputPdfIssues.Add(new PdfInputIssue("links.read.failed", PdfInputIssueSeverity.Warning,
                "リンクを読み込めませんでした", ex.Message, "リンク"));
            await _log.WriteAsync(PdfCorrectorium.Infrastructure.LogLevel.Warning, "links.read.failed", ex.Message, ex);
        }
        finally
        {
            if (IsCurrent())
            {
                _isDocumentMetadataLoading = false;
                _isDocumentInspectionLoading = false;
                _documentMetadataCancellation = null;
                SignalLinkMetadataProgress();
                NotifyDocumentLoading();
            }
            cancellation.Dispose();
        }
    }

    private void ApplyLinkPageScan(PdfSourceLinkService.ScanResult scan, bool coordinatesOnly)
    {
        foreach (var pair in scan.PageCoordinates)
        {
            _sourceLinkPageCoordinates[pair.Key] = pair.Value;
            _readyLinkPages.Add(pair.Key);
        }
        if (!coordinatesOnly)
            _project = UpgradeImportedLinks(_project!, scan, complete: false);
        var currentSource = _project!.PageSequence.ElementAtOrDefault((SelectedPage?.PageNumber ?? 0) - 1)?.SourcePageNumber;
        if (currentSource is { } number && scan.PageCoordinates.ContainsKey(number))
            RefreshLinkAreaItems();
        else
        {
            // Do not replace active ItemsControls during a user's drag just because an offscreen page finished.
            LinkOverlayVersion++;
            OnPropertyChanged(nameof(LinkOverlayVersion));
        }
        SignalLinkMetadataProgress();
        NotifyDocumentLoading();
    }

    private void SignalLinkMetadataProgress()
    {
        var previous = _linkMetadataProgress;
        _linkMetadataProgress = new(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.TrySetResult();
    }

    // Internal navigation needs the destination's coordinates, not all links or font inspection.
    private async Task EnsureLinkPageReadyAsync(int pageNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var generation = _documentMetadataGeneration;
        RequestLinkPage(pageNumber);
        while (_isDocumentMetadataLoading && !IsLinkPageReady(pageNumber))
        {
            // Cancel this waiter, not the shared document reader or other consumers.
            await _linkMetadataProgress.Task.WaitAsync(cancellationToken);
            if (generation != _documentMetadataGeneration)
                throw new OperationCanceledException("Document changed while waiting for page coordinates.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (_documentMetadataError is not null)
            throw new InvalidOperationException("リンクの座標情報を読み込めませんでした。", _documentMetadataError);
    }

    /// <summary>Saving, exporting and structural edits must not snapshot partially imported links.</summary>
    internal async Task EnsureDocumentMetadataAsync(bool includeInspection = true)
    {
        // Geometry/link edits need imported coordinates but need not wait for font/security notices.
        if (!includeInspection && CanEditDocument) return;
        var task = _documentMetadataTask;
        var generation = _documentMetadataGeneration;
        await task;
        if (generation != _documentMetadataGeneration || !ReferenceEquals(task, _documentMetadataTask))
            throw new OperationCanceledException("Document changed while waiting for metadata.");
        if (_documentMetadataError is not null)
            throw new InvalidOperationException("リンクを読み込めていないため処理を開始できません。文書を開き直してください。",
                _documentMetadataError);
    }
}
