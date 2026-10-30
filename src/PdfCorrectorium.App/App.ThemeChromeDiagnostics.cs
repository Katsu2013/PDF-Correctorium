using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

public partial class App
{
    private async Task VerifyThemeChromeAsync(MainWindow main, string output, Action<bool, string> check)
    {
        var vm = (MainWindowViewModel)main.DataContext;
        var edit = (MenuItem)main.FindName("EditMenu");
        var settings = (MenuItem)main.FindName("ApplicationSettingsMenuItem");
        check(!vm.HasDocument && edit.IsEnabled && settings.IsEnabled, "Settings and its Edit parent are enabled before opening any document.");
        check(!((MenuItem)edit.Items[2]).IsEnabled && !((MenuItem)main.FindName("FindOcrTextMenuItem")).IsEnabled,
            "Removing the Edit-parent guard does not enable document actions without a document.");
        ApplicationSettingsWindow? openedSettings = null;
        var expectingSettings = true;
        EventManager.RegisterClassHandler(typeof(ApplicationSettingsWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, args) =>
            {
                if (!expectingSettings || sender is not ApplicationSettingsWindow dialog) return;
                expectingSettings = false; openedSettings = dialog;
                dialog.Opacity = 0; dialog.ShowInTaskbar = false;
                _ = Dispatcher.InvokeAsync(() => { dialog.DialogResult = false; }, DispatcherPriority.ApplicationIdle);
            }));
        settings.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        check(openedSettings is not null && !vm.HasDocument && !vm.HasUnsavedChanges,
            "The real Settings menu opens and cancels its dialog with no PDF or project loaded.");

