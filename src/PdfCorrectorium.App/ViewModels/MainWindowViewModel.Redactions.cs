using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;

namespace PdfCorrectorium.App.ViewModels;

public enum RedactionInputMode { Rectangle, TextSelection, Polygon, Freehand }

/// <summary>現在ページに重ね、選択・移動・サイズ変更できる墨消し指定です。</summary>
public sealed class RedactionOverlayViewModel : INotifyPropertyChanged
{
    private double _left;
    private double _top;
    private double _width;
    private double _height;
    private string _colorHex;
    private bool _isSelected;
    private readonly Geometry? _shapeGeometry;
    private readonly PdfRedactionShapeKind _shapeKind;

    public RedactionOverlayViewModel(
        Guid id,
        Rect bounds,
        string colorHex,
        PdfRedactionShapeKind shapeKind,
        IReadOnlyList<Point>? previewPath)
    {
        Id = id;
        _colorHex = colorHex;
        _shapeKind = shapeKind;
        _shapeGeometry = CreateGeometry(bounds, previewPath);
        UpdateBounds(bounds);
    }

    public Guid Id { get; }
    public double Left { get => _left; private set => Set(ref _left, value); }
    public double Top { get => _top; private set => Set(ref _top, value); }
    public double Width { get => _width; private set => Set(ref _width, value); }
    public double Height { get => _height; private set => Set(ref _height, value); }
    public string ColorHex { get => _colorHex; internal set => Set(ref _colorHex, value); }
    public string Description => $"{ShapeDisplayName}: {Left:0}, {Top:0}  {Width:0}×{Height:0}";
    public Rect Bounds => new(Left, Top, Width, Height);
    public bool IsSelected { get => _isSelected; internal set => Set(ref _isSelected, value); }
    public bool IsRectangle => _shapeGeometry is null;
    public bool IsPath => _shapeGeometry is not null;
    public bool IsTextSelection => _shapeKind == PdfRedactionShapeKind.TextSelection;
    public Geometry? ShapeGeometry => _shapeGeometry;
    private string ShapeDisplayName => _shapeKind switch
    {
        PdfRedactionShapeKind.TextSelection => "文字",
        PdfRedactionShapeKind.Polygon => "多角形",
        PdfRedactionShapeKind.Freehand => "フリーハンド",
        _ => "矩形",
    };

