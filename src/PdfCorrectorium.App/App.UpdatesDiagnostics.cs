using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.Infrastructure;
using PdfCorrectorium.ProjectFormat;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task RunUpdatesTestAsync(MainWindow main, string[] args, int option)
    {
        var output = Path.GetFullPath(args[option + 1]);
        var checks = new List<string>();
        MainWindow? second = null;
        try
        {
            Directory.CreateDirectory(output);
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add("PASS: " + message); }
            async Task Until(Func<bool> condition)
            {
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Update UI did not settle."); await Task.Delay(20); }
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
            async Task Click(MainWindow window)
            {
                ((MenuItem)window.FindName("CheckForUpdatesMenuItem")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await window.UpdateCheckTask;
            }
            main.ShowActivated = false; main.ShowInTaskbar = false; main.Opacity = 0; main.Show();
            main.ClosePromptOverride = () => MessageBoxResult.No;
            var vm = (MainWindowViewModel)main.DataContext;
            var launched = new List<Uri>(); main.UpdateLaunchOverride = launched.Add;
            Check(ApplicationPackageIdentity.IsPackaged || ApplicationPackageIdentity.ReadCurrent() is null,
                "Actual package identity API reports an unpackaged diagnostic without inventing an identity.");
            Check(((MenuItem)main.FindName("CheckForUpdatesMenuItem")).Header.ToString()!.Contains("配布ページ", StringComparison.Ordinal),
                "Portable UI names its action honestly as opening the releases page.");
            var requests = 0;
            main.UpdateCheckOverride = (_, _, _) => { requests++; throw new InvalidOperationException("Unexpected network check."); };
            await Click(main);
            Check(launched.Count == 1 && launched[0] == DistributionUpdates.ReleasesPage && requests == 0 && !vm.HasUnsavedChanges,
                "ZIP opens only the release page and never invokes an installer check.");
            var development = new UpdateDestination(null, "未設定") { Channel = DistributionChannel.Development };
            main.UpdateContextOverride = () => (development, null); await Click(main);
            Check(launched.Count == 1 && requests == 0, "Unsigned development packages never claim automatic update readiness.");
            var store = new UpdateDestination(DistributionUpdates.StoreUpdatesPage, "Store") { Channel = DistributionChannel.Store };
            main.UpdateContextOverride = () => (store, null); await Click(main);
            Check(launched[^1] == DistributionUpdates.StoreUpdatesPage && requests == 0, "Store keeps ownership of its package updates.");

            var feed = new Uri("https://updates.example.invalid/PdfCorrectorium.appinstaller");
            var appVersion = typeof(MainWindow).Assembly.GetName().Version!;
            var identity = new InstalledPackageIdentity("PdfCorrectorium.Test", "CN=Diagnostic", new Version(appVersion.Major, appVersion.Minor, appVersion.Revision, 0));
            var next = new Version(identity.Version.Major, identity.Version.Minor, identity.Version.Build + 1, 0);
            AppInstallerUpdate Candidate(Version version) => AppInstallerUpdateClient.Parse(Encoding.UTF8.GetBytes(
                $"<AppInstaller xmlns=\"http://schemas.microsoft.com/appx/appinstaller/2021\" Uri=\"{feed}\" Version=\"{version}\"><MainPackage Name=\"{identity.Name}\" Publisher=\"{identity.Publisher}\" Version=\"{version}\" ProcessorArchitecture=\"x64\" Uri=\"https://updates.example.invalid/new.msix\"/></AppInstaller>"), feed, identity);
            var direct = new UpdateDestination(feed, "Direct") { Channel = DistributionChannel.Direct };
            main.UpdateContextOverride = () => (direct, identity);
            var pending = new TaskCompletionSource<AppInstallerUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
            main.UpdateCheckOverride = async (_, _, token) => { requests++; return await pending.Task.WaitAsync(token); };
            ((MenuItem)main.FindName("CheckForUpdatesMenuItem")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var active = main.UpdateCheckTask;
            var heartbeat = false; await Dispatcher.InvokeAsync(() => heartbeat = true, DispatcherPriority.Background);
            Check(heartbeat && !active.IsCompleted && !((MenuItem)main.FindName("CheckForUpdatesMenuItem")).IsEnabled && !vm.IsBackgroundOperationVisible,
                "An actual pending check leaves editing/Dispatcher responsive without a blocking document overlay.");
            ((MenuItem)main.FindName("CheckForUpdatesMenuItem")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(ReferenceEquals(active, main.UpdateCheckTask) && requests == 1, "Repeated clicks share the in-flight check.");
            pending.SetResult(Candidate(identity.Version)); await active;
            Check(vm.StatusMessage.Contains("新しい更新はありません", StringComparison.Ordinal) && main.IsLoaded && !main.HasPendingInstaller,
                "Equal feed version reports no update without closing or staging.");
            main.UpdateCheckOverride = (_, _, _) => Task.FromException<AppInstallerUpdate>(new HttpRequestException("Offline")); await Click(main);
            Check(vm.StatusMessage.Contains("確認できません", StringComparison.Ordinal) && ((MenuItem)main.FindName("CheckForUpdatesMenuItem")).IsEnabled,
                "Connection failure is distinct from NoUpdates and allows retry.");
            main.UpdateCheckOverride = (_, _, _) => Task.FromResult(Candidate(next)); main.UpdateConfirmOverride = _ => false;
            await Click(main);
            Check(main.IsLoaded && !main.HasPendingInstaller && launched.Count == 2, "Declining an available update preserves the open editor.");

            var pdf = Path.Combine(output, "source.pdf"); WriteDocumentUiTestPdf(pdf);
            var packages = new ProjectPackageService();
            var geometry = new TextGeometry { LocalBounds = new(new(20, 300), new(200, 24)), RotationCenter = new(120, 312) };
            var region = new OcrTextRegion { OriginalText = "before", OriginalGeometry = geometry, EditedGeometry = geometry };
            var projectPath = Path.Combine(output, "work.pdfocrproj");
            await packages.SaveAsync(projectPath, new PdfCorrectoriumProject { Name = "Update safety", SourcePdf = await packages.CreateSourceReferenceAsync(pdf),
                Pages = [new() { PageNumber = 1, WidthPoints = 300, HeightPoints = 400, TextRegions = [region] }, new() { PageNumber = 2, WidthPoints = 300, HeightPoints = 400 }] });
            await vm.LoadProjectForDiagnosticsAsync(projectPath); await vm.EnsureDocumentMetadataAsync();
            vm.OverlayItems.Single(item => item.Id == region.Id).Text = "keep unsaved";
            main.ClosePromptOverride = () => MessageBoxResult.Cancel; main.UpdateConfirmOverride = _ => true;
            await Click(main); await Until(() => !main.HasPendingInstaller);
            Check(main.IsLoaded && vm.HasUnsavedChanges && launched.Count == 2 && ((MenuItem)main.FindName("CheckForUpdatesMenuItem")).IsEnabled,
                "Cancelling the real unsaved-work close cancels the installer handoff and restores retry.");
            Check(!Directory.Exists(Path.Combine(output, ".appstate", "cache", "Updates")), "Cancelled close does not leave a staged installer.");

            second = new MainWindow(diagnosticDirectory: Path.Combine(output, "saved-close")) { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
            second.Show(); var secondVm = (MainWindowViewModel)second.DataContext;
            await secondVm.LoadProjectForDiagnosticsAsync(projectPath); await secondVm.EnsureDocumentMetadataAsync();
            secondVm.OverlayItems.Single(item => item.Id == region.Id).Text = "saved before update";
            second.UpdateContextOverride = () => (direct, identity);
            second.UpdateCheckOverride = (_, _, _) => Task.FromResult(Candidate(next));
            second.UpdateConfirmOverride = _ => true; second.ClosePromptOverride = () => MessageBoxResult.Yes;
            var handoff = new List<Uri>(); var handoffAfterClose = false; string? handoffError = null;
            var savedWindow = second;
            second.UpdateLaunchOverride = uri => { handoffAfterClose = !savedWindow.IsVisible && !secondVm.HasUnsavedChanges; handoff.Add(uri); };
            second.UpdateErrorOverride = error => handoffError = error;
            await Click(second); await Until(() => !savedWindow.IsVisible);
            Check(handoff.Count == 1 && handoffAfterClose && handoffError is null, "The native installer handoff happens only after successful saving and actual window close.");
            Check(handoff[0].IsFile && File.Exists(handoff[0].LocalPath) && handoff[0].LocalPath.EndsWith(".appinstaller", StringComparison.Ordinal),
                "Handoff opens a checked local App Installer document rather than a browser or downloaded executable.");
            var saved = await packages.OpenAsync(projectPath);
            Check(saved.Pages[0].TextRegions.Single().EffectiveText == "saved before update", "The real project saved the edit before Windows receives the installer.");
            second = null;
            Check(!main.HasPendingInstaller && main.IsLoaded, "An unrelated instance is never forcibly terminated.");
            second = new MainWindow(diagnosticDirectory: Path.Combine(output, "cancel-network")) { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
            second.Show(); second.UpdateContextOverride = () => (direct, identity);
            CancellationToken checkCancellation = default;
            var waiting = new TaskCompletionSource<AppInstallerUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
            second.UpdateCheckOverride = async (_, _, token) => { checkCancellation = token; return await waiting.Task.WaitAsync(token); };
            var interruptedLaunches = 0; second.UpdateLaunchOverride = _ => interruptedLaunches++;
            ((MenuItem)second.FindName("CheckForUpdatesMenuItem")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var interruptedCheck = second.UpdateCheckTask;
            Check(!interruptedCheck.IsCompleted, "A slow network request does not block ordinary closing.");
            second.Close(); await interruptedCheck;
            Check(checkCancellation.IsCancellationRequested && interruptedLaunches == 0, "Closing during a check cancels it without launching a stale installer.");
            second = null;
            Check(LocalizationService.Translate("配布ページを開く").Length > 0, "Update action captions remain localizable.");
            File.WriteAllLines(Path.Combine(output, "checks.txt"), checks);
            main.ClosePromptOverride = () => MessageBoxResult.No; main.Close(); Shutdown(0);
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
            File.WriteAllLines(Path.Combine(output, "checks.txt"), checks);
            if (second is not null) { second.ClosePromptOverride = () => MessageBoxResult.No; second.Close(); }
            Shutdown(1);
        }
    }
}
