using System.Collections.ObjectModel;
using System.ComponentModel;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App.ViewModels;

/// <summary>A stable filter item whose label can change without clearing the ComboBox selection.</summary>
public sealed class ReviewFilterOption(string japaneseName, string englishName) : INotifyPropertyChanged
{
    public string DisplayName => LocalizationService.IsEnglish ? englishName : japaneseName;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
}

public sealed partial class MainWindowViewModel
{
    private int _reviewFilterIndex;
    private int _overlaySessionVersion;
    private bool _refreshingReviewItems;
    private bool _refreshingLocalizedOptions;
    private CancellationTokenSource? _reviewNavigationCancellation;

    public bool IsReviewMode => EditorModeIndex == 2;
    // This is a temporary interaction restriction, not a change to the saved geometry locks.
    public bool CanEditGeometry => !IsReviewMode;
    public bool CanAddOcrRegion => CanUsePreview && CanEditGeometry;
    public bool CanEditSelectedCharacterAdvance => CanEditGeometry && SelectedOverlay?.HasUnlockedSelectedCharacters == true;
    public bool IsReviewNavigating => _reviewNavigationCancellation is not null;
    public ObservableCollection<OverlayRegionViewModel> ReviewItems { get; } = [];
    public IReadOnlyList<ReviewFilterOption> ReviewFilterOptions { get; } =
    [
        new("未確認・要再確認", "Unreviewed / Needs review"),
        new("未確認", "Unreviewed"),
        new("要再確認", "Needs review"),
        new("すべてのステータス", "All statuses"),
    ];
    public int ReviewFilterIndex
    {
        get => _reviewFilterIndex;
        set
        {
            if (_refreshingLocalizedOptions) return;
            if (!Set(ref _reviewFilterIndex, Math.Clamp(value, 0, 3))) return;
            CancelReviewNavigation();
            RefreshReviewItems();
        }
    }

    public string ReviewSummary => LocalizationService.IsEnglish
        ? $"Page {SelectedPage?.PageNumber ?? 0}: {ReviewItems.Count} targets"
        : $"このページ（{SelectedPage?.PageNumber ?? 0}ページ）: 対象 {ReviewItems.Count}件";
    public bool HasNoReviewItems => ReviewItems.Count == 0;
    public OverlayRegionViewModel? SelectedReviewItem
    {
        get => SelectedOverlay is { } region && ReviewItems.Contains(region) ? region : null;
        set
        {
            // A status change can remove the selected row. Keep its editor open until navigation.
            if (_refreshingReviewItems || value is null || !ReviewItems.Contains(value)) return;
            CancelReviewNavigation();
            SelectReviewRegion(value);
        }
    }

    public AsyncCommand PreviousReviewCommand { get; private set; } = null!;
    public AsyncCommand NextReviewCommand { get; private set; } = null!;
    public AsyncCommand VerifyAndNextCommand { get; private set; } = null!;
    public RelayCommand CancelReviewNavigationCommand { get; private set; } = null!;

    private void InitializeReview()
    {
        PreviousReviewCommand = new AsyncCommand(() => NavigateReviewAsync(-1), CanNavigateReview);
        NextReviewCommand = new AsyncCommand(() => NavigateReviewAsync(1), CanNavigateReview);
        VerifyAndNextCommand = new AsyncCommand(VerifyAndNextAsync,
            () => CanNavigateReview() && SelectedOverlay is { IsDeleted: false } && _selectedOverlays.Count == 1);
        CancelReviewNavigationCommand = new RelayCommand(CancelReviewNavigation, () => IsReviewNavigating);
        OverlayItems.CollectionChanged += (_, _) => RefreshReviewItems();
    }

    private bool CanNavigateReview() => IsReviewMode && HasDocument && !IsOpeningDocument &&
        !IsPreviewLoading && !IsReviewNavigating;

    private RelayCommand GeometryCommand(Action execute, Func<bool> canExecute) =>
        new(execute, () => CanEditGeometry && canExecute());

    private void NotifyReviewState()
    {
        OnPropertyChanged(nameof(IsReviewNavigating));
        OnPropertyChanged(nameof(SelectedReviewItem));
        PreviousReviewCommand?.RaiseCanExecuteChanged();
        NextReviewCommand?.RaiseCanExecuteChanged();
        VerifyAndNextCommand?.RaiseCanExecuteChanged();
        CancelReviewNavigationCommand?.RaiseCanExecuteChanged();
    }

    private void OnEditorModeChanged()
    {
        CancelReviewNavigation();
        if (IsReviewMode)
        {
            IsAddOcrRegionMode = false;
            EditUnitIndex = (int)OcrEditUnit.Line;
        }
        OnPropertyChanged(nameof(IsReviewMode));
        OnPropertyChanged(nameof(CanEditGeometry));
        OnPropertyChanged(nameof(CanAddOcrRegion));
        OnPropertyChanged(nameof(IsSelectedGeometryEditable));
        OnPropertyChanged(nameof(CanEditSelectedCharacterAdvance));
        RaiseMultiSelectionCommands();
        RaiseCharacterAdvanceCommands();
        MoveReadingEarlierCommand.RaiseCanExecuteChanged();
        MoveReadingLaterCommand.RaiseCanExecuteChanged();
        RecalculateReadingOrderCommand.RaiseCanExecuteChanged();
        DeleteOcrRegionsCommand.RaiseCanExecuteChanged();
        ToggleAddOcrRegionModeCommand.RaiseCanExecuteChanged();
        RefreshReviewItems();
    }

