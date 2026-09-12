using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// PDFの初期表示、編集画面の表示設定、プロジェクトへ保存する表示上書きを相互変換します。
/// </summary>
public static class ProjectEditorViewStateMapping
{
    /// <summary>PDFの初期表示設定から、編集画面で使用する表示状態を作成します。</summary>
    public static ProjectEditorViewState FromViewerSettings(ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new ProjectEditorViewState
        {
            PageLayout = settings.PageMode is InitialPageMode.FacingPages or InitialPageMode.ContinuousFacingPages
                ? ProjectEditorPageLayout.FacingPages
                : ProjectEditorPageLayout.SinglePage,
            PageFlow = settings.PageMode is InitialPageMode.Continuous or InitialPageMode.ContinuousFacingPages
                ? ProjectEditorPageFlow.Continuous
                : ProjectEditorPageFlow.PageByPage,
            ShowCoverSeparately = settings.ShowCoverSeparately,
            BindingDirection = settings.BindingDirection,
        };
    }

    /// <summary>現在の編集画面設定から、プロジェクトへ保存する表示上書きを作成します。</summary>
    public static ProjectEditorViewState FromApplicationSettings(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new ProjectEditorViewState
        {
            PageLayout = settings.DocumentViewMode == DocumentViewMode.FacingPages
                ? ProjectEditorPageLayout.FacingPages
                : ProjectEditorPageLayout.SinglePage,
            PageFlow = settings.DocumentPageFlowMode == DocumentPageFlowMode.Continuous
                ? ProjectEditorPageFlow.Continuous
                : ProjectEditorPageFlow.PageByPage,
            ShowCoverSeparately = settings.FacingPagesShowCoverSeparately,
            BindingDirection = settings.FacingPagesBindingDirection == FacingPageBindingDirection.RightBinding
                ? BindingDirection.RightToLeft
                : BindingDirection.LeftToRight,
        };
    }

    /// <summary>保存されたプロジェクト表示状態を、現在のアプリ設定へ一時的に重ねます。</summary>
    public static ApplicationSettings ApplyToApplicationSettings(
        ProjectEditorViewState viewState,
        ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(viewState);
        ArgumentNullException.ThrowIfNull(settings);
        return (settings with
        {
            DocumentViewMode = viewState.PageLayout == ProjectEditorPageLayout.FacingPages
                ? DocumentViewMode.FacingPages
                : DocumentViewMode.SinglePage,
            DocumentPageFlowMode = viewState.PageFlow == ProjectEditorPageFlow.Continuous
                ? DocumentPageFlowMode.Continuous
                : DocumentPageFlowMode.PageByPage,
            FacingPagesShowCoverSeparately = viewState.ShowCoverSeparately,
            FacingPagesBindingDirection = viewState.BindingDirection == BindingDirection.RightToLeft
                ? FacingPageBindingDirection.RightBinding
                : FacingPageBindingDirection.LeftBinding,
        }).Normalize();
    }
}
