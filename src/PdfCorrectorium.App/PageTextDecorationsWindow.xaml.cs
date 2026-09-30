using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App;

public partial class PageTextDecorationsWindow : Window
{
    public sealed record NamedOption<T>(string Name, T Value);

    public IReadOnlyList<NamedOption<PdfTextDecorationKind>> KindOptions { get; } =
    [
        new("ページ番号", PdfTextDecorationKind.PageNumber),
        new("ヘッダー", PdfTextDecorationKind.Header),
        new("フッター", PdfTextDecorationKind.Footer),
        new("文字透かし", PdfTextDecorationKind.Watermark),
    ];

    public IReadOnlyList<NamedOption<PdfTextDecorationPosition>> PositionOptions { get; } =
    [
        new("左上", PdfTextDecorationPosition.TopLeft),
        new("上中央", PdfTextDecorationPosition.TopCenter),
        new("右上", PdfTextDecorationPosition.TopRight),
        new("左下", PdfTextDecorationPosition.BottomLeft),
        new("下中央", PdfTextDecorationPosition.BottomCenter),
        new("右下", PdfTextDecorationPosition.BottomRight),
        new("中央", PdfTextDecorationPosition.Center),
    ];

    public IReadOnlyList<NamedOption<PdfTextDecorationPosition?>> EvenPositionOptions { get; } =
    [
        new("奇数ページと同じ", null),
        new("左上", PdfTextDecorationPosition.TopLeft),
        new("上中央", PdfTextDecorationPosition.TopCenter),
        new("右上", PdfTextDecorationPosition.TopRight),
        new("左下", PdfTextDecorationPosition.BottomLeft),
        new("下中央", PdfTextDecorationPosition.BottomCenter),
        new("右下", PdfTextDecorationPosition.BottomRight),
        new("中央", PdfTextDecorationPosition.Center),
    ];

    public ObservableCollection<Draft> Decorations { get; }
    public IReadOnlyList<PdfTextDecoration> ResultDecorations { get; private set; } = [];
    internal (string Odd, string Even) PreviewTexts => (OddPreviewText.Text, EvenPreviewText.Text);
    internal (TextAlignment Odd, TextAlignment Even) PreviewAlignments =>
        (OddPreviewText.TextAlignment, EvenPreviewText.TextAlignment);
    internal string EvenPositionDisplay => EvenPositionBox.Text;
    private readonly int _pageCount;

    public PageTextDecorationsWindow(int pageCount, IReadOnlyList<PdfTextDecoration> current)
    {
        _pageCount = pageCount;
        Decorations = new ObservableCollection<Draft>(current.Select(item => new Draft(item)));
        InitializeComponent();
        DataContext = this;
        DecorationsList.ItemsSource = Decorations;
        if (Decorations.Count > 0) DecorationsList.SelectedIndex = 0;
        RefreshPreview();
        LocalizationService.Apply(this);
    }

    private void AddPageNumber_OnClick(object sender, RoutedEventArgs e) => Add(new PdfTextDecoration());
    private void AddHeader_OnClick(object sender, RoutedEventArgs e) => Add(new PdfTextDecoration
    {
        Kind = PdfTextDecorationKind.Header,
        Position = PdfTextDecorationPosition.TopCenter,
        TextTemplate = "ヘッダー",
    });
    private void AddFooter_OnClick(object sender, RoutedEventArgs e) => Add(new PdfTextDecoration
    {
        Kind = PdfTextDecorationKind.Footer,
        Position = PdfTextDecorationPosition.BottomCenter,
        TextTemplate = "フッター",
    });
    private void AddWatermark_OnClick(object sender, RoutedEventArgs e) => Add(new PdfTextDecoration
    {
        Kind = PdfTextDecorationKind.Watermark,
        Position = PdfTextDecorationPosition.Center,
        TextTemplate = "複製禁止",
        FontSizePoints = 40,
        ColorHex = "#777777",
        Opacity = 0.25,
    });

    private void Add(PdfTextDecoration item)
    {
        var draft = new Draft(item);
        Decorations.Add(draft);
        DecorationsList.SelectedItem = draft;
        TextBox.Focus();
    }

    private void Remove_OnClick(object sender, RoutedEventArgs e)
    {
        if (DecorationsList.SelectedItem is not Draft selected) return;
        var index = Decorations.IndexOf(selected);
        Decorations.RemoveAt(index);
        if (Decorations.Count > 0) DecorationsList.SelectedIndex = Math.Min(index, Decorations.Count - 1);
        RefreshPreview();
    }

