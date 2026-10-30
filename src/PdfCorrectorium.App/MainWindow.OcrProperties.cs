using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PdfCorrectorium.App;

public partial class MainWindow
{
    private string[] _ocrGuideParagraphs = [];

    /// <summary>One-time presentation migration; retain original controls, bindings and namescope.</summary>
    private void InitializeOcrPropertyPresentation()
    {
        Closing += (_, _) => _rapidOcrCancellation?.Cancel();
        var helpStyle = FindResource("PropertyHelpTextStyle");
        var descriptions = OcrLogicalDescendants(OcrPropertyContent).OfType<TextBlock>()
            .Where(text => ReferenceEquals(text.Style, helpStyle)).ToArray();
        _ocrGuideParagraphs = descriptions.Select(text => text.Text).ToArray();
        foreach (var text in descriptions) text.Visibility = Visibility.Collapsed;
        // Region locking stays directly accessible, not hidden among numeric inputs.
        OcrDetailsContent.Children.Remove(OcrRegionLockTray);
        OcrSelectionProperties.Children.Insert(OcrSelectionProperties.Children.IndexOf(OcrDetailsExpander), OcrRegionLockTray);
    }

    private static IEnumerable<DependencyObject> OcrLogicalDescendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in OcrLogicalDescendants(child)) yield return descendant;
        }
    }

    internal OcrOperationGuideWindow CreateOcrOperationGuide() => new(_ocrGuideParagraphs) { Owner = this };

    private void OcrGuide_OnClick(object sender, RoutedEventArgs e) => CreateOcrOperationGuide().ShowDialog();

    /// <summary>Keep existing Alt input access reachable even while details are collapsed.</summary>
    private bool TryFocusOcrDetail(KeyEventArgs e)
    {
        if (e.Key != Key.System || Keyboard.Modifiers != ModifierKeys.Alt ||
            !KeyboardPropertiesPane.IsVisible || !ViewModel.IsOcrInteractionMode ||
            !ViewModel.CanEditDocument || !ViewModel.HasSelectedOverlay) return false;
        return FocusOcrDetail(e.SystemKey);
    }

    internal bool FocusOcrDetail(Key key)
    {
        TextBox? field = key switch
        {
            Key.I => KeyboardField4,
            Key.X => KeyboardX,
            Key.Y => KeyboardY,
            Key.W => KeyboardWidth,
            Key.G => KeyboardHeight,
            Key.R => KeyboardField5,
            _ => null
        };
        if (field is null || !field.IsEnabled || !ViewModel.ShowSelectedOverlayProperties || !KeyboardPropertiesPane.IsVisible) return false;
        OcrDetailsExpander.IsExpanded = true;
        OcrDetailsExpander.UpdateLayout();
        field.BringIntoView();
        return field.Focus();
    }
}
