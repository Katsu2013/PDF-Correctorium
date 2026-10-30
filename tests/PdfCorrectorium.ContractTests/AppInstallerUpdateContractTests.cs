using System.Net;
using System.Text;
using PdfCorrectorium.Infrastructure;

public static class AppInstallerUpdateContractTests
{
    private static readonly Uri Feed = new("https://updates.example.invalid/PdfCorrectorium.appinstaller");
    private static readonly InstalledPackageIdentity Installed = new("PdfCorrectorium.Test", "CN=Test", new Version(1, 0, 10, 0));
    private static byte[] Xml(string version = "1.0.11.0") => Encoding.UTF8.GetBytes(
        $"<AppInstaller xmlns=\"http://schemas.microsoft.com/appx/appinstaller/2021\" Uri=\"{Feed}\" Version=\"{version}\"><MainPackage Name=\"{Installed.Name}\" Publisher=\"{Installed.Publisher}\" Version=\"{version}\" ProcessorArchitecture=\"x64\" Uri=\"https://updates.example.invalid/new.msix\"/><UpdateSettings><OnLaunch HoursBetweenUpdateChecks=\"24\" ShowPrompt=\"false\" UpdateBlocksActivation=\"false\"/><AutomaticBackgroundTask/></UpdateSettings></AppInstaller>");
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(byte[] bytes)
    {
        try { AppInstallerUpdateClient.Parse(bytes, Feed, Installed); }
        catch (Exception error) when (error is InvalidDataException or System.Xml.XmlException or InvalidOperationException) { return; }
        throw new InvalidOperationException("Unsafe installer feed was accepted.");
    }
    public static Task FeedValidationAsync()
    {
        Check(AppInstallerUpdateClient.Parse(Xml(), Feed, Installed).IsNewerThan(Installed), "New package must be detected.");
        Check(!AppInstallerUpdateClient.Parse(Xml("1.0.10.0"), Feed, Installed).IsNewerThan(Installed), "Equal version is not an update.");
        Check(!AppInstallerUpdateClient.Parse(Xml("1.0.9.0"), Feed, Installed).IsNewerThan(Installed), "Older feed must never trigger downgrade.");
        var valid = Encoding.UTF8.GetString(Xml());
        foreach (var pair in new[] { ("PdfCorrectorium.Test", "Another.App"), ("CN=Test", "CN=Other"), ("ProcessorArchitecture=\"x64\"", "ProcessorArchitecture=\"arm64\""),
            ("https://updates.example.invalid/new.msix", "http://updates.example.invalid/new.msix"),
            ("https://updates.example.invalid/new.msix", "https://updates.example.invalid/installer.exe"),
            ("https://updates.example.invalid/new.msix", "https://user:pass@updates.example.invalid/new.msix"),
            ("https://updates.example.invalid/new.msix", "file:///C:/bad.msix"),
            ("https://updates.example.invalid/new.msix", "https://updates.example.invalid/new.msix#other"),
            ("Uri=\"" + Feed + "\"", "Uri=\"https://other.example.invalid/feed.appinstaller\""),
            ("<AutomaticBackgroundTask/>", "<ForceUpdateFromAnyVersion>true</ForceUpdateFromAnyVersion>"),
            ("UpdateBlocksActivation=\"false\"", "UpdateBlocksActivation=\"true\""),
            ("</AppInstaller>", "<Dependencies/></AppInstaller>"),
            ("1.0.11.0", "1.0.99999.0"), ("1.0.11.0", "1.0.11"), ("1.0.11.0", "1.0.11-dev.0") })
            Reject(Encoding.UTF8.GetBytes(valid.Replace(pair.Item1, pair.Item2, StringComparison.Ordinal)));
        Reject(Encoding.UTF8.GetBytes("<!DOCTYPE AppInstaller [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]>" + valid));
        Reject(new byte[AppInstallerUpdateClient.MaximumFeedBytes + 1]);
        Reject(Encoding.UTF8.GetBytes("<AppInstaller/>"));
        return Task.CompletedTask;
    }

    public static async Task NetworkAsync()
    {
        var requests = 0;
        using (var client = new AppInstallerUpdateClient(new Handler((request, token) =>
        {
            Check(request.RequestUri == Feed, "Only the pinned feed may be requested."); requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Xml()) });
        })))
            Check((await client.CheckAsync(Feed, Installed, default)).IsNewerThan(Installed) && requests == 1, "Download and parse the real HTTP response path.");
        foreach (var status in new[] { HttpStatusCode.Redirect, HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable })
        {
            using var client = new AppInstallerUpdateClient(new Handler((request, token) => Task.FromResult(new HttpResponseMessage(status) { Headers = { Location = new Uri("https://other.example.invalid/unsafe.appinstaller") } })));
            var failed = false;
            try { await client.CheckAsync(Feed, Installed, default); } catch (HttpRequestException) { failed = true; }
            Check(failed, "A redirect/error must not become NoUpdates.");
        }
        using (var client = new AppInstallerUpdateClient(new Handler((request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(new byte[AppInstallerUpdateClient.MaximumFeedBytes + 1]) }))))
        {
            var failed = false;
            try { await client.CheckAsync(Feed, Installed, default); } catch (InvalidDataException) { failed = true; }
            Check(failed, "Bound HTTP content before parsing.");
        }
        using (var cancellation = new CancellationTokenSource())
        using (var client = new AppInstallerUpdateClient(new Handler((request, token) => Task.Delay(Timeout.Infinite, token).ContinueWith<HttpResponseMessage>(
            _ => throw new OperationCanceledException(token), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default))))
        {
            var task = client.CheckAsync(Feed, Installed, cancellation.Token);
            cancellation.Cancel(); var cancelled = false;
            try { await task; } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "Cancel a pending network check without a result.");
        }
    }

    public static Task StagingAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PdfCorrectorium-UpdateContract-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var sentinel = Path.Combine(directory, "application.exe"); File.WriteAllText(sentinel, "unchanged");
            var update = AppInstallerUpdateClient.Parse(Xml(), Feed, Installed);
            var one = AppInstallerUpdateStager.Stage(directory, update, Installed);
            var two = AppInstallerUpdateStager.Stage(directory, update, Installed);
            Check(one != two && File.ReadAllBytes(one).SequenceEqual(Xml()), "Each attempt gets a new validated local App Installer file.");
            Check(File.ReadAllText(sentinel) == "unchanged", "Never replace application binaries from the editor.");
            var rejected = false;
            try { AppInstallerUpdateStager.Stage(directory, AppInstallerUpdateClient.Parse(Xml("1.0.9.0"), Feed, Installed), Installed); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Do not hand a downgrade to Windows.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        return Task.CompletedTask;
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
