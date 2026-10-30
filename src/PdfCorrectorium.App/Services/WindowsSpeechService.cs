using System.IO;
using System.Windows.Threading;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace PdfCorrectorium.App.Services;

internal sealed record WindowsSpeechVoice(string Name, string Language, bool IsMicrosoft);

internal interface ISpeechPlayback : IAsyncDisposable
{
    string DefaultVoice { get; }
    Task<IReadOnlyList<WindowsSpeechVoice>> InitializeAsync();
    Task SpeakAsync(string text, string voice, double rate, CancellationToken token, Action<int>? progress = null);
    Task PauseAsync(bool paused);
}

/// <summary>Local Windows synthesis and in-memory playback, initialized only when the reader is opened.</summary>
internal sealed class WindowsSpeechService : ISpeechPlayback
{
    private readonly TaskCompletionSource<Dispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string? _wavePath;
    private SpeechSynthesizer? _synth;
    private MediaPlayer? _player;
    private Task<IReadOnlyList<WindowsSpeechVoice>>? _initialization;
    private bool _disposed, _paused;
    public string DefaultVoice { get; private set; } = string.Empty;
    internal WindowsSpeechService(string? diagnosticWavePath = null) => _wavePath = diagnosticWavePath;
    internal MediaPlaybackState? DiagnosticState => _player?.PlaybackSession.PlaybackState;
    internal TimeSpan? DiagnosticPosition => _player?.PlaybackSession.Position;
    internal static bool VoiceNamesMatch(string name, string saved) =>
        string.Equals(name, saved, StringComparison.OrdinalIgnoreCase) ||
        saved.EndsWith(" Desktop", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(name, saved[..^8], StringComparison.OrdinalIgnoreCase);
    internal static double SpeakingRate(double adjustment) => Math.Pow(3, ApplicationSettings.NormalizeSpeechRate(adjustment) / 10);

    public Task<IReadOnlyList<WindowsSpeechVoice>> InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _initialization ??= InitializeCoreAsync();
    }
    private async Task<IReadOnlyList<WindowsSpeechVoice>> InitializeCoreAsync()
    {
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            try
            {
                _synth = new SpeechSynthesizer();
                _synth.Options.IncludeWordBoundaryMetadata = true;
                DefaultVoice = _synth.Voice.DisplayName;
                _ready.TrySetResult(dispatcher);
                Dispatcher.Run();
            }
            catch (Exception error) { _ready.TrySetException(error); }
            finally { _synth?.Dispose(); _synth = null; }
        }) { IsBackground = true, Name = "PDF Correctorium speech" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await _ready.Task;
        return SpeechSynthesizer.AllVoices.Select(voice => new WindowsSpeechVoice(voice.DisplayName, voice.Language, true)).ToArray();
    }

    public async Task SpeakAsync(string text, string voice, double rate, CancellationToken token, Action<int>? progress = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var cancellation = linked.Token;
        await InitializeAsync().WaitAsync(cancellation);
        await _serial.WaitAsync(cancellation);
        var dispatcher = await _ready.Task;
        SpeechSynthesisStream? stream = null;
        MediaSource? source = null;
        MediaPlayer? player = null;
        var active = 1;
        Action? detach = null;
        try
        {
            var synthesis = await dispatcher.InvokeAsync(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                if (voice.Length > 0)
                    _synth!.Voice = SpeechSynthesizer.AllVoices.FirstOrDefault(item => VoiceNamesMatch(item.DisplayName, voice)) ??
                        throw new InvalidOperationException("The selected Windows voice is no longer installed.");
                _synth!.Options.SpeakingRate = SpeakingRate(rate);
                return _synth.SynthesizeTextToStreamAsync(text).AsTask(cancellation);
            });
            stream = await synthesis;
            cancellation.ThrowIfCancellationRequested();
            if (_wavePath is not null)
            {
                using var copy = stream.CloneStream(); copy.Seek(0);
                using var input = copy.AsStreamForRead();
                await using var output = File.Create(_wavePath);
                await input.CopyToAsync(output, cancellation);
            }
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await dispatcher.InvokeAsync(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                source = MediaSource.CreateFromStream(stream, stream.ContentType);
                var item = new MediaPlaybackItem(source);
                var registered = new HashSet<TimedMetadataTrack>();
                void CueEntered(TimedMetadataTrack sender, MediaCueEventArgs args)
                {
                    if (Volatile.Read(ref active) != 0 && !cancellation.IsCancellationRequested && !_disposed &&
                        args.Cue is SpeechCue { StartPositionInInput: { } position })
                        progress?.Invoke(Math.Clamp(position, 0, Math.Max(0, text.Length - 1)));
                }
                void RegisterTracks()
                {
                    if (Volatile.Read(ref active) == 0 || cancellation.IsCancellationRequested || _disposed) return;
                    for (var index = 0; index < item.TimedMetadataTracks.Count; index++)
                    {
                        var track = item.TimedMetadataTracks[index];
                        if (track.TimedMetadataKind != TimedMetadataKind.Speech || track.Label != "SpeechWord") continue;
                        if (registered.Add(track)) track.CueEntered += CueEntered;
                        item.TimedMetadataTracks.SetPresentationMode((uint)index, TimedMetadataTrackPresentationMode.ApplicationPresented);
                    }
                }
                void TracksChanged(MediaPlaybackItem sender, Windows.Foundation.Collections.IVectorChangedEventArgs args)
                {
                    if (!dispatcher.HasShutdownStarted) _ = dispatcher.InvokeAsync(RegisterTracks);
                }
                item.TimedMetadataTracksChanged += TracksChanged;
                RegisterTracks();
                player = new MediaPlayer { AutoPlay = false, IsMuted = _wavePath is not null };
                player.CommandManager.IsEnabled = false;
                void Ended(MediaPlayer sender, object args) { if (Volatile.Read(ref active) != 0) completion.TrySetResult(); }
                void Failed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
                { if (Volatile.Read(ref active) != 0) completion.TrySetException(new InvalidOperationException(args.ErrorMessage)); }
                player.MediaEnded += Ended;
                player.MediaFailed += Failed;
                detach = () =>
                {
                    item.TimedMetadataTracksChanged -= TracksChanged;
                    foreach (var track in registered) track.CueEntered -= CueEntered;
                    player.MediaEnded -= Ended; player.MediaFailed -= Failed;
                };
                _player = player;
                player.Source = item;
                progress?.Invoke(0);
                if (!_paused) player.Play();
            });
            await completion.Task.WaitAsync(cancellation);
        }
        finally
        {
            Volatile.Write(ref active, 0);
            try { await dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(_player, player)) _player = null;
                _paused = false;
                detach?.Invoke();
                player?.Pause(); player?.Dispose(); source?.Dispose(); stream?.Dispose();
            }); }
            finally { _serial.Release(); }
        }
    }

    public async Task PauseAsync(bool paused)
    {
        if (_initialization is null || _disposed) return;
        var dispatcher = await _ready.Task;
        await dispatcher.InvokeAsync(() =>
        {
            _paused = paused;
            if (paused) _player?.Pause(); else _player?.Play();
        });
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel();
        if (_initialization is null) { _lifetime.Dispose(); return; }
        await _serial.WaitAsync();
        try
        {
            var dispatcher = await _ready.Task;
            if (!dispatcher.HasShutdownStarted)
                await dispatcher.InvokeAsync(() => dispatcher.BeginInvokeShutdown(DispatcherPriority.Background));
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException or System.PlatformNotSupportedException)
        { /* Initialization failure is reported by the reader. */ }
        finally { _serial.Release(); _lifetime.Dispose(); }
    }
}
