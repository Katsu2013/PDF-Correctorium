using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using PdfCorrectorium.App.Resources;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

public partial class App
{
    /// <summary>Exercise the actual shared styles, rather than merely counting XAML bindings.</summary>
    private async Task RunPropertyPaneConsistencyChecksAsync(MainWindow window, MainWindowViewModel vm,
        Action<bool, string> check, Func<Task> layout, Action<string> snapshot, string directory)
    {
        var pane = (ScrollViewer)window.FindName("KeyboardPropertiesPane");
        var linkPane = (ScrollViewer)window.FindName("LinkPropertiesScroll");
        var modeBeforeTabVisit = vm.EditorMode;
        foreach (var dock in window.PropertyPanes.Values) { dock.IsSelected = true; await layout(); }
        check(vm.EditorMode == modeBeforeTabVisit, "Visiting each property tab does not change the editing mode.");
        window.PropertyPanes["properties"].IsSelected = true; await layout();
        // Materialize template content once so coverage includes initially collapsed details.
        var details = (Expander)window.FindName("OcrDetailsExpander");
        details.IsExpanded = true; await layout(); details.IsExpanded = false; await layout();
        var readings = (Expander)window.FindName("WordReadingExpander");
        var raw = (Expander)window.FindName("WordReadingsAdvancedExpander");
        readings.IsExpanded = true; raw.IsExpanded = true; await layout();
        raw.IsExpanded = false; readings.IsExpanded = false; await layout();
        var textStyle = (Style)window.FindResource("PropertyHelpTextStyle");
        var borderStyle = (Style)window.FindResource("PropertyHelpBorderStyle");
        var help = window.PropertyPaneViews.SelectMany(PropertyDiagnosticElements).OfType<FrameworkElement>()
            .Where(element => element.Style == textStyle || element.Style == borderStyle).ToArray();
        var ocrHelp = PropertyDiagnosticElements(pane).OfType<FrameworkElement>().Where(element => element.Style == textStyle).ToHashSet();
        bool HelpMatches(bool show) => help.All(element => element.Visibility ==
            (show && !ocrHelp.Contains(element) ? Visibility.Visible : Visibility.Collapsed));
        check(help.Length == 19 && help.Count(element => element is Border) == 1 && ocrHelp.Count > 0,
            $"All 19 explanations are retained; OCR uses its guide and other panes retain their shared help toggle (total={help.Length}, OCR={ocrHelp.Count}).");
        var statePaths = new[] { "OverlayItems.Count", "SelectedLinkSummary", "LinkSelectionSummary" };
        var state = window.PropertyPaneViews.SelectMany(IconVisuals).OfType<TextBlock>().Where(text =>
            statePaths.Contains(BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path)).ToArray();
        check(state.Length == statePaths.Length, "Titles, counts and destination/description data remain separate from optional help.");
        var oldMode = vm.EditorMode;
        var oldHelp = vm.ShowPropertyHelpText;
        var oldSelection = vm.SelectedLinkArea;
        var oldLanguage = LocalizationService.CurrentLanguage;
        var originalIds = vm.LinkListItems.Where(item => item.IsSelected).Select(item => item.Id).ToArray();
        try
        {
            foreach (var mode in Enum.GetValues<EditorInteractionMode>())
            {
                vm.EditorMode = mode;
                await layout();
                var activeId = mode == EditorInteractionMode.LinkEditing ? "link-properties" :
                    mode == EditorInteractionMode.Redaction ? "redaction-properties" : "properties";
                check(window.PropertyPanes[activeId].IsSelected, "Mode activates its own property tab: " + mode);
                foreach (var show in new[] { false, true, false })
                {
                    vm.ShowPropertyHelpText = show; await layout();
                    check(HelpMatches(show),
                        $"Help toggles without leaving its margins/border in {mode}: {show}.");
                    check(state.All(text => text.Visibility == Visibility.Visible),
                        $"Help toggle leaves state and user-entered data available in {mode}: {show}.");
                }
                pane.ScrollToTop(); await layout();
                snapshot("12-help-off-" + mode);
            }
            window.PropertyPanes["link-properties"].IsSelected = true; await layout();
            var toggle = IconVisuals(linkPane).OfType<ToggleButton>().Single(button =>
                BindingOperations.GetBinding(button, ToggleButton.IsCheckedProperty)?.Path?.Path == "ShowPropertyHelpText");
            var wasDirty = vm.HasUnsavedChanges;
            toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, true); await layout();
            check(vm.ShowPropertyHelpText && HelpMatches(true),
                "The actual Explanation button updates the shared setting via its two-way binding.");
            toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, false); await layout();
            check(!vm.ShowPropertyHelpText && vm.HasUnsavedChanges == wasDirty,
                "Toggling explanations off leaves project edit state unchanged.");
            if (window.AvalonProperties is { } properties)
            {
                var dockingSettings = vm.CurrentApplicationSettings;
                try
                {
                    properties.Float(); await layout();
                    vm.ShowPropertyHelpText = true; await layout();
                    check(HelpMatches(true) &&
                        ReferenceEquals(pane.DataContext, vm),
                        "Floating property content retains its model and shared help binding.");
                    vm.ShowPropertyHelpText = false; await layout();
                    check(help.All(element => element.Visibility == Visibility.Collapsed),
                        "Floating help collapses immediately, without a main-window ancestor binding.");
                }
                finally
                {
                    properties.Dock(); await layout();
                    // A native Dock round trip may select the document tab. Restore the
                    // test's settings and activate properties before subsequent visible-row checks.
                    await vm.ApplyApplicationSettingsAsync(dockingSettings);
                    window.SynchronizeAvalonDockLayout();
                    window.AvalonProperties!.IsSelected = true; window.AvalonProperties.IsActive = true;
                    await layout();
                    check(pane.IsVisible, "Property checks restore the active docked pane after floating.");
                }
            }
            vm.EditorMode = EditorInteractionMode.LinkEditing;
            var links = vm.LinkListItems.Take(2).ToArray();
            check(links.Length == 2, "Property checks have two editable links.");
            var lockButton = (Button)window.FindName("LinkLockButton");
            vm.SelectedLinkArea = null; await layout();
            check(!lockButton.IsEnabled, "No link selection disables the lock action.");
            vm.SelectLink(links[0], false); await layout();
            check(lockButton.IsEnabled && lockButton.Width == 32 && lockButton.Height == 32,
                "One selected link has the same compact lock hit target as OCR.");
            vm.SelectLink(links[1], true);
            vm.SetSelectedLinksLocked(true); await layout();
            check(vm.AreSelectedLinksLocked && !vm.CanDeleteLinkSelection && !vm.CanAlignLinks &&
                Equals(lockButton.Background, window.FindResource("AccentSoftBrush")),
                "Locked multiple links show the pressed lock style and still forbid deletion/alignment.");
            snapshot("13-link-lock-on-help-off");
            vm.SetSelectedLinksLocked(false); await layout();
            check(!vm.AreSelectedLinksLocked && vm.CanDeleteLinkSelection && vm.CanAlignLinks &&
                !Equals(lockButton.Background, window.FindResource("AccentSoftBrush")),
                "Unlocking restores actions and removes the lock highlight.");
            check(AutomationProperties.GetAccessKey(lockButton) == "Alt+X" &&
                lockButton.ToolTip?.ToString()?.Contains("Alt+X") == true,
                "The icon-only lock keeps a meaningful access key and tooltip.");
            foreach (var name in new[] { "LinkActionPanel" })
            {
                var grid = (UniformGrid)window.FindName(name);
                check(grid.Columns == 2 && grid.Children.OfType<Button>().Select(ActionIcon.GetKey)
                    .SequenceEqual(new[] { "Link", "LinkAdd", "NextPage", "Delete", "PreviousPage", "NextPage" }) &&
                    grid.Children.OfType<Button>().All(button =>
                        button.Style == window.FindResource("PropertyTextActionButtonStyle") &&
                        button.ToolTip is not null && AutomationProperties.GetName(button).Length > 0),
                    "The same six link actions share order, style, names and hints in " + name + ".");
            }
            vm.ShowPropertyHelpText = true; await layout();
            linkPane.ScrollToBottom(); await layout(); snapshot("14-link-help-on");
            LocalizationService.SetLanguage("en-US");
            foreach (var view in window.PropertyPaneViews) LocalizationService.Apply(view);
            await layout();
            var alignment = (UniformGrid)window.FindName("LinkAlignmentIconPanel");
            check(alignment.Children.OfType<Button>().All(button => button.ToolTip?.ToString()?.StartsWith("Match ") == true ||
                    button.ToolTip?.ToString()?.StartsWith("Align ") == true),
                "Every icon-only link alignment tooltip translates in English.");
            snapshot("15-link-property-english");
            await RunPropertyDockChecksAsync(window, vm, check, layout, snapshot);
        }
        finally
        {
            vm.SetSelectedLinksLocked(false);
            vm.EditorMode = oldMode; vm.ShowPropertyHelpText = oldHelp;
            vm.SelectedLinkArea = oldSelection;
            foreach (var id in originalIds.Where(id => id != oldSelection?.Id))
                if (vm.LinkListItems.FirstOrDefault(item => item.Id == id) is { } item) vm.SelectLink(item, true);
            LocalizationService.SetLanguage(oldLanguage);
            foreach (var view in window.PropertyPaneViews) LocalizationService.Apply(view);
            pane.ScrollToTop(); await layout();
        }
    }

    private static IEnumerable<DependencyObject> PropertyDiagnosticElements(DependencyObject root) =>
        PropertyLogicalElements(root).Concat(IconVisuals(root)).Distinct();

    private static IEnumerable<DependencyObject> PropertyLogicalElements(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var element in PropertyLogicalElements(child)) yield return element;
    }
}
