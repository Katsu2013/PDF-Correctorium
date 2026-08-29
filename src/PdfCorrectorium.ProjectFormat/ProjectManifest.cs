namespace PdfCorrectorium.ProjectFormat;

/// <summary>
/// ZIP互換の<c>.pdfocrproj</c>を識別し、互換性を判定するためのマニフェストです。
/// </summary>
public sealed record ProjectManifest
{
    /// <summary>対応するプロジェクト形式を識別する固定文字列です。</summary>
    public const string CurrentFormat = "PdfCorrectoriumProject";
    /// <summary>旧名称のアプリで保存されたプロジェクト形式識別子です。</summary>
    public const string LegacyFormat = "PdfOcrEditorProject";
    /// <summary>このアプリが新規保存するプロジェクト形式のバージョンです。</summary>
    public const string CurrentVersion = "1.0";
    /// <summary>読み込んだコンテナの形式識別子です。</summary>
    public string Format { get; init; } = CurrentFormat;
    /// <summary>読み込んだコンテナのデータ構造バージョンです。</summary>
    public string FormatVersion { get; init; } = CurrentVersion;
    /// <summary>このプロジェクトを安全に開ける最小アプリバージョンです。</summary>
    public string MinimumApplicationVersion { get; init; } = "0.1.0";
    /// <summary>最後に保存したアプリのバージョンです。</summary>
    public string ApplicationVersion { get; init; } = "0.1.0";
    /// <summary>project.jsonとmanifest.jsonの対応を確認するプロジェクトIDです。</summary>
    public Guid ProjectId { get; init; }
    /// <summary>プロジェクト作成時刻です。</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }
    /// <summary>コンテナを最後に正常保存した時刻です。</summary>
    public DateTimeOffset LastSavedAtUtc { get; init; }

    /// <summary>
    /// 現在または旧名称のアプリが保存した互換プロジェクト形式かを判定します。
    /// </summary>
    public static bool IsSupportedFormat(string? format) =>
        string.Equals(format, CurrentFormat, StringComparison.Ordinal) ||
        string.Equals(format, LegacyFormat, StringComparison.Ordinal);
}

/// <summary>プロジェクトパッケージ検証で検出した1件の問題を表します。</summary>
/// <param name="Code">機械判定に使用する安定した問題コード。</param>
/// <param name="Message">利用者または診断ログへ表示する説明。</param>
/// <param name="IsError">プロジェクトを安全に開けない問題の場合は<c>true</c>。</param>
public sealed record ProjectValidationIssue(string Code, string Message, bool IsError);

/// <summary>プロジェクトパッケージの検証結果をまとめます。</summary>
/// <param name="Issues">検証中に検出したエラーおよび警告の一覧。</param>
public sealed record ProjectValidationResult(IReadOnlyList<ProjectValidationIssue> Issues)
{
    /// <summary>エラー重大度の問題が1件もないかを示します。</summary>
    public bool IsValid => Issues.All(x => !x.IsError);
}
