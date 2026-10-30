using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace PdfCorrectorium.App.Resources;

/// <summary>
/// Shared 24-unit icon renderer. Menu and button instances share frozen geometry,
/// not visual elements; their size, enabled state and foreground remain independent.
/// </summary>
public sealed class ActionIcon : FrameworkElement
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(ActionIcon), new PropertyMetadata("", KeyChanged));
    public static readonly DependencyProperty IconOnlyProperty = DependencyProperty.RegisterAttached(
        "IconOnly", typeof(bool), typeof(ActionIcon), new PropertyMetadata(false, IconOnlyChanged));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(ActionIcon), new FrameworkPropertyMetadata(Brushes.Black,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Dictionary<(string Name, double Thickness), Geometry> InkCache = [];
    // Outlined Material paths already contain both contour edges. Widening them
    // again would blur small icons. App-specific line drawings retain a stroke.
    internal static readonly IReadOnlyList<string> MaterialFilledKeys =
    [
        "Reset", "Apply", "Deselect", "SelectAll", "ReviewList", "Replace", "Keep", "Comments", "Thumbnail", "Eyedropper", "Info", "DocumentProperties", "Restore", "Redo", "Warning", "Undo", "Validate", "Edit", "Exit", "Settings", "Help", "MultiCharacterInput", "Propagate", "Unreviewed", "Excluded", "Analysis", "Search",
        "Stop", "Pending", "CommentsAdd", "Play", "Pause", "ReadAloud"
    ];
    private static readonly HashSet<string> FilledKeys = new(MaterialFilledKeys, StringComparer.Ordinal);
    private Brush LinkBrush => TryFindResource("LinkTextBrush") as Brush ?? Brushes.RoyalBlue;
    private static readonly Brush PdfBrush = (Brush)new SolidColorBrush(Color.FromRgb(211, 47, 47)).GetAsFrozen();
    private static readonly Point[] ProjectHandles = [new(13.1, 3.1), new(19.1, 3.1), new(13.1, 10.1), new(19.1, 10.1)];
    public string Key { get => GetKey(this); set => SetKey(this, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public static string GetKey(DependencyObject target) => (string)target.GetValue(KeyProperty);
    public static void SetKey(DependencyObject target, string key) => target.SetValue(KeyProperty, key);
    public static bool GetIconOnly(DependencyObject target) => (bool)target.GetValue(IconOnlyProperty);
    public static void SetIconOnly(DependencyObject target, bool value) => target.SetValue(IconOnlyProperty, value);

    private static void IconOnlyChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is ButtonBase button && !string.IsNullOrEmpty(GetKey(button))) UpdateButtonTemplate(button);
    }

    private static void UpdateButtonTemplate(ButtonBase button) =>
        button.SetResourceReference(ContentControl.ContentTemplateProperty,
            GetIconOnly(button) ? "ActionIconOnlyTemplate" : "ActionIconLabelTemplate");

    private static void KeyChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is ActionIcon icon) { icon.InvalidateVisual(); return; }
        if (target is MenuItem menu)
            menu.Icon = string.IsNullOrEmpty(args.NewValue as string) ? null : new ActionIcon { Key = (string)args.NewValue, Width = 18, Height = 18 };
        // Button usage is for text captions. A deferred resource reference also works
        // when the XAML parser assigns Key before Content or before app resources load.
        else if (target is ButtonBase button)
            UpdateButtonTemplate(button);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(24, availableSize.Width), Math.Min(24, availableSize.Height));
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty) InvalidateVisual();
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0 || string.IsNullOrEmpty(Key)) return;
        drawing.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        drawing.PushTransform(new ScaleTransform(size / 24, size / 24));
        drawing.PushOpacity(IsEnabled ? 1 : .4);
        switch (Key)
        {
            case "OpenFolder":
                Draw("OpenFolderBack", Foreground, 1.7);
                Draw("OpenFolderFront", Brushes.White); Draw("OpenFolderFront", Foreground, 1.7);
                break;
            case "OpenPdf": case "OpenProject":
                Draw("OpenFolderBack", Foreground, 1.7);
                Draw(Key == "OpenPdf" ? "OpenPdfBadge" : "OpenProjectBadge", PdfBrush);
                if (Key == "OpenPdf")
                { Draw("OpenPdfBadgeFold", Brushes.White, 1.15); Draw("OpenPdfBadgeLines", Brushes.White, 1.15); }
                else
                {
                    Draw("OpenProjectBadgeSelection", Brushes.White, 1);
                    foreach (var point in ProjectHandles)
                        drawing.DrawRectangle(Brushes.White, null, new Rect(point, new Size(1.8, 1.8)));
                }
                Draw("OpenFolderFront", Brushes.White); Draw("OpenFolderFront", Foreground, 1.7);
                break;
            case "ExportPdf": Draw("ExportPdf", Foreground, 1.8); Draw("ExportPdfLetters", PdfBrush); break;
            case "SaveProject": Draw("SaveProject", Foreground); Draw("SaveProject", Foreground, 1.8); break;
            case "Link": Draw("Link", LinkBrush); break;
            case "LinkVisibility":
                Draw("LinkFrame", LinkBrush, 1.8);
                drawing.PushTransform(new MatrixTransform(.72, 0, 0, .72, 3.36, 3.36));
                Draw("Link", LinkBrush); drawing.Pop(); break;
            case "LinkAdd": Draw("LinkAdd", LinkBrush); break;
            case "PageInsert": case "BookmarkAdd": case "BookmarkRemove": case "AddOcrRegion":
            case "ImportOcr": case "PageDelete": case "PageRotateLeft": case "PageRotateRight": case "Redaction":
                Draw(Key, Foreground); break;
            default: Draw(Key, Foreground, FilledKeys.Contains(Key) ? 0 : 1.8); break;
        }
        drawing.Pop(); drawing.Pop(); drawing.Pop();

        void Draw(string name, Brush brush, double thickness = 0)
        {
            var cacheKey = (name, thickness);
            if (!InkCache.TryGetValue(cacheKey, out var ink))
            {
                var geometry = (Geometry)FindResource("IconGeometry." + name);
                ink = thickness == 0 ? geometry : geometry.GetWidenedPathGeometry(new Pen(Brushes.Black, thickness)
                    { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
                if (ink.CanFreeze) ink.Freeze();
                InkCache.Add(cacheKey, ink);
            }
            drawing.DrawGeometry(brush, null, ink);
        }
    }
}
