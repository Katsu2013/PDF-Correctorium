namespace PdfCorrectorium.Infrastructure;

/// <summary>設定・ログ等の保存先を決めるアプリケーション配置形態です。</summary>
public enum StorageMode { Portable, Installed }

/// <summary>
/// 実行形態に応じて解決された設定、ログ、キャッシュ、作業領域のパスです。
/// </summary>
/// <param name="Mode">Portableまたはインストールの保存形態。</param>
/// <param name="ConfigurationDirectory">利用者設定の保存先。</param>
/// <param name="LogDirectory">診断ログの保存先。</param>
/// <param name="CacheDirectory">再生成可能な一時キャッシュの保存先。</param>
/// <param name="WorkspaceDirectory">展開中プロジェクトと自動保存の作業先。</param>
public sealed record ApplicationPaths(
    StorageMode Mode,
    string ConfigurationDirectory,
    string LogDirectory,
    string CacheDirectory,
    string WorkspaceDirectory);

/// <summary>Portable版とインストール版のデータ保存先を一元的に解決します。</summary>
public static class ApplicationPathResolver
{
    /// <summary>現在の製品名に対応するAppData配下のディレクトリ名です。</summary>
    private const string ApplicationDirectoryName = "PdfCorrectorium";
    /// <summary>名称変更前の設定を引き継ぐために参照する旧ディレクトリ名です。</summary>
    private const string LegacyApplicationDirectoryName = "PdfOcrEditor";

    /// <summary>
    /// 実行ファイルと同じ場所に<c>portable.marker</c>があればPortableモード、
    /// それ以外はAppDataを使用するインストールモードとしてパスを返します。
    /// </summary>
    /// <param name="executableDirectory">実行ファイルが置かれているディレクトリ。</param>
    public static ApplicationPaths Resolve(string executableDirectory)
    {
        var basePath = Path.GetFullPath(executableDirectory);
        if (File.Exists(Path.Combine(basePath, "portable.marker")))
        {
            return new(StorageMode.Portable,
                Path.Combine(basePath, "config"),
                Path.Combine(basePath, "logs"),
                Path.Combine(basePath, "cache"),
                Path.Combine(basePath, "workspaces"));
        }

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new(StorageMode.Installed,
            Path.Combine(roaming, ApplicationDirectoryName),
            Path.Combine(local, ApplicationDirectoryName, "Logs"),
            Path.Combine(local, ApplicationDirectoryName, "Cache"),
            Path.Combine(local, ApplicationDirectoryName, "Workspaces"));
    }

    /// <summary>アプリケーションが使用する全ディレクトリを作成します。</summary>
    public static void EnsureDirectories(ApplicationPaths paths)
    {
        if (paths.Mode == StorageMode.Installed)
            CopyLegacyConfigurationIfNeeded(paths.ConfigurationDirectory);

        Directory.CreateDirectory(paths.ConfigurationDirectory);
        Directory.CreateDirectory(paths.LogDirectory);
        Directory.CreateDirectory(paths.CacheDirectory);
        Directory.CreateDirectory(paths.WorkspaceDirectory);
    }

    /// <summary>
    /// 新名称側に設定がまだ存在しない場合だけ、旧名称のRoaming AppData設定をコピーします。
    /// </summary>
    /// <remarks>
    /// 旧アプリを引き続き起動できるよう、移動ではなくコピーを使用します。ログ、キャッシュ、
    /// 展開中ワークスペースは再生成可能または一時的なため移行対象に含めません。
    /// </remarks>
    private static void CopyLegacyConfigurationIfNeeded(string destinationDirectory)
    {
        if (Directory.Exists(destinationDirectory) && Directory.EnumerateFileSystemEntries(destinationDirectory).Any())
            return;

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var legacyDirectory = Path.Combine(roaming, LegacyApplicationDirectoryName);
        if (!Directory.Exists(legacyDirectory)) return;

        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(legacyDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(legacyDirectory, sourceFile);
            var destinationFile = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: false);
        }
    }
}
