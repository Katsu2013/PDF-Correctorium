using System.Net;
using System.Xml;
using System.Xml.Linq;

namespace PdfCorrectorium.Infrastructure;

public sealed record InstalledPackageIdentity(string Name, string Publisher, Version Version);

public sealed record AppInstallerUpdate(Version Version, Uri PackageUri, Uri FeedUri, byte[] ValidatedFeed)
{
    public bool IsNewerThan(InstalledPackageIdentity installed) => Version > installed.Version;
}

/// <summary>Bounded, asynchronous version checks. Windows alone verifies and installs the signed MSIX.</summary>
public sealed class AppInstallerUpdateClient : IDisposable
{
    public const int MaximumFeedBytes = 256 * 1024;
    private readonly HttpClient _http;

    public AppInstallerUpdateClient(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All });
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PdfCorrectorium");
    }

    public async Task<AppInstallerUpdate> CheckAsync(Uri feed, InstalledPackageIdentity installed, CancellationToken cancellationToken)
    {
        RequireHttps(feed, ".appinstaller");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await _http.GetAsync(feed, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException("The update feed is unavailable.", null, response.StatusCode);
        if (response.Content.Headers.ContentLength > MaximumFeedBytes) throw new InvalidDataException("Update feed is too large.");
        await using var source = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var count = await source.ReadAsync(chunk, deadline.Token).ConfigureAwait(false);
            if (count == 0) break;
            if (bytes.Length + count > MaximumFeedBytes) throw new InvalidDataException("Update feed is too large.");
            bytes.Write(chunk, 0, count);
        }
        deadline.Token.ThrowIfCancellationRequested();
        return Parse(bytes.ToArray(), feed, installed);
    }

    public static AppInstallerUpdate Parse(byte[] bytes, Uri expectedFeed, InstalledPackageIdentity installed)
    {
        RequireHttps(expectedFeed, ".appinstaller");
        if (bytes.Length is 0 or > MaximumFeedBytes) throw new InvalidDataException("Invalid update feed size.");
        using var input = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaximumFeedBytes, MaxCharactersFromEntities = 0 });
        var document = XDocument.Load(reader, LoadOptions.None);
        var root = document.Root ?? throw new InvalidDataException("Missing App Installer document.");
        var ns = root.Name.Namespace;
        if (root.Name.LocalName != "AppInstaller" || ns.NamespaceName is not
            ("http://schemas.microsoft.com/appx/appinstaller/2018" or "http://schemas.microsoft.com/appx/appinstaller/2021"))
            throw new InvalidDataException("Unsupported App Installer schema.");
        var declaredFeed = ReadUri(root, "Uri", ".appinstaller");
        if (declaredFeed != expectedFeed) throw new InvalidDataException("Update feed changed its pinned address.");
        _ = ReadVersion(root);
        var main = root.Elements(ns + "MainPackage").SingleOrDefault() ?? throw new InvalidDataException("Expected one main package.");
        if (root.Elements().Any(element => element.Name != ns + "MainPackage" && element.Name != ns + "UpdateSettings"))
            throw new InvalidDataException("Additional packages and alternate update sources are not supported.");
        if ((string?)main.Attribute("Name") != installed.Name || (string?)main.Attribute("Publisher") != installed.Publisher ||
            (string?)main.Attribute("ProcessorArchitecture") != "x64" || main.HasElements)
            throw new InvalidDataException("The update does not match this installed package.");
        var settings = root.Elements(ns + "UpdateSettings").SingleOrDefault();
        if (settings is not null)
        {
            foreach (var setting in settings.Elements())
            {
                if (setting.Name != ns + "OnLaunch" && setting.Name != ns + "AutomaticBackgroundTask")
                    throw new InvalidDataException("Unexpected or downgrade-capable update policy.");
                if (setting.Attribute("UpdateBlocksActivation") is { } blocks && !string.Equals(blocks.Value, "false", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Updates must not block application launch.");
            }
        }
        return new(ReadVersion(main), ReadUri(main, "Uri", ".msix"), expectedFeed, bytes.ToArray());
    }

    private static Version ReadVersion(XElement element)
    {
        var value = (string?)element.Attribute("Version");
        var parts = value?.Split('.');
        if (parts?.Length != 4 || parts.Any(part => part.Length == 0 || part.Any(ch => ch is < '0' or > '9') || !ushort.TryParse(part, out _)) ||
            !Version.TryParse(value, out var version) || version.Major == 0)
            throw new InvalidDataException("Invalid Windows package version.");
        return version;
    }

    private static Uri ReadUri(XElement element, string attribute, string suffix)
    {
        if (!Uri.TryCreate((string?)element.Attribute(attribute), UriKind.Absolute, out var value)) throw new InvalidDataException("Invalid update address.");
        RequireHttps(value, suffix); return value;
    }

    public static void RequireHttps(Uri address, string suffix)
    {
        if (!address.IsAbsoluteUri || address.Scheme != Uri.UriSchemeHttps || address.UserInfo.Length != 0 || address.Fragment.Length != 0 ||
            !address.AbsolutePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Expected a credential-free HTTPS update address.");
    }

    public void Dispose() => _http.Dispose();
}

public static class AppInstallerUpdateStager
{
    /// <summary>Write only a checked feed under an application-owned cache; never replace application binaries.</summary>
    public static string Stage(string cacheDirectory, AppInstallerUpdate update, InstalledPackageIdentity installed)
    {
        var checkedUpdate = AppInstallerUpdateClient.Parse(update.ValidatedFeed, update.FeedUri, installed);
        if (!checkedUpdate.IsNewerThan(installed)) throw new InvalidDataException("Do not stage an equal or older update.");
        var root = Path.GetFullPath(cacheDirectory);
        Directory.CreateDirectory(root);
        CheckDirectory(root);
        var updates = Path.Combine(root, "Updates"); Directory.CreateDirectory(updates); CheckDirectory(updates);
        var attempt = Path.Combine(updates, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(attempt); CheckDirectory(attempt);
        var path = Path.Combine(attempt, "PdfCorrectorium.appinstaller");
        using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        target.Write(checkedUpdate.ValidatedFeed); target.Flush(flushToDisk: true);
        return path;
    }
    private static void CheckDirectory(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Update staging must not use a redirected directory.");
    }
}
