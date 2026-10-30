using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using PdfCorrectorium.App.ViewModels;
using PdfCorrectorium.App.Controls;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.Resources;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App;

public partial class MainWindow
{
    private Popup? _rubyEditor;
    private bool TryOpenRubyEditor(MouseButtonEventArgs e)
    {
        if (!ViewModel.IsOcrEditMode || !ViewModel.AreWordReadingRubiesVisible) return false;
        foreach (var layer in FindVisualDescendants<OcrCharacterLayer>(OverlayCanvas).Where(layer => layer.IsVisible))
            if (layer.DataContext is OverlayRegionViewModel region && layer.FindReadingAt(e.GetPosition(layer)) is { } reading)
            {
                OpenRubyEditor(region, reading, e.GetPosition(PreviewPageHost)); return true;
            }
        return false;
    }
    internal Popup OpenRubyEditor(OverlayRegionViewModel region, WordReadingMatch reading, Point position)
    {
        _rubyEditor?.SetCurrentValue(Popup.IsOpenProperty, false);
        ViewModel.SetOverlaySelection([region], region);
        region.SelectCharacterRangeByTextOffset(reading.Start, reading.Length);
        ViewModel.WordReadingScopeIndex = region.WordReadingDefinitions.Any(item => item.SurfaceText == reading.SurfaceText) ? 0 : 1;
        ViewModel.WordReadingSurface = reading.SurfaceText; ViewModel.WordReadingPronunciation = reading.ReadingText;
        string L(string text) => LocalizationService.Translate(text);
        var panel = new StackPanel { Margin = new Thickness(8), DataContext = ViewModel };
        panel.Children.Add(new TextBlock { Text = reading.SurfaceText, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var scope = new ComboBox { Margin = new Thickness(0, 5, 0, 0), ItemsSource = new[] { L("この行だけ"), L("プロジェクト全体") } };
        scope.SetBinding(ComboBox.SelectedIndexProperty, new Binding(nameof(MainWindowViewModel.WordReadingScopeIndex)) { Mode = BindingMode.TwoWay });
        panel.Children.Add(scope);
        var pronunciation = new TextBox { Margin = new Thickness(0, 5, 0, 0) };
        pronunciation.SetBinding(TextBox.TextProperty, new Binding(nameof(MainWindowViewModel.WordReadingPronunciation)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        System.Windows.Automation.AutomationProperties.SetName(pronunciation, L("よみ"));
        panel.Children.Add(pronunciation);
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        hint.SetBinding(TextBlock.TextProperty, new Binding(nameof(MainWindowViewModel.WordReadingScopeDescription)));
        panel.Children.Add(hint);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        error.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        error.SetBinding(TextBlock.TextProperty, new Binding(nameof(MainWindowViewModel.WordReadingInputMessage))); panel.Children.Add(error);
        var apply = new Button { Style = (Style)FindResource("PropertyTextActionButtonStyle"), Margin = new Thickness(0, 5, 0, 0) };
        ActionIcon.SetKey(apply, "Apply");
        apply.SetBinding(ContentControl.ContentProperty, new Binding(nameof(MainWindowViewModel.WordReadingActionText)));
        apply.SetBinding(Button.CommandProperty, new Binding(nameof(MainWindowViewModel.RegisterWordReadingCommand)));
        panel.Children.Add(apply);
        var border = new Border { Child = panel, Width = 280, BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "InputBrush"); border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var popup = new Popup { Child = border, PlacementTarget = PreviewPageHost, Placement = PlacementMode.RelativePoint,
            HorizontalOffset = position.X, VerticalOffset = position.Y, StaysOpen = false, AllowsTransparency = true };
        void ApplyAndClose()
        {
            if (!ReferenceEquals(ViewModel.SelectedOverlay, region)) { popup.IsOpen = false; return; }
            if (ViewModel.RegisterWordReadingCommand.CanExecute(null)) ViewModel.RegisterWordReadingCommand.Execute(null);
            popup.IsOpen = false;
        }
        // Button executes its bound command after Click; closing retains the draft for that execution.
        apply.Click += (_, _) => popup.IsOpen = false;
        pronunciation.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; }
            else if (e.Key == Key.Enter && ViewModel.RegisterWordReadingCommand.CanExecute(null)) { ApplyAndClose(); e.Handled = true; }
        };
        void Changed(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(MainWindowViewModel.SelectedOverlay) or nameof(MainWindowViewModel.IsOpeningDocument) or nameof(MainWindowViewModel.SelectedPage))
                popup.IsOpen = false;
        }
        ViewModel.PropertyChanged += Changed;
        popup.Closed += (_, _) => { ViewModel.PropertyChanged -= Changed; if (ReferenceEquals(_rubyEditor, popup)) _rubyEditor = null; };
        popup.Opened += (_, _) => { pronunciation.Focus(); pronunciation.SelectAll(); };
        _rubyEditor = popup; popup.IsOpen = true;
        return popup;
    }
    private void WordReadingMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { CanEditWordReadings: true }) return;
        PropertyPanes["properties"].Show();
        PropertyPanes["properties"].IsSelected = true;
        WordReadingExpander.IsExpanded = true;
        Dispatcher.BeginInvoke(() =>
        {
            KeyboardField4.BringIntoView();
            Keyboard.Focus(KeyboardField4);
            KeyboardField4.SelectAll();
        }, DispatcherPriority.Input);
    }
}
