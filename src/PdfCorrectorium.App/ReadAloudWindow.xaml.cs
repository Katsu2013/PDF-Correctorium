using System.Windows;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.App.ViewModels;

namespace PdfCorrectorium.App;

// A reusable floating host for the same panel used in the docked workspace.
public partial class ReadAloudWindow : Window
{
    internal ReadAloudPanel Panel { get; }
    internal Task? PlaybackTask => Panel.PlaybackTask;
    internal int? FollowingPage => Panel.FollowingPage;
    internal ReadAloudWindow(MainWindow owner, MainWindowViewModel viewModel, ISpeechPlayback? speech = null)
    {
        InitializeComponent();
        Panel = new ReadAloudPanel(owner, viewModel, speech); Content = Panel;
        Panel.CloseRequested += (_, _) => Close();
        foreach (var name in new[] { "ScopeBox", "PlayButton", "PauseButton", "StopButton", "PreviousButton", "NextButton",
            "VoiceBox", "RateSlider", "NavigationUnitBox", "PlaybackStatus", "PositionText", "SpokenText", "SpeechOptionsExpander" })
            RegisterName(name, Panel.FindName(name));
        LocalizationService.Apply(this);
    }
    private async void Window_OnLoaded(object sender, RoutedEventArgs e) => await Panel.InitializeSpeechAsync();
    private async void Window_OnClosed(object? sender, EventArgs e) => await Panel.DisposeAsync();
    internal void StopPlayback(string? message = null) => Panel.StopPlayback(message);
    internal static IEnumerable<string> Chunks(string text) => ReadAloudPanel.Chunks(text);
}