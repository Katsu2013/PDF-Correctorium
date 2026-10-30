using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App.ViewModels;

public partial class MainWindowViewModel
{
    public RelayCommand GroupParagraphCommand { get; }
    public RelayCommand SeparateParagraphLinesCommand { get; }
    public RelayCommand ResetParagraphGroupingCommand { get; }
    public string ParagraphGroupingSummary => LocalizationService.Translate(_selectedOverlays.Count == 0 ? "段落の指定" :
        _selectedOverlays.Select(row => row.ParagraphGroupId).Distinct().Skip(1).Any() ? "段落：指定が混在" :
        _selectedOverlays.All(row => row.ParagraphGroupId == Guid.Empty) ? "段落：行ごとに区切る" :
        _selectedOverlays.All(row => row.ParagraphGroupId is { } id && id != Guid.Empty) ? "段落：手動指定" : "段落：自動判定");

    private List<OverlayRegionViewModel> ParagraphRows() => OverlayItems.Where(row => !row.IsDeleted).OrderBy(row => row.ReadingOrder).ToList();

    private bool CanGroupParagraph()
    {
        if (!CanUseOcrEditControls || _selectedOverlays.Count < 2) return false;
        var rows = ParagraphRows();
        var selected = _selectedOverlays.ToHashSet();
        var indices = rows.Select((row, index) => (row, index)).Where(item => selected.Contains(item.row)).Select(item => item.index).ToArray();
        if (indices.Length != selected.Count || indices[^1] - indices[0] + 1 != indices.Length) return false;
        var existing = _selectedOverlays[0].ParagraphGroupId;
        return existing is null || existing == Guid.Empty || _selectedOverlays.Any(row => row.ParagraphGroupId != existing) ||
            rows.Count(row => row.ParagraphGroupId == existing) != selected.Count;
    }

    public string GroupParagraphToolTip => LocalizationService.Translate(
        !CanUseOcrEditControls ? "OCR編集モードで段落を指定できます。" :
        _selectedOverlays.Count < 2 ? "Ctrlで読み順が連続する2項目以上を選び、同じ段落に設定します。文字方向・位置・固定状態は保持します。" :
        CanGroupParagraph() ? "選択した項目を同じ段落に設定します。横書き・縦書きが混在していても、文字方向・位置・固定状態を保持します。" :
        _selectedOverlays.All(row => row.ParagraphGroupId is { } id && id != Guid.Empty && id == _selectedOverlays[0].ParagraphGroupId)
            ? "選択した項目は既に同じ段落です。" : "読み順の途中に未選択の項目があります。読み順が連続する項目を選んでください。");

    private void SetSelectedParagraph(bool separate)
    {
        if (!(separate ? CanUseOcrEditControls && _selectedOverlays.Count > 0 : CanGroupParagraph())) return;
        var group = separate ? Guid.Empty : Guid.NewGuid();
        var rows = _selectedOverlays.ToArray();
        ApplyRegionEdit(separate ? "選択行を段落ごとに区切る" : "選択行を同じ段落に設定", rows, () =>
        { foreach (var row in rows) row.ParagraphGroupId = group; });
        RaiseParagraphCommands();
        StatusMessage = separate ? "選択した行を個別の段落にしました。" : "選択した行を同じ段落に設定しました。行の位置と文字は保持しています。";
    }

    private void ResetSelectedParagraph()
    {
        if (!ResetParagraphGroupingCommand.CanExecute(null)) return;
        var groups = _selectedOverlays.Where(row => row.ParagraphGroupId is { } id && id != Guid.Empty).Select(row => row.ParagraphGroupId).ToHashSet();
        var selected = _selectedOverlays.ToHashSet();
        var rows = ParagraphRows().Where(row => selected.Contains(row) || groups.Contains(row.ParagraphGroupId)).ToArray();
        ApplyRegionEdit("段落指定を自動判定に戻す", rows, () => { foreach (var row in rows) row.ParagraphGroupId = null; });
        RaiseParagraphCommands();
        StatusMessage = "段落指定を自動判定に戻しました。";
    }

    private IReadOnlyList<OverlayRegionViewModel> ResolveParagraphSelection(OverlayRegionViewModel region)
        => GetParagraphGroups().FirstOrDefault(group => group.Contains(region)) ?? [region];

    // One shared partition for selection, speech and the current-page drawing layer.
    internal IReadOnlyList<IReadOnlyList<OverlayRegionViewModel>> GetParagraphGroups()
    {
        var rows = ParagraphRows();
        var parents = _project?.Pages.FirstOrDefault(page => page.PageNumber == SelectedPage?.PageNumber)?.TextRegions.ToDictionary(row => row.Id);
        var metrics = _pageMetrics.GetValueOrDefault(SelectedPage?.PageNumber ?? 0);
        var lines = rows.Select(row => new SpeechTextSegment(row.Id, row.Text,
            metrics is null ? new PdfRectangle(new PdfPoint(row.Left, PreviewPixelHeight - row.Top - row.Height), new PdfSize(row.Width, row.Height)) :
            SpeechText.GetBounds(ToPdfGeometry(row.Capture(), metrics, row.IsVertical)))
        {
            WritingMode = row.IsVertical ? WritingMode.Vertical : WritingMode.Horizontal,
            FlowDirection = parents?.GetValueOrDefault(row.Id)?.FlowDirection ?? TextFlowDirection.LeftToRight,
            RotationDegrees = row.RotationDegrees, ParagraphGroupId = row.ParagraphGroupId,
            ParentRegionId = parents?.GetValueOrDefault(row.Id)?.ParentRegionId,
        }).ToArray();
        var byId = rows.ToDictionary(row => row.Id);
        var groups = SpeechParagraphs.Partition(lines).Select(group =>
            (IReadOnlyList<OverlayRegionViewModel>)group.Select(line => byId[line.RegionId!.Value]).ToArray()).ToList();
        // Blank edited rows remain selectable even though speech skips their content.
        var grouped = groups.SelectMany(group => group).ToHashSet();
        foreach (var row in rows.Where(row => !grouped.Contains(row))) groups.Add([row]);
        return groups;
    }

    private void RaiseParagraphCommands()
    {
        GroupParagraphCommand?.RaiseCanExecuteChanged(); SeparateParagraphLinesCommand?.RaiseCanExecuteChanged(); ResetParagraphGroupingCommand?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(ParagraphGroupingSummary));
        OnPropertyChanged(nameof(GroupParagraphToolTip));
    }
}
