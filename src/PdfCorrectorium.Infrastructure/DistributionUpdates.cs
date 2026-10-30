using System.Text.Json;

namespace PdfCorrectorium.Infrastructure;

public enum DistributionChannel { Development, Portable, Store, Direct }
public sealed record UpdateDestination(Uri? Address, string Message)
{
    public DistributionChannel Channel { get; init; }
}

/// <summary>Windows/Store owns installation; this router never replaces files or closes documents.</summary>
public static class DistributionUpdates
{
    public static readonly Uri ReleasesPage = new("https://github.com/Katsu2013/PDF-Correctorium/releases");
    public static readonly Uri StoreUpdatesPage = new("ms-windows-store://downloadsandupdates");
    public const string DescriptorFileName = "distribution.json";

    public static UpdateDestination GetDestination(string executableDirectory, bool packaged)
    {
        // Only package-protected metadata may redirect an update command.
        if (!packaged) return new(ReleasesPage, "配布ページを開きました。ZIP版は新しい版を別フォルダーへ展開して更新してください。") { Channel = DistributionChannel.Portable };
        var path = Path.Combine(executableDirectory, DescriptorFileName);
        if (!File.Exists(path)) return Unconfigured();
        using var stream = File.OpenRead(path);
        return ReadPackageDestination(stream);
    }

    public static UpdateDestination ReadPackageDestination(Stream stream)
    {
        var buffer = new byte[16 * 1024 + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = stream.Read(buffer.AsSpan(length));
            if (read == 0) break;
            length += read;
        }
        if (length == buffer.Length) throw new InvalidDataException("Update descriptor exceeds its limit.");
        using var document = JsonDocument.Parse(buffer.AsMemory(0, length), new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported update descriptor.");
        var channel = root.GetProperty("channel").GetString();
        if (channel == "Store") return new(StoreUpdatesPage, "Microsoft Storeの更新ページを開きました。更新はStoreが管理します。") { Channel = DistributionChannel.Store };
        if (channel == "Development") return Unconfigured();
        if (channel != "Direct") throw new InvalidDataException("Unknown distribution channel.");
        var value = root.GetProperty("appInstallerUri").GetString();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Fragment) ||
            !address.AbsolutePath.EndsWith(".appinstaller", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A direct update must reference an HTTPS App Installer file.");
        return new(address, "Windowsの更新配信先を確認します。") { Channel = DistributionChannel.Direct };
    }

    private static UpdateDestination Unconfigured() => new(null,
        "このMSIXは公開前の検証用です。Store登録または署名と更新配信先の設定後に更新できます。");
}
