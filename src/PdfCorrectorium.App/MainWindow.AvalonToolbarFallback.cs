using AvalonDock.Layout;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App;

public partial class MainWindow
{
    /// <summary>
    /// AvalonDock's supported fallback hook is used only when Dock() cannot find
    /// its old (pruned, empty) container. Explicit native guide drops are unchanged.
    /// Never choose a toolbar's destination based on the active property tab.
    /// </summary>
    private sealed class ToolbarFallbackLayoutStrategy(MainWindow owner) : ILayoutUpdateStrategy
    {
        public bool BeforeInsertAnchorable(LayoutRoot root, LayoutAnchorable anchorable, ILayoutContainer destinationContainer)
        {
            if (anchorable.ContentId is not ("main-toolbar" or "page-toolbar")) return false;
            var side = anchorable.ContentId == "main-toolbar" ? owner._mainToolbarSide : owner._pageToolbarSide;
            var target = side is ToolbarDockSide.Top or ToolbarDockSide.Bottom
                ? owner.GetToolbarBand(ToAnchorSide(side)) : owner._avalonWorkspaceBand;
            if (target is null) return false;
            (anchorable.Parent as LayoutAnchorablePane)?.Children.Remove(anchorable);
            var pane = CreateAvalonToolbarPane(anchorable);
            if (side == ToolbarDockSide.Left || side is ToolbarDockSide.Top or ToolbarDockSide.Bottom && anchorable.ContentId == "main-toolbar")
                target.Children.Insert(0, pane);
            else target.Children.Add(pane);
            owner._toolbarChromeNeedsRefresh = true;
            return true;
        }

        public void AfterInsertAnchorable(LayoutRoot root, LayoutAnchorable anchorable) { }
        public bool BeforeInsertDocument(LayoutRoot root, LayoutDocument document, ILayoutContainer destinationContainer) => false;
        public void AfterInsertDocument(LayoutRoot root, LayoutDocument document) { }
    }
}
