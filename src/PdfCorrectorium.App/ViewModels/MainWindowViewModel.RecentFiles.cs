using System.Collections.ObjectModel;
using System.IO;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App.ViewModels;

public sealed class RecentFileItemViewModel(string path, Func<Task> open, Func<bool> canOpen)
{
    public string FullPath { get; } = path;
    public string DisplayName { get; } = $"{Path.GetFileName(path)} — {Path.GetDirectoryName(path)}";
    public AsyncCommand OpenCommand { get; } = new(open, canOpen);
}

public partial class MainWindowViewModel
{
    private readonly RecentFilesService _recentFilesService;
    public ObservableCollection<RecentFileItemViewModel> RecentFiles { get; } = [];
    public int RecentFileCount => _recentFilesService.Files.Count;
    public bool CanOpenRecentFiles => RecentFiles.Count > 0 && !IsOpeningDocument &&
        !IsPdfExporting && !IsBackgroundOperationVisible && !_autoSaveInProgress;

    public void ReloadRecentFiles()
    {
        _recentFilesService.Reload();
        RefreshRecentFiles();
    }

    private void RefreshRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var path in _recentFilesService.Files.Take(_applicationSettings.RecentFileLimit))
            RecentFiles.Add(new RecentFileItemViewModel(path, async () => { await OpenRecentFileAsync(path); }, () => CanOpenRecentFiles));
        OnPropertyChanged(nameof(RecentFileCount));
        NotifyRecentFileCommands();
    }

    private void NotifyRecentFileCommands()
    {
        OnPropertyChanged(nameof(CanOpenRecentFiles));
        foreach (var item in RecentFiles) item.OpenCommand.RaiseCanExecuteChanged();
    }

    public Task<bool> OpenRecentFileAsync(string path) =>
        !CanOpenRecentFiles || !_recentFilesService.Files.Contains(path, StringComparer.OrdinalIgnoreCase)
            ? Task.FromResult(false)
            : OpenDocumentPathAsync(path);

    private async Task RecordRecentFileAsync(string path)
    {
        if (_applicationSettings.RecentFileLimit == 0) return;
        try
        {
            await _recentFilesService.RecordAsync(path);
            RefreshRecentFiles();
        }
        catch (Exception)
        {
            // History storage must not turn an already successful document open into a failure.
            StatusMessage = "文書は開きましたが、最近開いたファイルの履歴を保存できませんでした。";
        }
    }

    public async Task<bool> ClearRecentFilesAsync()
    {
        try
        {
            await _recentFilesService.ClearAsync();
            RefreshRecentFiles();
            StatusMessage = "最近開いたファイルの履歴をクリアしました。PDFやプロジェクト自体は削除していません。";
            return true;
        }
        catch (Exception error)
        {
            await ShowErrorAsync("最近開いたファイルの履歴をクリアできませんでした。", error);
            return false;
        }
    }
}
