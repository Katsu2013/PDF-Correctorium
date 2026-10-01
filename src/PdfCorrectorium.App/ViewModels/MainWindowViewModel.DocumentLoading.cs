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
    private bool _isDocumentMetadataLoading;
    private bool _isDocumentInspectionLoading;
    public bool IsDocumentMetadataLoading => _isDocumentMetadataLoading;
    public bool NeedsDocumentLoadingNotice => _isDocumentMetadataLoading || _isDocumentInspectionLoading || _documentMetadataError is not null;
    public bool CanEditDocument => HasDocument && !_isDocumentMetadataLoading && _documentMetadataError is null;
    public string DocumentLoadingSummary => _isDocumentMetadataLoading
        ? "リンクを読み込み中です。ページ移動・スクロール・拡大縮小は利用できます。"
        : _isDocumentInspectionLoading ? "PDFの特性をバックグラウンドで確認しています。編集操作は利用できます。"
        : _documentMetadataError is not null
            ? $"リンクの読み込みに失敗しました：{_documentMetadataError.Message[..Math.Min(_documentMetadataError.Message.Length, 500)]}\n文書を開き直してください。安全のため編集・保存は開始していません。"
            : "文書の読み込みが完了しました。";

    private void NotifyDocumentLoading()
    {
        OnPropertyChanged(nameof(IsDocumentMetadataLoading));
        OnPropertyChanged(nameof(CanEditDocument));
        OnPropertyChanged(nameof(DocumentLoadingSummary));
        OnPropertyChanged(nameof(NeedsDocumentLoadingNotice));
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
        // The running task disposes its token source after all child operations finish.
    }

    private void StartDocumentMetadata(string pdfPath)
    {
        CancelDocumentMetadata();
        _documentMetadataError = null;
        _isDocumentMetadataLoading = true;
        _isDocumentInspectionLoading = true;
        InputPdfIssues.Clear();
        var cancellation = new CancellationTokenSource();
        _documentMetadataCancellation = cancellation;
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
            var scan = await Task.Run(() => scanReader is null
                ? PdfSourceLinkService.ReadAsync(pdfPath, sequence, token, coordinatesOnly)
                : scanReader(pdfPath, sequence, token, coordinatesOnly), token);
            if (!IsCurrent()) return;
            _sourceLinkPageCoordinates.Clear();
            foreach (var pair in scan.PageCoordinates) _sourceLinkPageCoordinates[pair.Key] = pair.Value;
            if (!_project!.SourceLinksInitialized || _project.SourceLinkImportVersion < 2)
                _project = UpgradeImportedLinks(_project, scan);
            _unsupportedSourceLinkActions = _project.InternalLinks.Count(link => link.UnsupportedAction is not null);
            _isDocumentMetadataLoading = false;
            RefreshLinkAreaItems();
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
                NotifyDocumentLoading();
            }
            cancellation.Dispose();
        }
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
