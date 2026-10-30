using System.Text.Json;
using System.IO;
using System.Windows;
using AvalonDock.Layout;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.Infrastructure;
using PdfCorrectorium.Core.Documents;
using System.Windows.Controls;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task RunPropertyDockChecksAsync(MainWindow window, MainWindowViewModel vm,
        Action<bool, string> check, Func<Task> layout, Action<string> snapshot)
    {
        var original = window.CaptureNativeWorkspace();
        check(WorkspaceDockLayout.Normalize(original) == original,
            "Native workspace contains all ten known content IDs and passes bounded XML validation.");
        var oldMode = vm.EditorMode;
        try
        {
            check(window.PropertyPanes.Count == 5 && window.PropertyPanes.Values
                .Select(pane => pane.Content).Distinct().Count() == 5,
                "Five independent tool contents retain one shared document view model.");
            foreach (var view in window.PropertyPaneViews)
                check(ReferenceEquals(view.DataContext, vm), "Every property panel shares the same selection / Undo model.");
            vm.EditorMode = EditorInteractionMode.OcrEditing; await layout();
            check(!window.PropertyPaneViews.First().IsAncestorOf((FrameworkElement)window.FindName("LinkEditingPanel")) &&
                !window.PropertyPaneViews.First().IsAncestorOf((FrameworkElement)window.FindName("AnnotationLinkActionPanel")),
                "OCR does not display the six duplicate link editing actions.");
            var link = window.PropertyPanes["link-properties"];
            link.IsSelected = true; await layout();
            check(vm.EditorMode == EditorInteractionMode.OcrEditing,
                "Selecting the link tab alone leaves the current editing mode unchanged.");
            link.Float(); await layout();
            window.PropertyPanes["comment-properties"].Hide(); await layout();
            var floating = window.CaptureNativeWorkspace();
            var settings = (vm.CurrentApplicationSettings with { WorkspaceLayoutXml = floating }).Normalize();
            var serialized = JsonSerializer.Serialize(settings);
            check(JsonSerializer.Deserialize<ApplicationSettings>(serialized)!.Normalize().WorkspaceLayoutXml == floating,
                "Floating / hidden native layout survives settings serialization and normalization.");
            var preset = WorkspacePreset.Capture("Property layout", settings);
            check(preset.ApplyTo(new ApplicationSettings()).WorkspaceLayoutXml == floating,
                "Workspace presets include the complete native property layout.");
            check(window.RestoreNativeWorkspace(floating), "The native layout can be deserialized with existing controls.");
            await layout();
            check(window.PropertyPanes["link-properties"].IsFloating && window.PropertyPanes["comment-properties"].IsHidden,
                "A detached link panel and hidden comments panel retain their individual state after restoration.");
            vm.EditorMode = EditorInteractionMode.LinkEditing; await layout();
            check(window.PropertyPanes["link-properties"].IsFloating && window.PropertyPanes["link-properties"].IsSelected,
                "Changing mode selects a floating property panel without docking it.");
            vm.IsFacingPagesView = true; await layout();
            vm.IsSinglePageView = true; await layout();
            check(window.PropertyPanes["link-properties"].IsFloating,
                "Single / facing page changes do not relocate floating property panels.");
            window.PropertyPanes["comment-properties"].Show();
            window.PropertyPanes["comment-properties"].IsSelected = true; await layout();
            var target = vm.GetCurrentCommentTarget();
            var commentsBefore = vm.GetComments().ToArray();
            var tagsBefore = vm.GetTags().ToArray();
            var tag = new ProjectTag { Name = "Review tag" };
            vm.ApplyCommentsAndTags([..commentsBefore, new ProjectComment { Target = target,
                Body = "Shared review note", TagIds = [tag.Id] }], [..tagsBefore, tag]);
            await layout();
            var commentList = IconVisuals((DependencyObject)window.PropertyPanes["comment-properties"].Content)
                .OfType<ListBox>().Single();
            check(commentList.Items.OfType<string>().Any(text => text.Contains("Shared review note") && text.Contains("Review tag")),
                "The comments panel shows the current target's body and attached tag using the shared model.");
            vm.UndoCommand.Execute(null); await layout();
            check(!commentList.Items.OfType<string>().Any(text => text.Contains("Shared review note")) &&
                vm.GetComments().SequenceEqual(commentsBefore), "Undo refreshes the visible comment panel even when the dirty flag was already set.");
            window.PropertyPanes["comment-properties"].Hide(); await layout();
            check(WorkspaceDockLayout.Normalize(original.Replace("link-properties", "unrecognized-panel")) is null &&
                WorkspaceDockLayout.Normalize(original.Replace("link-properties", "properties")) is null &&
                WorkspaceDockLayout.Normalize(new string('x', WorkspaceDockLayout.MaximumLength + 1)) is null &&
                WorkspaceDockLayout.Normalize("<!DOCTYPE LayoutRoot [<!ENTITY x SYSTEM 'file:///not-read'>]><LayoutRoot>&x;</LayoutRoot>") is null,
                "Unknown / duplicate panels, oversized data and external XML entities are rejected.");
            check(!window.RestoreNativeWorkspace("<invalid />") && window.PropertyPanes["link-properties"].IsFloating,
                "Invalid persisted layout leaves the live workspace intact.");

            var restartDirectory = Path.Combine(Path.GetDirectoryName(vm.SettingsFilePath)!, "property-restart");
            var restartPaths = new ApplicationPaths(StorageMode.Portable,
                Path.Combine(restartDirectory, ".appstate", "config"), Path.Combine(restartDirectory, ".appstate", "logs"),
                Path.Combine(restartDirectory, ".appstate", "cache"), Path.Combine(restartDirectory, ".appstate", "workspaces"));
            ApplicationPathResolver.EnsureDirectories(restartPaths);
            await new ApplicationSettingsService(restartPaths).SaveAsync(settings);
            var restarted = new MainWindow(diagnosticDirectory: restartDirectory)
            { ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000,
                ClosePromptOverride = () => MessageBoxResult.No };
            try
            {
                restarted.Show(); await layout(); restarted.UpdateLayout();
                check(restarted.PropertyPanes["link-properties"].IsFloating && restarted.PropertyPanes["comment-properties"].IsHidden,
                    "A fresh window restores the saved floating / hidden native property layout at startup.");
                await restarted.ResetPropertyWorkspaceAsync(); await layout(); restarted.UpdateLayout();
                check(restarted.PropertyPanes.Values.All(pane => !pane.IsHidden && !pane.IsFloating &&
                        ReferenceEquals(pane.Parent, restarted.AvalonProperties!.Parent)) &&
                    restarted.GetAvalonDockSide(restarted.AvalonProperties!.Parent as LayoutAnchorablePane) == AnchorSide.Right &&
                    ReferenceEquals(restarted.AvalonBookmarks!.Parent, restarted.AvalonNavigation!.Parent),
                    "Reset groups all properties and speech on the right and pages / bookmarks together on the left.");
                await ((MainWindowViewModel)restarted.DataContext).FlushDisplaySettingsAsync();
            }
            finally { restarted.Close(); await layout(); }
        }
        finally
        {
            check(window.RestoreNativeWorkspace(original), "Property tests restore the original workspace.");
            vm.EditorMode = oldMode; await layout();
            window.PropertyPanes[oldMode == EditorInteractionMode.LinkEditing ? "link-properties" :
                oldMode == EditorInteractionMode.Redaction ? "redaction-properties" : "properties"].IsSelected = true;
            await layout(); snapshot("16-independent-property-tabs");
        }
    }
}
