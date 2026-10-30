using PdfCorrectorium.Core.Documents;
using System.IO.Compression;
using System.Text.Json;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.ProjectFormat;

internal static class WordReadingContractTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static Task ParseAsync()
    {
        Check(WordReadingRules.TryParse(" 今日 = きょう \r\n\r\n日本語=にほんご", out var rules, out var error) && error.Length == 0 && rules.Count == 2, "Trimmed legacy entries and blank lines must remain valid.");
        Check(WordReadingRules.TryParse(WordReadingRules.Format(rules), out var restored, out _) && restored.SequenceEqual(rules), "Editor and legacy bulk syntax must share roundtrip semantics.");
        foreach (var value in new[] { "wrong", "=よみ", "語=", " =よみ", "語= " })
            Check(!WordReadingRules.TryParse(value, out var invalid, out var message) && invalid.Count == 0 && message.Contains("1行目"), "Invalid entries must fail without partial rules.");
        return Task.CompletedTask;
    }
    internal static Task MatchesAsync()
    {
        WordReading R(string word, string reading) => new() { SurfaceText = word, ReadingText = reading };
        var rules = new[] { R("日本", "にほん"), R("日本語", "にほんご"), R("😀", "えがお"), R("か\u3099", "が") };
        const string text = "日本語、日本😀か\u3099日本語";
        var matches = WordReadingRules.FindMatches(text, rules);
        Check(matches.Count == 5 && matches[0].SurfaceText == "日本語" && matches[^1].SurfaceText == "日本語", "Preview uses longest exact repeated matches.");
        Check(matches.All(match => text.Substring(match.Start, match.Length) == match.SurfaceText), "UTF16 match ranges preserve Unicode source mapping.");
        Check(SpeechText.ApplyReadings(text, rules) == "にほんご、にほんえがおがにほんご", "Speech and preview resolve identical non-cascading readings.");
        Check(WordReadingRules.FindMatches("X", [R("X", "one"), R("X", "two")]).Count == 0, "Conflicting definitions must not show a misleading ruby.");
        Check(WordReadingRules.FindMatches("AB", [R("AB", "C"), R("C", "D")]).Single().ReadingText == "C", "Preview cannot cascade replacements.");
        Check(WordReadingRules.FindMatches("abc", [R("ABC", "x")]).Count == 0, "Exact match case must be preserved.");
        Check(WordReadingRules.FindMatches("か\u3099😀", [R("か", "partial"), R("\uDE00", "partial")]).Count == 0,
            "Speech and preview must not replace part of a combining sequence or surrogate pair.");
        return Task.CompletedTask;
    }
    internal static Task SharedRulesAsync()
    {
        WordReading R(string word, string reading) => new() { SurfaceText = word, ReadingText = reading };
        var common = new[] { R("今日", "きょう"), R("日本語", "にほんご") };
        var local = new[] { R("今日", "こんにち") };
        Check(SpeechText.ApplyReadings("今日の日本語", WordReadingRules.Merge(common, local)) == "こんにちのにほんご", "Local exact-surface exceptions must override common pronunciation.");
        Check(SpeechText.ApplyReadings("今日の日本語", WordReadingRules.Merge(common, [])) == "きょうのにほんご", "Other rows must inherit shared rules without materialized copies.");
        Check(WordReadingRules.IsValidShared(common) && !WordReadingRules.IsValidShared([R("語", "a"), R("語", "b")]), "Shared dictionaries must reject ambiguous duplicate surfaces.");
        Check(!WordReadingRules.IsValidShared(null) && !WordReadingRules.IsValidShared([R("語", "")]) &&
            !WordReadingRules.IsValidShared([R("=", "よみ")]) && !WordReadingRules.IsValidShared([R("語", "改\n行")]) &&
            !WordReadingRules.IsValidShared([R(new string('a', 257), "a")]), "Shared inputs must be bounded and valid.");
        Check(!WordReadingRules.IsValidShared(Enumerable.Range(0, 5001).Select(index => R(index.ToString(), "よみ")).ToArray()), "Untrusted dictionary counts must be capped.");
        return Task.CompletedTask;
    }
    internal static async Task SharedPersistenceAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "PdfCorrectorium-SharedReadings-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var pdf = Path.Combine(root, "input.pdf"); await File.WriteAllTextAsync(pdf, "%PDF-1.7\n%%EOF");
            var service = new ProjectPackageService();
            var project = new PdfCorrectoriumProject { SourcePdf = await service.CreateSourceReferenceAsync(pdf),
                WordReadings = [new() { SurfaceText = "日本語", ReadingText = "にほんご" }] };
            var file = Path.Combine(root, "shared.pdfocrproj"); await service.SaveAsync(file, project);
            var reopened = await service.OpenAsync(file);
            Check(reopened.WordReadings.SequenceEqual(project.WordReadings), "Shared readings must survive a real package save/open.");
            using (var zip = ZipFile.OpenRead(file))
            using (var json = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open()))
                Check(json.RootElement.GetProperty("formatVersion").GetString() == "1.14" &&
                    json.RootElement.GetProperty("minimumApplicationVersion").GetString() == "1.0.0-dev.262", "New packages must protect shared pronunciations from old savers.");
            Check(ProjectManifest.IsSupportedVersion("1.13"), "Previous paragraph-only projects must remain readable.");
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Update))
            {
                var entry = zip.GetEntry("project.json")!; string text; using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd();
                var data = System.Text.Json.Nodes.JsonNode.Parse(text)!.AsObject(); data.Remove("wordReadings"); entry.Delete();
                using var writer = new StreamWriter(zip.CreateEntry("project.json").Open()); await writer.WriteAsync(data.ToJsonString());
            }
            Check((await service.OpenAsync(file)).WordReadings.Count == 0, "Missing shared data must default to an empty dictionary.");
            var rejected = false;
            try { await service.SaveAsync(Path.Combine(root, "invalid.pdfocrproj"), project with { WordReadings = null! }); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Null shared dictionaries must be rejected before writing.");
        }
        finally { Directory.Delete(root, true); }
    }
}
