using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PdfCorrectorium.App.Resources;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App;

public partial class ReadAloudPanel : UserControl, IAsyncDisposable
{
    private readonly MainWindow _owner;
    private readonly MainWindowViewModel _viewModel;
    private readonly ISpeechPlayback _speech;
    private CancellationTokenSource? _playback, _chunkCancellation;
    private TaskCompletionSource? _resume;
    private TaskCompletionSource _navigationWake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _paused, _ready, _closed, _preparing, _initializing;
    private int _pendingMove;
    private MainWindowViewModel.SpeechTicket? _ticket;
    private sealed record PagePlan(MainWindowViewModel.SpeechPage Page, IReadOnlyList<SpeechUtterance> Paragraphs);
    private PagePlan? _plan;
    private readonly HashSet<int> _emptyPages = [];
    private SpeechCursor _cursor;
    internal int? FollowingPage { get; private set; }
    internal Task? PlaybackTask { get; private set; }
    private SpeechNavigationUnit NavigationUnit => (SpeechNavigationUnit)Math.Clamp(NavigationUnitBox?.SelectedIndex ?? 0, 0, 1);
    private bool HasCurrentPosition => _ticket is { } ticket && _plan is { } plan &&
        _viewModel.IsSpeechTicketCurrent(ticket) && _viewModel.SelectedPage?.PageNumber == plan.Page.PageNumber;