    private void SkipCover_OnClick(object sender, RoutedEventArgs e)
    {
        if (DecorationsList.SelectedItem is not Draft draft) return;
        if (_pageCount < 2)
        {
            MessageBox.Show(this, "表紙を除くには2ページ以上必要です。", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ApplySkipCoverPreset(draft);
    }

    internal static void ApplySkipCoverPreset(Draft draft)
    {
        draft.FirstPageText = "2";
        draft.OffsetText = "-1";
    }

    private void DecorationsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var oldItem in e.RemovedItems.OfType<Draft>()) oldItem.PropertyChanged -= Draft_OnPropertyChanged;
        foreach (var newItem in e.AddedItems.OfType<Draft>()) newItem.PropertyChanged += Draft_OnPropertyChanged;
        RefreshPreview();
    }

    private void Draft_OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshPreview();

    private void RefreshPreview()
    {
        if (OddPreviewText is null || EvenPreviewText is null) return;
        if (DecorationsList.SelectedItem is not Draft draft)
        {
            OddPreviewText.Text = EvenPreviewText.Text = string.Empty;
            return;
        }
        try
        {
            var model = draft.ToModel(_pageCount);
            ShowPreview(model, isOdd: true, OddPreviewLabel, OddPreviewText);
            ShowPreview(model, isOdd: false, EvenPreviewLabel, EvenPreviewText);
        }
        catch (ArgumentException)
        {
            OddPreviewText.Text = EvenPreviewText.Text = "入力値を確認してください";
        }
    }

