using System.IO;
using System.Text;
using System.Text.Json;
using PdfCorrectorium.App.Services;
using Windows.Media.Playback;

namespace PdfCorrectorium.App;

public partial class App
{
    private static async Task VerifyHalfStepSpeechAsync(string output, Action<bool, string> check)
    {
        static double Duration(byte[] bytes)
        {
            uint byteRate = 0;
            for (var offset = 12; offset + 8 <= bytes.Length;)
            {
                var name = Encoding.ASCII.GetString(bytes, offset, 4);
                var length = BitConverter.ToUInt32(bytes, offset + 4);
                if (name == "fmt ") byteRate = BitConverter.ToUInt32(bytes, offset + 16);
                if (name == "data" && byteRate > 0) return (double)length / byteRate;
                offset = checked(offset + 8 + (int)length + (int)(length % 2));
            }
            throw new InvalidDataException("PCM wave duration is unavailable.");
        }
        static async Task Until(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!condition()) await Task.Delay(15, timeout.Token);
        }
        var durations = new List<double>();
        foreach (var adjustment in new[] { 0d, .5, 1d })
        {
            var wave = Path.Combine(output, "speed-" + adjustment.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".wav");
            await using var engine = new WindowsSpeechService(wave);
            var voices = await engine.InitializeAsync();
            var voice = voices.First(item => item.Language.StartsWith("ja", StringComparison.Ordinal));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var speaking = engine.SpeakAsync("読み上げ速度の中間値を確認します。", voice.Name, adjustment, timeout.Token);
            if (adjustment == .5)
            {
                await Until(() => engine.DiagnosticState == MediaPlaybackState.Playing);
                await engine.PauseAsync(true);
                await Until(() => engine.DiagnosticState == MediaPlaybackState.Paused);
                var position = engine.DiagnosticPosition!.Value;
                await Task.Delay(150);
                check(Math.Abs((engine.DiagnosticPosition!.Value - position).TotalMilliseconds) < 40,
                    "Actual muted Windows playback holds its audio position while paused.");
                await engine.PauseAsync(false);
            }
            await speaking;
            durations.Add(Duration(await File.ReadAllBytesAsync(wave)));
            if (adjustment == .5)
            {
                await using var cancelEngine = new WindowsSpeechService(Path.Combine(output, "speed-cancel-reuse.wav"));
                await cancelEngine.InitializeAsync();
                using var cancellation = new CancellationTokenSource();
                var cancelledSpeech = cancelEngine.SpeakAsync("読み上げ速度の中間値を確認します。", voice.Name, adjustment, cancellation.Token);
                await Until(() => cancelEngine.DiagnosticState == MediaPlaybackState.Playing);
                cancellation.Cancel();
                var cancelled = false;
                try { await cancelledSpeech.WaitAsync(TimeSpan.FromSeconds(3)); } catch (OperationCanceledException) { cancelled = true; }
                check(cancelled && cancelEngine.DiagnosticState is null, "Cancelling active native playback releases the player promptly.");
                await cancelEngine.SpeakAsync("再開します。", voice.Name, adjustment, timeout.Token);
                check(true, "The native speech engine can immediately synthesize and play again after cancellation.");
            }
        }
        check(durations[0] > durations[1] && durations[1] > durations[2],
            "Actual 0.5-step audio duration is strictly between the adjacent integer-speed durations.");
        await File.WriteAllTextAsync(Path.Combine(output, "half-step-durations.json"), JsonSerializer.Serialize(durations));
    }
}
