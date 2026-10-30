using System.Globalization;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App.ViewModels;

public partial class MainWindowViewModel
{
    private string _wordReadingSurface = string.Empty, _wordReadingPronunciation = string.Empty;
    private WordReading? _selectedWordReading;
    private OverlayRegionViewModel? _wordReadingDraftRegion;
    private string? _wordReadingDraftText;
    private int[] _wordReadingDraftIndices = [];
    private int _wordReadingScopeIndex;
    public int WordReadingScopeIndex
    {
        get => _wordReadingScopeIndex;
        set
        {
            if (!Set(ref _wordReadingScopeIndex, Math.Clamp(value, 0, 1))) return;
            SelectedWordReading = null;
            var existing = WordReadingEntries.FirstOrDefault(item => item.SurfaceText == WordReadingSurface.Trim());
            if (existing is not null) WordReadingPronunciation = existing.ReadingText;
            OnPropertyChanged(nameof(WordReadingEntries)); OnPropertyChanged(nameof(HasWordReadingEntries));
            OnPropertyChanged(nameof(IsLocalWordReadingScope)); OnPropertyChanged(nameof(WordReadingScopeDescription));
            RaiseWordReadingCommands();
        }
    }
    public bool IsLocalWordReadingScope => WordReadingScopeIndex == 0;
    public string WordReadingScopeDescription => LocalizationService.Translate(IsLocalWordReadingScope ?
        "この行の指定を優先します。削除すると共通の読み方に戻ります。" : "全ページの同じ表記に適用します。行ごとの指定は保持します。");
    internal IReadOnlyList<WordReading> CommonWordReadings => _project?.WordReadings ?? [];
    private IReadOnlyList<WordReading>? _compiledReadingSource;
    private WordReadingMatcher _compiledCommonReadings = WordReadingMatcher.Empty;
    private WordReadingMatcher CommonReadingMatcher
    {
        get
        {
            var rules = CommonWordReadings;
            if (!ReferenceEquals(rules, _compiledReadingSource))
            { _compiledReadingSource = rules; _compiledCommonReadings = rules.Count == 0 ? WordReadingMatcher.Empty : new(rules); }
            return _compiledCommonReadings;
        }
    }
    private void RefreshCommonWordReadings()
    {
        var common = CommonWordReadings;
        var matcher = CommonReadingMatcher;
        foreach (var row in _pageOverlays.Values.SelectMany(rows => rows)) row.SetCommonWordReadings(common, matcher);
        InvalidateReadAloud(); RefreshWordReadingEntries();
    }
    public RelayCommand RegisterWordReadingCommand { get; }
    public RelayCommand RemoveWordReadingCommand { get; }
    public bool CanEditWordReadings => CanUseOcrEditControls && SelectedOverlayCount == 1 && SelectedOverlay is { IsDeleted: false };
    public bool ShowWordReadingRuby
    {
        get => _applicationSettings.ShowWordReadingRuby;
        set
        {
            if (ShowWordReadingRuby == value) return;
            _applicationSettings = _applicationSettings with { ShowWordReadingRuby = value };
            OnPropertyChanged(); OnPropertyChanged(nameof(AreWordReadingRubiesVisible)); QueueDisplaySettingsSave();
        }
    }
    public bool AreWordReadingRubiesVisible => ShowWordReadingRuby && IsOcrOverlayVisible;
    public IReadOnlyList<WordReading> WordReadingEntries => IsLocalWordReadingScope ? SelectedOverlay?.WordReadingDefinitions ?? [] : CommonWordReadings;
    public bool HasWordReadingEntries => WordReadingEntries.Count > 0;
    public string WordReadingSurface
    {
        get => _wordReadingSurface;
        set { if (Set(ref _wordReadingSurface, value ?? string.Empty)) RaiseWordReadingCommands(); }
    }
    public string WordReadingPronunciation
    {
        get => _wordReadingPronunciation;
        set { if (Set(ref _wordReadingPronunciation, value ?? string.Empty)) RaiseWordReadingCommands(); }
    }
    public WordReading? SelectedWordReading
    {
        get => _selectedWordReading;
        set
        {
            if (!Set(ref _selectedWordReading, value)) return;
            if (value is not null) { WordReadingSurface = value.SurfaceText; WordReadingPronunciation = value.ReadingText; }
            RaiseWordReadingCommands();
        }
    }
    public string WordReadingActionText => LocalizationService.Translate(WordReadingEntries.Any(item => item.SurfaceText == WordReadingSurface.Trim()) ? "更新" : "登録");
    public string WordReadingInputMessage
    {
        get
        {
            if (IsLocalWordReadingScope && SelectedOverlay?.HasWordReadingErrors == true) return SelectedOverlay.WordReadingsValidationMessage;
            if (WordReadingSurface.IndexOfAny(['=', '\r', '\n']) >= 0 || WordReadingPronunciation.IndexOfAny(['\r', '\n']) >= 0)
                return LocalizationService.Translate("表記とよみは1行で入力してください。表記に=は使えません。");
            if (WordReadingSurface.Length > WordReadingRules.MaximumReadingLength || WordReadingPronunciation.Length > WordReadingRules.MaximumReadingLength ||
                WordReadingSurface.Any(char.IsControl) || WordReadingPronunciation.Any(char.IsControl))
                return LocalizationService.Translate("表記とよみはそれぞれ256文字以内で入力してください。");
            if (!IsLocalWordReadingScope && WordReadingEntries.Count >= WordReadingRules.MaximumSharedReadings &&
                !WordReadingEntries.Any(item => item.SurfaceText == WordReadingSurface.Trim()))
                return LocalizationService.Translate("共通の読み方は5,000件まで登録できます。");
            if (IsLocalWordReadingScope && WordReadingSurface.Trim().Length > 0 && SelectedOverlay is { } region && !region.Text.Contains(WordReadingSurface.Trim(), StringComparison.Ordinal))
                return LocalizationService.Translate("選択中のOCR行に、この表記がありません。");
            return string.Empty;
        }
    }
    public bool HasWordReadingInputMessage => WordReadingInputMessage.Length > 0;
    private bool CanRegisterWordReading() => CanEditWordReadings && WordReadingInputMessage.Length == 0 &&
        !string.IsNullOrWhiteSpace(WordReadingSurface) && !string.IsNullOrWhiteSpace(WordReadingPronunciation) &&
        !(WordReadingEntries.Count(item => item.SurfaceText == WordReadingSurface.Trim()) == 1 &&
          WordReadingEntries.Any(item => item.SurfaceText == WordReadingSurface.Trim() && item.ReadingText == WordReadingPronunciation.Trim()));
    private void RegisterWordReading()
    {
        if (!CanRegisterWordReading() || SelectedOverlay is not { } region) return;
        var reading = new WordReading { SurfaceText = WordReadingSurface.Trim(), ReadingText = WordReadingPronunciation.Trim() };
        var entries = WordReadingEntries.Where(item => item.SurfaceText != reading.SurfaceText).Append(reading).ToArray();
        if (IsLocalWordReadingScope)
            ApplyRegionEdit("読み方を登録", [region], () => region.WordReadingsText = WordReadingRules.Format(entries));
        else
        {
            var before = CaptureProjectAnnotationSnapshot();
            _project = _project! with { WordReadings = entries };
            RefreshCommonWordReadings(); RecordProjectAnnotationChange(before, "共通の読み方を登録");
        }
        SelectedWordReading = WordReadingEntries.First(item => item.SurfaceText == reading.SurfaceText);
        StatusMessage = LocalizationService.Translate(IsLocalWordReadingScope ? "選択中のOCR行に読み方を登録しました。" : "全ページ共通の読み方を登録しました。");
    }
    private void RemoveWordReading()
    {
        if (!CanEditWordReadings || SelectedOverlay is not { } region || SelectedWordReading is not { } selected) return;
        var entries = WordReadingEntries.Where(item => item.SurfaceText != selected.SurfaceText).ToArray();
        if (IsLocalWordReadingScope)
            ApplyRegionEdit("読み方を削除", [region], () => region.WordReadingsText = WordReadingRules.Format(entries));
        else
        {
            var before = CaptureProjectAnnotationSnapshot();
            _project = _project! with { WordReadings = entries };
            RefreshCommonWordReadings(); RecordProjectAnnotationChange(before, "共通の読み方を削除");
        }
        SelectedWordReading = null; WordReadingSurface = string.Empty; WordReadingPronunciation = string.Empty;
        StatusMessage = LocalizationService.Translate("読み方を削除しました。");
    }
    private void RefreshWordReadingDraft()
    {
        var region = SelectedOverlay;
        var indices = region?.SelectedCharacterIndices.Order().ToArray() ?? [];
        if (ReferenceEquals(region, _wordReadingDraftRegion) && region?.Text == _wordReadingDraftText && indices.SequenceEqual(_wordReadingDraftIndices))
        { RaiseWordReadingCommands(); return; }
        _wordReadingDraftRegion = region; _wordReadingDraftText = region?.Text; _wordReadingDraftIndices = indices;
        SelectedWordReading = null;
        var elements = region is null ? [] : StringInfo.ParseCombiningCharacters(region.Text);
        if (region is null || indices.Length == 0 || indices[^1] >= elements.Length || indices[^1] - indices[0] + 1 != indices.Length)
        { WordReadingSurface = string.Empty; WordReadingPronunciation = string.Empty; }
        else
        {
            var start = elements[indices[0]];
            var end = indices[^1] + 1 < elements.Length ? elements[indices[^1] + 1] : region.Text.Length;
            WordReadingSurface = region.Text[start..end];
            WordReadingPronunciation = region.EffectiveWordReadings.FirstOrDefault(item => item.SurfaceText == WordReadingSurface)?.ReadingText ?? string.Empty;
        }
        RefreshWordReadingEntries();
    }
    private void RefreshWordReadingEntries()
    {
        OnPropertyChanged(nameof(WordReadingEntries)); OnPropertyChanged(nameof(HasWordReadingEntries));
        if (WordReadingSurface.Length > 0)
            WordReadingPronunciation = (IsLocalWordReadingScope ? SelectedOverlay?.EffectiveWordReadings ?? [] : CommonWordReadings)
                .FirstOrDefault(item => item.SurfaceText == WordReadingSurface)?.ReadingText ?? string.Empty;
        RaiseWordReadingCommands();
    }
    private void RaiseWordReadingCommands()
    {
        RegisterWordReadingCommand?.RaiseCanExecuteChanged(); RemoveWordReadingCommand?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditWordReadings)); OnPropertyChanged(nameof(WordReadingInputMessage));
        OnPropertyChanged(nameof(HasWordReadingInputMessage)); OnPropertyChanged(nameof(WordReadingActionText));
        OnPropertyChanged(nameof(WordReadingScopeDescription));
    }
}
