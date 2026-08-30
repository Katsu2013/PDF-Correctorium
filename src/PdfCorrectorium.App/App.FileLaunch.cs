using System.Windows.Threading;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

public partial class App
{
    /// <summary>Windowsから渡された1つのファイルを、画面の初期化後に通常の読込経路で開きます。</summary>
    private async Task<bool> OpenStartupFileAsync(MainWindow window, string[] arguments)
    {
        if (arguments.Length == 0) return true;
        var viewModel = (MainWindowViewModel)window.DataContext;
        if (arguments.Length != 1)
        {
            await viewModel.ReportStartupFileErrorAsync(new ArgumentException(
                "一度に指定できるファイルは1つです。関連付けの起動コマンドではファイル名を引用符で囲んでください。"));
            _diagnostics?.Write("startup.file-open.failed", "Expected exactly one file argument.");
            return false;
        }
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        var opened = await viewModel.OpenDocumentPathAsync(arguments[0]);
        _diagnostics?.Write(opened ? "startup.file-open.complete" : "startup.file-open.failed",
            $"File: {arguments[0]}; pages: {viewModel.PageItems.Count}; preview: {viewModel.HasPreview}");
        return opened;
    }
}