        Color Role(string key) => ((SolidColorBrush)Current.Resources[key]).Color;
        static Color Ink(Brush value) => value is SolidColorBrush solid ? solid.Color : Colors.Transparent;
        async Task Save(FrameworkElement element, string name)
        {
            // Popup roots stay transparent while open. Render only after closing,
            // so this diagnostic never flashes a menu over the user's application.
            element.Opacity = 1; element.Measure(new Size(900, 900));
            var width = Math.Max(1, (int)Math.Ceiling(element.DesiredSize.Width));
            var height = Math.Max(1, (int)Math.Ceiling(element.DesiredSize.Height));
            element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using var file = File.Create(System.IO.Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        ApplicationThemeService.SystemDarkOverride = () => false;
        ApplicationThemeService.Apply(ApplicationThemeMode.Dark);
        edit.ApplyTemplate();
        var popup = (Popup)edit.Template.FindName("PART_Popup", edit);
        var popupRoot = (Border)popup.Child; popupRoot.Opacity = 0;
        edit.IsSubmenuOpen = true;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(popup.IsOpen && Ink(popupRoot.Background) == Role("InputBrush"),
            "An actually opened main-menu popup uses the dark app background with Windows-light selection simulated.");
        check(Ink(settings.Foreground) == Role("PrimaryTextBrush") && Ink(settings.Foreground) != Ink(popupRoot.Background),
            "Enabled menu captions remain readable independently of the OS theme.");
        check(Ink(((MenuItem)edit.Items[2]).Foreground) == Role("DisabledTextBrush"), "Disabled menu captions retain the disabled role.");
        var menuSeparator = edit.Items.OfType<Separator>().First(); menuSeparator.ApplyTemplate();
        check(Ink(menuSeparator.Background) == Role("BorderBrush"), "Menu separators use a single neutral gray rule.");
        var nativeRole = edit.Role;
        ApplicationThemeService.Apply(ApplicationThemeMode.Light);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(popup.IsOpen && Ink(popupRoot.Background) == Role("InputBrush") && Ink(settings.Foreground) == Role("PrimaryTextBrush") && edit.Role == nativeRole,
            "An open popup switches back to light without rebuilding MenuItems or changing their native role.");
        ApplicationThemeService.Apply(ApplicationThemeMode.Dark);
        edit.IsSubmenuOpen = false;
        await Save(popupRoot, "edit-menu-dark-no-document");

        var commandCount = 0;
        var toggle = new MenuItem { Header = "Checkable", IsCheckable = true, StaysOpenOnClick = true };
        toggle.Click += (_, _) => commandCount++;
        var child = new MenuItem { Header = "Child" };
        var nested = new MenuItem { Header = "Nested", Items = { child } };
        var context = new ContextMenu { PlacementTarget = main, Items = { toggle, new Separator(), nested }, Opacity = 0, StaysOpen = true };
        context.IsOpen = true;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        context.ApplyTemplate(); toggle.ApplyTemplate(); nested.ApplyTemplate();
        File.WriteAllText(System.IO.Path.Combine(output, "context-theme-state.txt"),
            $"Open={context.IsOpen}; Background={context.Background}; Role={Current.Resources["InputBrush"]}; Foreground={toggle.Foreground}; TextRole={Current.Resources["PrimaryTextBrush"]}; Style={context.Style}; ItemStyle={toggle.Style}; ContextTemplate={context.Template}");
        check(context.IsOpen && Ink(context.Background) == Role("InputBrush") && Ink(toggle.Foreground) == Role("PrimaryTextBrush"),
            "The actual context menu follows dark background and caption roles.");
        var invokeProvider = (IInvokeProvider)new MenuItemAutomationPeer(toggle).GetPattern(PatternInterface.Invoke);
        invokeProvider.Invoke();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(toggle.IsChecked && commandCount == 1 && context.IsOpen, "Native checkable menu handling and StaysOpenOnClick remain intact.");
        var nestedPopup = (Popup)nested.Template.FindName("PART_Popup", nested);
        ((FrameworkElement)nestedPopup.Child).Opacity = 0;
        nested.IsSubmenuOpen = true;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(nestedPopup.IsOpen && nestedPopup.Placement == PlacementMode.Right && Ink(child.Foreground) == Role("PrimaryTextBrush"),
            "Native nested menus open rightward and inherit the app's foreground.");
        nested.IsSubmenuOpen = false; context.IsOpen = false;
        await Save(context, "context-menu-dark");

        main.UpdateLayout();
        var buttons = ThemeChromeVisuals(main).OfType<Button>().ToArray();
        var import = buttons.Single(button => ToolbarItem.GetKey(button) == "main.importOcr");
        var importInk = ThemeChromeVisuals(import).OfType<System.Windows.Shapes.Path>().Single();
        check(!import.IsEnabled && Ink(importInk.Fill) == Ink(import.Foreground) && Ink(importInk.Fill) == Role("DisabledTextBrush"),
            "The disabled NDLOCR import glyph inherits the disabled toolbar ink rather than fixed black.");
        var toolbar = ThemeChromeVisuals(main).OfType<ToolBar>().First();
        var separator = ThemeChromeVisuals(toolbar).OfType<Separator>().First(); separator.ApplyTemplate();
        check(Ink(separator.Background) == Role("BorderBrush"), "Native toolbar separators use neutral gray rather than a pale embossed edge.");
        var selected = ThemeChromeVisuals(main).OfType<ToggleButton>().First(button => button.IsChecked == true && button.Template?.FindName("ActiveRail", button) is Border);
        var chrome = (Border)selected.Template.FindName("ToggleChrome", selected);
        var rail = (Border)selected.Template.FindName("ActiveRail", selected);
        check(Ink(chrome.BorderBrush) == Role("StrongBorderBrush") && Ink(rail.Background) == Role("AccentFillBrush"),
            "Selected toolbar buttons use a gray perimeter while retaining the red selection rail.");
        static bool Pale(Brush value) => value is SolidColorBrush { Color.A: > 0 } brush && brush.Color.R > 225 &&
            Math.Abs(brush.Color.R - brush.Color.G) < 8 && Math.Abs(brush.Color.G - brush.Color.B) < 8;
        var paleBorders = ThemeChromeVisuals(main).OfType<Border>().Where(border => border.TemplatedParent?.GetType().Namespace?.StartsWith("AvalonDock.Controls", StringComparison.Ordinal) == true &&
            (Pale(border.BorderBrush) || Pale(border.Background))).ToArray();
        check(paleBorders.Length == 0, "Dark docking chrome has no pale pane outlines or exposed white backing.");
        ApplicationThemeService.SystemDarkOverride = null;
    }

    private static IEnumerable<DependencyObject> ThemeChromeVisuals(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in ThemeChromeVisuals(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void VerifyEnabledImportChrome(MainWindow main, Action<bool, string> check)
    {
        var import = ThemeChromeVisuals(main).OfType<Button>().Single(button => ToolbarItem.GetKey(button) == "main.importOcr");
        var glyph = ThemeChromeVisuals(import).OfType<System.Windows.Shapes.Path>().Single();
        check(import.IsEnabled && glyph.Fill is SolidColorBrush ink &&
            ink.Color == ((SolidColorBrush)Current.Resources["PrimaryTextBrush"]).Color,
            "After opening a document, the enabled NDLOCR glyph uses the light foreground of the dark toolbar.");
    }
}