    internal ReadAloudPanel(MainWindow owner, MainWindowViewModel viewModel, ISpeechPlayback? speech = null)
    {
        _owner = owner; _viewModel = viewModel; _speech = speech ?? new WindowsSpeechService();
        InitializeComponent();
        IsVisibleChanged += async (_, _) => { if (IsVisible) await InitializeSpeechAsync(); };
        RateSlider.Value = viewModel.CurrentApplicationSettings.EffectiveSpeechRate;
        viewModel.ReadAloudInvalidated += ContentInvalidated;
        viewModel.PropertyChanged += ViewModelChanged;
        LocalizationService.Apply(this);
    }
    private string Text(string japanese, string english) => LocalizationService.IsEnglish ? english : japanese;
    private async void Panel_OnLoaded(object sender, RoutedEventArgs e) { if (IsVisible) await InitializeSpeechAsync(); }
    internal async Task InitializeSpeechAsync()
    {
        if (_initializing || _closed) return; _initializing = true;
        try
        {
            var voices = await _speech.InitializeAsync();
            if (_closed) return;
            VoiceBox.ItemsSource = voices;
            var preferred = _viewModel.CurrentApplicationSettings.SpeechVoiceName;
            var language = LocalizationService.IsEnglish ? "en" : "ja";
            VoiceBox.SelectedItem = voices.FirstOrDefault(voice => WindowsSpeechService.VoiceNamesMatch(voice.Name, preferred)) ??
                voices.FirstOrDefault(voice => voice.IsMicrosoft && voice.Language.StartsWith(language, StringComparison.OrdinalIgnoreCase)) ??
                voices.FirstOrDefault(voice => voice.Name == _speech.DefaultVoice) ?? voices.FirstOrDefault();
            _ready = voices.Count > 0;
            PlaybackStatus.Text = _ready ? Text("範囲を選んで再生してください。", "Choose a scope and play.") :
                Text("利用できる音声がありません。Windowsの音声設定を確認してください。", "No voice is available. Check Windows speech settings.");
            RefreshButtons();
        }
        catch (Exception error) { if (!_closed) PlaybackStatus.Text = Text("音声を初期化できませんでした：", "Could not initialize speech: ") + error.Message; }
    }
    private void ForgetPosition()
    {
        _ticket = null; _plan = null; _emptyPages.Clear();
        if (PositionText is not null) PositionText.Text = string.Empty;
    }
    private void ContentInvalidated(object? sender, EventArgs e)
    {
        ForgetPosition();
        if (_playback is not null) StopPlayback(Text("編集により読み上げを停止しました。", "Stopped because the document changed."));
        RefreshButtons();
    }
    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.SourcePdfPath) or nameof(MainWindowViewModel.IsOpeningDocument) or nameof(MainWindowViewModel.HasDocument) or
            nameof(MainWindowViewModel.DocumentViewMode) or nameof(MainWindowViewModel.DocumentPageFlowMode))
        {
            ForgetPosition();
            if (_playback is not null) StopPlayback(Text("文書の切り替えにより停止しました。", "Stopped because the document changed."));
            RefreshButtons();
        }
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedPage) && _viewModel.SelectedPage?.PageNumber != FollowingPage)
        {
            ForgetPosition();
            if (_playback is not null) StopPlayback(Text("ページの移動により停止しました。", "Stopped after manual page navigation."));
            RefreshButtons();
        }
        if (e.PropertyName == nameof(MainWindowViewModel.IsPreviewLoading)) RefreshButtons();
        if (e.PropertyName is nameof(MainWindowViewModel.ZoomFactor) or nameof(MainWindowViewModel.PreviewImage) or
            nameof(MainWindowViewModel.PreviewPixelWidth) or nameof(MainWindowViewModel.PreviewPixelHeight)) _owner.RefreshSpeechMarker();
    }
    private bool CanMove(int direction)
    {
        if (!_ready || _closed || _preparing || _pendingMove != 0 || !HasCurrentPosition || _plan is not { } plan || _ticket is not { } ticket) return false;
        var move = SpeechNavigation.Move(plan.Paragraphs, _cursor, direction, NavigationUnit);
        if (move.PageDelta == 0) return true;
        for (var page = plan.Page.PageNumber + move.PageDelta; page >= ticket.FirstPage && page <= ticket.LastPage; page += move.PageDelta)
            if (!_emptyPages.Contains(page)) return true;
        return false;
    }
    private void RefreshButtons()
    {
        if (_closed || PlayButton is null || PreviousButton is null || NextButton is null) return;
        var running = _playback is not null;
        PlayButton.IsEnabled = _ready && !running && _viewModel.HasDocument && !_viewModel.IsOpeningDocument && !_viewModel.IsPreviewLoading;
        ScopeBox.IsEnabled = _ready && !running;
        PauseButton.IsEnabled = StopButton.IsEnabled = running;
        PreviousButton.IsEnabled = CanMove(-1); NextButton.IsEnabled = CanMove(1);
        ActionIcon.SetKey(PauseButton, _paused ? "Play" : "Pause");
        var previous = NavigationUnit == SpeechNavigationUnit.Paragraph ? Text("前の段落", "Previous paragraph") : Text("前の行", "Previous line");
        var next = NavigationUnit == SpeechNavigationUnit.Paragraph ? Text("次の段落", "Next paragraph") : Text("次の行", "Next line");
        PreviousButton.ToolTip = previous; NextButton.ToolTip = next;
        AutomationProperties.SetName(PreviousButton, previous); AutomationProperties.SetName(NextButton, next);
    }
    private void Play_OnClick(object sender, RoutedEventArgs e) => PlaybackTask = StartPlaybackAsync();
    private void Previous_OnClick(object sender, RoutedEventArgs e) => RequestMove(-1);
    private void Next_OnClick(object sender, RoutedEventArgs e) => RequestMove(1);
    private void NavigationUnit_OnChanged(object sender, SelectionChangedEventArgs e) => RefreshButtons();
    private void Scope_OnChanged(object sender, SelectionChangedEventArgs e)
    { if (_ready && _playback is null) { ForgetPosition(); RefreshButtons(); } }
    private void RequestMove(int direction)
    {
        if (!CanMove(direction)) return;
        _pendingMove = direction;
        if (_playback is null) PlaybackTask = StartPlaybackAsync(resumePosition: true);
        else { _navigationWake.TrySetResult(); _chunkCancellation?.Cancel(); }
        RefreshButtons();
    }
    private void ShowPosition(PagePlan plan, SpeechCursor cursor)
    {
        var paragraph = plan.Paragraphs[cursor.Paragraph];
        PositionText.Text = Text($"段落 {cursor.Paragraph + 1}/{plan.Paragraphs.Count}・行 {cursor.Line + 1}/{paragraph.Spans.Count}",
            $"Paragraph {cursor.Paragraph + 1}/{plan.Paragraphs.Count} · Line {cursor.Line + 1}/{paragraph.Spans.Count}");
        _owner.SetSpeechMarker(plan.Page.PageNumber, paragraph.Spans[cursor.Line].Segment, plan.Page.WidthPoints, plan.Page.HeightPoints);
        RefreshButtons();
    }
    internal async Task StartPlaybackAsync(bool resumePosition = false)
    {
        if (!_ready || _playback is not null || _closed) return;
        var cancellation = new CancellationTokenSource();
        _playback = cancellation; _paused = false; _resume = null;
        _navigationWake = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FollowingPage = _viewModel.SelectedPage?.PageNumber;
        RefreshButtons();
        try
        {
            if (!resumePosition || !HasCurrentPosition)
            {
                _owner.CommitSpeechEditorBindings();
                _ticket = _viewModel.CreateSpeechTicket((ReadAloudScope)Math.Clamp(ScopeBox.SelectedIndex, 0, 2));
                _plan = null; _cursor = default; _pendingMove = 0; _emptyPages.Clear();
            }
            var ticket = _ticket!;
            var token = cancellation.Token;
            var spoken = false;
            await using var worker = new PdfNativeWorkerClient(reuseInputs: true);
            async Task<bool> LoadPlan(int start, int direction)
            {
                _preparing = true; RefreshButtons();
                try
                {
                    for (var number = start; number >= ticket.FirstPage && number <= ticket.LastPage; number += direction)
                    {
                        token.ThrowIfCancellationRequested();
                        if (_emptyPages.Contains(number)) continue;
                        PlaybackStatus.Text = Text($"{number}ページの文字を準備中…", $"Preparing page {number}…");
                        var page = await _viewModel.ReadSpeechPageAsync(ticket, number, worker, token);
                        var paragraphs = SpeechParagraphs.Build(page.Segments);
                        if (paragraphs.Count == 0) { _emptyPages.Add(number); continue; }
                        FollowingPage = number;
                        await _owner.FollowSpeechPageAsync(number, token);
                        token.ThrowIfCancellationRequested();
                        _plan = new(page, paragraphs);
                        return true;
                    }
                    return false;
                }
                finally { _preparing = false; RefreshButtons(); }
            }
            if (_plan is null && !await LoadPlan(ticket.FirstPage, 1))
            {
                PlaybackStatus.Text = Text("読み上げ可能な文字がありません。画像のみの場合はOCRを実行してください。", "No readable text. Use OCR for image-only pages.");
                return;
            }
            while (_plan is { } plan)
            {
                token.ThrowIfCancellationRequested();
                if (!_viewModel.IsSpeechTicketCurrent(ticket)) throw new OperationCanceledException(token);
                if (_pendingMove != 0)
                {
                    var direction = _pendingMove;
                    var move = SpeechNavigation.Move(plan.Paragraphs, _cursor, direction, NavigationUnit);
                    var found = true;
                    if (move.PageDelta == 0) _cursor = move.Cursor;
                    else if (await LoadPlan(plan.Page.PageNumber + move.PageDelta, move.PageDelta))
                    {
                        plan = _plan!;
                        var paragraphIndex = move.PageDelta > 0 ? 0 : plan.Paragraphs.Count - 1;
                        _cursor = new(paragraphIndex, move.PageDelta < 0 && NavigationUnit == SpeechNavigationUnit.Line ? plan.Paragraphs[paragraphIndex].Spans.Count - 1 : 0);
                    }
                    else found = false;
                    _pendingMove = 0;
                    _navigationWake = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    if (!found)
                    {
                        ShowPosition(plan, _cursor);
                        PlaybackStatus.Text = direction > 0 ? Text("これより後に読み上げ対象がありません。", "No readable text follows.") :
                            Text("これより前に読み上げ対象がありません。", "No readable text precedes this position.");
                        if (!_paused || _resume is null) return;
                        await Task.WhenAny(_resume.Task, _navigationWake.Task).WaitAsync(token);
                        if (_pendingMove != 0 || _paused) continue;
                    }
                }
                var utterance = plan.Paragraphs[_cursor.Paragraph];
                ShowPosition(plan, _cursor);
                PlaybackStatus.Text = _paused ? Text("一時停止中", "Paused") : Text($"{plan.Page.PageNumber}ページを読み上げ中", $"Reading page {plan.Page.PageNumber}");
                if (_paused && _resume is not null)
                {
                    await Task.WhenAny(_resume.Task, _navigationWake.Task).WaitAsync(token);
                    if (_pendingMove != 0 || _paused) continue;
                }
                var offset = utterance.Spans[_cursor.Line].Start;
                foreach (var chunk in Chunks(utterance.Text[offset..]))
                {
                    if (_paused && _resume is not null)
                    {
                        await Task.WhenAny(_resume.Task, _navigationWake.Task).WaitAsync(token);
                        if (_pendingMove != 0) break;
                    }
                    token.ThrowIfCancellationRequested();
                    var chunkOffset = offset; offset += chunk.Length;
                    SpokenText.Text = chunk;
                    var paragraphIndex = _cursor.Paragraph;
                    var active = true;
                    using var chunkCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                    _chunkCancellation = chunkCancellation;
                    try
                    {
                        spoken = true;
                        await _speech.SpeakAsync(chunk, VoiceBox.SelectedValue as string ?? string.Empty, RateSlider.Value, chunkCancellation.Token, position =>
                        {
                            if (Dispatcher.HasShutdownStarted || chunkCancellation.IsCancellationRequested) return;
                            _ = Dispatcher.InvokeAsync(() =>
                            {
                                if (!active || _closed || chunkCancellation.IsCancellationRequested || !ReferenceEquals(_playback, cancellation) ||
                                    !_viewModel.IsSpeechTicketCurrent(ticket) || !ReferenceEquals(_plan, plan)) return;
                                var line = utterance.SpanIndexAt(chunkOffset + Math.Clamp(position, 0, chunk.Length - 1));
                                if (_cursor.Line != line) { _cursor = new(paragraphIndex, line); ShowPosition(plan, _cursor); }
                            });
                        });
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested && _pendingMove != 0) { }
                    finally { active = false; _chunkCancellation = null; }
                    if (_pendingMove != 0) break;
                }
                if (_pendingMove != 0) continue;
                if (_cursor.Paragraph + 1 < plan.Paragraphs.Count) { _cursor = new(_cursor.Paragraph + 1, 0); continue; }
                _cursor = new(plan.Paragraphs.Count - 1, plan.Paragraphs[^1].Spans.Count - 1);
                if (await LoadPlan(plan.Page.PageNumber + 1, 1)) { _cursor = default; continue; }
                break;
            }
            PlaybackStatus.Text = spoken ? Text("読み上げが完了しました。", "Reading complete.") : Text("停止しました。", "Stopped.");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_closed) PlaybackStatus.Text = Text("読み上げできませんでした：", "Reading failed: ") + error.Message; }
        finally
        {
            if (ReferenceEquals(_playback, cancellation))
            { _playback = null; _paused = false; _pendingMove = 0; FollowingPage = null; _owner.ClearSpeechMarker(); RefreshButtons(); }
            cancellation.Dispose();
        }
    }
    internal static IEnumerable<string> Chunks(string text)
    {
        var elements = StringInfo.GetTextElementEnumerator(text); var buffer = new StringBuilder();
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (buffer.Length > 0 && buffer.Length + element.Length > 2000) { yield return buffer.ToString(); buffer.Clear(); }
            buffer.Append(element);
        }
        if (buffer.Length > 0) yield return buffer.ToString();
    }
    private async void Pause_OnClick(object sender, RoutedEventArgs e)
    {
        if (_playback is null) return;
        _paused = !_paused;
        if (_paused) _resume = new(TaskCreationOptions.RunContinuationsAsynchronously); else _resume?.TrySetResult();
        try { await _speech.PauseAsync(_paused); }
        catch (Exception error) { StopPlayback(error.Message); }
        if (_paused) PlaybackStatus.Text = Text("一時停止中", "Paused");
        RefreshButtons();
    }
    internal void StopPlayback(string? message = null)
    {
        _pendingMove = 0; _playback?.Cancel(); _resume?.TrySetResult(); _navigationWake.TrySetResult();
        _owner.ClearSpeechMarker(); if (message is not null) PlaybackStatus.Text = message;
    }
    private void Stop_OnClick(object sender, RoutedEventArgs e) => StopPlayback(Text("停止しました。", "Stopped."));
    private void Voice_OnChanged(object sender, SelectionChangedEventArgs e) => SavePreferences();
    private void Rate_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_ready) SavePreferences(); }
    private void SavePreferences()
    {
        if (!_ready || _closed) return;
        _viewModel.SetSpeechPreferences(VoiceBox.SelectedValue as string ?? string.Empty, RateSlider.Value);
    }
    private void Panel_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        if (_playback is not null) StopPlayback(Text("停止しました。", "Stopped.")); else CloseRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
    public event EventHandler? CloseRequested;
    public async ValueTask DisposeAsync()
    {
        if (_closed) return;
        _closed = true; StopPlayback();
        _viewModel.ReadAloudInvalidated -= ContentInvalidated; _viewModel.PropertyChanged -= ViewModelChanged;
        try { if (PlaybackTask is { } task) await task; await _speech.DisposeAsync(); }
        catch (Exception) { }
    }
}