    private bool MatchesReviewFilter(OverlayRegionViewModel region) => !region.IsDeleted && ReviewFilterIndex switch
    {
        0 => region.ReviewStatus is ReviewStatus.Unreviewed or ReviewStatus.NeedsReview,
        1 => region.ReviewStatus == ReviewStatus.Unreviewed,
        2 => region.ReviewStatus == ReviewStatus.NeedsReview,
        _ => true,
    };

    private void RefreshReviewItems()
    {
        if (_refreshingReviewItems) return;
        _refreshingReviewItems = true;
        try
        {
            var targets = IsReviewMode
                ? OverlayItems.Where(MatchesReviewFilter).OrderBy(region => region.ReadingOrder).ToArray()
                : [];
            if (!ReviewItems.SequenceEqual(targets))
            {
                ReviewItems.Clear();
                foreach (var region in targets) ReviewItems.Add(region);
            }
            OnPropertyChanged(nameof(ReviewSummary));
            OnPropertyChanged(nameof(HasNoReviewItems));
            NotifyReviewState();
        }
        finally { _refreshingReviewItems = false; }
    }

    private void SelectReviewRegion(OverlayRegionViewModel region)
    {
        ClearOcrSearchHighlight();
        SetOverlaySelection([region], region);
        OcrSearchSelectionRequested?.Invoke(this, region);
        NotifyReviewState();
    }

    internal async Task VerifyAndNextAsync()
    {
        if (!CanNavigateReview() || SelectedOverlay is not { IsDeleted: false } region || _selectedOverlays.Count != 1) return;
        ApplyRegionEdit("OCR領域を確認済みに変更", [region], () => region.ReviewStatus = ReviewStatus.Verified);
        OnPropertyChanged(nameof(SelectedReviewStatus));
        RefreshReviewItems();
        await NavigateReviewAsync(1);
    }

    // Load only the pages needed to find the next target, rather than scanning a large PDF on mode entry.
    internal async Task NavigateReviewAsync(int direction)
    {
        if (!CanNavigateReview() || direction is not (-1 or 1) || SelectedPage is null) return;
        using var cancellation = new CancellationTokenSource();
        _reviewNavigationCancellation = cancellation;
        NotifyReviewState();
        var token = cancellation.Token;
        var startPage = SelectedPage.PageNumber;
        var anchor = SelectedOverlay;
        try
        {
            for (var pageNumber = startPage; pageNumber >= 1 && pageNumber <= PageItems.Count; pageNumber += direction)
            {
                token.ThrowIfCancellationRequested();
                StatusMessage = LocalizationService.IsEnglish
                    ? $"Looking for review targets on page {pageNumber}..."
                    : $"{pageNumber}ページの確認対象を検索しています...";
                var regions = (await EnsurePageOverlaysLoadedForSearchAsync(pageNumber, token))
                    .OrderBy(region => region.ReadingOrder).ToList();
                token.ThrowIfCancellationRequested();
                var anchorIndex = pageNumber == startPage && anchor is not null ? regions.IndexOf(anchor) : -1;
                var index = anchorIndex >= 0 ? anchorIndex + direction : direction > 0 ? 0 : regions.Count - 1;
                for (; index >= 0 && index < regions.Count; index += direction)
                {
                    var target = regions[index];
                    if (!MatchesReviewFilter(target)) continue;
                    if (SelectedPage?.PageNumber != pageNumber)
                    {
                        SetCurrentPageWithoutRendering(PageItems[pageNumber - 1]);
                        await RenderPageAsync(pageNumber, populatePageList: false);
                    }
                    token.ThrowIfCancellationRequested();
                    if (!OverlayItems.Contains(target)) return; // Page loading failed or was replaced.
                    SelectReviewRegion(target);
                    StatusMessage = LocalizationService.IsEnglish
                        ? $"Page {pageNumber}: {Abbreviate(target.Text)}"
                        : $"{pageNumber}ページの確認対象: 「{Abbreviate(target.Text)}」";
                    return;
                }
            }
            StatusMessage = LocalizationService.IsEnglish
                ? (direction > 0 ? "No further review targets. Change the filter or go back to revisit reviewed regions." : "No earlier review targets.")
                : (direction > 0 ? "これより後に確認対象はありません。確認済みの領域は絞り込みを変更して確認できます。" : "これより前に確認対象はありません。");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested) await ShowErrorAsync("確認対象を読み込めませんでした。", exception);
        }
        finally
        {
            if (ReferenceEquals(_reviewNavigationCancellation, cancellation)) _reviewNavigationCancellation = null;
            NotifyReviewState();
        }
    }

    internal void CancelReviewNavigation()
    {
        if (_reviewNavigationCancellation is { IsCancellationRequested: false } cancellation)
        {
            cancellation.Cancel();
            StatusMessage = LocalizationService.IsEnglish ? "Review search canceled." : "確認対象の検索を中止しました。";
        }
        NotifyReviewState();
    }
}
