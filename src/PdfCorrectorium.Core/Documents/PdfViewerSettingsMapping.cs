namespace PdfCorrectorium.Core.Documents;

/// <summary>
/// アプリ内の初期表示設定を、PDF Catalogへ保存する名前オブジェクトへ変換します。
/// </summary>
/// <remarks>
/// <para>
/// PDFの<c>/Direction</c>は見開きを読む方向、<c>/PageLayout</c>の
/// <c>TwoPageLeft</c>/<c>TwoPageRight</c>は奇数ページを配置する側を表します。
/// 表紙を単独表示する場合、左綴じ（右開き）の表紙は右側、
/// 右綴じ（左開き）の表紙は左側になります。
/// </para>
/// <para>
/// 本クラスは、Adobe Acrobatでの初期表示とアプリのプレビューが一致するよう、
/// 綴じ方向と表紙単独表示の組み合わせを一か所で管理します。
/// </para>
/// </remarks>
public static class PdfViewerSettingsMapping
{
    /// <summary>
    /// PDF Catalogの<c>/PageLayout</c>へ保存する名前を返します。
    /// </summary>
    /// <param name="settings">変換元となる文書の初期表示設定。</param>
    /// <returns><c>/SinglePage</c>、<c>/OneColumn</c>、または見開き用の名前。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/>が<c>null</c>の場合。</exception>
    public static string GetPageLayoutName(ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.PageMode == InitialPageMode.SinglePage)
            return "/SinglePage";
        if (settings.PageMode == InitialPageMode.Continuous)
            return "/OneColumn";

        // TwoPageLeft/TwoPageRight の Left/Right は奇数ページを置く側です。
        // 左綴じ（右開き、L2R）は表紙（1ページ目）を右、
        // 右綴じ（左開き、R2L）は表紙を左に置きます。
        // 表紙を単独表示しない場合は、最初の見開きにおける奇数ページ側を反転します。
        if (settings.ShowCoverSeparately)
            return settings.BindingDirection == BindingDirection.RightToLeft
                ? "/TwoPageLeft"
                : "/TwoPageRight";

        return settings.BindingDirection == BindingDirection.RightToLeft
            ? "/TwoPageRight"
            : "/TwoPageLeft";
    }

    /// <summary>
    /// PDF Catalogの<c>/ViewerPreferences /Direction</c>へ保存する名前を返します。
    /// </summary>
    /// <param name="settings">変換元となる文書の初期表示設定。</param>
    /// <returns>
    /// 左綴じ（右開き）の場合は<c>/L2R</c>、右綴じ（左開き）の場合は<c>/R2L</c>。
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/>が<c>null</c>の場合。</exception>
    public static string GetDirectionName(ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.BindingDirection == BindingDirection.RightToLeft ? "/R2L" : "/L2R";
    }
}
