using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Analysis;

namespace PdfCorrectorium.App;

public partial class OcrLexiconWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly OcrLexicon _before;
    private readonly ObservableCollection<RuleEditor> _rules;
    internal OcrLexiconWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel; _before = viewModel.CurrentOcrLexicon;
        InitializeComponent(); LocalizationService.Apply(this);
        KnownTermsEditor.Text = string.Join(Environment.NewLine, _before.KnownTerms);
        IgnoredTermsEditor.Text = string.Join(Environment.NewLine, _before.IgnoredTerms);
        _rules = new(_before.Rules.Select(r => new RuleEditor(r))); RulesGrid.ItemsSource = _rules;
        FrequencyGrid.ItemsSource = viewModel.OcrWordFrequencies.OrderByDescending(w => w.Count).ThenBy(w => w.Word, StringComparer.Ordinal).ToArray();
        FrequencyStatus.Text = viewModel.IsOcrLexiconReady ? viewModel.LexiconPreparationStatus : LocalizationService.Translate("語彙を背景で準備しています。この画面を開き直すと最新の集計を確認できます。");
    }
    private void AddRule_OnClick(object sender, RoutedEventArgs e)
    {
        if (_rules.Count >= OcrLexiconPolicy.MaximumRules) return;
        var row = new RuleEditor(new()); _rules.Add(row); RulesGrid.SelectedItem = row; RulesGrid.ScrollIntoView(row);
    }
    private void DeleteRule_OnClick(object sender, RoutedEventArgs e)
    { foreach (var row in RulesGrid.SelectedItems.Cast<RuleEditor>().ToArray()) _rules.Remove(row); }
    private static string[] ReadTerms(string text) => text.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();
    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        RulesGrid.CommitEdit(DataGridEditingUnit.Cell, true); RulesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (!ReferenceEquals(_before, _viewModel.CurrentOcrLexicon))
        { ValidationText.Text = LocalizationService.Translate("辞書が別の操作で変更されました。閉じてから開き直してください。"); return; }
        try
        {
            var next = new OcrLexicon { KnownTerms = ReadTerms(KnownTermsEditor.Text), IgnoredTerms = ReadTerms(IgnoredTermsEditor.Text), Rules = _rules.Select(r => r.ToRule()).ToArray() };
            OcrLexiconPolicy.Validate(next);
            if (_viewModel.SetOcrLexicon(next)) DialogResult = true;
            else ValidationText.Text = LocalizationService.Translate("現在は保存できません。処理が完了してからやり直してください。");
        }
        catch (System.IO.InvalidDataException error) { ValidationText.Text = LocalizationService.Translate(error.Message); }
    }
    private sealed class RuleEditor(OcrCorrectionRule rule)
    {
        public Guid Id { get; } = rule.Id;
        public string Source { get; set; } = rule.Source;
        public string Target { get; set; } = rule.Target;
        public bool Enabled { get; set; } = rule.Enabled;
        public bool AllowAutomatic { get; set; } = rule.AllowAutomatic;
        public OcrCorrectionRule ToRule() => new() { Id = Id, Source = Source.Trim(), Target = Target.Trim(), Enabled = Enabled, AllowAutomatic = AllowAutomatic };
    }
}
