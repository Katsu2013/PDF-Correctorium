using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App.ViewModels;

public sealed partial class OverlayRegionViewModel
{
    private IReadOnlyList<WordReading> _commonWordReadings = [];
    private WordReadingMatcher _commonMatcher = WordReadingMatcher.Empty;
    internal void SetCommonWordReadings(IReadOnlyList<WordReading> readings, WordReadingMatcher matcher)
    {
        if (ReferenceEquals(_commonWordReadings, readings)) return;
        _commonWordReadings = readings; _commonMatcher = matcher; _wordReadingMatches = null;
        OnPropertyChanged(nameof(WordReadingMatches));
    }
    public IReadOnlyList<WordReading> EffectiveWordReadings => WordReadingRules.Merge(_commonWordReadings, WordReadingDefinitions);
    private (IReadOnlyList<WordReading> Rules, string Error)? _wordReadingDefinitions;
    private IReadOnlyList<WordReadingMatch>? _wordReadingMatches;
    private (IReadOnlyList<WordReading> Rules, string Error) ReadingDefinitionState
    {
        get
        {
            if (_wordReadingDefinitions is { } cached) return cached;
            WordReadingRules.TryParse(WordReadingsText, out var rules, out var error);
            return (_wordReadingDefinitions = (rules, error)).Value;
        }
    }
    public IReadOnlyList<WordReading> WordReadingDefinitions => ReadingDefinitionState.Rules;
    public IReadOnlyList<WordReadingMatch> WordReadingMatches => _wordReadingMatches ??=
        HasWordReadingErrors ? [] : _commonMatcher.FindMatches(Text, WordReadingDefinitions);
}
