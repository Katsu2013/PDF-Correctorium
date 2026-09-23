using System.IO;
using System.Windows;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

public partial class RecoveryProjectsWindow : Window
{
    private readonly List<RecoveryProjectCandidate> _candidates;

    public RecoveryProjectsWindow(IReadOnlyList<RecoveryProjectCandidate> candidates)
    {
        _candidates = candidates.ToList();
        InitializeComponent();
        CandidateList.DataContext = _candidates;
        CandidateList.SelectedIndex = 0;
        LocalizationService.Apply(this);
    }

    public string? SelectedPath { get; private set; }

    private void Open_OnClick(object sender, RoutedEventArgs e)
    {
        if (CandidateList.SelectedItem is not RecoveryProjectCandidate selected)
        {
            MessageBox.Show(this, "開く復旧データを選択してください。", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SelectedPath = selected.Path;
        DialogResult = true;
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = CandidateList.SelectedItems.OfType<RecoveryProjectCandidate>().ToArray();
        if (selected.Length == 0) return;
        if (MessageBox.Show(this, $"選択した{selected.Length}件の復旧データを削除します。元に戻せません。",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        foreach (var candidate in selected)
        {
            try
            {
                File.Delete(candidate.Path);
                _candidates.Remove(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, $"復旧データを削除できませんでした。\n{ex.Message}", Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        CandidateList.Items.Refresh();
        CandidateList.SelectedIndex = _candidates.Count > 0 ? 0 : -1;
        if (_candidates.Count == 0) Close();
    }
}
