using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

public partial class App
{
    /// <summary>フラットなステータスボタンと、サイズ／ラベル設定ごとのコンパクトな余白を検証します。</summary>
    private static async Task VerifyCompactChromeAsync(MainWindow window, Func<Task> layoutAsync, Action<bool, string> check)
    {
        var viewModel = (MainWindowViewModel)window.DataContext;
        foreach (var (name, disabledZoom) in new[] { ("StatusZoomOutButton", 25d), ("StatusZoomInButton", 400d) })
        {
            var button = (Button)window.FindName(name);
            viewModel.ZoomPercent = 100;
            await layoutAsync();
            var chrome = (Border)button.Template.FindName("StatusButtonChrome", button);
            check(button.IsEnabled && chrome.BorderThickness == new Thickness(0) &&
                  chrome.Background is SolidColorBrush { Color.A: 0 } && button.ActualWidth == 20 && button.ActualHeight == 20,
                $"{name} is flat and transparent at rest, retaining a 20px hit target.");
            var states = button.Template.Triggers.OfType<Trigger>().Select(trigger => trigger.Property).ToArray();
            check(states.Contains(UIElement.IsMouseOverProperty) && states.Contains(ButtonBase.IsPressedProperty) &&
                  states.Contains(UIElement.IsKeyboardFocusedProperty) &&
                  button.Template.FindName("StatusButtonFocusIndicator", button) is Border,
                $"{name} retains hover, press, and keyboard-focus feedback.");
            viewModel.ZoomPercent = disabledZoom;
            await layoutAsync();
            check(!button.IsEnabled && chrome.BorderThickness == new Thickness(0) &&
                  chrome.Background is SolidColorBrush { Color.A: 0 } && button.Opacity < 1,
                $"{name} stays frameless and transparent when disabled.");
        }
        viewModel.ZoomPercent = 100;
        await layoutAsync();
        var toolbar = (ToolBar)window.FindName("MainToolbarPanel");
        check(toolbar.Padding == new Thickness(4, 2, 4, 2) &&
              toolbar.Items.OfType<Separator>().All(separator => separator.Margin == new Thickness(4, 0, 4, 0)),
            "Main toolbar padding and separator spacing are compact.");
        check(toolbar.Items.OfType<ButtonBase>().All(button => button.Padding == new Thickness(2) && button.Margin == new Thickness(1)),
            "Main toolbar buttons and toggles share 2px padding and 1px margins.");
        foreach (var (style, iconSize) in new[] { ("PropertyIconButtonStyle", 24d), ("LeftPaneIconButtonStyle", 18d) })
        {
            var sample = new Button { Style = (Style)window.FindResource(style) };
            check(sample.Width - sample.Padding.Left - sample.Padding.Right - 4 >= iconSize &&
                  sample.Height - sample.Padding.Top - sample.Padding.Bottom - 4 >= iconSize && sample.Margin == new Thickness(1),
                $"{style} leaves room for its original icon even with the 2px keyboard-focus border.");
        }

        // Isolate size variants from the real application's persisted preferences.
        foreach (var size in new[] { 28d, 36d, 64d })
        foreach (var showText in new[] { false, true })
        {
            var expectedSize = Math.Max(24, size - 4);
            var expectedIconSize = Math.Clamp(size - 16, 14, 36);
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var host = new Window
            {
                DataContext = new { CompactToolbarButtonSize = expectedSize, ToolbarIconSize = expectedIconSize, ShowToolbarText = showText },
                Content = panel,
            };
            try
            {
                foreach (var toggle in new[] { false, true })
                {
                    ButtonBase sample = toggle ? new ToggleButton() : new Button();
                    sample.Style = (Style)window.FindResource(toggle ? "ToolbarIconToggleStyle" : "ToolbarIconButtonStyle");
                    var icon = new Viewbox
                    {
                        Style = (Style)window.FindResource("ToolbarIconViewboxStyle"),
                        Child = new Border { Width = 24, Height = 24, Background = Brushes.Black },
                    };
                    var label = new TextBlock { Text = "ツールバー操作", Style = (Style)window.FindResource("ToolbarLabelStyle") };
                    var content = new StackPanel { Orientation = Orientation.Horizontal };
                    content.Children.Add(icon);
                    content.Children.Add(label);
                    sample.Content = content;
                    panel.Children.Add(sample);
                }
                await layoutAsync();
                panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                panel.Arrange(new Rect(panel.DesiredSize));
                panel.UpdateLayout();
                foreach (var sample in panel.Children.OfType<ButtonBase>())
                {
                    var content = (StackPanel)sample.Content;
                    var icon = (Viewbox)content.Children[0];
                    var label = (TextBlock)content.Children[1];
                    check(sample.ActualHeight == expectedSize && sample.ActualWidth >= expectedSize &&
                          (showText || sample.ActualWidth == expectedSize) &&
                          icon.ActualWidth == expectedIconSize && icon.ActualHeight == expectedIconSize &&
                          label.Visibility == (showText ? Visibility.Visible : Visibility.Collapsed) &&
                          content.DesiredSize.Width <= sample.ActualWidth - 6 + 0.01 &&
                          content.DesiredSize.Height <= sample.ActualHeight - 6 + 0.01,
                        $"{sample.GetType().Name} at size {size}, labels={showText}: unchanged icon fits the compact button without clipping.");
                }
            }
            finally { host.Close(); }
        }
    }
}
