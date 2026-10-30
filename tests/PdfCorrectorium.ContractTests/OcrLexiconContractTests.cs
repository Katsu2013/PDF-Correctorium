using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using PdfCorrectorium.Core.Analysis;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.ProjectFormat;

internal static class OcrLexiconContractTests
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static OcrCorrectionRule Rule(string source, string target, bool automatic = false) => new() { Source = source, Target = target, AllowAutomatic = automatic };
    private static void MustFail<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }

    internal static Task SharedIndexAsync()
    {
        var samples = new[] { new OcrWordSample(1, Guid.NewGuid(), "Node.js Node.js Node.js Node.js Node.js Node.is rnode windovv") };
        var accumulator = new OcrWordAccumulator(); foreach (var sample in samples) accumulator.Update(sample.PageNumber, sample.RegionId, sample.Text);
        var dictionary = new OcrLexicon { KnownTerms = ["mode", "window", "日本語辞書"] };
        var index = new OcrLexiconIndex(dictionary, accumulator.Snapshot());
        var original = new OcrSimilarWordAnalyzer().Analyze(samples, new(KnownTerms: ["mode", "window"]));
        var shared = index.Analyze(samples);
        Check(original.Candidates.Select(c => (c.Word, c.SuggestedWord)).SequenceEqual(shared.Candidates.Select(c => (c.Word, c.SuggestedWord))), "The shared index preserves the Latin analyzer's existing candidates.");
        var query = index.Query("Node.is rnode windovv 日本語辤書");
        Check(query.Matches.Any(m => m.Target == "Node.js") && query.Matches.Any(m => m.Target == "日本語辞書"), "Frequency and registered Japanese phrases share a line query.");
        Check(!query.Matches.Any(m => m.Target == "mode") && !query.Matches.Any(m => m.Target == "window"), "Re-OCR excludes length-changing rn/m and vv/w candidates.");
        Check(shared.Candidates.Any(c => c.Word == "rnode" && c.SuggestedWord == "mode"), "Normal correction still permits length-changing corrections.");
        Check(!query.CanApplyAutomatically && index.Query("Node.」s").SuggestedText == "Node.js", "Frequency is not permission and quote/j confusion retains exact source offsets.");
        var unseen = new OcrLexiconIndex(new() { KnownTerms = ["JavaScript"] }, []).Query("」avaScript");
        Check(unseen.SuggestedText == "JavaScript" && !unseen.CanApplyAutomatically, "A new raw OCR term queries registered knowledge without scanning document text.");
        return Task.CompletedTask;
    }

    internal static Task RulesAsync()
    {
        var dictionary = new OcrLexicon { Rules = [Rule("Node.is", "Node.js", true), Rule("日本語辤書", "日本語辞書", true)] };
        var query = new OcrLexiconIndex(dictionary, []).Query("😀 Node.is と日本語辤書");
        Check(query.CanApplyAutomatically && query.SuggestedText == "😀 Node.js と日本語辞書", "Only explicitly permitted, exact same-grapheme rules auto-apply.");
        Check(query.Matches.All(m => query.RawText.Substring(m.Start, m.Length) == m.Source), "Offsets refer to the unmodified UTF-16 raw result.");
        Check(new OcrLexiconIndex(dictionary, []).Query("my_Node.is Node.isExtra node.is").Matches.Count == 0, "Latin rules match whole tokens, preserving case and identifier boundaries.");
        Check(!new OcrLexiconIndex(dictionary, []).Query("Node.is", locked: true).CanApplyAutomatically, "Locked targets reject automatic use.");
        Check(!new OcrLexiconIndex(dictionary with { Rules = [Rule("Node.is", "Node.js")] }, []).Query("Node.is").CanApplyAutomatically, "New rules default to review, not automatic permission.");
        Check(new OcrLexiconIndex(dictionary with { IgnoredTerms = ["日本語辤書"] }, []).Query("日本語辤書").Matches.Count == 0, "Ignored Japanese phrases take precedence over explicit rules.");
        Check(new OcrLexiconIndex(dictionary with { KnownTerms = ["Node.is"] }, []).Query("Node.is").Matches.Count == 0, "Verified source terms are protected.");
        Check(new OcrLexiconIndex(new() { KnownTerms = ["Node.js"], Rules = [Rule("my_Node.js", "an_Node.js", true)] }, []).Query("my_Node.js").CanApplyAutomatically,
            "A verified Latin term does not accidentally protect a substring inside a different identifier.");
        Check(new OcrLexiconIndex(dictionary with { IgnoredTerms = ["日本語Node.is"] }, []).Query("日本語Node.is").Matches.Count == 0,
            "An ignored registered Japanese phrase protects its whole occurrence from embedded rules and fuzzy suggestions.");
        return Task.CompletedTask;
    }

    internal static Task AmbiguityAsync()
    {
        Check(!new OcrLexiconIndex(new() { Rules = [Rule("Alpha", "Bravo", true), Rule("Alpha", "Delta", true)] }, []).Query("Alpha").CanApplyAutomatically, "Competing rules cannot auto-apply.");
        Check(!new OcrLexiconIndex(new() { Rules = [Rule("Alpha", "Bravo", true), Rule("Bravo", "Alpha", true)] }, []).Query("Alpha").CanApplyAutomatically, "Cyclic rules cannot auto-apply.");
        var chain = new OcrLexiconIndex(new() { Rules = [Rule("Alpha", "Bravo", true), Rule("Bravo", "Delta", true)] }, []).Query("Alpha");
        Check(chain.CanApplyAutomatically && chain.SuggestedText == "Bravo", "Rules are applied once to raw input, never cascaded.");
        var overlap = new OcrLexiconIndex(new() { Rules = [Rule("日本語", "日本文", true), Rule("日本語辞書", "日本文辞書", true)] }, []).Query("日本語辞書");
        Check(!overlap.CanApplyAutomatically && overlap.Matches.Count == 2, "Overlapping registered phrases require review.");
        return Task.CompletedTask;
    }

    internal static Task UnicodeAndLimitsAsync()
    {
        var unicode = new OcrLexiconIndex(new() { Rules = [Rule("辞書𠮷", "辞書か\u3099", true)] }, []).Query("辞書𠮷");
        Check(unicode.CanApplyAutomatically && OcrLexiconPolicy.ElementCount(unicode.RawText) == OcrLexiconPolicy.ElementCount(unicode.SuggestedText), "Unicode text elements, not UTF-16 length, govern equal-count rules.");
        MustFail<InvalidDataException>(() => OcrLexiconPolicy.Validate(new() { KnownTerms = ["a"] }));
        MustFail<InvalidDataException>(() => OcrLexiconPolicy.Validate(new() { KnownTerms = ["two words"] }));
        MustFail<InvalidDataException>(() => OcrLexiconPolicy.Validate(new() { Rules = [Rule("日本語", "日\n本語")] }));
        var rule = Rule("Alpha", "Bravo");
        MustFail<InvalidDataException>(() => OcrLexiconPolicy.Validate(new() { Rules = [rule, rule with { Target = "Delta" }] }));
        MustFail<InvalidDataException>(() => OcrLexiconPolicy.Validate(new() { KnownTerms = Enumerable.Range(0, 2001).Select(i => "term" + i).ToArray() }));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        MustFail<OperationCanceledException>(() => new OcrLexiconIndex(new(), [], token: cancellation.Token));
        return Task.CompletedTask;
    }

    internal static Task IncrementalAsync()
    {
        var id = Guid.NewGuid(); var counts = new OcrWordAccumulator();
        counts.Update(1, id, "Alpha Alpha"); counts.Update(1, id, "Bravo"); counts.Update(1, id, "Charlie Charlie Charlie");
        Check(counts.Snapshot().SequenceEqual(new[] { new OcrWordCount("Charlie", 3) }), "A→B→C subtracts the actually counted contribution.");
        counts.Update(1, id, "Alpha Alpha"); Check(counts.Snapshot().Single() == new OcrWordCount("Alpha", 2), "Undo restores counts exactly.");
        counts.Update(1, id, null); Check(counts.Snapshot().Count == 0, "Deletion removes a contribution.");
        return Task.CompletedTask;
    }

    internal static async Task PersistenceAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PdfCorrectorium-Lexicon-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var pdf = Path.Combine(directory, "source.pdf"); await File.WriteAllTextAsync(pdf, "%PDF-1.4\n%%EOF");
            var service = new ProjectPackageService(); var pageId = Guid.NewGuid();
            var project = new PdfCorrectoriumProject { SourcePdf = (await service.CreateSourceReferenceAsync(pdf, directory)) with { PageCount = 1 },
                PageSequence = [new() { PageId = pageId, SourcePageNumber = 1 }],
                OcrLexicon = new() { KnownTerms = ["Node.js", "日本語辞書"], IgnoredTerms = ["KnownError"], Rules = [Rule("Node.is", "Node.js", true)] } };
            project = project with { OcrWordCountsCache = new() { Stamp = OcrLexiconPolicy.GetCacheStamp(project), Words = [new("Node.js", 20)] } };
            var file = Path.Combine(directory, "lexicon.pdfocrproj"); await service.SaveAsync(file, project);
            var opened = await service.OpenAsync(file);
            Check(opened.OcrLexicon.Rules.Single().Id == project.OcrLexicon.Rules.Single().Id && opened.OcrLexicon.Rules.Single().AllowAutomatic && opened.OcrLexicon.KnownTerms.SequenceEqual(project.OcrLexicon.KnownTerms), "Authoritative knowledge and per-rule permission round-trip.");
            Check(opened.OcrWordCountsCache?.Words.Single().Count == 20, "A complete matching frequency cache is restored.");
            using (var archive = ZipFile.OpenRead(file))
            {
                Check(archive.GetEntry("cache/ocr-word-counts.json") is not null, "Frequency counts have a separate optional ZIP entry.");
                using var reader = new StreamReader(archive.GetEntry("project.json")!.Open());
                Check(!(await reader.ReadToEndAsync()).Contains("ocrWordCountsCache", StringComparison.Ordinal), "Disposable cache never duplicates authoritative JSON.");
            }
            await service.SaveAutoSaveAsync(Path.Combine(directory, "recovery.pdfocrproj"), project, false, new Dictionary<int, byte[]>());
            Check((await service.OpenAsync(Path.Combine(directory, "recovery.pdfocrproj"))).OcrLexicon.Rules.Count == 1, "Autosave retains the authoritative dictionary.");
            await service.SaveAsync(file, project with { OcrLexicon = project.OcrLexicon with { KnownTerms = ["OtherTerm"] } });
            Check((await service.OpenAsync(file)).OcrWordCountsCache is null, "A stale dictionary stamp omits the cache without blocking save.");
            await service.SaveAsync(file, project);
            using (var archive = ZipFile.Open(file, ZipArchiveMode.Update))
            { archive.GetEntry("cache/ocr-word-counts.json")!.Delete(); using var writer = new StreamWriter(archive.CreateEntry("cache/ocr-word-counts.json").Open()); writer.Write("{broken"); }
            Check((await service.OpenAsync(file)).OcrWordCountsCache is null, "A corrupt bounded optional cache is discarded.");
            using (var archive = ZipFile.Open(file, ZipArchiveMode.Update))
            {
                var entry = archive.GetEntry("project.json")!; JsonNode json; using (var reader = new StreamReader(entry.Open())) json = JsonNode.Parse(await reader.ReadToEndAsync())!;
                json["ocrLexicon"]!["knownTerms"] = new JsonArray("bad\nterm"); entry.Delete();
                using var writer = new StreamWriter(archive.CreateEntry("project.json").Open()); writer.Write(json.ToJsonString());
            }
            try { await service.OpenAsync(file); throw new InvalidOperationException("Corrupt authoritative data was accepted."); } catch (InvalidDataException) { }
            Check(ProjectManifest.IsSupportedVersion("1.11") && ProjectManifest.IsSupportedVersion("1.12") && ProjectManifest.CurrentVersion == "1.14", "Old projects remain readable while new projects protect authoritative knowledge and paragraph metadata from older savers.");
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static Task PerformanceAsync()
    {
        foreach (var pages in new[] { 10, 100, 450 })
        {
            var counts = new OcrWordAccumulator(); var watch = Stopwatch.StartNew();
            for (var page = 1; page <= pages; page++) for (var line = 0; line < 40; line++)
                counts.Update(page, Guid.NewGuid(), "Node.js JavaScript function document element " + page);
            var index = new OcrLexiconIndex(new() { KnownTerms = ["Node.js"] }, counts.Snapshot());
            var cold = watch.Elapsed.TotalMilliseconds; index.Query("Node.is JavaScript function"); watch.Restart();
            for (var i = 0; i < 100; i++) index.Query("Node.is JavaScript function");
            var warm = watch.Elapsed.TotalMilliseconds / 100;
            Console.WriteLine($"  Lexicon performance: {pages} pages × 40 lines, prepare {cold:F1} ms, warm line {warm:F3} ms");
            Check(warm < 50, "Warm line queries meet the provisional 50 ms target without OCR/rendering.");
        }
        var nearLimit = Enumerable.Range(0, 25000).Select(i => new OcrWordCount("term_" + i.ToString("D5"), 4)).ToArray();
        var capClock = Stopwatch.StartNew(); var capIndex = new OcrLexiconIndex(new(), nearLimit);
        var preparedMs = capClock.Elapsed.TotalMilliseconds; capClock.Restart();
        for (var i = 0; i < 100; i++) capIndex.Query("term_I2345");
        Console.WriteLine($"  Lexicon cap: 25,000 distinct terms, prepare {preparedMs:F1} ms, warm line {capClock.Elapsed.TotalMilliseconds / 100:F3} ms");
        Check(capClock.Elapsed.TotalMilliseconds / 100 < 50, "Warm lookup stays bounded near the distinct-word cap.");
        var japanese = new OcrLexicon { KnownTerms = Enumerable.Range(0, 2000).Select(i => "登録用語" + i.ToString("D4")).ToArray() };
        capClock.Restart(); var phraseIndex = new OcrLexiconIndex(japanese, []); preparedMs = capClock.Elapsed.TotalMilliseconds; capClock.Restart();
        for (var i = 0; i < 100; i++) phraseIndex.Query("登録用語19O9");
        Console.WriteLine($"  Lexicon Japanese cap: 2,000 phrases, prepare {preparedMs:F1} ms, warm line {capClock.Elapsed.TotalMilliseconds / 100:F3} ms");
        Check(capClock.Elapsed.TotalMilliseconds / 100 < 50, "Registered Japanese phrase lookup uses a bounded prepared signature index.");
        return Task.CompletedTask;
    }
}
