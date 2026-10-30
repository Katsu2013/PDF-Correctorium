using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PdfCorrectorium.App.Services;

/// <summary>Event-driven, application-wide palette. PDF/content brushes are deliberately separate.</summary>
internal static class ApplicationThemeService
{
    private static bool _initialized;
    private static int _queued;
    internal static ApplicationThemeMode Mode { get; private set; }
    internal static bool IsDark { get; private set; }
    internal static Func<bool>? SystemDarkOverride { get; set; }
    internal static int PaletteApplicationCount { get; private set; }

    // Colors describe UI roles, not document pixels. The light values preserve the existing design.
    internal static readonly (string Key, string Light, string Dark)[] Palette =
    [
        ("AppBackgroundBrush", "#F4F6F8", "#202124"),
        ("PanelBrush", "#FAFBFC", "#25272B"),
        ("ToolbarBrush", "#FFFFFF", "#292B30"),
        ("InputBrush", "#FFFFFF", "#202226"),
        ("PrimaryTextBrush", "#222222", "#E8EAED"),
        ("BodyTextBrush", "#263746", "#DCE2E8"),
        ("SecondaryTextBrush", "#667085", "#B1BAC8"),
        ("BorderBrush", "#DDE1E6", "#41464E"),
        ("StrongBorderBrush", "#B8C0C8", "#626B76"),
        ("HoverBrush", "#F2F4F6", "#383C43"),
        ("PressedBrush", "#E5E8EC", "#454B55"),
        ("HoverBorderBrush", "#8E99A4", "#99A5B4"),
        ("DisabledTextBrush", "#8B98A3", "#8A929E"),
        ("DisabledBrush", "#EEF1F3", "#30333A"),
        ("DisabledBorderBrush", "#D4DADF", "#464B54"),
        ("AccentBrush", "#D32F2F", "#F16C6C"),
        ("AccentFillBrush", "#D32F2F", "#D32F2F"),
        ("AccentHoverBrush", "#B71C1C", "#FF8585"),
        ("AccentPressedBrush", "#991B1B", "#D95D5D"),
        ("AccentSoftBrush", "#FFF1F1", "#452B30"),
        ("AccentSoftHoverBrush", "#FFE4E4", "#583239"),
        ("AccentTextBrush", "#8F1F1F", "#FFB9B9"),
        ("WarningBackgroundBrush", "#FFF8E7", "#3F3520"),
        ("WarningBorderBrush", "#E7C66B", "#A98D43"),
        ("WarningTextBrush", "#8A5A00", "#F5D48A"),
        ("LinkTextBrush", "#2563EB", "#87B4FF"),
        ("PositiveTextBrush", "#228B4E", "#81D7A4"),
        ("TealTextBrush", "#006A6A", "#7CD8D2"),
        ("TealSoftBrush", "#E8F5F5", "#243F40"),
        ("OnAccentBrush", "#FFFFFF", "#FFFFFF"),
    ];

