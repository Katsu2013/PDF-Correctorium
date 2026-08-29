using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;
using PdfBindingDirection = PdfCorrectorium.Core.Documents.BindingDirection;

namespace PdfCorrectorium.App;

/// <summary>
/// PDF固有の文書情報を確認し、PDFを開いたときの表示方法を編集するダイアログです。
/// </summary>
public partial class DocumentPropertiesWindow : Window, INotifyPropertyChanged
{
    /// <summary>画面が参照する文書とプロジェクトの編集状態です。</summary>
    private readonly MainWindowViewModel _viewModel;

    /// <summary>ダイアログを閉じたときにPDF解析を中止するためのトークン発行元です。</summary>
    private readonly CancellationTokenSource _loadCancellationTokenSource = new();

    /// <summary>Loadedイベントが複数回発生してもPDFを一度だけ解析するためのフラグです。</summary>
    private bool _loadStarted;

    /// <summary>PDFから読み取ったメタデータ、セキュリティ、フォント等の情報です。</summary>
    private PdfDocumentPropertiesInfo _pdfProperties;

    /// <summary>文書プロパティダイアログを初期化します。</summary>
    /// <param name="viewModel">現在開いているPDFとプロジェクトの状態。</param>
    public DocumentPropertiesWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _pdfProperties = PdfDocumentPropertiesService.CreateUnavailable(viewModel.SourcePdfPath);

        InitializeComponent();
        DataContext = this;
        LocalizationService.Apply(this);

        SelectByTag(PageModeComboBox, viewModel.CurrentViewerSettings.PageMode.ToString());
        SelectByTag(BindingDirectionComboBox, viewModel.CurrentViewerSettings.BindingDirection.ToString());
        ShowCoverSeparatelyCheckBox.IsChecked = viewModel.CurrentViewerSettings.ShowCoverSeparately;

        Loaded += Window_OnLoaded;
        Closed += Window_OnClosed;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>現在参照している元PDFのパスです。</summary>
    public string SourcePdfPath => _viewModel.SourcePdfPath;

    /// <summary>編集作業を保存しているプロジェクトファイルのパスです。</summary>
    public string ProjectPath => _viewModel.ProjectPath;

    /// <summary>元PDFを識別するSHA-256ハッシュ値です。</summary>
    public string SourceHash => _viewModel.SourceHash;

    /// <summary>OCR文字情報を取り込んだデータソースの説明です。</summary>
    public string OcrDataSourceText => _viewModel.OcrDataSourceText;

    /// <summary>設定やキャッシュを保存する現在の動作モードです。</summary>
    public string StorageModeText => _viewModel.StorageModeText;

    /// <summary>PDF本体から読み取った文書情報です。</summary>
    public PdfDocumentPropertiesInfo PdfProperties
    {
        get => _pdfProperties;
        private set
        {
            if (ReferenceEquals(_pdfProperties, value))
            {
                return;
            }

            _pdfProperties = value;
            OnPropertyChanged();
        }
    }

    /// <summary>利用者が確定したPDF初期表示設定です。</summary>
    public ViewerSettings? ResultViewerSettings { get; private set; }

    /// <summary>初回表示後に、画面を固めないようバックグラウンドでPDF情報を読み取ります。</summary>
    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadStarted)
        {
            return;
        }

        _loadStarted = true;
        try
        {
            PdfProperties = await PdfDocumentPropertiesService.ReadAsync(
                _viewModel.SourcePdfPath,
                _loadCancellationTokenSource.Token);
            LoadingStatusText.Text = "PDF情報を読み込みました。";
        }
        catch (OperationCanceledException) when (_loadCancellationTokenSource.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            PdfProperties = PdfDocumentPropertiesService.CreateUnavailable(
                _viewModel.SourcePdfPath,
                $"PDF情報を読み込めませんでした: {exception.Message}");
            LoadingStatusText.Text = "一部のPDF情報を読み込めませんでした。";
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>閉じたダイアログに解析結果を反映しないよう、実行中の処理を中止します。</summary>
    private void Window_OnClosed(object? sender, EventArgs e)
    {
        _loadCancellationTokenSource.Cancel();
        _loadCancellationTokenSource.Dispose();
    }

    /// <summary>初期表示タブの選択内容を結果へ反映してダイアログを閉じます。</summary>
    private void ApplyButton_OnClick(object sender, RoutedEventArgs e)
    {
        var pageMode = Enum.Parse<InitialPageMode>(((ComboBoxItem)PageModeComboBox.SelectedItem).Tag!.ToString()!);
        var direction = Enum.Parse<PdfBindingDirection>(((ComboBoxItem)BindingDirectionComboBox.SelectedItem).Tag!.ToString()!);
        ResultViewerSettings = new ViewerSettings
        {
            PageMode = pageMode,
            BindingDirection = direction,
            ShowCoverSeparately = ShowCoverSeparatelyCheckBox.IsChecked == true,
        };
        DialogResult = true;
    }

    /// <summary>列挙値をTagへ保持している項目をコンボボックスから選択します。</summary>
    private static void SelectByTag(ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .First(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal));
    }

    /// <summary>指定したプロパティが変更されたことを画面へ通知します。</summary>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
