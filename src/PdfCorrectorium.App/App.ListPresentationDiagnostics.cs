using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

public partial class App
{
    private static async Task ClickListColumnMenuItemAsync(MenuItem item)
    {
        typeof(MenuItem).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(item, null);
        // Native MenuItem defers Click until after rendering. Observe the same settled state as a user.
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static async Task WaitForListColumnMenuCloseAsync(DataGrid grid)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        // Popup's Closed follows the system close animation, not just Dispatcher idle.
        while (ListDetails.GetOpenMenu(grid) is not null && DateTime.UtcNow < deadline) await Task.Delay(20);
    }

    private static ContextMenuEventArgs RequestListColumnMenu(FrameworkElement source, bool keyboard = false)
    {
        var args = (ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [source, true, keyboard ? -1d : 5d, keyboard ? -1d : 5d], null)!;
        source.RaiseEvent(args);
        return args;
    }

    private static ContextMenu OpenListColumnMenu(DataGrid grid, bool keyboard = false, FrameworkElement? sourceOverride = null)
    {
        grid.UpdateLayout();
        static IEnumerable<DependencyObject> Visuals(DependencyObject root)
        {
            yield return root;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        var source = sourceOverride ?? (keyboard ? (FrameworkElement)grid : Visuals(grid).OfType<DataGridColumnHeader>()
            .First(header => header.Column?.Visibility == Visibility.Visible));
        // Exercise the routed native context request, not the menu factory alone.
        RequestListColumnMenu(source, keyboard);
        return ListDetails.GetOpenMenu(grid) ?? throw new InvalidOperationException("Header context menu did not open.");
    }

    private async Task VerifyListPresentationAsync(MainWindow owner, MainWindowViewModel vm, string directory, Action<bool, string> check)
    {
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        async Task Show(Window dialog)
        {
            dialog.Owner = owner; dialog.ShowActivated = false; dialog.ShowInTaskbar = false;
            dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -10000; dialog.Top = -10000;
            dialog.Show(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
        }
        async Task Toggle(Window dialog, string gridName, int basic, int total)
        {
            var grid = (DataGrid)dialog.FindName(gridName);
            check(grid.Columns.Count(column => column.Visibility == Visibility.Visible) == basic,
                dialog.GetType().Name + ": initial columns prioritize the task, not technical metadata.");
            var source = grid.ItemsSource;
            var menu = OpenListColumnMenu(grid);
            var columns = menu.Items.OfType<MenuItem>().Where(item => item.Tag is DataGridColumn).ToArray();
            check(menu.IsOpen && columns.Length == total && columns.Count(item => item.IsChecked) == basic && columns.All(item => item.StaysOpenOnClick),
                dialog.GetType().Name + ": native header menu lists every column with current checks and stays open for multiple changes.");
            foreach (var item in columns.Where(item => !item.IsChecked)) await ClickListColumnMenuItemAsync(item);
            check(grid.Columns.Count(column => column.Visibility == Visibility.Visible) == total && ReferenceEquals(source, grid.ItemsSource),
                dialog.GetType().Name + ": details reveal existing columns without rebuilding the source.");
            var basicColumn = columns.First(item => !ListDetails.GetIsDetail((DataGridColumn)item.Tag));
            await ClickListColumnMenuItemAsync(basicColumn);
            check(!basicColumn.IsChecked && ((DataGridColumn)basicColumn.Tag).Visibility == Visibility.Collapsed && menu.IsOpen,
                dialog.GetType().Name + ": individual basic columns can be hidden without closing the native menu.");
            foreach (var item in columns.Where(item => item.IsChecked).ToArray())
                if (item.IsEnabled) await ClickListColumnMenuItemAsync(item);
            check(columns.Count(item => item.IsChecked) == 1 && columns.Single(item => item.IsChecked).IsEnabled == false,
                dialog.GetType().Name + ": the final visible column cannot be hidden, preserving a header for recovery.");
            await ClickListColumnMenuItemAsync(menu.Items.OfType<MenuItem>().Last());
            check(grid.Columns.Count(column => column.Visibility == Visibility.Visible) == basic,
                dialog.GetType().Name + ": reset restores the task-specific defaults, not an all-columns view.");
            menu.IsOpen = false;
            menu = OpenListColumnMenu(grid, keyboard: true);
            check(menu.IsOpen && menu.Placement == PlacementMode.Bottom && ReferenceEquals(menu.PlacementTarget, grid),
                dialog.GetType().Name + ": keyboard context requests work even when there are no result rows.");
            menu.IsOpen = false;
            LocalizationService.SetLanguage(LocalizationService.EnglishLanguage); LocalizationService.Apply(dialog);
            menu = OpenListColumnMenu(grid, keyboard: true);
            check(menu.Items.OfType<MenuItem>().Last().Header?.ToString() == "Restore default columns(_R)" &&
                  menu.Items.OfType<MenuItem>().Where(item => item.Tag is DataGridColumn).All(item => Equals(item.Header, ((DataGridColumn)item.Tag).Header)),
                dialog.GetType().Name + ": menu captions follow current translated headers, including reset.");
            menu.IsOpen = false;
            LocalizationService.SetLanguage(LocalizationService.JapaneseLanguage); LocalizationService.Apply(dialog);
            menu = OpenListColumnMenu(grid, keyboard: true);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            menu.UpdateLayout();
            SaveIconDiagnosticImage(menu, Path.Combine(directory, gridName + "-column-menu.png"));
            menu.IsOpen = false;
            var last = grid.Columns[^1]; last.DisplayIndex = 0;
            menu = OpenListColumnMenu(grid, keyboard: true);
            check(ReferenceEquals(menu.Items.OfType<MenuItem>().First().Tag, last),
                dialog.GetType().Name + ": the menu follows user-reordered columns, including hidden columns.");
            await ClickListColumnMenuItemAsync(menu.Items.OfType<MenuItem>().Last());
            check(last.DisplayIndex == 0, dialog.GetType().Name + ": resetting visibility does not reset column order.");
            menu.IsOpen = false; last.DisplayIndex = total - 1;
            await WaitForListColumnMenuCloseAsync(grid);
            var released = ListDetails.GetOpenMenu(grid) is null;
            var bodyRequest = RequestListColumnMenu(grid);
            check(released && !bodyRequest.Handled && ListDetails.GetOpenMenu(grid) is null,
                $"{dialog.GetType().Name}: closing releases the popup and body right-click is not intercepted (released={released}, handled={bodyRequest.Handled}, menu={ListDetails.GetOpenMenu(grid)?.IsOpen}).");
            menu = OpenListColumnMenu(grid, sourceOverride: Descendants(grid).OfType<DataGridColumnHeadersPresenter>().Single());
            check(menu.IsOpen, dialog.GetType().Name + ": header-row whitespace also opens the column menu.");
            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, grid));
            await WaitForListColumnMenuCloseAsync(grid);
            check(!menu.IsOpen && ListDetails.GetOpenMenu(grid) is null,
                dialog.GetType().Name + ": unloading a tab/window closes and releases its active column menu.");
        }
        var quality = new OcrQualityAnalysisWindow(vm);
        try
        {
            await Show(quality);
            await Toggle(quality, "CountResultsGrid", 5, 11);
            var tabs = (TabControl)quality.FindName("AnalysisTabs");
            tabs.SelectedIndex = 1; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Toggle(quality, "KeywordResultsGrid", 4, 7);
            var keyword = (DataGrid)quality.FindName("KeywordResultsGrid");
            check(keyword.Columns.Any(column => Equals(column.Header, "固定") && column.Visibility == Visibility.Visible),
                "Keyword corrections always show lock state, even in the basic view.");
            tabs.SelectedIndex = 2; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Toggle(quality, "WordResultsGrid", 5, 8);
            check(((DataGrid)quality.FindName("WordResultsGrid")).Columns.Any(column => Equals(column.Header, "注意") && column.Visibility == Visibility.Visible),
                "Similar-word ambiguity and lock attention remain visible without full technical reasons.");
        }
        finally { quality.Close(); }

        var search = new OcrSearchReplaceWindow(vm);
        try { await Show(search); await Toggle(search, "ResultsGrid", 2, 3); }
        finally { search.Close(); }

        var repeated = new RepeatedRegionCandidateWindow([], new([], .9, true, RepeatedRegionPropagationMode.ReplaceStructure), vm);
        try
        {
            await Show(repeated); await Toggle(repeated, "CandidateGrid", 5, 6);
            check(((DataGrid)repeated.FindName("CandidateGrid")).Columns.Any(column => Equals(column.Header, "状態") && column.Visibility == Visibility.Visible),
                "Propagation candidates retain state and applicability while coordinates move to details.");
        }
        finally { repeated.Close(); }

        foreach (var kind in Enum.GetValues<DocumentBatchKind>())
        {
            var batch = new DocumentBatchWindow(vm, kind);
            try
            {
                await Show(batch);
                var column = (DataGridColumn)batch.FindName("DestinationColumn");
                check((column.Visibility == Visibility.Visible) == (kind == DocumentBatchKind.AutomaticLinks),
                    kind + ": the destination column is only shown for actual page links, not empty redaction destinations.");
                check(((DataGrid)batch.FindName("CandidateGrid")).Columns.Any(value => Equals(value.Header, "注意") && value.Visibility == Visibility.Visible),
                    kind + ": applicability/safety attention is always present in batch previews.");
            }
            finally { batch.Close(); }
        }

        var issue = new PdfInputIssue("test.warning", PdfInputIssueSeverity.Critical, "Important warning", "Keep this safety description.", "Structure");
        var issues = new InputPdfIssuesWindow([issue]);
        try
        {
            await Show(issues);
            var list = (ListBox)issues.FindName("IssueList");
            var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var text = Descendants(item).OfType<TextBlock>().Select(block => block.Text).ToArray();
            check(text.Contains(issue.Title) && text.Contains(issue.Description) && !text.Contains(issue.Code),
                "Input notices keep title, severity and safety text visible instead of a repeated diagnostic identifier.");
            check(Equals(item.ToolTip, issue.Code) && System.Windows.Automation.AutomationProperties.GetHelpText(item) == issue.Code,
                "The diagnostic identifier remains in the tooltip and accessible item help.");
        }
        finally { issues.Close(); }

        await vm.OpenDocumentPathAsync(Path.Combine(directory, "visible.pdf"));
        await vm.EnsureDocumentMetadataAsync();
        var properties = new DocumentPropertiesWindow(vm);
        try
        {
            await Show(properties);
            var tabs = Descendants(properties).OfType<TabControl>().First(); tabs.SelectedIndex = 2;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (properties.PdfProperties.Fonts.Count == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); properties.UpdateLayout();
            var list = (ListBox)properties.FindName("FontsList");
            check(list.Items.Count == 1, "The font list still contains the actual unembedded font.");
            var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var expander = Descendants(item).OfType<Expander>().Single();
            check(!expander.IsExpanded && Descendants((DependencyObject)expander.Header).OfType<TextBlock>().Any(block => block.Text == "いいえ"),
                "Fonts default to compact rows while unembedded status remains immediately visible.");
            expander.IsExpanded = true;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); properties.UpdateLayout();
            check(Descendants((DependencyObject)expander.Content).OfType<TextBlock>().Any(block => block.Text == "エンコーディング:"),
                "Font family/type/encoding details remain keyboard-expandable rather than being removed.");
            expander.IsExpanded = false; properties.UpdateLayout();
            SaveIconDiagnosticImage((FrameworkElement)properties.Content, Path.Combine(directory, "compact-fonts.png"));
        }
        finally { properties.Close(); }
        check(!vm.HasUnsavedChanges, "All list presentation changes leave the document clean.");
    }
}
