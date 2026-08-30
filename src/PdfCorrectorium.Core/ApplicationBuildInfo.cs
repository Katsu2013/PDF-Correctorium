using System.Reflection;

namespace PdfCorrectorium.Core;

/// <summary>共通ビルド設定から埋め込まれた製品版番号を、画面・ログ・保存形式で共有します。</summary>
public static class ApplicationBuildInfo
{
    public static string InformationalVersion { get; } =
        typeof(ApplicationBuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? throw new InvalidOperationException("Missing application informational version.");

    public static string Version { get; } = InformationalVersion.Split('+')[0];

    public static string NumericVersion { get; } =
        typeof(ApplicationBuildInfo).Assembly.GetName().Version?.ToString(4)
        ?? throw new InvalidOperationException("Missing application assembly version.");

    public static string WindowTitle => $"PDF Correctorium — v{Version}";

    public static string AboutText =>
        $"PDF Correctorium\nVersion {InformationalVersion}\nBuild {NumericVersion}\n\nApache License 2.0";
}
