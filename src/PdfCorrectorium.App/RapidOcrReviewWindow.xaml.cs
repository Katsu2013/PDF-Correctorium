using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Analysis;

namespace PdfCorrectorium.App;

public partial class RapidOcrReviewWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly RapidOcrTicket _ticket;
    private readonly DispatcherTimer _differenceTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private CancellationTokenSource? _applyCancellation;
    private bool _ready, _closed, _busy, _fitting = true, _zooming;
    private double _zoom = 1;
    private string? _failure;
    internal RapidOcrReviewWindow(MainWindowViewModel viewModel, RapidOcrTicket ticket, OcrLexiconQuery? dictionary = null)
    {
        _viewModel = viewModel; _ticket = ticket;
        InitializeComponent();
        // Keep the footer reachable on small screens. The body, not the image,
        // scrolls if long text or translated captions exceed the available height.
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 32);
        // Localize chrome before adding user-document text. Never translate OCR content.
        LocalizationService.Apply(this);
        LineImage.Source = ticket.Result.Image;
        var suggested = ticket.Result.Recognition.Reading.Text;
        CandidateEditor.Text = dictionary?.SuggestedText ?? suggested;
        if (dictionary is { Matches.Count: > 0 })
        { DictionaryCandidates.ItemsSource = dictionary.Matches.Select(m => new DictionaryRow(m)).ToArray(); DictionaryCandidates.Visibility = Visibility.Visible; }
        WordDifference.SetOriginal(CurrentText, ticket.Before.Text);
        WordDifference.SetOriginal(CandidateText, ticket.Before.Text);
        WordDifference.SetShowSuggested(CandidateText, true);
        _differenceTimer.Tick += (_, _) => { _differenceTimer.Stop(); UpdateDifference(); };
        _ready = true; UpdateDifference(); UpdateReviewState();
        Loaded += (_, _) => FitImage();
        Closed += (_, _) => { _closed = true; _differenceTimer.Stop(); _applyCancellation?.Cancel(); };
    }

    private void CandidateEditor_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        _failure = null; UpdateReviewState();
        _differenceTimer.Stop(); _differenceTimer.Start();
    }

    private void UpdateDifference()
    {
        var text = CandidateEditor.Text;
        var valid = MainWindowViewModel.ValidateRapidOcrReviewText(text) is null;
        CandidateText.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        if (!valid) { WordDifference.SetSuggested(CurrentText, _ticket.Before.Text); return; }
        WordDifference.SetSuggested(CurrentText, text);
        WordDifference.SetSuggested(CandidateText, text);
    }

    private void UpdateReviewState()
    {
        var invalid = MainWindowViewModel.ValidateRapidOcrReviewText(CandidateEditor.Text);
        var current = _viewModel.IsRapidOcrTicketCurrent(_ticket);
        ApplyButton.IsEnabled = !_busy && current && invalid is null;
        CandidateEditor.IsReadOnly = _busy;
        RestoreCandidateButton.IsEnabled = !_busy && CandidateEditor.Text != _ticket.Result.Recognition.Reading.Text;
        var canRegister = !_busy && invalid is null && MainWindowViewModel.GetOcrRegistrationPairs(_ticket.Result.Recognition.Reading.Text, CandidateEditor.Text).Length > 0;
        RegisterKnownCheck.IsEnabled = canRegister; RegisterRuleCheck.IsEnabled = canRegister;
        ReasonText.Text = LocalizationService.Translate(_busy ? "修正した文字に合わせて文字サイズを調整しています…"
            : !current ? "対象行が変更されています。閉じてから再認識してください。"
            : invalid ?? _failure ?? (CandidateEditor.Text == _ticket.Result.Recognition.Reading.Text ? _ticket.Completion.Reason
                : "手動で修正した候補です。原画像と照合して適用してください。"));
    }

    private void RestoreCandidate_OnClick(object sender, RoutedEventArgs e)
    { DictionaryCandidates.SelectedIndex = -1; CandidateEditor.Text = _ticket.Result.Recognition.Reading.Text; }
    private void DictionaryCandidates_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_busy || DictionaryCandidates.SelectedItem is not DictionaryRow row) return;
        var match = row.Match;
        CandidateEditor.Text = _ticket.Result.Recognition.Reading.Text.Remove(match.Start, match.Length).Insert(match.Start, match.Target);
    }
    private sealed record DictionaryRow(OcrLexiconMatch Match)
    {
        public string Source => Match.Source;
        public string Target => Match.Target;
        public string Reason => LocalizationService.Translate(Match.Reason);
    }
    private void CandidateEditor_OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string pasted) return;
        var text = CandidateEditor.Text.Remove(CandidateEditor.SelectionStart, CandidateEditor.SelectionLength)
            .Insert(CandidateEditor.SelectionStart, pasted);
        if (MainWindowViewModel.ValidateRapidOcrReviewText(text) is not { } invalid) return;
        // Reject the paste instead of silently truncating at a line break or character limit.
        e.CancelCommand(); _failure = invalid; UpdateReviewState();
    }
    private void CandidateEditor_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter in this single-line editor must not submit a half-edited candidate. IME uses ImeProcessed.
        if (e.Key == Key.Return) e.Handled = true;
    }

    private async void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy || MainWindowViewModel.ValidateRapidOcrReviewText(CandidateEditor.Text) is not null) return;
        var registerKnown = RegisterKnownCheck.IsEnabled && RegisterKnownCheck.IsChecked == true;
        var registerRules = RegisterRuleCheck.IsEnabled && RegisterRuleCheck.IsChecked == true;
        _busy = true; UpdateReviewState();
        using var cancellation = new CancellationTokenSource(); _applyCancellation = cancellation;
        try
        {
            var prepared = await _viewModel.PrepareRapidOcrReviewAsync(_ticket, CandidateEditor.Text, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested) return;
            if (prepared is not null && _viewModel.ApplyRapidOcrResult(prepared, automatic: false,
                registerKnown: registerKnown,
                registerRules: registerRules,
                rawText: _ticket.Result.Recognition.Reading.Text)) DialogResult = true;
            else _failure = "対象行が変更されています。閉じてから再認識してください。";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!_closed) { _failure = "修正候補を適用できませんでした。文字と対象行を確認してください。"; await _viewModel.ReportRapidOcrFailureAsync(error); }
        }
        finally { _applyCancellation = null; _busy = false; if (!_closed) UpdateReviewState(); }
    }

    private void FitImage()
    {
        if (!_ready || ImageViewport.ViewportWidth <= 2 || ImageViewport.ViewportHeight <= 2) return;
        SetZoom(Math.Min(1, Math.Min((ImageViewport.ViewportWidth - 2) / _ticket.Result.Image.PixelWidth,
            (ImageViewport.ViewportHeight - 2) / _ticket.Result.Image.PixelHeight)), fit: true);
    }
    private void ImageFrame_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_ready || e.NewSize.Width <= 12) return;
        // Fit a short line to its aspect ratio, rather than consuming all spare
        // window height. Zooming scrolls inside this stable frame and never asks
        // the recognizer to render or crop the source again.
        var image = _ticket.Result.Image;
        var fittedHeight = image.PixelHeight * Math.Min(1, (e.NewSize.Width - 12) / image.PixelWidth);
        var height = Math.Clamp(Math.Ceiling(fittedHeight) + 12, 64, 180);
        if (Math.Abs(ImageFrame.Height - height) > .5) ImageFrame.Height = height;
    }
    private void SetZoom(double zoom, bool fit = false, Point? anchor = null)
    {
        if (_zooming) return;
        _zooming = true;
        try
        {
            var point = anchor ?? new Point(ImageViewport.ViewportWidth / 2, ImageViewport.ViewportHeight / 2);
            var left = ImageViewport.HorizontalOffset; var top = ImageViewport.VerticalOffset;
            var ratio = Math.Clamp(zoom, .01, 8) / _zoom;
            _zoom = Math.Clamp(zoom, .01, 8); _fitting = fit;
            LineImage.Stretch = System.Windows.Media.Stretch.Fill;
            LineImage.Width = _ticket.Result.Image.PixelWidth * _zoom;
            LineImage.Height = _ticket.Result.Image.PixelHeight * _zoom;
            ZoomText.Text = _zoom.ToString("P0", System.Globalization.CultureInfo.CurrentCulture);
            ZoomOutButton.IsEnabled = _zoom > .010001; ZoomInButton.IsEnabled = _zoom < 7.999999;
            ImageViewport.UpdateLayout();
            ImageViewport.ScrollToHorizontalOffset(fit ? 0 : (left + point.X) * ratio - point.X);
            ImageViewport.ScrollToVerticalOffset(fit ? 0 : (top + point.Y) * ratio - point.Y);
        }
        finally { _zooming = false; }
    }
    private void ZoomOut_OnClick(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.25);
    private void ZoomIn_OnClick(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.25);
    private void ActualSize_OnClick(object sender, RoutedEventArgs e) => SetZoom(1);
    private void FitImage_OnClick(object sender, RoutedEventArgs e) => FitImage();
    private void ImageViewport_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_ready && _fitting && !_zooming && (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)) FitImage();
    }
    private void ImageViewport_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        SetZoom(e.Delta > 0 ? _zoom * 1.25 : _zoom / 1.25, anchor: e.GetPosition(ImageViewport)); e.Handled = true;
    }
}
