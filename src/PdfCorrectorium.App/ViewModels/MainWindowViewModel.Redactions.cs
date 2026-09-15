using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;

namespace PdfCorrectorium.App.ViewModels;

/// <summary>現在ページに重ね、選択・移動・サイズ変更できる墨消し指定です。</summary>
public sealed class RedactionOverlayViewModel : INotifyPropertyChanged
{
    private double _left;
    private double _top;
    private double _width;
    private double _height;
    private string _colorHex;
    private bool _isSelected;

    public RedactionOverlayViewModel(Guid id, Rect bounds, string colorHex)
    {
        Id = id;
        _colorHex = colorHex;
        UpdateBounds(bounds);
    }

    public Guid Id { get; }
    public double Left { get => _left; private set => Set(ref _left, value); }
    public double Top { get => _top; private set => Set(ref _top, value); }
    public double Width { get => _width; private set => Set(ref _width, value); }
    public double Height { get => _height; private set => Set(ref _height, value); }
    public string ColorHex { get => _colorHex; internal set => Set(ref _colorHex, value); }
    public string Description => $"{Left:0}, {Top:0}  {Width:0}×{Height:0}";
    public Rect Bounds => new(Left, Top, Width, Height);
    public bool IsSelected { get => _isSelected; internal set => Set(ref _isSelected, value); }

