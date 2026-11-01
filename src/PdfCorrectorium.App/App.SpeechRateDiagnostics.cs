using System.IO;
using System.Text;
using System.Text.Json;
using PdfCorrectorium.App.Services;
using Windows.Media.Playback;

namespace PdfCorrectorium.App;

public partial class App
{
    private static WindowsSpeechVoice SelectDiagnosticSpeechVoice(IReadOnlyList<WindowsSpeechVoice> voices) =>
        voices.FirstOrDefault(voice => voice.IsMicrosoft && voice.Language.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) ??
        voices.FirstOrDefault(voice => voice.IsMicrosoft) ??
        throw new InvalidOperationException("At least one installed Microsoft Windows voice is required for the native speech diagnostic.");

    private static double SpeechWaveDuration(byte[] bytes)
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

    private static async Task VerifyHalfStepSpeechAsync(string output, Action<bool, string> check)
    {
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
            var voice = SelectDiagnosticSpeechVoice(voices);
            var japanese = voice.Language.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
            var synthesisText = japanese ? "読み上げ速度の中間値を確認します。" : "Checking intermediate values for the speech playback speed.";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var speaking = engine.SpeakAsync(synthesisText, voice.Name, adjustment, timeout.Token);
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
            durations.Add(SpeechWaveDuration(await File.ReadAllBytesAsync(wave)));
            if (adjustment == .5)
            {
                await using var cancelEngine = new WindowsSpeechService(Path.Combine(output, "speed-cancel-reuse.wav"));
                await cancelEngine.InitializeAsync();
                using var cancellation = new CancellationTokenSource();
                var cancelledSpeech = cancelEngine.SpeakAsync(synthesisText, voice.Name, adjustment, cancellation.Token);
                await Until(() => cancelEngine.DiagnosticState == MediaPlaybackState.Playing);
                cancellation.Cancel();
                var cancelled = false;
                try { await cancelledSpeech.WaitAsync(TimeSpan.FromSeconds(3)); } catch (OperationCanceledException) { cancelled = true; }
                check(cancelled && cancelEngine.DiagnosticState is null, "Cancelling active native playback releases the player promptly.");
                await cancelEngine.SpeakAsync(japanese ? "再開します。" : "Resuming playback.", voice.Name, adjustment, timeout.Token);
                check(true, "The native speech engine can immediately synthesize and play again after cancellation.");
            }
        }
        check(durations[0] > durations[1] && durations[1] > durations[2],
            "Actual 0.5-step audio duration is strictly between the adjacent integer-speed durations.");
        await File.WriteAllTextAsync(Path.Combine(output, "half-step-durations.json"), JsonSerializer.Serialize(durations));
    }

    private static async Task VerifySpeechSynthesisOnlyAsync(string output, Action<bool, string> check)
    {
        await using (var normalEngine = new WindowsSpeechService())
        {
            var rejected = false;
            try { await normalEngine.SynthesizeForDiagnosticsAsync("guard", "", 0, default); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Synthesis-only diagnostics reject the normal service without an explicit WAV destination.");
        }
        var durations = new List<double>();
        foreach (var rate in new[] { 0d, .5, 1d })
        {
            var wave = Path.Combine(output, "synthesis-" + rate.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".wav");
            await using var engine = new WindowsSpeechService(wave);
            var voice = SelectDiagnosticSpeechVoice(await engine.InitializeAsync());
            File.WriteAllText(Path.Combine(output, "speech-voice.json"), JsonSerializer.Serialize(voice));
            var text = voice.Language.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ?
                "読み上げ速度の中間値を確認します。" : "Checking intermediate values for the speech synthesis speed.";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await engine.SynthesizeForDiagnosticsAsync(text, voice.Name, rate, timeout.Token);
            var bytes = await File.ReadAllBytesAsync(wave);
            check(bytes.Length > 1000 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && bytes.Skip(80).Any(value => value != 0),
                "Actual Windows synthesis produces a nonempty WAV at speed " + rate.ToString(System.Globalization.CultureInfo.InvariantCulture));
            durations.Add(SpeechWaveDuration(bytes));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var rejected = false;
            try { await engine.SynthesizeForDiagnosticsAsync(text, voice.Name, rate, cancelled.Token); }
            catch (OperationCanceledException) { rejected = true; }
            check(rejected, "Cancelled synthesis is rejected without requiring an audio renderer.");
            await engine.SynthesizeForDiagnosticsAsync(text, voice.Name, rate, timeout.Token);
            check((await File.ReadAllBytesAsync(wave)).Length > 1000, "The synthesis service remains reusable after cancellation.");
        }
        check(durations[0] > durations[1] && durations[1] > durations[2],
            "Actual synthesized 0.5-step WAV duration lies strictly between adjacent integer rates.");
        File.WriteAllText(Path.Combine(output, "half-step-durations.json"), JsonSerializer.Serialize(durations));
    }

}
