using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

public partial class App
{
    /// <summary>手入力や一覧選択の後も、倍率表示のバインドと各操作の同期が維持されることを検証します。</summary>
    private static async Task VerifyZoomSynchronizationAsync(
        MainWindow window, Func<Task> layoutAsync, Action<bool, string> check)
    {
        var viewModel = (MainWindowViewModel)window.DataContext;
        var slider = (Slider)window.FindName("StatusZoomSlider");
        var comboBox = (ComboBox)window.FindName("StatusZoomComboBox");
        var editor = (TextBox)comboBox.Template.FindName("PART_EditableTextBox", comboBox);
        void Invoke(string method, params object[] arguments) =>
            typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, arguments);
        async Task CheckSyncAsync(string scenario, double? expected = null)
        {
            await layoutAsync();
            check(BindingOperations.IsDataBound(comboBox, ComboBox.TextProperty) &&
                  (!expected.HasValue || viewModel.ZoomPercent == expected.Value) &&
                  Math.Abs(EditorInteractionMath.SliderPositionToZoomPercent(slider.Value) - viewModel.ZoomPercent) <= 0.500001 &&
                  comboBox.Text == viewModel.ZoomDisplay && editor.Text == viewModel.ZoomDisplay,
                $"Zoom synchronization after {scenario}: model={viewModel.ZoomPercent}, slider={slider.Value}, display={comboBox.Text}, editor={editor.Text}.");
        }
        async Task MoveSliderAsync(double value, string scenario)
        {
            slider.SetCurrentValue(RangeBase.ValueProperty, EditorInteractionMath.ZoomPercentToSliderPosition(value));
            await CheckSyncAsync(scenario, Math.Clamp(Math.Round(value), 25, 400));
        }
        async Task EnterTextAsync(string text, double expected)
        {
            editor.SetCurrentValue(TextBox.TextProperty, text);
            await layoutAsync();
            Invoke("ApplyZoomComboBoxText", comboBox);
            await CheckSyncAsync($"committing '{text}'", expected);
        }

        check(slider.Minimum == 0 && slider.Maximum == 100 && slider.IsMoveToPointEnabled,
            "Zoom slider uses a normalized range and allows moving directly to a clicked point.");
        check(Enumerable.Range(25, 376).All(zoom =>
                Math.Abs(EditorInteractionMath.SliderPositionToZoomPercent(EditorInteractionMath.ZoomPercentToSliderPosition(zoom)) - zoom) < 1e-9),
            "Every integer zoom from 25% through 400% round-trips through slider coordinates.");
        check(EditorInteractionMath.ZoomPercentToSliderPosition(0) == 0 &&
              EditorInteractionMath.ZoomPercentToSliderPosition(500) == 100 &&
              EditorInteractionMath.SliderPositionToZoomPercent(-1) == 25 &&
              EditorInteractionMath.SliderPositionToZoomPercent(101) == 400,
            "Slider conversion clamps both zoom and position at their endpoints.");
        foreach (var (position, zoom) in new[] { (0d, 25d), (25d, 62d), (40d, 85d), (49d, 98d), (50d, 100d), (51d, 106d), (60d, 160d), (75d, 250d), (100d, 400d) })
        {
            slider.SetCurrentValue(RangeBase.ValueProperty, position);
            await CheckSyncAsync($"moving to position {position}", zoom);
        }
        check(EditorInteractionMath.SliderPositionToZoomPercent(50) - EditorInteractionMath.SliderPositionToZoomPercent(40) == 15 &&
              EditorInteractionMath.SliderPositionToZoomPercent(60) - EditorInteractionMath.SliderPositionToZoomPercent(50) == 60,
            "The right half changes zoom four times as much as the left half for the same movement.");
        viewModel.ActualSizeCommand.Execute(null);
        await CheckSyncAsync("moving to the 100% center", 100);
        var track = (Track)slider.Template.FindName("PART_Track", slider);
        var marker = (FrameworkElement)slider.Template.FindName("ZoomCenterMarker", slider);
        foreach (var width in new[] { 92d, 140d })
        {
            slider.SetCurrentValue(FrameworkElement.WidthProperty, width);
            await layoutAsync();
            var thumbCenter = track.Thumb.TranslatePoint(new Point(track.Thumb.ActualWidth / 2, track.Thumb.ActualHeight / 2), slider);
            var markerCenter = marker.TranslatePoint(new Point(marker.ActualWidth / 2, marker.ActualHeight / 2), slider);
            check(Math.Abs(slider.Value - 50) < 1e-9 && Math.Abs(markerCenter.X - slider.ActualWidth / 2) <= 0.5 &&
                  Math.Abs(thumbCenter.X - markerCenter.X) <= 0.5 && marker.ActualHeight > track.Thumb.ActualHeight && !marker.IsHitTestVisible,
                $"At width {width}, the 100% thumb and non-interactive marker share the exact center, with the marker visible above/below.");
        }
        slider.SetCurrentValue(FrameworkElement.WidthProperty, 92d);
        viewModel.ZoomPercent = 99;
        Slider.IncreaseSmall.Execute(null, slider);
        await CheckSyncAsync("keyboard increment from 99% across center", 100);
        Slider.IncreaseSmall.Execute(null, slider);
        await CheckSyncAsync("keyboard increment from 100%", 101);
        Slider.DecreaseSmall.Execute(null, slider);
        await CheckSyncAsync("keyboard decrement to center", 100);
        Slider.DecreaseSmall.Execute(null, slider);
        await CheckSyncAsync("keyboard decrement below center", 99);
        Slider.IncreaseLarge.Execute(null, slider);
        await CheckSyncAsync("keyboard large increment across center", 109);
        Slider.DecreaseLarge.Execute(null, slider);
        await CheckSyncAsync("keyboard large decrement across center", 99);
        Slider.MinimizeValue.Execute(null, slider);
        await CheckSyncAsync("Home key endpoint", 25);
        check(!Slider.DecreaseSmall.CanExecute(null, slider), "Keyboard decrement is disabled at minimum zoom.");
        Slider.MaximizeValue.Execute(null, slider);
        await CheckSyncAsync("End key endpoint", 400);
        check(!Slider.IncreaseSmall.CanExecute(null, slider), "Keyboard increment is disabled at maximum zoom.");