    internal void UpdateBounds(Rect bounds)
    {
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        OnPropertyChanged(nameof(Bounds));
        OnPropertyChanged(nameof(Description));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed partial class MainWindowViewModel
{
    private string _redactionColorHex = "#000000";
    private RedactionOverlayViewModel? _selectedRedaction;
    private bool _isRedactionPreviewOpaque;

    public ObservableCollection<RedactionOverlayViewModel> RedactionItems { get; } = [];
    public RelayCommand AddSelectedRedactionsCommand { get; }
    public RelayCommand ActivateOcrEditModeCommand { get; }
    public RelayCommand ActivateRedactionModeCommand { get; }
    public RelayCommand DeleteSelectedRedactionCommand { get; }
    public RelayCommand ClearCurrentPageRedactionsCommand { get; }

    private bool CanEnterRedactionMode() => HasDocument && CanUsePreview &&
                                             !IsPdfExporting && !IsBackgroundOperationVisible;

    private void ActivateOcrEditMode()
    {
        EditorMode = EditorInteractionMode.OcrEditing;
        // Reassert checked state when an already-selected mode button is clicked.
        OnPropertyChanged(nameof(IsOcrEditMode));
    }

    private void ActivateRedactionMode()
    {
        if (!CanEnterRedactionMode()) return;
        EditorMode = EditorInteractionMode.Redaction;
        IsOcrOverlayVisible = true;
        // Reassert checked state when an already-selected mode button is clicked.
        OnPropertyChanged(nameof(IsRedactionMode));
    }

    /// <summary>出力PDFで墨消し範囲を塗りつぶすRGB色です。</summary>
    public string RedactionColorHex
    {
        get => _redactionColorHex;
        set
        {
            var normalized = NormalizeColorHex(value);
            if (normalized is null)
            {
                StatusMessage = "墨消し色は #RRGGBB 形式で指定してください。";
                return;
            }
            if (!Set(ref _redactionColorHex, normalized)) return;
            OnPropertyChanged(nameof(RedactionColorPreview));
            ApplySelectedRedactionColor(normalized);
        }
    }

    public string RedactionColorPreview => RedactionColorHex;
    /// <summary>編集面だけの墨消し表示を、出力色と同じ不透明表示にする場合は<c>true</c>です。</summary>
    public bool IsRedactionPreviewOpaque
    {
        get => _isRedactionPreviewOpaque;
        set
        {
            if (!Set(ref _isRedactionPreviewOpaque, value)) return;
            OnPropertyChanged(nameof(IsRedactionPreviewTranslucent));
            OnPropertyChanged(nameof(RedactionPreviewOpacity));
            StatusMessage = value
                ? "墨消し範囲を編集画面でも不透明に表示します。"
                : "墨消し範囲を編集画面では半透明に表示します。";
        }
    }
    public bool IsRedactionPreviewTranslucent
    {
        get => !IsRedactionPreviewOpaque;
        set { if (value) IsRedactionPreviewOpaque = false; }
    }
    public double RedactionPreviewOpacity => IsRedactionPreviewOpaque ? 1d : 0.62d;
    public RedactionOverlayViewModel? SelectedRedaction
    {
        get => _selectedRedaction;
        set
        {
            if (ReferenceEquals(_selectedRedaction, value)) return;
            if (_selectedRedaction is not null) _selectedRedaction.IsSelected = false;
            Set(ref _selectedRedaction, value);
            if (_selectedRedaction is not null)
            {
                _selectedRedaction.IsSelected = true;
                if (!string.Equals(_redactionColorHex, _selectedRedaction.ColorHex, StringComparison.OrdinalIgnoreCase))
                {
                    _redactionColorHex = _selectedRedaction.ColorHex;
                    OnPropertyChanged(nameof(RedactionColorHex));
                    OnPropertyChanged(nameof(RedactionColorPreview));
                }
            }
            DeleteSelectedRedactionCommand.RaiseCanExecuteChanged();
        }
    }

    public int CurrentPageRedactionCount => RedactionItems.Count;

    public void SetRedactionColor(string colorHex)
    {
        RedactionColorHex = colorHex;
        if (SelectedRedaction is null)
            StatusMessage = $"新しい墨消し範囲の色を {RedactionColorHex} に設定しました。";
    }

    /// <summary>現在選択中の墨消し範囲だけの色を変更し、1件のUndo履歴として記録します。</summary>
    private void ApplySelectedRedactionColor(string colorHex)
    {
        if (_project is null || SelectedRedaction is null) return;
        var index = _project.Redactions.ToList().FindIndex(redaction => redaction.Id == SelectedRedaction.Id);
        if (index < 0 || string.Equals(_project.Redactions[index].ColorHex, colorHex, StringComparison.OrdinalIgnoreCase)) return;
        var before = CaptureProjectAnnotationSnapshot();
        var updated = _project.Redactions.ToArray();
        updated[index] = updated[index] with { ColorHex = colorHex };
        _project = _project with { Redactions = updated };
        SelectedRedaction.ColorHex = colorHex;
        RecordProjectAnnotationChange(before, "墨消し範囲の色を変更");
        StatusMessage = $"選択中の墨消し範囲の色を {colorHex} に変更しました。Undoで元に戻せます。";
    }

    public PdfRedaction? AddManualRedaction(Rect previewBounds)
    {
        if (!CanAddRedaction() || SelectedPage is null || _project is null ||
            !_pageMetrics.TryGetValue(SelectedPage.PageNumber, out var metrics)) return null;
        var bounds = NormalizePreviewBounds(previewBounds);
        if (bounds.Width < 4 || bounds.Height < 4) return null;
        SynchronizeProjectPages();
        var page = _project.Pages.FirstOrDefault(item => item.PageNumber == SelectedPage.PageNumber);
        if (page is null) return null;
        var before = CaptureProjectAnnotationSnapshot();
        var redaction = new PdfRedaction
        {
            PageId = page.Id,
            Bounds = ToPdfRectangle(bounds, metrics),
            ColorHex = RedactionColorHex,
        };
        _project = _project with { Redactions = _project.Redactions.Append(redaction).ToArray() };
        RecordProjectAnnotationChange(before, "墨消し範囲を追加");
        RefreshRedactionItems();
        SelectedRedaction = RedactionItems.FirstOrDefault(item => item.Id == redaction.Id);
        StatusMessage = "墨消し範囲を追加しました。PDF出力時に対象ページを安全な画像へ変換します。";
        return redaction;
    }

    private bool CanAddRedaction() => IsRedactionMode && HasDocument && CanUsePreview && SelectedPage is not null &&
                                      !IsPdfExporting && !IsBackgroundOperationVisible;

    private bool CanAddSelectedRedactions() => CanAddRedaction() &&
        (_selectedOverlays.Any(region => !region.IsDeleted) || SelectedOverlay is { IsDeleted: false });

    private void AddSelectedRedactions()
    {
        if (!CanAddSelectedRedactions() || _project is null || SelectedPage is null ||
            !_pageMetrics.TryGetValue(SelectedPage.PageNumber, out var metrics)) return;
        SynchronizeProjectPages();
        var page = _project.Pages.FirstOrDefault(item => item.PageNumber == SelectedPage.PageNumber);
        if (page is null) return;
        var targets = _selectedOverlays.Where(region => !region.IsDeleted).Distinct().ToArray();
        if (targets.Length == 0 && SelectedOverlay is { IsDeleted: false } selected) targets = [selected];
        var before = CaptureProjectAnnotationSnapshot();
        var additions = new List<PdfRedaction>();
        foreach (var region in targets)
        {
            var selectedCells = region.CharacterCells.Where(cell => cell.IsSelected).ToArray();
            Rect bounds;
            int? start = null;
            int? length = null;
            if (targets.Length == 1 && selectedCells.Length > 0)
            {
                bounds = Union(selectedCells.Select(cell => new Rect(
                    region.Left + cell.Left, region.Top + cell.Top, cell.Width, cell.Height)));
                var indexes = selectedCells.Select(cell => cell.Index).Order().ToArray();
                if (indexes[^1] - indexes[0] + 1 == indexes.Length)
                {
                    start = indexes[0];
                    length = indexes.Length;
                }
            }
            else
            {
                bounds = new Rect(region.Left, region.Top, region.Width, region.Height);
            }
            bounds = NormalizePreviewBounds(bounds);
            if (bounds.Width < 1 || bounds.Height < 1) continue;
            additions.Add(new PdfRedaction
            {
                PageId = page.Id,
                Bounds = ToPdfRectangle(bounds, metrics),
                ColorHex = RedactionColorHex,
                SourceRegionId = region.Id,
                SourceCharacterStart = start,
                SourceCharacterLength = length,
            });
        }
        if (additions.Count == 0) return;
        _project = _project with { Redactions = _project.Redactions.Concat(additions).ToArray() };
        RecordProjectAnnotationChange(before, additions.Count == 1 ? "選択範囲を墨消し" : $"{additions.Count}件を墨消し");
        RefreshRedactionItems();
        SelectedRedaction = RedactionItems.FirstOrDefault(item => item.Id == additions[^1].Id);
        StatusMessage = additions.Count == 1
            ? "選択範囲を墨消し対象にしました。Undoで取り消せます。"
            : $"{additions.Count}件を墨消し対象にしました。Undoで取り消せます。";
    }

    private void DeleteSelectedRedaction()
    {
        if (_project is null || SelectedRedaction is null) return;
        var before = CaptureProjectAnnotationSnapshot();
        var id = SelectedRedaction.Id;
        _project = _project with { Redactions = _project.Redactions.Where(item => item.Id != id).ToArray() };
        RecordProjectAnnotationChange(before, "墨消し範囲を削除");
        RefreshRedactionItems();
        StatusMessage = "墨消し範囲を削除しました。Undoで元に戻せます。";
    }

    private void ClearCurrentPageRedactions()
    {
        if (_project is null || SelectedPage is null || RedactionItems.Count == 0) return;
        var page = _project.Pages.FirstOrDefault(item => item.PageNumber == SelectedPage.PageNumber);
        if (page is null) return;
        var count = RedactionItems.Count;
        var before = CaptureProjectAnnotationSnapshot();
        _project = _project with { Redactions = _project.Redactions.Where(item => item.PageId != page.Id).ToArray() };
        RecordProjectAnnotationChange(before, $"このページの墨消し{count}件を解除");
        RefreshRedactionItems();
    }

    private void RefreshRedactionItems()
    {
        var selectedId = SelectedRedaction?.Id;
        RedactionItems.Clear();
        if (_project is not null && SelectedPage is not null &&
            _pageMetrics.TryGetValue(SelectedPage.PageNumber, out var metrics) &&
            _project.Pages.FirstOrDefault(item => item.PageNumber == SelectedPage.PageNumber) is { } page)
        {
            foreach (var redaction in _project.Redactions.Where(item => item.PageId == page.Id))
            {
                var bounds = ToPreviewRectangle(redaction.Bounds, metrics);
                RedactionItems.Add(new RedactionOverlayViewModel(redaction.Id, bounds, redaction.ColorHex));
            }
        }
        SelectedRedaction = RedactionItems.FirstOrDefault(item => item.Id == selectedId);
        OnPropertyChanged(nameof(CurrentPageRedactionCount));
        OnPropertyChanged(nameof(PropertiesPaneSummary));
        RefreshRedactionCommandState();
    }

    /// <summary>ドラッグ中の墨消し範囲だけを更新し、プロジェクトとUndo履歴はまだ変更しません。</summary>
    internal bool PreviewRedactionBounds(Guid id, Rect previewBounds)
    {
        if (!IsRedactionMode || RedactionItems.FirstOrDefault(item => item.Id == id) is not { } item) return false;
        item.UpdateBounds(NormalizeEditableRedactionBounds(previewBounds));
        SelectedRedaction = item;
        return true;
    }

    /// <summary>ドラッグ完了時に1回だけプロジェクトへ反映し、1操作分のUndo履歴を作ります。</summary>
    internal bool CommitRedactionBounds(Guid id, Rect originalBounds)
    {
        if (_project is null || SelectedPage is null ||
            RedactionItems.FirstOrDefault(item => item.Id == id) is not { } item ||
            !_pageMetrics.TryGetValue(SelectedPage.PageNumber, out var metrics)) return false;
        var bounds = NormalizeEditableRedactionBounds(item.Bounds);
        if (AreClose(bounds, originalBounds)) return false;
        var index = _project.Redactions.ToList().FindIndex(redaction => redaction.Id == id);
        if (index < 0) return false;
        var before = CaptureProjectAnnotationSnapshot();
        var updated = _project.Redactions.ToArray();
        updated[index] = updated[index] with { Bounds = ToPdfRectangle(bounds, metrics) };
        _project = _project with { Redactions = updated };
        RecordProjectAnnotationChange(before, "墨消し範囲の位置・サイズを変更");
        RefreshRedactionItems();
        SelectedRedaction = RedactionItems.FirstOrDefault(candidate => candidate.Id == id);
        StatusMessage = "墨消し範囲の位置・サイズを変更しました。Undoで元に戻せます。";
        return true;
    }

    /// <summary>未確定のドラッグ表示を破棄し、保存済みの範囲へ戻します。</summary>
    internal void CancelRedactionBounds(Guid id)
    {
        var selectedId = RedactionItems.Any(item => item.Id == id) ? id : (Guid?)null;
        RefreshRedactionItems();
        SelectedRedaction = selectedId is null ? null : RedactionItems.FirstOrDefault(item => item.Id == selectedId);
    }

    private void RefreshRedactionCommandState()
    {
        AddSelectedRedactionsCommand?.RaiseCanExecuteChanged();
        ActivateOcrEditModeCommand?.RaiseCanExecuteChanged();
        ActivateRedactionModeCommand?.RaiseCanExecuteChanged();
        DeleteSelectedRedactionCommand?.RaiseCanExecuteChanged();
        ClearCurrentPageRedactionsCommand?.RaiseCanExecuteChanged();
    }

    private Rect NormalizePreviewBounds(Rect value)
    {
        var left = Math.Clamp(value.Left, 0, Math.Max(0, PreviewPixelWidth - 1));
        var top = Math.Clamp(value.Top, 0, Math.Max(0, PreviewPixelHeight - 1));
        return new Rect(left, top,
            Math.Clamp(value.Width, 1, Math.Max(1, PreviewPixelWidth - left)),
            Math.Clamp(value.Height, 1, Math.Max(1, PreviewPixelHeight - top)));
    }

    private Rect NormalizeEditableRedactionBounds(Rect value)
    {
        var maximumWidth = Math.Max(1d, PreviewPixelWidth);
        var maximumHeight = Math.Max(1d, PreviewPixelHeight);
        var minimumWidth = Math.Min(8d, maximumWidth);
        var minimumHeight = Math.Min(8d, maximumHeight);
        var width = Math.Clamp(value.Width, minimumWidth, maximumWidth);
        var height = Math.Clamp(value.Height, minimumHeight, maximumHeight);
        return new Rect(
            Math.Clamp(value.Left, 0d, maximumWidth - width),
            Math.Clamp(value.Top, 0d, maximumHeight - height),
            width,
            height);
    }

    private static bool AreClose(Rect first, Rect second) =>
        Math.Abs(first.Left - second.Left) < 0.01 &&
        Math.Abs(first.Top - second.Top) < 0.01 &&
        Math.Abs(first.Width - second.Width) < 0.01 &&
        Math.Abs(first.Height - second.Height) < 0.01;

    private static PdfRectangle ToPdfRectangle(Rect value, PageMetrics metrics) => new(
        new PdfPoint(
            value.Left / metrics.PixelWidth * metrics.WidthPoints,
            metrics.HeightPoints - (value.Top + value.Height) / metrics.PixelHeight * metrics.HeightPoints),
        new PdfSize(
            value.Width / metrics.PixelWidth * metrics.WidthPoints,
            value.Height / metrics.PixelHeight * metrics.HeightPoints));

    private static Rect ToPreviewRectangle(PdfRectangle value, PageMetrics metrics) => new(
        value.Left / metrics.WidthPoints * metrics.PixelWidth,
        (metrics.HeightPoints - value.Top) / metrics.HeightPoints * metrics.PixelHeight,
        value.Size.Width / metrics.WidthPoints * metrics.PixelWidth,
        value.Size.Height / metrics.HeightPoints * metrics.PixelHeight);

    private static Rect Union(IEnumerable<Rect> values)
    {
        var result = Rect.Empty;
        foreach (var value in values) result.Union(value);
        return result;
    }

    private static string? NormalizeColorHex(string? value)
    {
        var text = value?.Trim().ToUpperInvariant();
        if (text is null || text.Length != 7 || text[0] != '#' ||
            !text.AsSpan(1).ToArray().All(Uri.IsHexDigit)) return null;
        return text;
    }
}
