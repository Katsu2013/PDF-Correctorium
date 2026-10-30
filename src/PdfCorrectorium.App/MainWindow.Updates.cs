using System.Diagnostics;
using System.Windows;
using System.IO;
using System.Net.Http;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Infrastructure;

namespace PdfCorrectorium.App;

public partial class MainWindow
{
    internal Action<Uri>? UpdateLaunchOverride { get; set; }
    internal Func<(UpdateDestination Destination, InstalledPackageIdentity? Identity)>? UpdateContextOverride { get; set; }
    internal Func<Uri, InstalledPackageIdentity, CancellationToken, Task<AppInstallerUpdate>>? UpdateCheckOverride { get; set; }
    internal Func<Version, bool>? UpdateConfirmOverride { get; set; }
    internal Action<string>? UpdateErrorOverride { get; set; }
    internal Task UpdateCheckTask { get; private set; } = Task.CompletedTask;
    internal bool HasPendingInstaller => _pendingInstaller is not null;
    private bool _updateCheckActive;
    private CancellationTokenSource? _updateCheckCancellation;
    private (AppInstallerUpdate Update, InstalledPackageIdentity Identity)? _pendingInstaller;

    private (UpdateDestination Destination, InstalledPackageIdentity? Identity) GetUpdateContext()
    {
        if (UpdateContextOverride is { } read) return read();
        var destination = DistributionUpdates.GetDestination(AppContext.BaseDirectory, ApplicationPackageIdentity.IsPackaged);
        return (destination, destination.Channel == DistributionChannel.Direct ? ApplicationPackageIdentity.ReadCurrent() : null);
    }

    private void InitializeDistributionUpdates()
    {
        try
        {
            var channel = GetUpdateContext().Destination.Channel;
            CheckForUpdatesMenuItem.Header = channel == DistributionChannel.Portable ? "配布ページを開く(_U)" : "更新を確認(_U)";
        }
        catch (Exception error) when (IsUpdateError(error)) { }
        Closed += (_, _) =>
        {
            _updateCheckCancellation?.Cancel();
            if (_pendingInstaller is not { } pending) return;
            _pendingInstaller = null;
            try
            {
                var path = AppInstallerUpdateStager.Stage(_applicationPaths.CacheDirectory, pending.Update, pending.Identity);
                LaunchUpdate(new Uri(path));
            }
            catch (Exception error) when (IsUpdateError(error))
            {
                var message = LocalizationService.Translate("Windowsの更新画面を開けませんでした。アプリを再起動して、もう一度更新を確認してください。");
                if (UpdateErrorOverride is { } report) report(message);
                else MessageBox.Show(message, "PDF Correctorium", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
    }

    private void CheckForUpdates_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_updateCheckActive && _pendingInstaller is null) UpdateCheckTask = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var (destination, identity) = GetUpdateContext();
            if (destination.Channel != DistributionChannel.Direct)
            {
                if (destination.Address is { } address) LaunchUpdate(address);
                ViewModel.StatusMessageForInteraction(destination.Message);
                return;
            }
            if (identity is null || destination.Address is null) throw new InvalidOperationException("Installed package identity is required.");
            _updateCheckActive = true;
            CheckForUpdatesMenuItem.IsEnabled = false;
            using var cancellation = new CancellationTokenSource();
            _updateCheckCancellation = cancellation;
            ViewModel.StatusMessageForInteraction("更新を確認しています…");
            AppInstallerUpdate result;
            if (UpdateCheckOverride is { } check) result = await check(destination.Address, identity, cancellation.Token);
            else
            {
                using var client = new AppInstallerUpdateClient();
                result = await client.CheckAsync(destination.Address, identity, cancellation.Token);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (!result.IsNewerThan(identity))
            {
                ViewModel.StatusMessageForInteraction("この配信先に新しい更新はありません。");
                return;
            }
            ViewModel.StatusMessageForInteraction("新しいバージョンが見つかりました。");
            var accept = UpdateConfirmOverride?.Invoke(result.Version) ?? MessageBox.Show(
                string.Format(LocalizationService.Translate("新しいバージョン {0} があります。\n保存確認後にアプリを終了し、Windowsの更新画面を開きますか？\n作業を続ける場合は「いいえ」を選んでください。"), result.Version),
                "PDF Correctorium", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No) == MessageBoxResult.Yes;
            if (!accept) return;
            if (ViewModel.IsOpeningDocument || ViewModel.IsBackgroundOperationVisible || ViewModel.IsPdfExporting)
            {
                ViewModel.StatusMessageForInteraction("処理中は更新を開始できません。完了後にもう一度確認してください。");
                return;
            }
            // Use normal save/close validation. Only Closed may launch Windows.
            _pendingInstaller = (result, identity);
            Close();
        }
        catch (OperationCanceledException)
        {
            if (IsLoaded) ViewModel.StatusMessageForInteraction("更新の確認を中断しました。接続を確認して、後でお試しください。");
        }
        catch (Exception error) when (IsUpdateError(error))
        {
            ViewModel.StatusMessageForInteraction("更新を確認できませんでした。接続や配信設定を確認して、後でお試しください。");
        }
        finally
        {
            _updateCheckActive = false;
            _updateCheckCancellation = null;
            CheckForUpdatesMenuItem.IsEnabled = _pendingInstaller is null;
        }
    }

    private void CancelPendingUpdate()
    {
        if (_pendingInstaller is null) return;
        _pendingInstaller = null;
        CheckForUpdatesMenuItem.IsEnabled = true;
        ViewModel.StatusMessageForInteraction("更新を保留しました。作業を続けられます。");
    }

    private void LaunchUpdate(Uri address)
    {
        if (UpdateLaunchOverride is { } launch) launch(address);
        else Process.Start(new ProcessStartInfo(address.IsFile ? address.LocalPath : address.AbsoluteUri) { UseShellExecute = true });
    }

    private static bool IsUpdateError(Exception error) => error is IOException or System.Text.Json.JsonException or
        System.Xml.XmlException or HttpRequestException or System.ComponentModel.Win32Exception or InvalidOperationException or
        KeyNotFoundException or FormatException or ArgumentException or UnauthorizedAccessException or System.Security.SecurityException;
}
