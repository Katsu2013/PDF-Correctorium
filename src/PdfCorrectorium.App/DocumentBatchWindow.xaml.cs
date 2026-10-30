using System.Globalization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

public partial class DocumentBatchWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly DocumentBatchKind _kind;
    private CancellationTokenSource? _workCancellation;
    private CancellationTokenSource? _previewCancellation;
    private bool _busy;
    private bool _closed;
    private bool _changingSelection;
    public IReadOnlyList<DocumentBatchCandidate> Candidates { get; private set; } = [];
    public Guid ProjectId { get; }

    public DocumentBatchWindow(MainWindowViewModel viewModel, DocumentBatchKind kind)
    {
        _viewModel = viewModel; _kind = kind;
        ProjectId = viewModel.BatchProjectId ?? throw new InvalidOperationException("文書が開かれていません。");
        InitializeComponent();
        Title = kind switch { DocumentBatchKind.AutomaticLinks => "ページリンクの自動作成", DocumentBatchKind.RepeatedRedaction => "墨消し範囲を複数ページへ適用", _ => "文字列から一括墨消し" };
        Explanation.Text = kind switch
        {
            DocumentBatchKind.AutomaticLinks => "目次・索引などの対象ページを指定してください。末尾の数字から候補を作成します。元ページ・リンク先を確認し、不要な候補のチェックを外してください。既存リンクとの重複は既定で除外します。",
            DocumentBatchKind.RepeatedRedaction => "選択した墨消し範囲を同じ位置へ繰り返します。元ページは除外し、サイズ・回転の違うページには適用しません。色と形状は元の範囲を維持します。",
            _ => "OCR編集後の文字とPDFの可視文字を検索します。既定は文字列の完全一致です。正規表現も選べます。画像のみで文字情報がないページは検索できません。結果がゼロでも機密情報がないことを保証しません。"
        };
        Explanation.Text += " 候補作成だけでは編集しません。適用後は一括Undoが可能です。墨消しの確定はPDF出力時です。";
        LinkInputs.Visibility = kind == DocumentBatchKind.AutomaticLinks ? Visibility.Visible : Visibility.Collapsed;
        TextInputs.Visibility = kind == DocumentBatchKind.TextRedaction ? Visibility.Visible : Visibility.Collapsed;
        DestinationButton.Visibility = kind == DocumentBatchKind.AutomaticLinks ? Visibility.Visible : Visibility.Collapsed;
        DestinationColumn.Visibility = kind == DocumentBatchKind.AutomaticLinks ? Visibility.Visible : Visibility.Collapsed;
        CandidatePageColumn.Header = kind == DocumentBatchKind.AutomaticLinks ? "元ページ" : "ページ";
        PagesBox.Text = kind == DocumentBatchKind.AutomaticLinks ? (viewModel.SelectedPage?.PageNumber ?? 1).ToString(CultureInfo.InvariantCulture) : $"1-{viewModel.PageItems.Count}";
        Closing += (_, _) => { _closed = true; _workCancellation?.Cancel(); _previewCancellation?.Cancel(); };
        LocalizationService.Apply(this);
    }

    private void Input_OnChanged(object sender, RoutedEventArgs e)
    {
        if (CandidateGrid is null) return;
        ClearCandidates();
    }

    internal async Task PreparePreviewForDiagnosticsAsync(IReadOnlyList<DocumentBatchCandidate> candidates)
    {
        ClearCandidates();
        Candidates = candidates;
        foreach (var item in Candidates) item.PropertyChanged += Candidate_OnChanged;
        CandidateGrid.ItemsSource = candidates;
        UpdateCount();
        CandidateGrid.SelectedIndex = candidates.Count > 0 ? 0 : -1;
        if (candidates.Count > 0) await PreviewAsync(candidates[0], destination: false);
    }
    private void ClearCandidates()
    {
        foreach (var item in Candidates) item.PropertyChanged -= Candidate_OnChanged;
        Candidates = [];
        CandidateGrid.ItemsSource = null;
        ApplyButton.IsEnabled = false;
        DestinationButton.IsEnabled = false;
        _previewCancellation?.Cancel();
        PreviewImage.Source = null; PreviewMarkers.Children.Clear();
        PreviewTitle.Text = "候補を選択するとプレビューを表示します。";
        StatusText.Text = "条件を設定して候補を作成してください。";
    }

    private async void Search_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!PageRangeParser.TryParse(PagesBox.Text, _viewModel.PageItems.Count, out var pages))
        { StatusText.Text = "ページ範囲が正しくありません。例：1-3, 8, 10-12"; return; }
        var offset = 0;
        if (_kind == DocumentBatchKind.AutomaticLinks && !int.TryParse(OffsetBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset))
        { StatusText.Text = "ページ番号の差を整数で入力してください。"; return; }
        ClearCandidates();
        _workCancellation = new();
        _busy = true; Inputs.IsEnabled = false; StopButton.Visibility = Visibility.Visible;
        try
        {
            Candidates = await _viewModel.BuildBatchCandidatesAsync(_kind, pages, SearchTextBox.Text, offset,
                MatchCase.IsChecked == true, new Progress<string>(value => { if (!_closed) StatusText.Text = value; }), _workCancellation.Token,
                UseRegularExpression.IsChecked == true);
            if (_closed) return;
            foreach (var item in Candidates) item.PropertyChanged += Candidate_OnChanged;
            CandidateGrid.ItemsSource = Candidates;
            UpdateCount();
            CandidateGrid.SelectedIndex = Candidates.Count > 0 ? 0 : -1;
        }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "候補作成を中止しました。編集内容は変更していません。"; }
        catch (Exception ex) { if (!_closed) StatusText.Text = "候補を作成できませんでした：" + ex.Message; }
        finally
        {
            _workCancellation.Dispose(); _workCancellation = null;
            _busy = false;
            if (!_closed) { Inputs.IsEnabled = true; StopButton.Visibility = Visibility.Collapsed; UpdateCount(keepMessage: true); }
        }
    }

    private void Candidate_OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_changingSelection) UpdateCount();
    }
    private void UpdateCount(bool keepMessage = false)
    {
        var included = Candidates.Count(item => item.IsIncluded && item.CanInclude);
        ApplyButton.IsEnabled = !_busy && included > 0;
        if (!keepMessage) StatusText.Text = $"候補: {Candidates.Count}件／適用: {included}件。注意欄とプレビューを確認してください。";
    }
    private void Stop_OnClick(object sender, RoutedEventArgs e) => _workCancellation?.Cancel();
    private void SelectAll_OnClick(object sender, RoutedEventArgs e) => SetAllIncluded(true);
    private void UnselectAll_OnClick(object sender, RoutedEventArgs e) => SetAllIncluded(false);
    private void SetAllIncluded(bool included)
    {
        // Refresh the count once, not once per row (quadratic for large result sets).
        _changingSelection = true;
        try { foreach (var item in Candidates) item.IsIncluded = included; }
        finally { _changingSelection = false; UpdateCount(); }
    }
    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        CandidateGrid.CommitEdit();
        if (!_busy && Candidates.Any(item => item.CanInclude && item.IsIncluded)) DialogResult = true;
    }
    private async void Candidate_OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CandidateGrid.SelectedItem is not DocumentBatchCandidate candidate) return;
        DestinationButton.IsEnabled = candidate.DestinationPage is not null;
        await PreviewAsync(candidate, destination: false);
    }
    private async void Destination_OnClick(object sender, RoutedEventArgs e)
    {
        if (CandidateGrid.SelectedItem is DocumentBatchCandidate candidate) await PreviewAsync(candidate, destination: true);
    }
    private async Task PreviewAsync(DocumentBatchCandidate candidate, bool destination)
    {
        _previewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource(); _previewCancellation = cancellation;
        PreviewImage.Source = null;
        PreviewMarkers.Children.Clear();
        PreviewTitle.Text = "プレビューを読み込んでいます…";
        try
        {
            var page = destination ? candidate.DestinationPage ?? candidate.PageNumber : candidate.PageNumber;
            var result = await _viewModel.RenderBatchPreviewAsync(page, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested || result is null) return;
            PreviewImage.Source = result.Image;
            PreviewSurface.Width = result.Image.PixelWidth; PreviewSurface.Height = result.Image.PixelHeight;
            PreviewMarkers.Children.Clear();
            PreviewTitle.Text = destination ? $"リンク先：{page}ページ（候補を選択すると元へ戻ります）" : $"元ページ：{page}";
            if (destination) return;
            var sx = result.Image.PixelWidth / candidate.PreviewWidth;
            var sy = result.Image.PixelHeight / candidate.PreviewHeight;
            if (candidate.PreviewPath.Count >= 3)
            {
                var polygon = new Polygon { Points = new(candidate.PreviewPath.Select(point => new Point(point.X * sx, point.Y * sy))),
                    Fill = new SolidColorBrush(Color.FromArgb(95, 211, 47, 47)), Stroke = Brushes.Red, StrokeThickness = 2 };
                PreviewMarkers.Children.Add(polygon);
            }
            else foreach (var area in candidate.PreviewAreas)
            {
                var marker = new Rectangle { Width = area.Width * sx, Height = area.Height * sy,
                    Fill = new SolidColorBrush(Color.FromArgb(70, 37, 99, 235)), Stroke = Brushes.RoyalBlue, StrokeThickness = 2 };
                Canvas.SetLeft(marker, area.Left * sx); Canvas.SetTop(marker, area.Top * sy); PreviewMarkers.Children.Add(marker);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_closed && !cancellation.IsCancellationRequested) PreviewTitle.Text = "プレビュー失敗：" + error.Message; }
        finally { if (ReferenceEquals(_previewCancellation, cancellation)) _previewCancellation = null; cancellation.Dispose(); }
    }
}
