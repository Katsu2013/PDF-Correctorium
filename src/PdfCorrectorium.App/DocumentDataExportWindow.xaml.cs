using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

public partial class DocumentDataExportWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private CancellationTokenSource? _cancellation;
    private bool _busy;
    private bool _closeRequested;
    public DocumentDataExportWindow(MainWindowViewModel viewModel, DocumentDataExportKind kind)
    {
        _viewModel = viewModel;
        InitializeComponent();
        KindBox.SelectedIndex = (int)kind;
        PagesBox.Text = $"1-{viewModel.PageItems.Count}";
        UpdateOptions();
        Loaded += (_, _) => KindBox.Focus();
        Closing += (_, args) => { if (_busy) { args.Cancel = true; _closeRequested = true; _cancellation?.Cancel(); } };
        LocalizationService.Apply(this);
    }
    private void Options_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QualityBox is not null) UpdateOptions();
    }
    private void UpdateOptions()
    {
        var kind = (DocumentDataExportKind)KindBox.SelectedIndex;
        WorkDataWarning.Visibility = kind == DocumentDataExportKind.PageImages ? Visibility.Collapsed : Visibility.Visible;
        ImageInputs.Visibility = kind == DocumentDataExportKind.PageImages ? Visibility.Visible : Visibility.Collapsed;
        PageHeadings.Visibility = kind == DocumentDataExportKind.OcrText ? Visibility.Visible : Visibility.Collapsed;
        PageRangeInputs.IsEnabled = kind != DocumentDataExportKind.CommentsCsv;
        QualityBox.IsEnabled = ImageFormat.SelectedIndex == 1;
        ScopeText.Text = LocalizationService.Translate(kind switch
        {
            DocumentDataExportKind.OcrText => "編集後のOCR文字を文書順・読み順で出力します。削除領域は除きます。新しい文字認識や可視本文の抽出は行いません。",
            DocumentDataExportKind.CommentsCsv => "文書全体の独自コメント・対応状態・重要度・タグを出力します。標準PDF注釈の一覧ではありません。CSVの先頭の数式文字は保護されます。",
            _ => "墨消し・装飾・ページ編集を反映したPDFを検証して画像化します。出力先に新しい専用フォルダーを作り、1ページずつ保存します。最初のPDF生成・検証は文書全体が対象です。"
        });
    }
    private async void Export_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var kind = (DocumentDataExportKind)KindBox.SelectedIndex;
        var range = kind == DocumentDataExportKind.CommentsCsv ? $"1-{_viewModel.PageItems.Count}" : PagesBox.Text;
        if (!PageRangeParser.TryParse(range, _viewModel.PageItems.Count, out var pages))
        { StatusText.Text = LocalizationService.Translate("ページ範囲が正しくありません。例：1-3, 8, 10-12"); return; }
        var quality = 90;
        if (kind == DocumentDataExportKind.PageImages && ImageFormat.SelectedIndex == 1 &&
            (!int.TryParse(QualityBox.Text, out quality) || quality is < 1 or > 100))
        { StatusText.Text = LocalizationService.Translate("JPEG品質は1〜100の整数で指定してください。"); return; }
        string destination;
        if (kind == DocumentDataExportKind.PageImages)
        {
            var folder = new OpenFolderDialog { Title = LocalizationService.Translate("ページ画像の保存先（親フォルダー）") };
            if (folder.ShowDialog(this) != true) return;
            destination = folder.FolderName;
        }
        else
        {
            var file = new SaveFileDialog
            {
                Filter = kind == DocumentDataExportKind.OcrText ? "UTF-8 text (*.txt)|*.txt" : "UTF-8 CSV (*.csv)|*.csv",
                DefaultExt = kind == DocumentDataExportKind.OcrText ? ".txt" : ".csv", AddExtension = true,
                FileName = kind == DocumentDataExportKind.OcrText ? "ocr-work-text.txt" : "project-comments.csv"
            };
            if (file.ShowDialog(this) != true) return;
            destination = file.FileName;
        }
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        _busy = true; ExportInputs.IsEnabled = false; ExportButton.IsEnabled = false; CloseButton.IsEnabled = false;
        StopButton.Visibility = WorkProgress.Visibility = Visibility.Visible; ResultPathBox.Visibility = Visibility.Collapsed;
        try
        {
            var dpi = int.Parse(((ComboBoxItem)DpiBox.SelectedItem).Content.ToString()!, CultureInfo.InvariantCulture);
            var result = await _viewModel.ExportDocumentDataAsync(kind, destination, pages,
                PageHeadings.IsChecked == true, ImageFormat.SelectedIndex == 1, dpi, quality,
                new Progress<string>(message => StatusText.Text = LocalizationService.Translate(message)), cancellation.Token);
            StatusText.Text = LocalizationService.Translate("書き出しが完了しました。出力先：");
            ResultPathBox.Text = result; ResultPathBox.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { StatusText.Text = LocalizationService.Translate("書き出しを中止しました。"); }
        catch (Exception error) { StatusText.Text = LocalizationService.Translate("書き出しできませんでした：") + error.Message; }
        finally
        {
            _cancellation = null; _busy = false; ExportInputs.IsEnabled = true; ExportButton.IsEnabled = CloseButton.IsEnabled = true;
            StopButton.Visibility = WorkProgress.Visibility = Visibility.Collapsed;
            if (_closeRequested) Close();
        }
    }
    private void Stop_OnClick(object sender, RoutedEventArgs e) => _cancellation?.Cancel();
}
