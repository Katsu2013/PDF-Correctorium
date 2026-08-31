using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Documents;
using System.ComponentModel;
using System.Diagnostics;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core;
using PdfCorrectorium.Infrastructure;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

/// <summary>
/// メイン編集画面のマウス、キーボード、ズーム、スクロール、およびダイアログ操作を仲介します。
/// </summary>
/// <remarks>
/// 文書状態と編集処理は<see cref="MainWindowViewModel"/>へ委譲し、このクラスはWPF固有の
/// 入力座標変換とビュー要素の制御に限定します。
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>倍率を自動再計算するときの基準を表します。</summary>
    private enum PreviewFitMode { None, Width, Height, Page }
    /// <summary>現在ドラッグ移動またはリサイズしているOCR領域です。</summary>
    private OverlayRegionViewModel? _draggedRegion;
    /// <summary>ドラッグ開始時のプレビュー座標です。</summary>
    private Point _dragStart;
    /// <summary>ドラッグ開始時点の領域左端です。</summary>
    private double _dragStartLeft;
    /// <summary>ドラッグ開始時点の領域上端です。</summary>
    private double _dragStartTop;
    /// <summary>ウィンドウサイズ変更後も維持する自動フィット方式です。</summary>
    private PreviewFitMode _previewFitMode = PreviewFitMode.Width;
    /// <summary>自動倍率反映中の再帰的なスクロール・倍率更新を防ぎます。</summary>
    private bool _isApplyingFit;
    /// <summary>倍率変更前後で選択位置を画面中央へ保つための直前倍率です。</summary>
    private double _lastZoomFactor = 1;
    /// <summary>囲み選択のドラッグ中であることを示します。</summary>
    private bool _isMarqueeSelecting;
    /// <summary>囲み矩形を新規OCR領域として確定する操作中であることを示します。</summary>
    private bool _isAddingOcrRegion;
    /// <summary>囲み選択または領域追加を開始したプレビュー座標です。</summary>
    private Point _marqueeStart;
    /// <summary>保存確認が完了し、次のClosingイベントで終了を許可するフラグです。</summary>
    private bool _allowClose;
    /// <summary>終了確認ダイアログの重複表示を防ぐフラグです。</summary>
    private bool _closePromptActive;
    /// <summary>しおりのドラッグ開始位置です。</summary>
    private Point _bookmarkDragStart;
    /// <summary>ドラッグによる移動候補になっているしおりです。</summary>
    private BookmarkNodeViewModel? _bookmarkDragSource;
    /// <summary>現在しおり名を編集中の項目です。</summary>
    private BookmarkNodeViewModel? _bookmarkEditingNode;
    /// <summary>Escapeで編集を取り消すために保持する編集前のしおり名です。</summary>
    private string? _bookmarkOriginalTitle;
    /// <summary>しおりのドラッグ先を示している装飾レイヤーです。</summary>
    private AdornerLayer? _bookmarkDropAdornerLayer;
    /// <summary>しおりのドラッグ先へ重ねている挿入位置表示です。</summary>
    private BookmarkDropAdorner? _bookmarkDropAdorner;
    /// <summary>現在挿入位置表示を重ねているツリー項目です。</summary>
    private TreeViewItem? _bookmarkDropTargetItem;
    /// <summary>現在表示している前・子・後のドロップ候補です。</summary>
    private BookmarkDropPosition? _bookmarkDropPosition;
    /// <summary>ページ一覧でドラッグを開始した画面座標です。</summary>
    private Point _pageDragStart;
    /// <summary>ページ一覧でドラッグ元として押されたページです。</summary>
    private PdfPageItem? _pageDragSource;
    /// <summary>ページの挿入位置線を表示している装飾レイヤーです。</summary>
    private AdornerLayer? _pageDropAdornerLayer;
    /// <summary>ページのドロップ先を示す挿入位置線です。</summary>
    private BookmarkDropAdorner? _pageDropAdorner;
    /// <summary>現在ドラッグ先になっているページ項目です。</summary>
    private ListBoxItem? _pageDropTargetItem;
    /// <summary>ドロップ先ページの後へ挿入する場合は true、前なら false です。</summary>
    private bool? _pageDropAfter;
    /// <summary>Ctrl+F/Ctrl+Hで再利用する、モデルレスの透明テキスト検索・置換画面です。</summary>
    private OcrSearchReplaceWindow? _ocrSearchReplaceWindow;
    /// <summary>文書全体の文字数外れ値とキーワード幅を分析するモデルレス画面です。</summary>
    private OcrQualityAnalysisWindow? _ocrQualityAnalysisWindow;
    /// <summary>ページ番号入力欄の確定処理がフォーカス移動で再入することを防ぎます。</summary>
    private bool _isCommittingToolbarPageNumber;
    /// <summary>設定、ログ、自動保存の配置先です。</summary>
    private readonly ApplicationPaths _applicationPaths;
    /// <summary>編集を妨げずに復旧データを定期保存するタイマーです。</summary>
    private readonly DispatcherTimer _autoSaveTimer;

    // Allows the smoke test to exercise the unsaved-changes close path without
    // displaying a modal dialog. Normal application runs leave this unset.
    internal Func<MessageBoxResult>? ClosePromptOverride { get; set; }

    public MainWindow()
    {
        var paths = ApplicationPathResolver.Resolve(AppContext.BaseDirectory);
        ApplicationPathResolver.EnsureDirectories(paths);
        var startupSettings = new ApplicationSettingsService(paths).Load();
        LocalizationService.SetLanguage(startupSettings.UiLanguage);

        InitializeComponent();
        Title = ApplicationBuildInfo.WindowTitle;
        _applicationPaths = paths;
        var projectPackages = new ProjectPackageService
        {
            BackupGenerationCount = startupSettings.BackupGenerationCount,
        };
        DataContext = new MainWindowViewModel(
            projectPackages,
            new PdfPreviewService(),
            new PdfExportService(),
            new NdlOcrCompanionService(),
            new DiagnosticLog(paths.LogDirectory),
            paths,
            Close);
        _lastZoomFactor = ViewModel.ZoomFactor;
        ViewModel.CommitPendingInputs = CommitPendingEditorBindings;
        PreviewKeyDown += (_, _) => ViewModel.NotifyUserActivity();
        PreviewMouseDown += (_, _) => ViewModel.NotifyUserActivity();
        PreviewMouseMove += (_, _) => ViewModel.NotifyUserActivity();
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        ViewModel.OcrSearchSelectionRequested += ViewModel_OnOcrSearchSelectionRequested;
        _autoSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _autoSaveTimer.Tick += async (_, _) => await ViewModel.AutoSaveIfDueAsync();
        _autoSaveTimer.Start();
        Closed += (_, _) => _autoSaveTimer.Stop();
        LocalizationService.Apply(this);
    }

    /// <summary>画面の編集状態とコマンドを提供するDataContextです。</summary>
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

    /// <summary>検索結果のOCR領域を、プレビュー上でも単一選択として強調します。</summary>
    private void ViewModel_OnOcrSearchSelectionRequested(object? sender, OverlayRegionViewModel region)
    {
        OverlayCanvas.SelectedItems.Clear();
        OverlayCanvas.SelectedItems.Add(region);
        if (!ViewModel.IsReviewMode) OverlayCanvas.Focus();
        if (ViewModel.IsReviewMode)
        {
            // Selecting a target should reveal it even at high zoom.
            var x = (region.Left + region.Width / 2) * ViewModel.ZoomFactor;
            var y = (region.Top + region.Height / 2) * ViewModel.ZoomFactor;
            PreviewScrollViewer.ScrollToHorizontalOffset(Math.Max(0, x - PreviewScrollViewer.ViewportWidth / 2));
            PreviewScrollViewer.ScrollToVerticalOffset(Math.Max(0, y - PreviewScrollViewer.ViewportHeight / 2));
        }
    }

    private void ReviewNavigationButton_OnClick(object sender, RoutedEventArgs e) => CommitPendingEditorBindings();

    private void ReviewTargetList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CommitPendingEditorBindings();

    /// <summary>ページ一覧の複数選択をページ操作コマンドへ通知します。</summary>
    private void PageList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SetPageSelection(PageList.SelectedItems.OfType<PdfPageItem>());

    /// <summary>ページ一覧にフォーカスがあるとき、Delete キーで選択ページを削除します。</summary>
    private void PageList_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || !ViewModel.DeletePagesCommand.CanExecute(null)) return;
        ViewModel.DeletePagesCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>右クリックしたページを操作対象にし、既存の複数選択は必要な場合だけ維持します。</summary>
    private void PageList_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item is null) return;
        if (!item.IsSelected)
        {
            PageList.SelectedItems.Clear();
            item.IsSelected = true;
        }
        item.Focus();
    }

    /// <summary>ページ並べ替えのドラッグ開始候補を記録します。</summary>
    private void PageList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pageDragStart = e.GetPosition(PageList);
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        _pageDragSource = item?.DataContext as PdfPageItem;
    }

    /// <summary>マウスが規定距離を越えたとき、選択ページ群の並べ替えを開始します。</summary>
    private void PageList_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pageDragSource is null || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(PageList);
        if (Math.Abs(current.X - _pageDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _pageDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var source = _pageDragSource;
        _pageDragSource = null;
        try
        {
            DragDrop.DoDragDrop(PageList, new DataObject(typeof(PdfPageItem), source), DragDropEffects.Move);
        }
        finally
        {
            ClearPageDropIndicator();
        }
    }

    /// <summary>ドラッグ位置に応じ、移動先ページの前後へ細い挿入線を表示します。</summary>
    private void PageList_OnDragOver(object sender, DragEventArgs e)
    {
        var source = e.Data.GetData(typeof(PdfPageItem)) as PdfPageItem;
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        var target = item?.DataContext as PdfPageItem;
        var selected = PageList.SelectedItems.OfType<PdfPageItem>().Select(page => page.PageNumber).ToHashSet();
        var canDrop = source is not null && target is not null && !selected.Contains(target.PageNumber);
        e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
        if (canDrop)
        {
            var after = e.GetPosition(item!).Y >= item!.ActualHeight / 2d;
            UpdatePageDropIndicator(item, after);
        }
        else
        {
            ClearPageDropIndicator();
        }
        e.Handled = true;
    }

    /// <summary>ページ一覧の外へ出たときに挿入位置線を消します。</summary>
    private void PageList_OnDragLeave(object sender, DragEventArgs e)
    {
        var point = e.GetPosition(PageList);
        if (point.X < 0 || point.Y < 0 || point.X > PageList.ActualWidth || point.Y > PageList.ActualHeight)
            ClearPageDropIndicator();
    }

    /// <summary>選択ページ群を、挿入位置線で示した場所へ元の順序を保って移動します。</summary>
    private async void PageList_OnDrop(object sender, DragEventArgs e)
    {
        var item = _pageDropTargetItem ?? FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        var target = item?.DataContext as PdfPageItem;
        var after = _pageDropAfter ?? (item is not null && e.GetPosition(item).Y >= item.ActualHeight / 2d);
        ClearPageDropIndicator();
        if (target is null) return;

        var selected = PageList.SelectedItems.OfType<PdfPageItem>().Select(page => page.PageNumber).ToHashSet();
        if (selected.Count == 0 || selected.Contains(target.PageNumber)) return;
        var remaining = ViewModel.PageItems.Where(page => !selected.Contains(page.PageNumber)).ToList();
        var insertionIndex = remaining.FindIndex(page => page.PageNumber == target.PageNumber);
        if (insertionIndex < 0) return;
        if (after) insertionIndex++;
        await ViewModel.ReorderSelectedPagesAsync(insertionIndex);
        e.Handled = true;
    }

    /// <summary>ページ項目の上端または下端へ並べ替え先の線を表示します。</summary>
    private void UpdatePageDropIndicator(ListBoxItem item, bool after)
    {
        if (ReferenceEquals(_pageDropTargetItem, item) && _pageDropAfter == after) return;
        ClearPageDropIndicator();
        var layer = AdornerLayer.GetAdornerLayer(item);
        if (layer is null) return;
        _pageDropAdorner = new BookmarkDropAdorner(
            item,
            after ? BookmarkDropPosition.After : BookmarkDropPosition.Before);
        _pageDropAdornerLayer = layer;
        _pageDropTargetItem = item;
        _pageDropAfter = after;
        layer.Add(_pageDropAdorner);
    }

    /// <summary>ページ一覧に表示中の並べ替え先の線を消します。</summary>
    private void ClearPageDropIndicator()
    {
        if (_pageDropAdornerLayer is not null && _pageDropAdorner is not null)
            _pageDropAdornerLayer.Remove(_pageDropAdorner);
        _pageDropAdornerLayer = null;
        _pageDropAdorner = null;
        _pageDropTargetItem = null;
        _pageDropAfter = null;
    }

    private void BookmarkTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_bookmarkEditingNode is not null && !ReferenceEquals(_bookmarkEditingNode, e.NewValue))
            EndBookmarkEdit();
        ViewModel.SelectedBookmark = e.NewValue as BookmarkNodeViewModel;
        if (ViewModel.SelectedBookmark is not null && ViewModel.GoToBookmarkCommand.CanExecute(null))
            ViewModel.GoToBookmarkCommand.Execute(null);
    }

    /// <summary>ページ番号入力欄へ入ったとき、既存値を選択して即座に置換できるようにします。</summary>
    private void ToolbarPageNumberBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, textBox.SelectAll);
    }

    /// <summary>Enterでページ移動を確定し、Escapeで現在ページ番号へ戻します。</summary>
    private void ToolbarPageNumberBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (e.Key == Key.Enter)
        {
            CommitToolbarPageNumber(textBox);
            OverlayCanvas.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            RestoreToolbarPageNumber(textBox);
            OverlayCanvas.Focus();
            e.Handled = true;
        }
    }

    /// <summary>マウス操作などでページ番号入力欄を離れた場合にも入力値を確定します。</summary>
    private void ToolbarPageNumberBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox) CommitToolbarPageNumber(textBox);
    }

    /// <summary>1始まりのページ番号を検証して移動し、不正な値は現在値へ戻します。</summary>
    private void CommitToolbarPageNumber(TextBox textBox)
    {
        if (_isCommittingToolbarPageNumber) return;
        _isCommittingToolbarPageNumber = true;
        try
        {
            if (!int.TryParse(textBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var pageNumber)
                || !ViewModel.GoToPageNumber(pageNumber))
            {
                RestoreToolbarPageNumber(textBox);
            }
        }
        finally
        {
            _isCommittingToolbarPageNumber = false;
        }
    }

    /// <summary>ページ番号入力欄を現在表示中のページへ同期します。</summary>
    private void RestoreToolbarPageNumber(TextBox textBox) =>
        textBox.Text = ViewModel.SelectedPage?.PageNumber.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

    private void BookmarkTreeItem_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null) return;
        if (sender is not TreeViewItem { DataContext: BookmarkNodeViewModel node }) return;
        BeginBookmarkEdit(node);
        e.Handled = true;
    }

    /// <summary>右クリックした項目を先に選択し、メニュー操作の対象を明確にします。</summary>
    private void BookmarkTreeItem_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem item) return;
        item.IsSelected = true;
        item.Focus();
    }

    /// <summary>しおり一覧でF2が押されたとき、選択中のタイトル編集を開始します。</summary>
    private void BookmarkTree_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || ViewModel.SelectedBookmark is not { } node) return;
        BeginBookmarkEdit(node);
        e.Handled = true;
    }

    /// <summary>右クリックメニューから選択したしおりのタイトル編集を開始します。</summary>
    private void EditBookmarkMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedBookmark is not { } node) return;
        BeginBookmarkEdit(node);
    }

    /// <summary>右クリックしたしおりのページへ移動します。</summary>
    private void GoToBookmarkMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedBookmark is null) return;
        if (ViewModel.GoToBookmarkCommand.CanExecute(null)) ViewModel.GoToBookmarkCommand.Execute(null);
    }

    /// <summary>右クリックしたしおりの子階層へ新しいしおりを追加します。</summary>
    private void AddChildBookmarkMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedBookmark is null) return;
        if (ViewModel.AddChildBookmarkCommand.CanExecute(null)) ViewModel.AddChildBookmarkCommand.Execute(null);
    }

    /// <summary>右クリックしたしおりを削除します。</summary>
    private void DeleteBookmarkMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedBookmark is null) return;
        if (ViewModel.DeleteBookmarkCommand.CanExecute(null)) ViewModel.DeleteBookmarkCommand.Execute(null);
    }

    /// <summary>しおり名を表示状態からインライン編集状態へ切り替えます。</summary>
    private void BeginBookmarkEdit(BookmarkNodeViewModel node)
    {
        if (_bookmarkEditingNode is not null && !ReferenceEquals(_bookmarkEditingNode, node)) EndBookmarkEdit();
        _bookmarkEditingNode = node;
        _bookmarkOriginalTitle = node.Title;
        node.IsEditing = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            var editor = FindVisualDescendants<TextBox>(BookmarkTree)
                .FirstOrDefault(textBox => Equals(textBox.Tag, "BookmarkTitleEditor") && ReferenceEquals(textBox.DataContext, node));
            if (editor is null) return;
            editor.Focus();
            editor.SelectAll();
        });
    }

    /// <summary>しおり名編集欄のEnterで確定し、Escapeで編集前へ戻します。</summary>
    private void BookmarkTitleEditor_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox editor || editor.DataContext is not BookmarkNodeViewModel node) return;
        if (e.Key == Key.Enter)
        {
            editor.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            EndBookmarkEdit();
            BookmarkTree.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (ReferenceEquals(node, _bookmarkEditingNode) && _bookmarkOriginalTitle is not null)
                node.Title = _bookmarkOriginalTitle;
            EndBookmarkEdit();
            BookmarkTree.Focus();
            e.Handled = true;
        }
    }

    /// <summary>編集欄からフォーカスが離れたとき、入力中のしおり名を確定します。</summary>
    private void BookmarkTitleEditor_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox editor) editor.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        EndBookmarkEdit();
    }

    /// <summary>現在のしおり名編集を終了して通常の選択表示へ戻します。</summary>
    private void EndBookmarkEdit()
    {
        var node = _bookmarkEditingNode;
        _bookmarkEditingNode = null;
        _bookmarkOriginalTitle = null;
        if (node is not null) node.IsEditing = false;
    }

    private void BookmarkTree_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _bookmarkDragStart = e.GetPosition(BookmarkTree);
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        _bookmarkDragSource = FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is null
            ? item?.DataContext as BookmarkNodeViewModel
            : null;
    }

    private void BookmarkTree_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_bookmarkDragSource is null || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(BookmarkTree);
        if (Math.Abs(current.X - _bookmarkDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _bookmarkDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var source = _bookmarkDragSource;
        _bookmarkDragSource = null;
        try
        {
            DragDrop.DoDragDrop(BookmarkTree, source, DragDropEffects.Move);
        }
        finally
        {
            ClearBookmarkDropIndicator();
        }
    }

    private void BookmarkTree_OnDragOver(object sender, DragEventArgs e)
    {
        var source = e.Data.GetData(typeof(BookmarkNodeViewModel)) as BookmarkNodeViewModel;
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        var target = item?.DataContext as BookmarkNodeViewModel;
        var canDrop = source is not null && target is not null && !ReferenceEquals(source, target);
        e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
        if (canDrop)
        {
            var position = GetBookmarkDropPosition(item!, e);
            UpdateBookmarkDropIndicator(item!, position);
        }
        else
        {
            ClearBookmarkDropIndicator();
        }
        e.Handled = true;
    }

    /// <summary>ツリー外へドラッグが離れた場合に挿入位置表示を消去します。</summary>
    private void BookmarkTree_OnDragLeave(object sender, DragEventArgs e)
    {
        var point = e.GetPosition(BookmarkTree);
        if (point.X < 0 || point.Y < 0 || point.X > BookmarkTree.ActualWidth || point.Y > BookmarkTree.ActualHeight)
            ClearBookmarkDropIndicator();
    }

    private void BookmarkTree_OnDrop(object sender, DragEventArgs e)
    {
        ClearBookmarkDropIndicator();
        var source = e.Data.GetData(typeof(BookmarkNodeViewModel)) as BookmarkNodeViewModel;
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        var target = item?.DataContext as BookmarkNodeViewModel;
        if (source is null || target is null) return;

        var position = GetBookmarkDropPosition(item!, e);
        ViewModel.MoveBookmarkByDrop(source, target, position);
        e.Handled = true;
    }

    /// <summary>しおり見出しへ前・子・後のドロップ候補を視覚表示します。</summary>
    private void UpdateBookmarkDropIndicator(TreeViewItem item, BookmarkDropPosition position)
    {
        if (ReferenceEquals(_bookmarkDropTargetItem, item) && _bookmarkDropPosition == position) return;
        ClearBookmarkDropIndicator();
        var header = item.Template.FindName("PART_Header", item) as UIElement ?? item;
        var layer = AdornerLayer.GetAdornerLayer(header);
        if (layer is null) return;
        _bookmarkDropAdorner = new BookmarkDropAdorner(header, position);
        _bookmarkDropAdornerLayer = layer;
        _bookmarkDropTargetItem = item;
        _bookmarkDropPosition = position;
        layer.Add(_bookmarkDropAdorner);
    }

    /// <summary>現在表示しているしおりのドロップ候補を消去します。</summary>
    private void ClearBookmarkDropIndicator()
    {
        if (_bookmarkDropAdornerLayer is not null && _bookmarkDropAdorner is not null)
            _bookmarkDropAdornerLayer.Remove(_bookmarkDropAdorner);
        _bookmarkDropAdornerLayer = null;
        _bookmarkDropAdorner = null;
        _bookmarkDropTargetItem = null;
        _bookmarkDropPosition = null;
    }

    private static BookmarkDropPosition GetBookmarkDropPosition(TreeViewItem target, DragEventArgs e)
    {
        var header = target.Template.FindName("PART_Header", target) as FrameworkElement ?? target;
        var point = e.GetPosition(header);
        var ratio = header.ActualHeight <= 0 ? 0.5 : point.Y / header.ActualHeight;
        return ratio < 0.28 ? BookmarkDropPosition.Before
            : ratio > 0.72 ? BookmarkDropPosition.After
            : BookmarkDropPosition.AsChild;
    }

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel.IsPdfExporting)
        {
            // PDFium and the project snapshot are owned by the export operation until the
            // worker has finished. Suppress editing and navigation shortcuts meanwhile.
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F6 && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            MoveKeyboardPane(Keyboard.Modifiers == ModifierKeys.Shift);
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.F or Key.H)
        {
            ShowOcrSearchReplaceWindow(focusReplacement: e.Key == Key.H);
            e.Handled = true;
            return;
        }
        if (TryExecuteConfiguredShortcut(e))
        {
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && ViewModel.IsAddOcrRegionMode)
        {
            CancelPointerRectangle();
            ViewModel.IsAddOcrRegionMode = false;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Delete &&
            ViewModel.DeleteOcrRegionsCommand.CanExecute(null) &&
            OverlayCanvas.IsKeyboardFocusWithin)
        {
            ViewModel.DeleteOcrRegionsCommand.Execute(null);
            OverlayCanvas.UnselectAll();
            e.Handled = true;
            return;
        }
        if (!ViewModel.HasOverlaySelection || !OverlayCanvas.IsKeyboardFocusWithin) return;
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;

        // Keep keyboard movement visually consistent at every zoom level.
        // Arrow = 1 screen pixel; Shift+Arrow = 10 screen pixels.
        var screenStep = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10d : 1d;
        var logicalStep = screenStep / Math.Max(0.25, ViewModel.ZoomFactor);
        var (horizontal, vertical) = e.Key switch
        {
            Key.Left => (-logicalStep, 0d),
            Key.Right => (logicalStep, 0d),
            Key.Up => (0d, -logicalStep),
            Key.Down => (0d, logicalStep),
            _ => (0d, 0d),
        };

        if (horizontal == 0 && vertical == 0) return;
        ViewModel.NudgeSelection(horizontal, vertical);
        e.Handled = true;
    }

    private bool TryExecuteConfiguredShortcut(KeyEventArgs e)
    {
        // Let a focused dropdown consume its native Alt+Up/Down open/close gestures.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers == ModifierKeys.Alt && key is Key.Up or Key.Down &&
            e.OriginalSource is DependencyObject source && FindAncestor<ComboBox>(source) is not null) return false;
        var settings = ViewModel.CurrentApplicationSettings;
        var mappings = new (string Shortcut, ICommand Command)[]
        {
            (settings.PreviousCharacterShortcut, ViewModel.PreviousCharacterCommand),
            (settings.NextCharacterShortcut, ViewModel.NextCharacterCommand),
            (settings.DecreaseCharacterAdvanceShortcut, ViewModel.DecreaseCharacterAdvanceCommand),
            (settings.IncreaseCharacterAdvanceShortcut, ViewModel.IncreaseCharacterAdvanceCommand),
            (settings.EstimateCharacterAdvancesShortcut, ViewModel.EstimateCharacterAdvancesCommand),
            (settings.EstimateCharacterSuffixAdvancesShortcut, ViewModel.EstimateCharacterSuffixAdvancesCommand),
            (settings.EqualizeCharacterAdvancesShortcut, ViewModel.EqualizeCharacterAdvancesCommand),
            (settings.RestoreOriginalCharacterAdvancesShortcut, ViewModel.RestoreOriginalCharacterAdvancesCommand),
        };
        foreach (var (shortcut, command) in mappings)
        {
            if (EditorShortcutService.IsReserved(shortcut) || !EditorShortcutService.Matches(e, shortcut) || !command.CanExecute(null)) continue;
            CommitPendingEditorBindings();
            command.Execute(null);
            OverlayCanvas.Focus();
            return true;
        }
        return false;
    }

    /// <summary>F6/Shift+F6で主要な作業領域を順方向／逆方向へ移動します。</summary>
    internal void MoveKeyboardPane(bool reverse)
    {
        FrameworkElement[] panes = [MainToolbarPanel, KeyboardNavigationTabs, OverlayCanvas, KeyboardPropertiesPane, StatusZoomComboBox];
        var index = Array.FindIndex(panes, pane => pane.IsKeyboardFocusWithin);
        if (index < 0 && reverse) index = 0;
        for (var step = 1; step <= panes.Length; step++)
        {
            var candidate = panes[(index + (reverse ? -step : step) + panes.Length * 2) % panes.Length];
            if (!candidate.IsVisible || !candidate.IsEnabled) continue;
            if ((candidate.Focusable && candidate.Focus()) || candidate.MoveFocus(new TraversalRequest(FocusNavigationDirection.First))) return;
        }
    }

    private void RegionBody_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.IsReviewNavigating) ViewModel.CancelReviewNavigation();
        CommitPendingEditorBindings();
        if (sender is not Border { DataContext: OverlayRegionViewModel region } border) return;
        var horizontalOffset = PreviewScrollViewer.HorizontalOffset;
        var verticalOffset = PreviewScrollViewer.VerticalOffset;
        if (ViewModel.IsCharacterEditMode)
        {
            if (FindAncestor<ListBoxItem>(border) is { } characterContainer &&
                !characterContainer.IsSelected)
            {
                OverlayCanvas.UnselectAll();
                characterContainer.IsSelected = true;
            }
            ViewModel.SelectedOverlay = region;
            SynchronizeOverlaySelection(region);
            OverlayCanvas.Focus();
            var characterPoint = e.GetPosition(border);
            ViewModel.SelectCharacterAt(
                region,
                characterPoint.X,
                characterPoint.Y,
                toggle: Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
                extendRange: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            RestorePreviewScrollPosition(horizontalOffset, verticalOffset);
            e.Handled = true;
            return;
        }
        if (FindAncestor<ListBoxItem>(border) is { } container)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                container.IsSelected = !container.IsSelected;
                SynchronizeOverlaySelection(container.IsSelected ? region : null);
                OverlayCanvas.Focus();
                RestorePreviewScrollPosition(horizontalOffset, verticalOffset);
                e.Handled = true;
                return;
            }
            var editUnitRegions = ViewModel.ResolveEditUnitSelection(region);
            if (!container.IsSelected || editUnitRegions.Count > 1 || OverlayCanvas.SelectedItems.Count != 1)
            {
                OverlayCanvas.UnselectAll();
                foreach (var editRegion in editUnitRegions)
                    if (OverlayCanvas.ItemContainerGenerator.ContainerFromItem(editRegion) is ListBoxItem item)
                        item.IsSelected = true;
            }
        }
        ViewModel.SelectedOverlay = region;
        SynchronizeOverlaySelection(region);
        OverlayCanvas.Focus();
        var localPoint = e.GetPosition(border);
        ViewModel.SelectCharacterAt(region, localPoint.X, localPoint.Y);
        RestorePreviewScrollPosition(horizontalOffset, verticalOffset);
        if (!ViewModel.CanEditGeometry || ViewModel.EditUnit != OcrEditUnit.Line || region.IsGeometryLocked)
        {
            e.Handled = true;
            return;
        }
        ViewModel.BeginOverlayEdit(region);
        _draggedRegion = region;
        _dragStart = e.GetPosition(OverlayCanvas);
        _dragStartLeft = region.Left;
        _dragStartTop = region.Top;
        border.CaptureMouse();
        e.Handled = true;
    }

    private void RegionBody_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!ViewModel.CanEditGeometry || _draggedRegion is null || _draggedRegion.IsGeometryLocked || e.LeftButton != MouseButtonState.Pressed || sender is not Border border || !border.IsMouseCaptured) return;
        var point = e.GetPosition(OverlayCanvas);
        _draggedRegion.Left = Math.Clamp(_dragStartLeft + point.X - _dragStart.X, 0, Math.Max(0, ViewModel.PreviewPixelWidth - _draggedRegion.Width));
        _draggedRegion.Top = Math.Clamp(_dragStartTop + point.Y - _dragStart.Y, 0, Math.Max(0, ViewModel.PreviewPixelHeight - _draggedRegion.Height));
    }

    private void RegionBody_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedRegion is null) return;
        if (sender is Border border && border.IsMouseCaptured) border.ReleaseMouseCapture();
        ViewModel.EndOverlayEdit("OCR領域を移動");
        _draggedRegion = null;
        e.Handled = true;
    }

    private void ResizeThumb_OnDragStarted(object sender, DragStartedEventArgs e)
    {
        if (!ViewModel.CanEditGeometry) return;
        if (sender is not Thumb { DataContext: OverlayRegionViewModel region } || region.IsGeometryLocked) return;
        ViewModel.SelectedOverlay = region;
        ViewModel.BeginOverlayEdit(region);
    }

    private void ResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!ViewModel.CanEditGeometry) return;
        if (sender is not Thumb { DataContext: OverlayRegionViewModel region } thumb || region.IsGeometryLocked || thumb.Tag is not string direction) return;
        var zoom = Math.Max(0.25, ViewModel.ZoomFactor);
        var horizontal = e.HorizontalChange / zoom;
        var vertical = e.VerticalChange / zoom;
        EditorInteractionMath.Resize(region, direction, horizontal, vertical, ViewModel.PreviewPixelWidth, ViewModel.PreviewPixelHeight);
    }

    private void ResizeThumb_OnDragCompleted(object sender, DragCompletedEventArgs e) =>
        ViewModel.EndOverlayEdit("OCR領域のサイズを変更");

    private void CharacterAdvanceThumb_OnDragStarted(object sender, DragStartedEventArgs e)
    {
        if (!ViewModel.CanEditGeometry) return;
        if (sender is not Thumb { DataContext: OverlayRegionViewModel region } || !region.HasUnlockedSelectedCharacters) return;
        ViewModel.SelectedOverlay = region;
        ViewModel.BeginOverlayEdit(region);
    }

    private void CharacterAdvanceThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!ViewModel.CanEditGeometry) return;
        if (sender is not Thumb { DataContext: OverlayRegionViewModel region } || !region.HasUnlockedSelectedCharacters) return;
        var zoom = Math.Max(0.25, ViewModel.ZoomFactor);
        var change = (region.IsVertical ? e.VerticalChange : e.HorizontalChange) / zoom;
        region.AdjustSelectedCharacterAdvances(change);
    }

    private void CharacterAdvanceThumb_OnDragCompleted(object sender, DragCompletedEventArgs e) =>
        ViewModel.EndOverlayEdit("文字単位の幅を変更");

    private void RotationThumb_OnDragStarted(object sender, DragStartedEventArgs e)
    {
        if (!ViewModel.CanEditGeometry) return;
        if (sender is not Thumb { DataContext: OverlayRegionViewModel region } || region.IsGeometryLocked) return;
        ViewModel.SelectedOverlay = region;
        ViewModel.BeginOverlayEdit(region);
    }

    private void RotationThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!ViewModel.CanEditGeometry) return;
        if (sender is not Thumb { DataContext: OverlayRegionViewModel region } || region.IsGeometryLocked) return;
        var sensitivity = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 0.5;
        var angle = region.RotationDegrees + e.HorizontalChange * sensitivity / Math.Max(0.25, ViewModel.ZoomFactor);
        region.RotationDegrees = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            ? Math.Round(angle / 15d) * 15d
            : angle;
    }

    private void RotationThumb_OnDragCompleted(object sender, DragCompletedEventArgs e) =>
        ViewModel.EndOverlayEdit("OCR領域を回転");

    private void RotatePresetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditGeometry) return;
        if (ViewModel.SelectedOverlay is not { } region ||
            region.IsGeometryLocked ||
            sender is not Button { Tag: string value } ||
            !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var angle)) return;

        ViewModel.BeginOverlayEdit(region);
        region.RotationDegrees = angle;
        ViewModel.EndOverlayEdit("OCR領域を回転");
    }

    private void NumericTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (e.Key is Key.Tab or Key.Enter)
        {
            CommitNumericTextBox(textBox);
            // Tab must continue through WPF's normal focus traversal.  Enter is
            // treated as an explicit commit but keeps the current field selected.
            if (e.Key == Key.Enter) e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Up or Key.Down)) return;
        if (!AdjustNumericTextBox(textBox, e.Key == Key.Up ? 1 : -1)) return;
        e.Handled = true;
    }

    /// <summary>
    /// Commits a numeric property when keyboard focus leaves its editor.
    /// This gives mouse focus changes and Tab navigation the same behavior.
    /// </summary>
    private void NumericTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox) CommitNumericTextBox(textBox);
    }

    /// <summary>Pushes the displayed numeric value into its two-way binding.</summary>
    private static void CommitNumericTextBox(TextBox textBox) =>
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

    private void NumericTextBox_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not TextBox textBox || e.Delta == 0) return;
        if (!AdjustNumericTextBox(textBox, e.Delta > 0 ? 1 : -1)) return;
        textBox.Focus();
        e.Handled = true;
    }

    internal static bool AdjustNumericTextBox(TextBox textBox, int direction)
    {
        if (!double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var current) &&
            !double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out current))
            return false;

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            ? 0.1
            : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var next = current + Math.Sign(direction) * step;
        textBox.Text = next.ToString("0.###", CultureInfo.CurrentCulture);
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        textBox.SelectAll();
        return true;
    }

    private void DocumentPropertiesMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasDocument) return;
        var dialog = new DocumentPropertiesWindow(ViewModel) { Owner = this };
        if (dialog.ShowDialog() == true &&
            dialog.ResultViewerSettings is not null &&
            dialog.ResultDocumentMetadata is not null &&
            dialog.ResultOutputPdfVersion is not null &&
            dialog.ResultDocumentLanguage is not null)
            ViewModel.UpdateDocumentProperties(
                dialog.ResultViewerSettings,
                dialog.ResultDocumentMetadata,
                dialog.ResultOutputPdfVersion.Value,
                dialog.ResultDocumentLanguage);
    }

    /// <summary>現在のプロジェクトコンテナを検証し、診断結果を表示します。</summary>
    private async void ValidateProjectMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasDocument) return;
        var result = await ViewModel.ValidateCurrentProjectAsync();
        if (result is null) return;
        var details = result.Issues.Count == 0
            ? "問題は見つかりませんでした。"
            : string.Join(Environment.NewLine, result.Issues.Select(issue => $"・{issue.Message}"));
        MessageBox.Show(details, "プロジェクト検証", MessageBoxButton.OK,
            result.IsValid ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    /// <summary>自動保存または直近の世代バックアップからプロジェクトを復旧します。</summary>
    private async void RestoreProjectMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanRestoreProjectBackup) return;
        if (MessageBox.Show("現在のプロジェクトを直近の正常なバックアップへ戻しますか？\n現在のファイルは復旧前コピーとして保全されます。",
                "バックアップから復旧", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        var restored = await ViewModel.RestoreLatestProjectBackupAsync();
        MessageBox.Show(restored ? "バックアップから復旧しました。" : "利用できる正常なバックアップが見つかりませんでした。",
            "バックアップから復旧", MessageBoxButton.OK,
            restored ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    /// <summary>障害調査に使用するログ保存先をエクスプローラーで開きます。</summary>
    private void OpenLogFolderMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", _applicationPaths.LogDirectory) { UseShellExecute = true });

    /// <summary>アプリケーション名と実行バージョンを表示します。</summary>
    private void AboutMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(ApplicationBuildInfo.AboutText, LocalizationService.IsEnglish ? "About" : "バージョン情報",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>検索欄を選択した状態で透明テキスト検索画面を開きます。</summary>
    private void FindOcrTextMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ShowOcrSearchReplaceWindow(focusReplacement: false);

    /// <summary>置換欄へ移動できる状態で透明テキスト検索画面を開きます。</summary>
    private void ReplaceOcrTextMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ShowOcrSearchReplaceWindow(focusReplacement: true);

    /// <summary>検索・置換画面を1つだけ表示し、既に開いている場合は手前へ戻します。</summary>
    private void ShowOcrSearchReplaceWindow(bool focusReplacement)
    {
        if (!ViewModel.HasDocument) return;
        if (_ocrSearchReplaceWindow is { IsLoaded: true })
        {
            _ocrSearchReplaceWindow.ActivateSearch(focusReplacement);
            return;
        }
        _ocrSearchReplaceWindow = new OcrSearchReplaceWindow(ViewModel, focusReplacement) { Owner = this };
        _ocrSearchReplaceWindow.Closed += (_, _) => _ocrSearchReplaceWindow = null;
        _ocrSearchReplaceWindow.Show();
    }

    /// <summary>OCR品質分析画面を1つだけ表示し、既に開いている場合は手前へ戻します。</summary>
    private void OcrQualityAnalysisMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasDocument) return;
        if (_ocrQualityAnalysisWindow is { IsLoaded: true })
        {
            _ocrQualityAnalysisWindow.Activate();
            return;
        }
        _ocrQualityAnalysisWindow = new OcrQualityAnalysisWindow(ViewModel) { Owner = this };
        _ocrQualityAnalysisWindow.Closed += (_, _) => _ocrQualityAnalysisWindow = null;
        _ocrQualityAnalysisWindow.Show();
    }

    private async void ApplicationSettingsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ReloadRecentFiles();
        var dialog = new ApplicationSettingsWindow(
            CaptureWorkspaceSettings(),
            ViewModel.StorageModeText,
            ViewModel.SettingsFilePath,
            ViewModel.RecentFileCount)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true) return;
        await ApplySettingsDialogAsync(dialog);
    }

    internal async Task<bool> ApplySettingsDialogAsync(ApplicationSettingsWindow dialog)
    {
        var previousLanguage = ViewModel.CurrentApplicationSettings.UiLanguage;
        if (!await ViewModel.ApplyApplicationSettingsAsync(dialog.ResultSettings)) return false;
        RestoreWorkspaceWidthBindings();
        var cleared = !dialog.ClearRecentFilesRequested || await ViewModel.ClearRecentFilesAsync();
        if (!string.Equals(previousLanguage, dialog.ResultSettings.UiLanguage, StringComparison.OrdinalIgnoreCase))
        {
            LocalizationService.SetLanguage(dialog.ResultSettings.UiLanguage);
            LocalizationService.Apply(this);
            ViewModel.RefreshLocalization();
        }
        return cleared;
    }

    private void FileMenu_OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource)) ViewModel.ReloadRecentFiles();
    }

    /// <summary>スプリッターで変更した実際の幅も、設定画面の下書きへ引き継ぎます。</summary>
    internal ApplicationSettings CaptureWorkspaceSettings()
    {
        var settings = ViewModel.CurrentApplicationSettings;
        return (settings with
        {
            PageListWidth = settings.ShowPageListPanel && WorkspaceLayoutGrid.ColumnDefinitions[0].ActualWidth > 0
                ? WorkspaceLayoutGrid.ColumnDefinitions[0].ActualWidth : settings.PageListWidth,
            PropertiesPanelWidth = settings.ShowPropertiesPanel && WorkspaceLayoutGrid.ColumnDefinitions[4].ActualWidth > 0
                ? WorkspaceLayoutGrid.ColumnDefinitions[4].ActualWidth : settings.PropertiesPanelWidth,
        }).Normalize();
    }

    internal void RestoreWorkspaceWidthBindings()
    {
        // GridSplitter can replace a one-way Width binding; reattach it when applying settings.
        WorkspaceLayoutGrid.ColumnDefinitions[0].SetBinding(ColumnDefinition.WidthProperty,
            new System.Windows.Data.Binding(nameof(MainWindowViewModel.PageListColumnWidth)));
        WorkspaceLayoutGrid.ColumnDefinitions[4].SetBinding(ColumnDefinition.WidthProperty,
            new System.Windows.Data.Binding(nameof(MainWindowViewModel.PropertiesPanelColumnWidth)));
    }

    private void OverlayCanvas_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var primary = e.AddedItems.Cast<OverlayRegionViewModel>().LastOrDefault();
        SynchronizeOverlaySelection(primary);
    }

    private void OverlayCanvas_OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        // Selecting an overlay must not reposition the PDF viewport. The user may
        // intentionally be editing a region that is only partially visible.
        e.Handled = true;
    }

    private void RestorePreviewScrollPosition(double horizontalOffset, double verticalOffset)
    {
        PreviewScrollViewer.ScrollToHorizontalOffset(horizontalOffset);
        PreviewScrollViewer.ScrollToVerticalOffset(verticalOffset);
        Dispatcher.BeginInvoke(() =>
        {
            PreviewScrollViewer.ScrollToHorizontalOffset(horizontalOffset);
            PreviewScrollViewer.ScrollToVerticalOffset(verticalOffset);
        }, DispatcherPriority.Loaded);
    }

    private void OverlayCanvas_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Preview events run before ListBox selection changes. Commit the
        // property editor while its binding still points at the old region.
        CommitPendingEditorBindings();
        if (ViewModel.IsAddOcrRegionMode)
        {
            _isAddingOcrRegion = true;
            _marqueeStart = e.GetPosition(OverlayCanvas);
            Canvas.SetLeft(MarqueeSelectionRectangle, _marqueeStart.X);
            Canvas.SetTop(MarqueeSelectionRectangle, _marqueeStart.Y);
            MarqueeSelectionRectangle.Width = 0;
            MarqueeSelectionRectangle.Height = 0;
            MarqueeSelectionRectangle.Background = new SolidColorBrush(Color.FromArgb(45, 0, 170, 90));
            MarqueeSelectionRectangle.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 145, 75));
            MarqueeSelectionRectangle.Visibility = Visibility.Visible;
            OverlayCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not null) return;
        _isMarqueeSelecting = true;
        _marqueeStart = e.GetPosition(OverlayCanvas);
        Canvas.SetLeft(MarqueeSelectionRectangle, _marqueeStart.X);
        Canvas.SetTop(MarqueeSelectionRectangle, _marqueeStart.Y);
        MarqueeSelectionRectangle.Width = 0;
        MarqueeSelectionRectangle.Height = 0;
        MarqueeSelectionRectangle.Visibility = Visibility.Visible;
        OverlayCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void OverlayCanvas_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if ((!_isMarqueeSelecting && !_isAddingOcrRegion) || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(OverlayCanvas);
        var left = Math.Min(_marqueeStart.X, current.X);
        var top = Math.Min(_marqueeStart.Y, current.Y);
        Canvas.SetLeft(MarqueeSelectionRectangle, left);
        Canvas.SetTop(MarqueeSelectionRectangle, top);
        MarqueeSelectionRectangle.Width = Math.Abs(current.X - _marqueeStart.X);
        MarqueeSelectionRectangle.Height = Math.Abs(current.Y - _marqueeStart.Y);
        e.Handled = true;
    }

    private void OverlayCanvas_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isAddingOcrRegion)
        {
            _isAddingOcrRegion = false;
            if (OverlayCanvas.IsMouseCaptured) OverlayCanvas.ReleaseMouseCapture();
            var addEnd = e.GetPosition(OverlayCanvas);
            var bounds = new Rect(
                Math.Min(_marqueeStart.X, addEnd.X),
                Math.Min(_marqueeStart.Y, addEnd.Y),
                Math.Abs(addEnd.X - _marqueeStart.X),
                Math.Abs(addEnd.Y - _marqueeStart.Y));
            ResetPointerRectangle();
            if (bounds.Width >= 8 && bounds.Height >= 8 &&
                ViewModel.AddManualOcrRegion(bounds) is { } added)
            {
                OverlayCanvas.UpdateLayout();
                OverlayCanvas.UnselectAll();
                if (OverlayCanvas.ItemContainerGenerator.ContainerFromItem(added) is ListBoxItem item)
                    item.IsSelected = true;
                SynchronizeOverlaySelection(added);
                Dispatcher.BeginInvoke(() =>
                {
                    SelectedLineTextBox.Focus();
                    SelectedLineTextBox.SelectAll();
                }, DispatcherPriority.Loaded);
            }
            else
            {
                ViewModel.IsAddOcrRegionMode = false;
                ViewModel.StatusMessageForInteraction("追加範囲が小さすぎるため、透明テキスト領域を作成しませんでした。");
            }
            e.Handled = true;
            return;
        }
        if (!_isMarqueeSelecting) return;
        _isMarqueeSelecting = false;
        if (OverlayCanvas.IsMouseCaptured) OverlayCanvas.ReleaseMouseCapture();
        var current = e.GetPosition(OverlayCanvas);
        var selection = new Rect(
            Math.Min(_marqueeStart.X, current.X),
            Math.Min(_marqueeStart.Y, current.Y),
            Math.Abs(current.X - _marqueeStart.X),
            Math.Abs(current.Y - _marqueeStart.Y));
        MarqueeSelectionRectangle.Visibility = Visibility.Collapsed;

        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) OverlayCanvas.UnselectAll();
        OverlayRegionViewModel? primary = null;
        if (selection.Width >= 2 && selection.Height >= 2)
        {
            foreach (var region in ViewModel.OverlayItems)
            {
                var bounds = new Rect(region.Left, region.Top, region.Width, region.Height);
                if (!selection.IntersectsWith(bounds)) continue;
                if (OverlayCanvas.ItemContainerGenerator.ContainerFromItem(region) is ListBoxItem item)
                    item.IsSelected = true;
                primary = region;
            }
        }
        SynchronizeOverlaySelection(primary);
        OverlayCanvas.Focus();
        e.Handled = true;
    }

    private void CancelPointerRectangle()
    {
        _isAddingOcrRegion = false;
        _isMarqueeSelecting = false;
        if (OverlayCanvas.IsMouseCaptured) OverlayCanvas.ReleaseMouseCapture();
        ResetPointerRectangle();
    }

    private void ResetPointerRectangle()
    {
        MarqueeSelectionRectangle.Visibility = Visibility.Collapsed;
        MarqueeSelectionRectangle.Background = new SolidColorBrush(Color.FromArgb(40, 120, 183, 255));
        MarqueeSelectionRectangle.BorderBrush = new SolidColorBrush(Color.FromRgb(24, 117, 209));
    }

    private void SynchronizeOverlaySelection(OverlayRegionViewModel? primary)
    {
        var selected = OverlayCanvas.SelectedItems.Cast<OverlayRegionViewModel>().ToArray();
        ViewModel.SetOverlaySelection(selected, primary);
    }

    private void CommitPendingEditorBindings()
    {
        if (Keyboard.FocusedElement is TextBox textBox)
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private void SelectedLineTextBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ViewModel.SelectedOverlay is { } region)
            ViewModel.BeginOverlayEdit(region);
    }

    private void SelectedLineTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        ViewModel.EndOverlayEdit("OCR文字列を変更");

    /// <summary>文字セルの置換または分割を、1回のUndo操作として記録し始めます。</summary>
    private void SelectedCharacterTextBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ViewModel.SelectedOverlay is { } region)
            ViewModel.BeginOverlayEdit(region);
    }

    /// <summary>保留中の文字入力を反映し、文字セルの置換または分割履歴を確定します。</summary>
    private void SelectedCharacterTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        ViewModel.EndOverlayEdit("OCR文字を置換・分割");
    }

    private void PreviewPageHost_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) =>
        CommitPendingEditorBindings();

    private void RegionBody_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        CommitPendingEditorBindings();
        if (sender is not Border { DataContext: OverlayRegionViewModel region } border) return;
        SelectRegionForContextMenu(region, border);
    }

    private void SelectRegionForContextMenu(OverlayRegionViewModel region, DependencyObject source)
    {
        if (FindAncestor<ListBoxItem>(source) is not { } container) return;
        if (!container.IsSelected)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                OverlayCanvas.UnselectAll();
            container.IsSelected = true;
        }
        ViewModel.SelectedOverlay = region;
        SynchronizeOverlaySelection(region);
    }

    private void RegionContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        CommitPendingEditorBindings();
        if (sender is not ContextMenu { DataContext: OverlayRegionViewModel region } menu) return;
        SelectRegionForContextMenu(region, menu.PlacementTarget);
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (Equals(item.Tag, "SetAlignmentReference"))
                item.IsEnabled = ViewModel.SetAlignmentReferenceCommand.CanExecute(null);
            else if (Equals(item.Tag, "SplitSelectedCharacter"))
                item.IsEnabled = region.HasSingleCharacterSelection;
            else if (Equals(item.Tag, "SplitOcrRegion"))
                item.IsEnabled = ViewModel.SplitRegionAtSelectedCharacterCommand.CanExecute(null);
            else if (Equals(item.Tag, "MergeOcrRegions"))
                item.IsEnabled = ViewModel.MergeSelectedRegionsCommand.CanExecute(null);
        }
    }

    private static OverlayRegionViewModel? GetContextRegion(object sender) =>
        sender is MenuItem { DataContext: OverlayRegionViewModel region } ? region : null;

    private void RegionEditTextMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetContextRegion(sender) is not { } region) return;
        ViewModel.EditUnitIndex = (int)OcrEditUnit.Line;
        ViewModel.SelectedOverlay = region;
        Dispatcher.BeginInvoke(() =>
        {
            SelectedLineTextBox.Focus();
            SelectedLineTextBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    /// <summary>
    /// 行編集へ切り替えず、現在選択中の1文字セルを複数文字へ分割できる入力欄へフォーカスを移します。
    /// </summary>
    private void RegionSplitCharacterMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetContextRegion(sender) is not { HasSingleCharacterSelection: true } region) return;
        ViewModel.EditUnitIndex = (int)OcrEditUnit.Character;
        ViewModel.SelectedOverlay = region;
        Dispatcher.BeginInvoke(() =>
        {
            SelectedCharacterTextBox.Focus();
            SelectedCharacterTextBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    /// <summary>
    /// 選択中の文字を後半領域の先頭として、現在のOCR領域を2つへ分割します。
    /// </summary>
    private void RegionSplitOcrRegionMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.SplitRegionAtSelectedCharacterCommand.CanExecute(null)) return;
        ViewModel.SplitRegionAtSelectedCharacterCommand.Execute(null);
        SynchronizeSelectionAfterStructuralEdit();
    }

    /// <summary>
    /// 同一行上で隣接する、選択中の2つのOCR領域を1つへ結合します。
    /// </summary>
    private void RegionMergeOcrRegionsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.MergeSelectedRegionsCommand.CanExecute(null)) return;
        ViewModel.MergeSelectedRegionsCommand.Execute(null);
        SynchronizeSelectionAfterStructuralEdit();
    }

    /// <summary>
    /// 分割・結合によってItemsSourceが変化した後、ViewModelが選んだ新しい領域を
    /// ListBox側の選択状態へ反映します。プレビュー位置を変えないため自動スクロールは行いません。
    /// </summary>
    private void SynchronizeSelectionAfterStructuralEdit()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var selected = ViewModel.SelectedOverlay;
            OverlayCanvas.UnselectAll();
            if (selected is not null &&
                OverlayCanvas.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem item)
                item.IsSelected = true;
            SynchronizeOverlaySelection(selected);
            OverlayCanvas.Focus();
        }, DispatcherPriority.Loaded);
    }

    private void RegionWritingModeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string mode } ||
            !Enum.TryParse<WritingMode>(mode, out var writingMode)) return;
        ViewModel.SelectedWritingMode = writingMode;
    }

    private void RegionReviewStatusMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string status } ||
            !Enum.TryParse<ReviewStatus>(status, out var reviewStatus)) return;
        ViewModel.SelectedReviewStatus = reviewStatus;
    }

    private void RegionEstimateCharacterAdvancesMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ExecuteLineContextCommand(ViewModel.EstimateCharacterAdvancesCommand);

    /// <summary>
    /// 利用者が指定したページへ前処理を適用してから、文字幅の自動調整を一括実行します。
    /// </summary>
    private async void BatchCharacterAdjustmentMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasDocument || !ViewModel.CanEditGeometry) return;
        var pageCount = ViewModel.BatchCharacterAdjustmentPageCount;
        if (pageCount <= 0)
        {
            MessageBox.Show(
                LocalizationService.IsEnglish
                    ? "Open a PDF before running batch auto-adjustment."
                    : "一括自動調整を実行する前にPDFを開いてください。",
                LocalizationService.IsEnglish ? "Batch Auto-adjust" : "一括自動調整",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var currentPageNumber = ViewModel.SelectedPage?.PageNumber ?? 1;
        var dialog = new BatchCharacterAdjustmentWindow(
            pageCount,
            currentPageNumber,
            ViewModel.SelectedPageNumbers)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true) return;
        var targetPageNumbers = dialog.TargetPageNumbers;
        if (targetPageNumbers.Count == 0) return;

        var previousCursor = Mouse.OverrideCursor;
        using var cancellationSource = new CancellationTokenSource();
        var progressWindow = new BatchCharacterAdjustmentProgressWindow(targetPageNumbers.Count)
        {
            Owner = this,
        };
        progressWindow.CancellationRequested += (_, _) => cancellationSource.Cancel();
        var progress = new Progress<BatchCharacterAdjustmentProgress>(progressWindow.Report);
        try
        {
            progressWindow.Show();
            IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
            await ViewModel.RunBatchCharacterAdjustmentAsync(
                targetPageNumbers,
                dialog.Options,
                progress,
                cancellationSource.Token);
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show(
                LocalizationService.IsEnglish
                    ? "Batch auto-adjustment was canceled; no partial changes were kept."
                    : "一括自動調整を中止しました。途中までの変更は残していません。",
                LocalizationService.IsEnglish ? "Batch Auto-adjust" : "一括自動調整",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                LocalizationService.IsEnglish
                    ? $"Batch auto-adjustment failed. No partial changes were kept.\n\n{ex.Message}"
                    : $"一括自動調整に失敗しました。途中までの変更は残していません。\n\n{ex.Message}",
                LocalizationService.IsEnglish ? "Batch Auto-adjust" : "一括自動調整",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            progressWindow.CompleteAndClose();
            Mouse.OverrideCursor = previousCursor;
            IsEnabled = true;
            Activate();
        }
    }

    /// <summary>
    /// 現在ページで選択した定型領域を他ページから検索し、確認された候補へ編集内容を反映します。
    /// </summary>
    private async void RepeatedRegionPropagationMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasDocument || !ViewModel.CanEditGeometry) return;
        if (ViewModel.SelectedPage is null || ViewModel.SelectedOverlays.Count == 0)
        {
            MessageBox.Show(
                LocalizationService.IsEnglish
                    ? "Select the edited header, footer, or other repeated OCR regions first."
                    : "先に、反映元にする編集済みのヘッダー／フッター等を選択してください。\n分割後の領域は複数選択できます。",
                LocalizationService.IsEnglish ? "Propagate Repeated Region" : "定型領域の一括反映",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var optionsWindow = new RepeatedRegionPropagationOptionsWindow(
            ViewModel.PageItems.Count,
            ViewModel.SelectedPage.PageNumber,
            ViewModel.SelectedPageNumbers)
        {
            Owner = this,
        };
        if (optionsWindow.ShowDialog() != true || optionsWindow.Options is not { } options) return;

        var previousCursor = Mouse.OverrideCursor;
        using var cancellationSource = new CancellationTokenSource();
        var targetPageCount = options.TargetPageNumbers.Count(page => page != ViewModel.SelectedPage.PageNumber);
        var progressWindow = new RepeatedRegionSearchProgressWindow(targetPageCount) { Owner = this };
        progressWindow.CancellationRequested += (_, _) => cancellationSource.Cancel();
        var progress = new Progress<RepeatedRegionSearchProgress>(progressWindow.Report);
        try
        {
            progressWindow.Show();
            IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
            ViewModel.StatusMessageForInteraction("他ページから同じヘッダー／フッターを検索しています...");
            var candidates = await ViewModel.FindRepeatedRegionCandidatesAsync(
                options, progress, cancellationSource.Token);
            progressWindow.CompleteAndClose();
            IsEnabled = true;
            Mouse.OverrideCursor = previousCursor;
            if (candidates.Count == 0)
            {
                MessageBox.Show(
                    LocalizationService.IsEnglish
                        ? "No matching repeated regions were found. Try lowering the similarity threshold."
                        : "条件に合う定型領域は見つかりませんでした。\n一致度を少し下げると検出できる場合があります。",
                    LocalizationService.IsEnglish ? "Propagate Repeated Region" : "定型領域の一括反映",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var candidateWindow = new RepeatedRegionCandidateWindow(candidates, options, ViewModel) { Owner = this };
            if (candidateWindow.ShowDialog() != true)
            {
                if (candidateWindow.NavigationCandidate is { } navigationCandidate)
                    await ViewModel.NavigateToRepeatedRegionCandidateAsync(navigationCandidate);
                return;
            }

            IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
            var applied = ViewModel.ApplyRepeatedRegionPropagation(options, candidateWindow.Candidates.ToArray());
            MessageBox.Show(
                LocalizationService.IsEnglish
                    ? $"Applied the edit to {applied} page(s). Locked regions were left unchanged."
                    : $"{applied}ページへ編集内容を反映しました。\n固定されている領域・文字は変更していません。",
                LocalizationService.IsEnglish ? "Propagate Repeated Region" : "定型領域の一括反映",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show(
                LocalizationService.IsEnglish
                    ? "The repeated-region search was canceled. No edits were applied."
                    : "定型領域の検索を中止しました。編集内容は反映していません。",
                LocalizationService.IsEnglish ? "Propagate Repeated Region" : "定型領域の一括反映",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            // XAML の読み込み失敗では Message だけに根本原因が現れないため、
            // 内部例外と発生位置を含む完全な例外情報を診断ログへ残す。
            await ViewModel.WriteDiagnosticErrorAsync("repeated-region.propagation.failed", ex);
            var rootCause = ex.GetBaseException().Message;
            MessageBox.Show(
                LocalizationService.IsEnglish
                    ? $"Could not propagate the repeated-region edit. No partial changes were kept.\n\n{rootCause}"
                    : $"定型領域の一括反映に失敗しました。途中までの変更は残していません。\n\n{rootCause}",
                LocalizationService.IsEnglish ? "Propagate Repeated Region" : "定型領域の一括反映",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            progressWindow.CompleteAndClose();
            Mouse.OverrideCursor = previousCursor;
            IsEnabled = true;
            Activate();
        }
    }

    private void RegionEqualizeCharacterAdvancesMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ExecuteLineContextCommand(ViewModel.EqualizeCharacterAdvancesCommand);

    private void RegionRestoreCharacterAdvancesMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ExecuteLineContextCommand(ViewModel.RestoreOriginalCharacterAdvancesCommand);

    private void RegionToggleGeometryLockMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ToggleGeometryLockCommand.CanExecute(null))
            ViewModel.ToggleGeometryLockCommand.Execute(null);
    }

    private void RegionToggleSelectedCharacterLockMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.EditUnitIndex = (int)OcrEditUnit.Character;
        if (ViewModel.ToggleSelectedCharacterLockCommand.CanExecute(null))
            ViewModel.ToggleSelectedCharacterLockCommand.Execute(null);
    }

    private void ExecuteLineContextCommand(ICommand command)
    {
        ViewModel.EditUnitIndex = (int)OcrEditUnit.Line;
        if (command.CanExecute(null))
            command.Execute(null);
    }

    private void RegionSetAlignmentReferenceMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SetAlignmentReferenceCommand.CanExecute(null))
            ViewModel.SetAlignmentReferenceCommand.Execute(null);
    }

    private void RegionDeleteMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.DeleteOcrRegionsCommand.CanExecute(null)) return;
        ViewModel.DeleteOcrRegionsCommand.Execute(null);
        OverlayCanvas.UnselectAll();
    }

    private void PreviewContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        CommitPendingEditorBindings();
        if (sender is not ContextMenu menu) return;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (Equals(item.Tag, "AddOcrRegion"))
            {
                item.IsChecked = ViewModel.IsAddOcrRegionMode;
                item.IsEnabled = ViewModel.CanAddOcrRegion;
            }
            else if (Equals(item.Tag, "ToggleOverlay"))
            {
                item.IsChecked = ViewModel.IsOcrOverlayVisible;
                item.IsEnabled = ViewModel.HasPreview;
            }
        }
    }

    private void PreviewAddOcrRegionMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsOcrOverlayVisible = true;
        if (ViewModel.ToggleAddOcrRegionModeCommand.CanExecute(null))
            ViewModel.ToggleAddOcrRegionModeCommand.Execute(null);
    }

    private void PreviewToggleOverlayMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        ViewModel.IsOcrOverlayVisible = !ViewModel.IsOcrOverlayVisible;

    private void PreviewClearSelectionMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        OverlayCanvas.UnselectAll();
        SynchronizeOverlaySelection(null);
        OverlayCanvas.Focus();
    }

    private void PreviewZoomInMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ZoomInCommand.CanExecute(null))
            ViewModel.ZoomInCommand.Execute(null);
    }

    private void PreviewZoomOutMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ZoomOutCommand.CanExecute(null))
            ViewModel.ZoomOutCommand.Execute(null);
    }

    private void PreviewFitWidthMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        FitWidthButton_OnClick(sender, e);

    private void PreviewFitHeightMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        FitHeightButton_OnClick(sender, e);

    private void PreviewFitPageMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        FitPageButton_OnClick(sender, e);

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }

    /// <summary>指定した要素の表示ツリーから、型が一致する子要素を再帰的に列挙します。</summary>
    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualDescendants<T>(child)) yield return descendant;
        }
    }

    private void PreviewScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        _previewFitMode = PreviewFitMode.None;
        var command = e.Delta > 0 ? ViewModel.ZoomInCommand : ViewModel.ZoomOutCommand;
        if (command.CanExecute(null)) command.Execute(null);
        e.Handled = true;
    }

    /// <summary>左右のスケールによらず、矢印キーは1%、PageUp/Downは10%ずつ倍率を変更します。</summary>
    private void StatusZoomSlider_OnChangeZoom(object sender, ExecutedRoutedEventArgs e)
    {
        if (ViewModel.CanUsePreview)
        {
            var change = e.Command == Slider.IncreaseSmall ? 1 :
                e.Command == Slider.DecreaseSmall ? -1 :
                e.Command == Slider.IncreaseLarge ? 10 : -10;
            _previewFitMode = PreviewFitMode.None;
            ViewModel.ZoomPercent += change;
        }
        e.Handled = true;
    }

    private void StatusZoomSlider_OnCanChangeZoom(object sender, CanExecuteRoutedEventArgs e)
    {
        var increase = e.Command == Slider.IncreaseSmall || e.Command == Slider.IncreaseLarge;
        e.CanExecute = ViewModel.CanUsePreview && (increase ? ViewModel.ZoomPercent < 400 : ViewModel.ZoomPercent > 25);
        e.Handled = true;
    }

    /// <summary>倍率一覧から数値またはページのフィット方法を適用します。</summary>
    private void StatusZoomComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: ComboBoxItem { Tag: string value } } comboBox) return;
        switch (value)
        {
            case "FitWidth":
                _previewFitMode = PreviewFitMode.Width;
                ApplyPreviewFit();
                break;
            case "FitHeight":
                _previewFitMode = PreviewFitMode.Height;
                ApplyPreviewFit();
                break;
            case "FitPage":
                _previewFitMode = PreviewFitMode.Page;
                ApplyPreviewFit();
                break;
            case "FitSelection":
                ApplySelectionFit();
                break;
            case var _ when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent):
                _previewFitMode = PreviewFitMode.None;
                ViewModel.ZoomPercent = Math.Clamp(percent, 25, 400);
                break;
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            comboBox.SelectedIndex = -1;
            RefreshZoomComboBoxText(comboBox);
        });
    }

    /// <summary>倍率入力欄のEnterで手動入力を確定し、Escapeで現在倍率へ戻します。</summary>
    private void StatusZoomComboBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBox comboBox) return;
        if (e.Key == Key.Enter)
        {
            ApplyZoomComboBoxText(comboBox);
            OverlayCanvas.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            RefreshZoomComboBoxText(comboBox);
            comboBox.IsDropDownOpen = false;
            OverlayCanvas.Focus();
            e.Handled = true;
        }
    }

    /// <summary>倍率入力欄からフォーカスが離れたとき、手動入力値を適用します。</summary>
    private void StatusZoomComboBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && !comboBox.IsDropDownOpen) ApplyZoomComboBoxText(comboBox);
    }

    /// <summary>「85」「85%」などの手動入力を解析し、許容倍率へ丸めて適用します。</summary>
    private void ApplyZoomComboBoxText(ComboBox comboBox)
    {
        var text = comboBox.Text.Trim().TrimEnd('%').Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var percent) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
        {
            _previewFitMode = PreviewFitMode.None;
            ViewModel.ZoomPercent = Math.Clamp(percent, 25, 400);
        }
        comboBox.SelectedIndex = -1;
        RefreshZoomComboBoxText(comboBox);
    }

    /// <summary>Textへの通常代入でZoomDisplayのバインドを解除せず、現在倍率へ表示を戻します。</summary>
    private void RefreshZoomComboBoxText(ComboBox comboBox) =>
        comboBox.SetCurrentValue(ComboBox.TextProperty, ViewModel.ZoomDisplay);

    private void ApplySelectionFit()
    {
        var bounds = ViewModel.GetSelectionBounds();
        if (bounds is null || bounds.Value.Width <= 0 || bounds.Value.Height <= 0) return;
        var availableWidth = Math.Max(1, PreviewScrollViewer.ViewportWidth - 24);
        var availableHeight = Math.Max(1, PreviewScrollViewer.ViewportHeight - 24);
        var zoom = Math.Min(availableWidth / bounds.Value.Width, availableHeight / bounds.Value.Height) * 100;
        _previewFitMode = PreviewFitMode.None;
        ViewModel.ZoomPercent = Math.Clamp(zoom, 25, 400);
    }

    private void FitWidthButton_OnClick(object sender, RoutedEventArgs e)
    {
        _previewFitMode = PreviewFitMode.Width;
        ApplyPreviewFit();
    }

    private void FitHeightButton_OnClick(object sender, RoutedEventArgs e)
    {
        _previewFitMode = PreviewFitMode.Height;
        ApplyPreviewFit();
    }

    private void FitPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        _previewFitMode = PreviewFitMode.Page;
        ApplyPreviewFit();
    }

    private void PreviewScrollViewer_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_previewFitMode != PreviewFitMode.None) ApplyPreviewFit();
    }

    private void PreviewImage_OnTargetUpdated(object sender, DataTransferEventArgs e)
    {
        if (_previewFitMode == PreviewFitMode.None || ViewModel.PreviewImage is null) return;
        Dispatcher.BeginInvoke(ApplyPreviewFit, DispatcherPriority.Loaded);
    }

    private void ApplyPreviewFit()
    {
        if (ViewModel.PreviewPixelWidth <= 0 || ViewModel.PreviewPixelHeight <= 0) return;
        var availableWidth = PreviewScrollViewer.ViewportWidth;
        if (!double.IsFinite(availableWidth) || availableWidth <= 20)
            availableWidth = PreviewScrollViewer.ActualWidth;
        var availableHeight = PreviewScrollViewer.ViewportHeight;
        if (!double.IsFinite(availableHeight) || availableHeight <= 20)
            availableHeight = PreviewScrollViewer.ActualHeight;
        if (!double.IsFinite(availableWidth) || availableWidth <= 20 ||
            !double.IsFinite(availableHeight) || availableHeight <= 20) return;
        var zoom = _previewFitMode switch
        {
            PreviewFitMode.Width => EditorInteractionMath.CalculateFitWidthPercent(availableWidth, ViewModel.PreviewPixelWidth),
            PreviewFitMode.Height => EditorInteractionMath.CalculateFitHeightPercent(availableHeight, ViewModel.PreviewPixelHeight),
            PreviewFitMode.Page => EditorInteractionMath.CalculateFitPagePercent(
                availableWidth,
                availableHeight,
                ViewModel.PreviewPixelWidth,
                ViewModel.PreviewPixelHeight),
            _ => ViewModel.ZoomPercent,
        };
        _isApplyingFit = true;
        try { ViewModel.ZoomPercent = zoom; }
        finally { _isApplyingFit = false; }
    }

    private void ViewModel_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedCharacterAdvance))
        {
            // A focused LostFocus binding may still contain the value that was
            // displayed before automatic character sizing.  Refresh the target so
            // a later Tab/mouse focus change cannot write that stale value back.
            Dispatcher.BeginInvoke(
                () => SelectedCharacterAdvanceTextBox
                    .GetBindingExpression(TextBox.TextProperty)?
                    .UpdateTarget(),
                DispatcherPriority.DataBind);
            return;
        }

        if (e.PropertyName != nameof(MainWindowViewModel.ZoomFactor)) return;
        var oldZoom = Math.Max(0.01, _lastZoomFactor);
        var newZoom = Math.Max(0.01, ViewModel.ZoomFactor);
        _lastZoomFactor = newZoom;
        if (!_isApplyingFit) _previewFitMode = PreviewFitMode.None;

        var viewportWidth = Math.Max(1, PreviewScrollViewer.ViewportWidth);
        var viewportHeight = Math.Max(1, PreviewScrollViewer.ViewportHeight);
        var selectionCenter = ViewModel.GetSelectionCenter();
        var anchorX = selectionCenter?.X ?? (PreviewScrollViewer.HorizontalOffset + viewportWidth / 2d) / oldZoom;
        var anchorY = selectionCenter?.Y ?? (PreviewScrollViewer.VerticalOffset + viewportHeight / 2d) / oldZoom;

        Dispatcher.BeginInvoke(() =>
        {
            PreviewScrollViewer.UpdateLayout();
            PreviewScrollViewer.ScrollToHorizontalOffset(EditorInteractionMath.CalculateCenteredScrollOffset(anchorX, newZoom, PreviewScrollViewer.ViewportWidth));
            PreviewScrollViewer.ScrollToVerticalOffset(EditorInteractionMath.CalculateCenteredScrollOffset(anchorY, newZoom, PreviewScrollViewer.ViewportHeight));
        }, DispatcherPriority.Loaded);
    }

    private async void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        if (ViewModel.IsPdfExporting)
        {
            e.Cancel = true;
            MessageBox.Show(
                "PDFを生成・検証しています。完成ファイルを保護するため、処理が完了してから終了してください。",
                "PDF Correctorium",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Commit LostFocus bindings (text, numeric properties, readings) before
        // deciding whether the project contains unsaved edits.
        CommitPendingEditorBindings();
        OverlayCanvas.Focus();
        if (!ViewModel.HasUnsavedChanges) return;
        if (_closePromptActive)
        {
            e.Cancel = true;
            return;
        }

        _closePromptActive = true;
        try
        {
            var choice = ClosePromptOverride?.Invoke() ?? MessageBox.Show(
                "プロジェクトに保存していない変更があります。終了する前に保存しますか？",
                "PDF Correctorium",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Yes);
            if (choice == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (choice == MessageBoxResult.No)
            {
                // This handler is already part of Window.Close(). Calling
                // Close() again here raises VerifyNotClosing. Let the active
                // close operation complete instead.
                _allowClose = true;
                e.Cancel = false;
                return;
            }

            // Saving is asynchronous. Cancel this close operation, then queue a
            // new one after the save succeeds and this handler has returned.
            e.Cancel = true;
            if (!await ViewModel.SaveBeforeCloseAsync()) return;
            _allowClose = true;
            _ = Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.ApplicationIdle);
        }
        finally
        {
            _closePromptActive = false;
        }
    }
}