    internal static void Apply(ApplicationThemeMode mode)
    {
        if (Application.Current is not { } app) return;
        app.Dispatcher.VerifyAccess();
        if (!_initialized)
        {
            _initialized = true;
            SystemEvents.UserPreferenceChanged += PreferencesChanged;
            SystemParameters.StaticPropertyChanged += SystemParametersChanged;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, _) => { UpdateTitle((Window)sender); QueueChrome((Window)sender); }), true);
            EventManager.RegisterClassHandler(typeof(Border), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, args) => { if (ReferenceEquals(sender, args.OriginalSource)) ThemePaneBorder((Border)sender); }), true);
            EventManager.RegisterClassHandler(typeof(ToggleButton), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, args) =>
                {
                    if (ReferenceEquals(sender, args.OriginalSource) && sender is ToggleButton { Name: "OverflowButton", TemplatedParent: ToolBar } overflow)
                    {
                        overflow.SetResourceReference(Control.BackgroundProperty, "ToolbarBrush");
                        overflow.SetResourceReference(Control.ForegroundProperty, "PrimaryTextBrush");
                        overflow.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
                        if (overflow.TryFindResource("ToolbarOverflowToggleTemplate") is ControlTemplate template) overflow.Template = template;
                    }
                }), true);
            EventManager.RegisterClassHandler(typeof(Control), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, args) =>
                {
                    if (ReferenceEquals(sender, args.OriginalSource) && sender is Control control &&
                        control.TemplatedParent?.GetType().Namespace?.StartsWith("AvalonDock.Controls", StringComparison.Ordinal) == true)
                    {
                        SetCaptionForeground(control);
                        if (!(bool)control.GetValue(CaptionRoleProperty))
                        {
                            control.SetValue(CaptionRoleProperty, true);
                            control.IsEnabledChanged += static (target, _) => SetCaptionForeground((Control)target);
                        }
                    }
                }), true);
            app.Exit += (_, _) =>
            {
                SystemEvents.UserPreferenceChanged -= PreferencesChanged;
                SystemParameters.StaticPropertyChanged -= SystemParametersChanged;
            };
        }
        var dark = mode == ApplicationThemeMode.Dark || mode == ApplicationThemeMode.System && ReadSystemDark();
        if (PaletteApplicationCount > 0 && Mode == mode && IsDark == dark && _highContrast == SystemParameters.HighContrast) return;
        Mode = mode; IsDark = dark; _highContrast = SystemParameters.HighContrast;
        foreach (var (key, light, night) in Palette)
        {
            Brush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? night : light));
            if (_highContrast)
                brush = key.Contains("Text", StringComparison.Ordinal) ? SystemColors.WindowTextBrush :
                    key.Contains("Border", StringComparison.Ordinal) ? SystemColors.WindowTextBrush :
                    key == "OnAccentBrush" ? SystemColors.HighlightTextBrush :
                    key.StartsWith("Accent", StringComparison.Ordinal) ? SystemColors.HighlightBrush : SystemColors.WindowBrush;
            if (brush.CanFreeze) brush.Freeze();
            app.Resources[key] = brush;
        }
        PaletteApplicationCount++;
        foreach (var (key, brush) in new (ResourceKey, string)[]
        {
            (SystemColors.WindowBrushKey, "InputBrush"), (SystemColors.WindowTextBrushKey, "PrimaryTextBrush"),
            (SystemColors.ControlBrushKey, "PanelBrush"), (SystemColors.ControlTextBrushKey, "PrimaryTextBrush"),
            (SystemColors.HighlightBrushKey, "AccentFillBrush"), (SystemColors.HighlightTextBrushKey, "OnAccentBrush"),
            (SystemColors.ActiveCaptionTextBrushKey, "PrimaryTextBrush"), (SystemColors.InactiveCaptionTextBrushKey, "SecondaryTextBrush"),
            (SystemColors.ScrollBarBrushKey, "PanelBrush"), (SystemColors.ControlLightBrushKey, "HoverBrush"),
            (SystemColors.ControlLightLightBrushKey, "PanelBrush"), (SystemColors.ControlDarkBrushKey, "StrongBorderBrush"),
            (SystemColors.InactiveSelectionHighlightBrushKey, "AccentSoftBrush"), (SystemColors.GrayTextBrushKey, "DisabledTextBrush")
        }) app.Resources[key] = app.Resources[brush];
        foreach (var (key, brush) in new (ResourceKey, string)[]
        {
            (SystemColors.WindowColorKey, "InputBrush"), (SystemColors.WindowTextColorKey, "PrimaryTextBrush"),
            (SystemColors.ControlColorKey, "PanelBrush"), (SystemColors.ControlTextColorKey, "PrimaryTextBrush"),
            (SystemColors.ActiveCaptionTextColorKey, "PrimaryTextBrush"), (SystemColors.InactiveCaptionTextColorKey, "SecondaryTextBrush"),
            (SystemColors.ControlLightColorKey, "HoverBrush"), (SystemColors.ControlLightLightColorKey, "PanelBrush"),
            (SystemColors.ControlDarkColorKey, "StrongBorderBrush"), (SystemColors.GrayTextColorKey, "DisabledTextBrush")
        }) app.Resources[key] = ((SolidColorBrush)app.Resources[brush]).Color;
        foreach (Window window in app.Windows) { UpdateTitle(window); QueueChrome(window); }
    }

    private static bool _highContrast;
    private static readonly DependencyProperty ChromeQueuedProperty = DependencyProperty.RegisterAttached("ChromeQueued", typeof(bool), typeof(ApplicationThemeService), new PropertyMetadata(false));
    private static readonly DependencyProperty ThemedSourceProperty = DependencyProperty.RegisterAttached("ThemedSource", typeof(ImageSource), typeof(ApplicationThemeService));
    private static readonly DependencyProperty CaptionRoleProperty = DependencyProperty.RegisterAttached("CaptionRole", typeof(bool), typeof(ApplicationThemeService), new PropertyMetadata(false));
    private static void SetCaptionForeground(Control control) =>
        control.SetResourceReference(Control.ForegroundProperty, control.IsEnabled ? "PrimaryTextBrush" : "DisabledTextBrush");
    private static void QueueChrome(Window window)
    {
        if ((bool)window.GetValue(ChromeQueuedProperty)) return;
        window.SetValue(ChromeQueuedProperty, true);
        _ = window.Dispatcher.InvokeAsync(() =>
        {
            window.SetValue(ChromeQueuedProperty, false);
            if (!window.IsLoaded) return;
            // One pass after templates settle, never a timer or a per-frame walk.
            // Native ToolBar/Avalon templates contain a few non-inherited glyphs.
            void Visit(DependencyObject element, ButtonBase? caption = null)
            {
                if (element is ToggleButton { Name: "OverflowButton", TemplatedParent: ToolBar } overflow)
                {
                    overflow.SetResourceReference(Control.BackgroundProperty, "ToolbarBrush");
                    overflow.SetResourceReference(Control.ForegroundProperty, "PrimaryTextBrush");
                    if (overflow.TryFindResource("ToolbarOverflowToggleTemplate") is ControlTemplate template && overflow.Template != template)
                        overflow.SetCurrentValue(Control.TemplateProperty, template);
                }
                if (element is ButtonBase button && button.TemplatedParent?.GetType().Namespace?.StartsWith("AvalonDock.Controls", StringComparison.Ordinal) == true)
                    caption = button;
                // The stock docking templates contain pale, fixed pane borders.
                // Only neutral chrome from AvalonDock is affected, never document layers.
                if (element is Border paneBorder) ThemePaneBorder(paneBorder);
                if (caption is not null)
                {
                    if (element is System.Windows.Shapes.Path path)
                    {
                        if (IsNeutralInk(path.Fill) && !BindingOperations.IsDataBound(path, Shape.FillProperty))
                            BindingOperations.SetBinding(path, Shape.FillProperty, new Binding(nameof(Control.Foreground)) { Source = caption });
                        if (IsNeutralInk(path.Stroke) && !BindingOperations.IsDataBound(path, Shape.StrokeProperty))
                            BindingOperations.SetBinding(path, Shape.StrokeProperty, new Binding(nameof(Control.Foreground)) { Source = caption });
                    }
                    else if (element is Image image && image.Source is DrawingImage source && !ReferenceEquals(image.GetValue(ThemedSourceProperty), source))
                    {
                        var clone = source.CloneCurrentValue();
                        void Recolor(Drawing drawing)
                        {
                            if (drawing is DrawingGroup group) foreach (var child in group.Children) Recolor(child);
                            else if (drawing is ImageDrawing raster && raster.ImageSource is { Width: > 0, Height: > 0 } pixels)
                            {
                                var ink = new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, pixels.Width, pixels.Height)) };
                                BindingOperations.SetBinding(ink, GeometryDrawing.BrushProperty, new Binding(nameof(Control.Foreground)) { Source = caption });
                                var layer = new DrawingGroup { OpacityMask = new ImageBrush(pixels) };
                                layer.Children.Add(ink);
                                raster.ImageSource = new DrawingImage(layer);
                            }
                            else if (drawing is GeometryDrawing geometry)
                            {
                                if (IsNeutralInk(geometry.Brush)) BindingOperations.SetBinding(geometry, GeometryDrawing.BrushProperty, new Binding(nameof(Control.Foreground)) { Source = caption });
                                if (geometry.Pen is { } pen && IsNeutralInk(pen.Brush)) BindingOperations.SetBinding(pen, Pen.BrushProperty, new Binding(nameof(Control.Foreground)) { Source = caption });
                            }
                        }
                        Recolor(clone.Drawing);
                        image.SetValue(ThemedSourceProperty, clone);
                        // Keep original bindings/style triggers, including auto-hide state.
                        image.SetCurrentValue(Image.SourceProperty, clone);
                    }
                    else if (element is Image bitmapIcon && bitmapIcon.Source is { } bitmapSource &&
                        !ReferenceEquals(bitmapIcon.GetValue(ThemedSourceProperty), bitmapSource) && bitmapSource.Width > 0 && bitmapSource.Height > 0)
                    {
                        // Stock pane icons can be raster ImageDrawings. Tint their
                        // alpha instead of changing the shape or adding new assets.
                        var geometry = new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, bitmapSource.Width, bitmapSource.Height)) };
                        BindingOperations.SetBinding(geometry, GeometryDrawing.BrushProperty, new Binding(nameof(Control.Foreground)) { Source = caption });
                        var mask = new DrawingGroup { OpacityMask = new ImageBrush(bitmapSource) };
                        mask.Children.Add(geometry);
                        var tinted = new DrawingImage(mask);
                        bitmapIcon.SetValue(ThemedSourceProperty, tinted);
                        bitmapIcon.SetCurrentValue(Image.SourceProperty, tinted);
                    }
                    if (element is Border border && border.Background is SolidColorBrush back && back.Color.A > 0 && back.Color.R > 225 &&
                        Math.Abs(back.Color.R - back.Color.G) < 8 && Math.Abs(back.Color.G - back.Color.B) < 8 && !BindingOperations.IsDataBound(border, Border.BackgroundProperty))
                    {
                        var binding = new MultiBinding { Converter = CaptionBackgroundConverter.Instance };
                        binding.Bindings.Add(new Binding(nameof(UIElement.IsMouseOver)) { Source = caption });
                        binding.Bindings.Add(new Binding(nameof(ButtonBase.IsPressed)) { Source = caption });
                        binding.Bindings.Add(new Binding(nameof(Control.Foreground)) { Source = caption });
                        BindingOperations.SetBinding(border, Border.BackgroundProperty, binding);
                    }
                }
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++) Visit(VisualTreeHelper.GetChild(element, index), caption);
            }
            Visit(window);
        }, DispatcherPriority.Loaded);
    }
    private static void ThemePaneBorder(Border border)
    {
        if (border.TemplatedParent?.GetType().Namespace?.StartsWith("AvalonDock.Controls", StringComparison.Ordinal) != true) return;
        if (border.BorderBrush is SolidColorBrush { Color.A: > 0 } ink && ink.Color.R > 170 &&
            Math.Abs(ink.Color.R - ink.Color.G) < 8 && Math.Abs(ink.Color.G - ink.Color.B) < 8)
            border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        // Stock document-pane padding exposes its fixed white background as an
        // outline. This is UI chrome; actual PDF-paper borders have no such parent.
        if (border.Background is SolidColorBrush { Color.A: > 0 } surface && surface.Color.R > 225 &&
            Math.Abs(surface.Color.R - surface.Color.G) < 8 && Math.Abs(surface.Color.G - surface.Color.B) < 8)
            border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
    }
    private static bool IsNeutralInk(Brush? brush) => brush is SolidColorBrush { Color.A: > 0 } ink &&
        ink.Color.R <= 160 && Math.Abs(ink.Color.R - ink.Color.G) < 8 && Math.Abs(ink.Color.G - ink.Color.B) < 8;
    private sealed class CaptionBackgroundConverter : IMultiValueConverter
    {
        internal static readonly CaptionBackgroundConverter Instance = new();
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            Application.Current?.TryFindResource(values.ElementAtOrDefault(1) is true ? "PressedBrush" : values.ElementAtOrDefault(0) is true ? "HoverBrush" : "ToolbarBrush") as Brush ?? Brushes.Transparent;
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
    private static bool ReadSystemDark()
    {
        if (SystemDarkOverride is { } read) return read();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        { return false; }
    }

    private static void PreferencesChanged(object sender, UserPreferenceChangedEventArgs args) => QueueRefresh();
    private static void SystemParametersChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(SystemParameters.HighContrast)) QueueRefresh(); }
    internal static void QueueRefresh()
    {
        if (Application.Current is not { } app || Interlocked.Exchange(ref _queued, 1) != 0) return;
        _ = app.Dispatcher.InvokeAsync(() => { Interlocked.Exchange(ref _queued, 0); Apply(Mode); });
    }
    private static void UpdateTitle(Window window)
    {
        // Derived windows can retain the native Window default style. Give only
        // those default values explicit role references; honor local/custom styles.
        if (DependencyPropertyHelper.GetValueSource(window, Control.BackgroundProperty).BaseValueSource is BaseValueSource.Default or BaseValueSource.DefaultStyle)
            window.SetResourceReference(Control.BackgroundProperty, "AppBackgroundBrush");
        if (DependencyPropertyHelper.GetValueSource(window, Control.ForegroundProperty).BaseValueSource is BaseValueSource.Default or BaseValueSource.DefaultStyle)
            window.SetResourceReference(Control.ForegroundProperty, "PrimaryTextBrush");
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = IsDark && !SystemParameters.HighContrast ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