    private static Geometry? CreateGeometry(Rect bounds, IReadOnlyList<Point>? points)
    {
        if (points is null || points.Count < 3) return null;
        var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(points[0].X - bounds.Left, points[0].Y - bounds.Top), true, true);
            context.PolyLineTo(points.Skip(1).Select(point =>
                new Point(point.X - bounds.Left, point.Y - bounds.Top)).ToArray(), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

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
    private int _redactionInputModeIndex;
    private IReadOnlyList<PdfSelectableTextCharacter> _currentSelectableCharacters = [];

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

    public int RedactionInputModeIndex
    {
        get => _redactionInputModeIndex;
        set
        {
            if (!Set(ref _redactionInputModeIndex, Math.Clamp(value, 0, 3))) return;
            OnPropertyChanged(nameof(RedactionInputMode));
            SelectedRedaction = null;
            StatusMessage = RedactionInputMode switch
            {
                RedactionInputMode.TextSelection => "PDF上の文字を囲んで墨消し対象にします。可視文字と透明文字の両方を選択できます。",
                RedactionInputMode.Polygon => "ページ上を順にクリックし、ダブルクリックまたはEnterで多角形を確定します。",
                RedactionInputMode.Freehand => "ページ上をドラッグし、自由形状の墨消し範囲を描きます。",
                _ => "ページ上をドラッグし、矩形の墨消し範囲を作成します。",
            };
        }
    }

    public RedactionInputMode RedactionInputMode => (RedactionInputMode)RedactionInputModeIndex;

    internal void SetSelectablePdfCharacters(IReadOnlyList<PdfSelectableTextCharacter> characters) =>
        _currentSelectableCharacters = characters;

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
            ShapeKind = PdfRedactionShapeKind.Rectangle,
        };
        _project = _project with { Redactions = _project.Redactions.Append(redaction).ToArray() };
        RecordProjectAnnotationChange(before, "墨消し範囲を追加");
        RefreshRedactionItems();
        SelectedRedaction = RedactionItems.FirstOrDefault(item => item.Id == redaction.Id);
        StatusMessage = "墨消し範囲を追加しました。PDF出力時に対象ページを安全な画像へ変換します。";
        return redaction;
    }

    /// <summary>PDFiumが返した可視・不可視文字のうち、囲み範囲と交差する文字を墨消しへ変換します。</summary>
    internal int AddPdfTextRedactions(Rect previewSelection)
    {
        if (!CanAddRedaction() || _project is null || SelectedPage is null ||
            !_pageMetrics.TryGetValue(SelectedPage.PageNumber, out var metrics)) return 0;
        var selection = NormalizeTextSelectionBounds(previewSelection);
        var characters = GetSelectablePdfCharacters(selection, previewSelection);
        if (characters.Length == 0)
        {
            StatusMessage = "選択範囲内にPDFテキストが見つかりませんでした。画像内の文字は矩形・多角形・フリーハンドで指定してください。";
            return 0;
        }
        SynchronizeProjectPages();
        var page = _project.Pages.FirstOrDefault(item => item.PageNumber == SelectedPage.PageNumber);
        if (page is null) return 0;
        var before = CaptureProjectAnnotationSnapshot();
        var bands = BuildTextRedactionBands(
            characters,
            PreviewPixelWidth,
            PreviewPixelHeight,
            _currentSelectableCharacters);
        var additions = bands.Select(bounds => new PdfRedaction
        {
            PageId = page.Id,
            Bounds = ToPdfRectangle(bounds, metrics),
            ColorHex = RedactionColorHex,
            ShapeKind = PdfRedactionShapeKind.TextSelection,
        }).ToArray();
        if (additions.Length == 0) return 0;
        var mergedRedactions = MergeTextSelectionRedactions(
            _project.Redactions,
            additions,
            page.Id,
            out var mergedCount);
        _project = _project with { Redactions = mergedRedactions };
        var currentPageBandCount = mergedRedactions.Count(item =>
            item.PageId == page.Id && item.ShapeKind == PdfRedactionShapeKind.TextSelection);
        RecordProjectAnnotationChange(before, $"PDF文字{characters.Length}字を墨消し帯へ変換・統合");
        RefreshRedactionItems();
        // マーカー操作は確定後すぐ次の範囲を引けることを優先し、白い選択枠や
        // サイズ変更ハンドルを残しません。必要な範囲は一覧または帯のクリックで再選択できます。
        SelectedRedaction = null;
        StatusMessage = mergedCount > 0
            ? $"PDFテキスト{characters.Length}字を選択し、重複・隣接する帯{mergedCount}件を統合しました。このページは{currentPageBandCount}本です。Undoで一括して取り消せます。"
            : $"PDFテキスト{characters.Length}字を、行に沿った{additions.Length}本の墨消し帯にしました。Undoで一括して取り消せます。";
        return characters.Length;
    }

    /// <summary>ドラッグ中の範囲を、確定時と同じ行単位のマーカー帯へ変換します。</summary>
    internal IReadOnlyList<Rect> GetPdfTextRedactionBandPreview(Rect previewSelection)
    {
        if (!CanAddRedaction()) return [];
        var selection = NormalizeTextSelectionBounds(previewSelection);
        return BuildTextRedactionBands(
            GetSelectablePdfCharacters(selection, previewSelection),
            PreviewPixelWidth,
            PreviewPixelHeight,
            _currentSelectableCharacters);
    }

    private PdfSelectableTextCharacter[] GetSelectablePdfCharacters(Rect selection, Rect pointerSelection) =>
        GetSelectablePdfCharactersForDiagnostics(_currentSelectableCharacters, selection, pointerSelection);

    /// <summary>
    /// 行方向は実際のドラッグ範囲へ文字中心が入った文字だけを選びます。行を見つけるために
    /// 補った細い方向の範囲は交差判定に使い、隣の文字ボックスへ触れただけでは選択しません。
    /// </summary>
    internal static PdfSelectableTextCharacter[] GetSelectablePdfCharactersForDiagnostics(
        IReadOnlyList<PdfSelectableTextCharacter> characters,
        Rect hitSelection,
        Rect pointerSelection)
    {
        var raw = new Rect(
            Math.Min(pointerSelection.Left, pointerSelection.Right),
            Math.Min(pointerSelection.Top, pointerSelection.Bottom),
            Math.Abs(pointerSelection.Width),
            Math.Abs(pointerSelection.Height));
        var horizontal = raw.Width >= raw.Height;
        const double coordinateTolerance = 0.01d;
        return characters
            .Where(character =>
            {
                if (string.IsNullOrWhiteSpace(character.Text)) return false;
                var box = new Rect(character.Left, character.Top, character.Width, character.Height);
                if (!hitSelection.IntersectsWith(box)) return false;
                var center = horizontal
                    ? character.Left + character.Width / 2d
                    : character.Top + character.Height / 2d;
                var start = horizontal ? raw.Left : raw.Top;
                var end = horizontal ? raw.Right : raw.Bottom;
                return center >= start - coordinateTolerance && center <= end + coordinateTolerance;
            })
            .ToArray();
    }

    /// <summary>
    /// ワープロの文字選択と同様に、横方向だけ（縦書きでは縦方向だけ）へドラッグしても
    /// ポインター位置を通る文字行へ当たり判定を持たせます。表示する帯は文字境界から
    /// 作るため、この補助幅自体が墨消し矩形になることはありません。
    /// </summary>
    private Rect NormalizeTextSelectionBounds(Rect value)
    {
        var normalized = NormalizePreviewBounds(value);
        var typicalHeight = _currentSelectableCharacters
            .Where(character => character.Height > 0 && double.IsFinite(character.Height))
            .Select(character => character.Height)
            .Order()
            .ToArray();
        var typicalWidth = _currentSelectableCharacters
            .Where(character => character.Width > 0 && double.IsFinite(character.Width))
            .Select(character => character.Width)
            .Order()
            .ToArray();
        var minimumHeight = Math.Clamp(
            typicalHeight.Length == 0 ? 6d : typicalHeight[typicalHeight.Length / 2] * 0.45d,
            4d,
            12d);
        var minimumWidth = Math.Clamp(
            typicalWidth.Length == 0 ? 4d : typicalWidth[typicalWidth.Length / 2] * 0.45d,
            3d,
            10d);
        var centerX = value.Left + value.Width / 2d;
        var centerY = value.Top + value.Height / 2d;
        var width = Math.Max(normalized.Width, minimumWidth);
        var height = Math.Max(normalized.Height, minimumHeight);
        var left = Math.Clamp(centerX - width / 2d, 0d, Math.Max(0d, PreviewPixelWidth - width));
        var top = Math.Clamp(centerY - height / 2d, 0d, Math.Max(0d, PreviewPixelHeight - height));
        return new Rect(left, top, width, height);
    }

    /// <summary>
    /// 選択文字を同じ行ごとにまとめ、蛍光マーカーのような連続した墨消し帯へ変換します。
    /// PDFiumの文字境界はグリフの描画端と完全には一致しないため、帯の外周にも余白を加えます。
    /// </summary>
    internal static IReadOnlyList<Rect> BuildTextRedactionBands(
        IReadOnlyList<PdfSelectableTextCharacter> characters,
        double pageWidth,
        double pageHeight,
        IReadOnlyList<PdfSelectableTextCharacter>? lineReferenceCharacters = null)
    {
        if (pageWidth <= 0 || pageHeight <= 0) return [];
        var boxes = characters
            .Select(character => new Rect(character.Left, character.Top, character.Width, character.Height))
            .Where(box => !box.IsEmpty && box.Width > 0 && box.Height > 0 &&
                          double.IsFinite(box.Left) && double.IsFinite(box.Top) &&
                          double.IsFinite(box.Width) && double.IsFinite(box.Height))
            .OrderBy(box => box.Top + box.Height / 2d)
            .ThenBy(box => box.Left)
            .ToArray();
        if (boxes.Length == 0) return [];
        var lineReferenceBoxes = (lineReferenceCharacters ?? characters)
            .Where(character => !string.IsNullOrWhiteSpace(character.Text))
            .Select(character => new Rect(character.Left, character.Top, character.Width, character.Height))
            .Where(box => !box.IsEmpty && box.Width > 0 && box.Height > 0 &&
                          double.IsFinite(box.Left) && double.IsFinite(box.Top) &&
                          double.IsFinite(box.Width) && double.IsFinite(box.Height))
            .ToArray();

        var lines = new List<List<Rect>>();
        foreach (var box in boxes)
        {
            List<Rect>? bestLine = null;
            var bestDistance = double.MaxValue;
            foreach (var line in lines)
            {
                var lineBounds = Union(line);
                var overlap = Math.Min(lineBounds.Bottom, box.Bottom) - Math.Max(lineBounds.Top, box.Top);
                var overlapRatio = overlap <= 0 ? 0 : overlap / Math.Min(lineBounds.Height, box.Height);
                var centerDistance = Math.Abs(
                    lineBounds.Top + lineBounds.Height / 2d - (box.Top + box.Height / 2d));
                var sameLine = overlapRatio >= 0.45 ||
                               centerDistance <= Math.Max(2d, Math.Min(lineBounds.Height, box.Height) * 0.45d);
                if (!sameLine || centerDistance >= bestDistance) continue;
                bestLine = line;
                bestDistance = centerDistance;
            }
            if (bestLine is null) lines.Add([box]);
            else bestLine.Add(box);
        }

        var result = new List<Rect>(lines.Count);
        foreach (var line in lines)
        {
            var bounds = Union(line);
            var referenceLine = lineReferenceBoxes
                .Where(box => AreOnSameTextLine(bounds, box))
                .ToArray();
            if (referenceLine.Length > 0)
            {
                var lineTop = referenceLine.Min(box => box.Top);
                var lineBottom = referenceLine.Max(box => box.Bottom);
                bounds = new Rect(bounds.Left, lineTop, bounds.Width, lineBottom - lineTop);
            }
            // Padding must also come from the whole line. Otherwise a separately selected
            // descender (for example "g") receives more margin than a capital on the same line,
            // even though their normalized top/bottom already match.
            var heightReference = referenceLine.Length > 0 ? referenceLine : line.ToArray();
            var orderedHeights = heightReference.Select(box => box.Height).Order().ToArray();
            var typicalHeight = orderedHeights[orderedHeights.Length / 2];
            // Keep the marker close to the glyph box. A large vertical margin can cover the
            // preceding/following line on tightly set documents, while the exporter adds its own
            // small PDF-point safety margin when it writes the opaque rectangle.
            var horizontalPadding = Math.Clamp(typicalHeight * 0.025d, 0.25d, 0.75d);
            var verticalPadding = Math.Clamp(typicalHeight * 0.03d, 0.5d, 1.2d);
            var left = Math.Max(0d, bounds.Left - horizontalPadding);
            var top = Math.Max(0d, bounds.Top - verticalPadding);
            var right = Math.Min(pageWidth, bounds.Right + horizontalPadding);
            var bottom = Math.Min(pageHeight, bounds.Bottom + verticalPadding);
            if (right > left && bottom > top) result.Add(new Rect(left, top, right - left, bottom - top));
        }
        return result.OrderBy(bounds => bounds.Top).ThenBy(bounds => bounds.Left).ToArray();
    }

    private static bool AreOnSameTextLine(Rect first, Rect second)
    {
        var overlap = Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top);
        var overlapRatio = overlap <= 0d ? 0d : overlap / Math.Min(first.Height, second.Height);
        var centerDistance = Math.Abs(
            first.Top + first.Height / 2d - (second.Top + second.Height / 2d));
        return overlapRatio >= 0.45d ||
               centerDistance <= Math.Max(2d, Math.Min(first.Height, second.Height) * 0.45d);
    }

    /// <summary>
    /// 同じ行で重複する文字帯は色にかかわらず1本へまとめます。同色かつ隣接する帯も
    /// 連続マーカーとして結合します。重複時は今回選んだ色を優先し、同じ文字を再選択しても
    /// PDF上に矩形が積み重なりません。
    /// </summary>
    internal static IReadOnlyList<PdfRedaction> MergeTextSelectionRedactions(
        IReadOnlyList<PdfRedaction> existing,
        IReadOnlyList<PdfRedaction> additions,
        Guid pageId,
        out int mergedCount)
    {
        var result = existing.ToList();
        mergedCount = 0;
        foreach (var addition in additions)
        {
            var merged = addition;
            Guid? retainedId = null;
            while (true)
            {
                var candidateIndex = result.FindIndex(candidate =>
                {
                    if (candidate.PageId != pageId ||
                        candidate.ShapeKind != PdfRedactionShapeKind.TextSelection ||
                        candidate.PathPoints.Count >= 3 ||
                        !AreOnSamePdfTextLine(candidate.Bounds, merged.Bounds))
                        return false;
                    var horizontalGap = Math.Max(
                        0d,
                        Math.Max(candidate.Bounds.Left, merged.Bounds.Left) -
                        Math.Min(candidate.Bounds.Right, merged.Bounds.Right));
                    var horizontallyOverlapping = horizontalGap <= 0.01d;
                    var adjacency = Math.Max(
                        0.75d,
                        Math.Min(candidate.Bounds.Size.Height, merged.Bounds.Size.Height) * 0.20d);
                    return horizontallyOverlapping ||
                           (string.Equals(candidate.ColorHex, merged.ColorHex, StringComparison.OrdinalIgnoreCase) &&
                            horizontalGap <= adjacency);
                });
                if (candidateIndex < 0) break;
                var candidate = result[candidateIndex];
                retainedId ??= candidate.Id;
                result.RemoveAt(candidateIndex);
                merged = merged with
                {
                    Id = retainedId.Value,
                    Bounds = UnionPdfRectangles(candidate.Bounds, merged.Bounds),
                    ColorHex = addition.ColorHex,
                };
                mergedCount++;
            }
            result.Add(merged);
        }
        return result;
    }

    /// <summary>旧プロジェクトを含む現在ページの既存文字帯を、モード開始時に1回のUndo操作として整理します。</summary>
    private void ConsolidateCurrentPageTextRedactions()
    {
        if (_project is null || SelectedPage is null ||
            _project.Pages.FirstOrDefault(item => item.PageNumber == SelectedPage.PageNumber) is not { } page)
            return;
        var pageTextBands = _project.Redactions
            .Where(item => item.PageId == page.Id &&
                           item.ShapeKind == PdfRedactionShapeKind.TextSelection &&
                           item.PathPoints.Count < 3)
            .OrderBy(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Bounds.Bottom)
            .ThenBy(item => item.Bounds.Left)
            .ToArray();
        if (pageTextBands.Length < 2) return;
        var textBandIds = pageTextBands.Select(item => item.Id).ToHashSet();
        var untouched = _project.Redactions
            .Where(item => !textBandIds.Contains(item.Id))
            .ToArray();
        var consolidated = MergeTextSelectionRedactions(
            untouched,
            pageTextBands,
            page.Id,
            out var mergedCount);
        if (mergedCount == 0) return;
        var before = CaptureProjectAnnotationSnapshot();
        _project = _project with { Redactions = consolidated };
        RecordProjectAnnotationChange(before, $"重複・隣接する文字墨消し{mergedCount}件を統合");
        RefreshRedactionItems();
        StatusMessage = $"同じ行で重複・隣接していた文字墨消し{mergedCount}件を統合しました。Undoで元に戻せます。";
    }

    private static bool AreOnSamePdfTextLine(PdfRectangle first, PdfRectangle second)
    {
        var overlap = Math.Min(first.Top, second.Top) - Math.Max(first.Bottom, second.Bottom);
        var overlapRatio = overlap <= 0d ? 0d : overlap / Math.Min(first.Size.Height, second.Size.Height);
        var centerDistance = Math.Abs(
            first.Bottom + first.Size.Height / 2d - (second.Bottom + second.Size.Height / 2d));
        return overlapRatio >= 0.55d ||
               centerDistance <= Math.Max(0.5d, Math.Min(first.Size.Height, second.Size.Height) * 0.30d);
    }

    private static PdfRectangle UnionPdfRectangles(PdfRectangle first, PdfRectangle second)
    {
        var left = Math.Min(first.Left, second.Left);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        var right = Math.Max(first.Right, second.Right);
        var top = Math.Max(first.Top, second.Top);
        return new PdfRectangle(new PdfPoint(left, bottom), new PdfSize(right - left, top - bottom));
    }

    /// <summary>多角形またはフリーハンドのプレビュー座標を、簡略化してPDF座標へ保存します。</summary>
    internal PdfRedaction? AddPathRedaction(IReadOnlyList<Point> previewPoints, PdfRedactionShapeKind shapeKind)
    {
        if (!CanAddRedaction() || _project is null || SelectedPage is null ||
            !_pageMetrics.TryGetValue(SelectedPage.PageNumber, out var metrics) || previewPoints.Count < 3) return null;
        var points = SimplifyPath(previewPoints.Select(NormalizePreviewPoint).ToArray(), shapeKind == PdfRedactionShapeKind.Freehand ? 1.5 : 0.1, 256);
        if (points.Count < 3) return null;
        var previewBounds = Union(points.Select(point => new Rect(point, new Size(0.01, 0.01))));
        if (previewBounds.Width < 4 || previewBounds.Height < 4) return null;
        SynchronizeProjectPages();
        var page = _project.Pages.FirstOrDefault(item => item.PageNumber == SelectedPage.PageNumber);
        if (page is null) return null;
        var pdfPoints = points.Select(point => ToPdfPoint(point, metrics)).ToArray();
        var pdfBounds = PdfRedactionGeometry.GetBounds(pdfPoints);
        var before = CaptureProjectAnnotationSnapshot();
        var redaction = new PdfRedaction
        {
            PageId = page.Id,
            Bounds = pdfBounds,
            PathPoints = pdfPoints,
            ShapeKind = shapeKind,
            ColorHex = RedactionColorHex,
        };
        _project = _project with { Redactions = _project.Redactions.Append(redaction).ToArray() };
        RecordProjectAnnotationChange(before, shapeKind == PdfRedactionShapeKind.Freehand ? "フリーハンド墨消しを追加" : "多角形墨消しを追加");
        RefreshRedactionItems();
        SelectedRedaction = RedactionItems.FirstOrDefault(item => item.Id == redaction.Id);
        StatusMessage = $"{(shapeKind == PdfRedactionShapeKind.Freehand ? "フリーハンド" : "多角形")}の墨消し範囲を追加しました。";
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
                var path = redaction.PathPoints.Count < 3
                    ? null
                    : redaction.PathPoints.Select(point => ToPreviewPoint(point, metrics)).ToArray();
                RedactionItems.Add(new RedactionOverlayViewModel(redaction.Id, bounds, redaction.ColorHex, redaction.ShapeKind, path));
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
        if (!IsRedactionMode || RedactionItems.FirstOrDefault(item => item.Id == id) is not { } item ||
            item.IsTextSelection) return false;
        item.UpdateBounds(NormalizeEditableRedactionBounds(previewBounds));
        SelectedRedaction = item;
        return true;
    }

    /// <summary>ドラッグ完了時に1回だけプロジェクトへ反映し、1操作分のUndo履歴を作ります。</summary>
    internal bool CommitRedactionBounds(Guid id, Rect originalBounds)
    {
        if (_project is null || SelectedPage is null ||
            RedactionItems.FirstOrDefault(item => item.Id == id) is not { } item ||
            item.IsTextSelection ||
            !_pageMetrics.TryGetValue(SelectedPage.PageNumber, out var metrics)) return false;
        var bounds = NormalizeEditableRedactionBounds(item.Bounds);
        if (AreClose(bounds, originalBounds)) return false;
        var index = _project.Redactions.ToList().FindIndex(redaction => redaction.Id == id);
        if (index < 0) return false;
        var before = CaptureProjectAnnotationSnapshot();
        var updated = _project.Redactions.ToArray();
        var oldRedaction = updated[index];
        var newPdfBounds = ToPdfRectangle(bounds, metrics);
        var transformedPath = oldRedaction.PathPoints.Count < 3
            ? oldRedaction.PathPoints
            : oldRedaction.PathPoints.Select(point => TransformPoint(point, oldRedaction.Bounds, newPdfBounds)).ToArray();
        updated[index] = oldRedaction with { Bounds = newPdfBounds, PathPoints = transformedPath };
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

    private Point NormalizePreviewPoint(Point point) => new(
        Math.Clamp(point.X, 0d, Math.Max(0d, PreviewPixelWidth)),
        Math.Clamp(point.Y, 0d, Math.Max(0d, PreviewPixelHeight)));

    private static PdfPoint ToPdfPoint(Point point, PageMetrics metrics) => new(
        point.X / metrics.PixelWidth * metrics.WidthPoints,
        metrics.HeightPoints - point.Y / metrics.PixelHeight * metrics.HeightPoints);

    private static Point ToPreviewPoint(PdfPoint point, PageMetrics metrics) => new(
        point.X / metrics.WidthPoints * metrics.PixelWidth,
        (metrics.HeightPoints - point.Y) / metrics.HeightPoints * metrics.PixelHeight);

    private static PdfPoint TransformPoint(PdfPoint point, PdfRectangle oldBounds, PdfRectangle newBounds)
    {
        var x = oldBounds.Size.Width <= 0 ? 0d : (point.X - oldBounds.Left) / oldBounds.Size.Width;
        var y = oldBounds.Size.Height <= 0 ? 0d : (point.Y - oldBounds.Bottom) / oldBounds.Size.Height;
        return new PdfPoint(newBounds.Left + x * newBounds.Size.Width, newBounds.Bottom + y * newBounds.Size.Height);
    }

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

    internal static IReadOnlyList<Point> SimplifyPath(IReadOnlyList<Point> points, double tolerance, int maximumPoints)
    {
        if (points.Count <= 3) return points.ToArray();
        var distinct = new List<Point>(points.Count);
        foreach (var point in points)
            if (distinct.Count == 0 || (point - distinct[^1]).Length >= tolerance) distinct.Add(point);
        if (distinct.Count > 2 && (distinct[0] - distinct[^1]).Length < tolerance) distinct.RemoveAt(distinct.Count - 1);
        while (distinct.Count > maximumPoints)
            distinct = distinct.Where((_, index) => index % 2 == 0 || index == distinct.Count - 1).ToList();
        return distinct;
    }

    private static string? NormalizeColorHex(string? value)
    {
        var text = value?.Trim().ToUpperInvariant();
        if (text is null || text.Length != 7 || text[0] != '#' ||
            !text.AsSpan(1).ToArray().All(Uri.IsHexDigit)) return null;
        return text;
    }
}