    private void ShowPreview(PdfTextDecoration model, bool isOdd, TextBlock label, TextBlock textBlock)
    {
        var pageNumber = Enumerable.Range(1, _pageCount)
            .FirstOrDefault(number => number % 2 == (isOdd ? 1 : 0) &&
                number >= model.FirstPage && number <= (model.LastPage ?? _pageCount));
        label.Text = (isOdd ? "奇数" : "偶数") + (pageNumber == 0 ? "ページ（対象なし）" : $"ページ（{pageNumber}ページ目）");
        textBlock.Text = pageNumber == 0 || !model.IsEnabled ? string.Empty : model.TextForPage(pageNumber, _pageCount);
        textBlock.FontSize = Math.Clamp(model.FontSizePoints * 0.8, 9, 20);
        var color = (Color)ColorConverter.ConvertFromString(model.ColorHex);
        textBlock.Foreground = new SolidColorBrush(color) { Opacity = model.Opacity };
        textBlock.Width = 144;
        var position = model.PositionForPage(pageNumber == 0 ? (isOdd ? 1 : 2) : pageNumber);
        textBlock.TextAlignment = position switch
        {
            PdfTextDecorationPosition.TopLeft or PdfTextDecorationPosition.BottomLeft => TextAlignment.Left,
            PdfTextDecorationPosition.TopRight or PdfTextDecorationPosition.BottomRight => TextAlignment.Right,
            _ => TextAlignment.Center,
        };
        Canvas.SetLeft(textBlock, 8);
        Canvas.SetTop(textBlock, position switch
        {
            PdfTextDecorationPosition.TopLeft or PdfTextDecorationPosition.TopCenter or PdfTextDecorationPosition.TopRight => 10,
            PdfTextDecorationPosition.BottomLeft or PdfTextDecorationPosition.BottomCenter or PdfTextDecorationPosition.BottomRight => 164,
            _ => 87,
        });
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ResultDecorations = Decorations.Select(item => item.ToModel(_pageCount)).ToArray();
            DialogResult = true;
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(this, exception.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public sealed class Draft : INotifyPropertyChanged
    {
        private PdfTextDecorationKind _kind;
        private PdfTextDecorationPosition _position;
        private PdfTextDecorationPosition? _evenPagePosition;
        private string _textTemplate;
        private string _firstPageText;
        private string _lastPageText;
        private string _offsetText;
        private string _fontSizeText;
        private string _colorHex;
        private string _opacityText;
        private string _marginText;
        private bool _isEnabled;
        private readonly Guid _id;

        public Draft(PdfTextDecoration model)
        {
            _id = model.Id;
            _kind = model.Kind;
            _position = model.Position;
            _evenPagePosition = model.EvenPagePosition;
            _textTemplate = model.TextTemplate;
            _firstPageText = model.FirstPage.ToString(CultureInfo.CurrentCulture);
            _lastPageText = model.LastPage?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            _offsetText = model.PageNumberOffset.ToString(CultureInfo.CurrentCulture);
            _fontSizeText = model.FontSizePoints.ToString("0.##", CultureInfo.CurrentCulture);
            _colorHex = model.ColorHex;
            _opacityText = (model.Opacity * 100).ToString("0.##", CultureInfo.CurrentCulture);
            _marginText = model.MarginPoints.ToString("0.##", CultureInfo.CurrentCulture);
            _isEnabled = model.IsEnabled;
        }

        public PdfTextDecorationKind Kind { get => _kind; set { Set(ref _kind, value); OnPropertyChanged(nameof(DisplayName)); } }
        public PdfTextDecorationPosition Position { get => _position; set => Set(ref _position, value); }
        public PdfTextDecorationPosition? EvenPagePosition { get => _evenPagePosition; set => Set(ref _evenPagePosition, value); }
        public string TextTemplate { get => _textTemplate; set { Set(ref _textTemplate, value); OnPropertyChanged(nameof(DisplayName)); } }
        public string FirstPageText { get => _firstPageText; set => Set(ref _firstPageText, value); }
        public string LastPageText { get => _lastPageText; set => Set(ref _lastPageText, value); }
        public string OffsetText { get => _offsetText; set => Set(ref _offsetText, value); }
        public string FontSizeText { get => _fontSizeText; set => Set(ref _fontSizeText, value); }
        public string ColorHex { get => _colorHex; set => Set(ref _colorHex, value); }
        public string OpacityText { get => _opacityText; set => Set(ref _opacityText, value); }
        public string MarginText { get => _marginText; set => Set(ref _marginText, value); }
        public bool IsEnabled { get => _isEnabled; set => Set(ref _isEnabled, value); }
        public string DisplayName => $"{Kind switch
        {
            PdfTextDecorationKind.PageNumber => "ページ番号",
            PdfTextDecorationKind.Header => "ヘッダー",
            PdfTextDecorationKind.Footer => "フッター",
            _ => "文字透かし",
        }}: {TextTemplate}";

        public PdfTextDecoration ToModel(int pageCount)
        {
            var label = DisplayName;
            if (string.IsNullOrWhiteSpace(TextTemplate) || TextTemplate.Length > 512)
                throw new ArgumentException($"{label}: 表示する文字を1～512文字で入力してください。");
            if (!int.TryParse(FirstPageText, out var first) || first < 1 || first > pageCount)
                throw new ArgumentException($"{label}: 開始ページは1～{pageCount}で指定してください。");
            int? last = null;
            if (!string.IsNullOrWhiteSpace(LastPageText))
            {
                if (!int.TryParse(LastPageText, out var parsed) || parsed < first || parsed > pageCount)
                    throw new ArgumentException($"{label}: 終了ページは{first}～{pageCount}で指定してください。");
                last = parsed;
            }
            if (!int.TryParse(OffsetText, out var offset) || offset < -pageCount || offset > pageCount)
                throw new ArgumentException($"{label}: ページ番号の補正値が範囲外です。");
            if (!double.TryParse(FontSizeText, NumberStyles.Float, CultureInfo.CurrentCulture, out var fontSize) ||
                fontSize is < 4 or > 144)
                throw new ArgumentException($"{label}: 文字サイズは4～144ptで指定してください。");
            if (!Regex.IsMatch(ColorHex, "^#[0-9a-fA-F]{6}$"))
                throw new ArgumentException($"{label}: 色は#RRGGBB形式で指定してください。");
            if (!double.TryParse(OpacityText, NumberStyles.Float, CultureInfo.CurrentCulture, out var opacity) ||
                opacity is < 0 or > 100)
                throw new ArgumentException($"{label}: 不透明度は0～100%で指定してください。");
            if (!double.TryParse(MarginText, NumberStyles.Float, CultureInfo.CurrentCulture, out var margin) ||
                margin is < 0 or > 300)
                throw new ArgumentException($"{label}: 余白は0～300ptで指定してください。");
            return new PdfTextDecoration
            {
                Id = _id,
                Kind = Kind,
                TextTemplate = TextTemplate,
                Position = Position,
                EvenPagePosition = EvenPagePosition,
                FirstPage = first,
                LastPage = last,
                PageNumberOffset = offset,
                FontSizePoints = fontSize,
                ColorHex = ColorHex,
                Opacity = opacity / 100,
                MarginPoints = margin,
                IsEnabled = IsEnabled,
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            OnPropertyChanged(name);
        }
        private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