        await MoveSliderAsync(137, "initial slider movement");
        // Leaving the field without typing also runs the commit handler. This must not remove Text's binding.
        Invoke("ApplyZoomComboBoxText", comboBox);
        await CheckSyncAsync("committing unchanged text", 137);
        await MoveSliderAsync(163, "slider movement after text commit");
        viewModel.ZoomInCommand.Execute(null);
        await CheckSyncAsync("toolbar zoom-in", 175);
        viewModel.ZoomOutCommand.Execute(null);
        await CheckSyncAsync("toolbar zoom-out", 150);
        viewModel.ActualSizeCommand.Execute(null);
        await CheckSyncAsync("toolbar actual size", 100);

        foreach (var tag in new[] { "150", "FitWidth", "FitHeight", "FitPage", "FitSelection" })
        {
            var option = comboBox.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, tag));
            comboBox.SetCurrentValue(Selector.SelectedItemProperty, option);
            await CheckSyncAsync($"dropdown option {tag}", tag == "150" ? 150 : null);
            check(comboBox.SelectedIndex == -1, $"Dropdown option {tag} resets selection for repeated use.");
            await MoveSliderAsync(173, $"slider movement after dropdown option {tag}");
            viewModel.ZoomInCommand.Execute(null);
            await CheckSyncAsync($"toolbar zoom after dropdown option {tag}", 175);
        }
        await EnterTextAsync("85", 85);
        await MoveSliderAsync(119, "slider movement after manual input");
        await EnterTextAsync("125%", 125);
        await EnterTextAsync("500%", 400);
        await EnterTextAsync("1", 25);
        await EnterTextAsync("invalid", 25);
        await MoveSliderAsync(137, "slider movement after invalid input");
        foreach (var method in new[] { "FitWidthButton_OnClick", "FitHeightButton_OnClick", "FitPageButton_OnClick" })
        {
            Invoke(method, window, new RoutedEventArgs());
            await CheckSyncAsync(method);
        }
        viewModel.ActualSizeCommand.Execute(null);
        await CheckSyncAsync("final actual size", 100);
    }
}
