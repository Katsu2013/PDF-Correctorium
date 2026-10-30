using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;

namespace PdfCorrectorium.App.Services;

public sealed record PdfReadingContent(IReadOnlyList<int> Mcids, string? AlternativeText);
public sealed record PdfReadingPage(int PageNumber, IReadOnlyList<PdfReadingContent> Contents, IReadOnlyList<int> NewMcids);
public sealed record PdfReadingPlan(IReadOnlyList<PdfReadingPage> Pages)
{
    public int ReadingCount => Pages.Sum(page => page.Contents.Count(content => content.AlternativeText is not null));
}

public sealed partial class PdfExportService
{
    private static bool HasPdfReadings(PdfCorrectoriumProject project)
    {
        var matcher = new WordReadingMatcher(project.WordReadings);
        return project.Pages.Any(page => page.TextRegions.Any(region =>
            CanExportReading(project, page, region) && matcher.FindMatches(region.EffectiveText, region.WordReadings).Count > 0));
    }

    private static bool CanExportReading(PdfCorrectoriumProject project, OcrPage page, OcrTextRegion region) =>
        !region.IsDeleted && region.Output.IncludeInPdf && region.Output.IncludeInSpeech &&
        region.EditedGeometry.LocalBounds.IsValid &&
        !project.Redactions.Any(redaction => redaction.PageId == page.Id &&
            (redaction.SourceRegionId == region.Id || IntersectsReadingBounds(redaction.Bounds, region.EditedGeometry.LocalBounds) ||
             IntersectsReadingBounds(redaction.Bounds, region.OriginalGeometry.LocalBounds)));

    private static bool IntersectsReadingBounds(PdfRectangle a, PdfRectangle b) =>
        !a.IsValid || !b.IsValid || a.Left < b.Right && b.Left < a.Right && a.Bottom < b.Top && b.Bottom < a.Top;

    private static string ApplyReadingMatches(string text, IReadOnlyList<WordReadingMatch> matches)
    {
        var result = new StringBuilder(); var start = 0;
        foreach (var match in matches)
        { result.Append(text.AsSpan(start, match.Start - start)); result.Append(match.ReadingText); start = match.Start + match.Length; }
        result.Append(text.AsSpan(start)); return result.ToString();
    }

    private static string WithoutSpacing(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c)));

    internal static bool ReadingTextMatches(string pdfText, string projectText)
    {
        var actual = WithoutSpacing(pdfText);
        var expected = WithoutSpacing(projectText);
        if (actual.Length != expected.Length) return false;
        for (var index = 0; index < actual.Length; index++)
        {
            // PDFium represents a detected line-ending hyphen (U+002D or
            // U+00AD) as U+0002 in FPDFTextObj_GetText. This is extraction
            // metadata, not a changed glyph. Keep every other character exact
            // and never remove the marker or modify the original PDF text.
            if (actual[index] != expected[index] &&
                !(actual[index] == '\u0002' && expected[index] is '-' or '\u00ad')) return false;
        }
        return true;
    }

    // Runs in the existing calibration document, after redactions and before its
    // single save. No extra PDF rewrite is introduced just to add content marks.
    private static PdfReadingPlan CollectPdfReadings(IntPtr document, PdfCorrectoriumProject project,
        IProgress<PdfExportProgress>? progress, CancellationToken cancellationToken, out bool changed)
    {
        changed = false; var pages = new List<PdfReadingPage>();
        var matcher = new WordReadingMatcher(project.WordReadings);
        var projectPages = project.Pages.ToDictionary(page => page.PageNumber);
        var count = NativeMethods.FPDF_GetPageCount(document);
        var spanName = Marshal.StringToCoTaskMemUTF8("Span"); var mcidName = Marshal.StringToCoTaskMemUTF8("MCID");
        try
        {
            for (var number = 1; number <= count; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = NativeMethods.FPDF_LoadPage(document, number - 1);
                if (page == IntPtr.Zero) throw CreatePdfException("読み方を関連付けるページを開けませんでした");
                var textPage = IntPtr.Zero;
                try
                {
                    textPage = NativeMethods.FPDFText_LoadPage(page);
                    if (textPage == IntPtr.Zero) throw CreatePdfException("読み方の対象文字を読み取れませんでした");
                    var candidates = CollectTextObjects(page, textPage);
                    var ids = new Dictionary<IntPtr, int>(); var maximum = -1;
                    foreach (var item in candidates)
                    {
                        var id = -1;
                        for (uint index = 0; index < NativeMethods.FPDFPageObj_CountMarks(item.Object); index++)
                            if (ReadingNative.FPDFPageObjMark_GetParamIntValue(NativeMethods.FPDFPageObj_GetMark(item.Object, index), mcidName, out var existing) != 0)
                                id = Math.Max(id, existing);
                        ids[item.Object] = id; maximum = Math.Max(maximum, id);
                    }
                    for (var index = 0; index < NativeMethods.FPDFPage_CountObjects(page); index++)
                    {
                        var item = NativeMethods.FPDFPage_GetObject(page, index);
                        for (uint markIndex = 0; markIndex < NativeMethods.FPDFPageObj_CountMarks(item); markIndex++)
                            if (ReadingNative.FPDFPageObjMark_GetParamIntValue(NativeMethods.FPDFPageObj_GetMark(item, markIndex), mcidName, out var existing) != 0)
                                maximum = Math.Max(maximum, existing);
                    }
                    var used = new HashSet<IntPtr>(); var contents = new List<PdfReadingContent>(); var pageChanged = false; var newIds = new List<int>();
                    if (projectPages.TryGetValue(number, out var projectPage))
                    {
                        var order = projectPage.ReadingOrder.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
                        foreach (var region in projectPage.TextRegions.Where(region => !region.IsDeleted && region.Output.IncludeInPdf)
                            .OrderBy(region => order.GetValueOrDefault(region.Id, int.MaxValue)))
                        {
                            var matches = CanExportReading(project, projectPage, region)
                                ? matcher.FindMatches(region.EffectiveText, region.WordReadings) : [];
                            var selected = new List<TextObjectCandidate>();
                            var exact = FindCandidate(candidates, used, region);
                            if (exact is not null && ReadingTextMatches(exact.Text, region.EffectiveText)) selected.Add(exact);
                            else
                            {
                                var bounds = region.EditedGeometry.LocalBounds;
                                selected.AddRange(candidates.Where(item => !used.Contains(item.Object) &&
                                    item.Bounds.Left + item.Bounds.Size.Width / 2 >= bounds.Left - .5 &&
                                    item.Bounds.Right - item.Bounds.Size.Width / 2 <= bounds.Right + .5 &&
                                    item.Bounds.Bottom + item.Bounds.Size.Height / 2 >= bounds.Bottom - .5 &&
                                    item.Bounds.Top - item.Bounds.Size.Height / 2 <= bounds.Top + .5));
                                if (!ReadingTextMatches(string.Concat(selected.Select(item => item.Text)), region.EffectiveText)) selected.Clear();
                            }
                            if (selected.Count == 0)
                            {
                                if (matches.Count > 0) throw new InvalidDataException($"{number}ページの「{Abbreviate(region.EffectiveText)}」に読み方を安全に関連付けられません。文字枠と本文を確認してください。");
                                continue;
                            }
                            foreach (var item in selected) used.Add(item.Object);
                            var mapped = selected.Select(item => Assign(item.Object)).Distinct().ToArray();
                            if (matches.Count > 0 && candidates.Any(item => !selected.Any(selectedItem => selectedItem.Object == item.Object) && mapped.Contains(ids[item.Object])))
                                throw new InvalidDataException($"{number}ページ: 読み方の対象タグが別の文字も含むため、安全に設定できません。");
                            contents.Add(new(mapped, matches.Count == 0 ? null : ApplyReadingMatches(region.EffectiveText, matches)));
                        }
                    }
                    foreach (var item in candidates.Where(item => !used.Contains(item.Object))) contents.Add(new([Assign(item.Object)], null));
                    // Untagged Form XObjects can contain ordinary text too. Mark
                    // the invocation so adding a structure tree does not omit it
                    // from reading. Preserve existing nested tagged forms as-is.
                    if (project.Redactions.Count > 0 || ReadingNative.FPDFCatalog_IsTagged(document) == 0)
                        for (var index = 0; index < NativeMethods.FPDFPage_CountObjects(page); index++)
                        {
                            var item = NativeMethods.FPDFPage_GetObject(page, index);
                            if (item == IntPtr.Zero || NativeMethods.FPDFPageObj_GetType(item) != 5) continue;
                            var id = -1;
                            for (uint markIndex = 0; markIndex < NativeMethods.FPDFPageObj_CountMarks(item); markIndex++)
                                if (ReadingNative.FPDFPageObjMark_GetParamIntValue(NativeMethods.FPDFPageObj_GetMark(item, markIndex), mcidName, out var existing) != 0) id = Math.Max(id, existing);
                            ids[item] = id; contents.Add(new([Assign(item)], null));
                        }
                    if (pageChanged && NativeMethods.FPDFPage_GenerateContent(page) == 0)
                        throw new InvalidDataException($"{number}ページの読み方の関連付けを確定できませんでした。");
                    changed |= pageChanged; pages.Add(new(number, contents, newIds));
                    progress?.Report(new("readings", number, count, $"読み方をPDFの文字へ関連付けています（{number:N0}/{count:N0}ページ）..."));

                    int Assign(IntPtr item)
                    {
                        if (ids[item] >= 0) return ids[item];
                        var mark = NativeMethods.FPDFPageObj_AddMark(item, spanName); var id = checked(++maximum);
                        if (mark == IntPtr.Zero || ReadingNative.FPDFPageObjMark_SetIntParam(document, item, mark, mcidName, id) == 0)
                            throw new InvalidDataException("読み方の文字識別子をPDFへ保存できませんでした。");
                        newIds.Add(id); pageChanged = true; return ids[item] = id;
                    }
                }
                finally { if (textPage != IntPtr.Zero) NativeMethods.FPDFText_ClosePage(textPage); NativeMethods.FPDF_ClosePage(page); }
            }
        }
        finally { Marshal.FreeCoTaskMem(spanName); Marshal.FreeCoTaskMem(mcidName); }
        return new(pages);
    }

    internal static void PopulateReadingFixture(string path, IReadOnlyList<OcrPage> pages)
    {
        lock (PdfiumSynchronization.Gate)
        {
            EnsureInitialized(); var name = Marshal.StringToCoTaskMemUTF8(path); var document = IntPtr.Zero; var font = IntPtr.Zero;
            var temporary = path + ".fixture.pdf";
            try
            {
                document = NativeMethods.FPDF_LoadDocument(name, IntPtr.Zero);
                if (document == IntPtr.Zero) throw CreatePdfException("読み方テスト用PDFを開けませんでした");
                font = LoadDecorationFont(document, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "components", "fonts", "NotoSansJP.ttf")));
                foreach (var item in pages)
                {
                    var page = NativeMethods.FPDF_LoadPage(document, item.PageNumber - 1);
                    if (page == IntPtr.Zero) throw CreatePdfException("読み方テスト用ページを開けませんでした");
                    try
                    {
                        foreach (var region in item.TextRegions)
                        {
                            var chars = region.WritingMode == WritingMode.Vertical ? StringInfo.GetTextElementEnumerator(region.EffectiveText) : null;
                            var parts = new List<string>();
                            if (chars is null) parts.Add(region.EffectiveText); else while (chars.MoveNext()) parts.Add(chars.GetTextElement());
                            for (var index = 0; index < parts.Count; index++)
                            {
                                var textObject = NativeMethods.FPDFPageObj_CreateTextObj(document, font, 20);
                                var unicode = Marshal.StringToHGlobalUni(parts[index]);
                                try { if (NativeMethods.FPDFText_SetText(textObject, unicode) == 0) throw new InvalidDataException("Fixture text could not be encoded."); }
                                finally { Marshal.FreeHGlobal(unicode); }
                                if (NativeMethods.FPDFPageObj_GetBounds(textObject, out var l, out var b, out var r, out var t) == 0 || r <= l || t <= b) throw new InvalidDataException("Fixture bounds unavailable.");
                                var target = region.EditedGeometry.LocalBounds;
                                var height = target.Size.Height / parts.Count;
                                var left = target.Left; var bottom = parts.Count == 1 ? target.Bottom : target.Top - height * (index + 1);
                                var sx = target.Size.Width / (r - l); var sy = height / (t - b);
                                NativeMethods.FPDFPageObj_Transform(textObject, sx, 0, 0, sy, left - sx * l, bottom - sy * b);
                                NativeMethods.FPDFPage_InsertObject(page, textObject);
                            }
                        }
                        if (NativeMethods.FPDFPage_GenerateContent(page) == 0) throw new InvalidDataException("Fixture generation failed.");
                    }
                    finally { NativeMethods.FPDF_ClosePage(page); }
                }
                NativeMethods.FPDFFont_Close(font); font = IntPtr.Zero; SaveDocument(document, temporary);
            }
            finally { if (font != IntPtr.Zero) NativeMethods.FPDFFont_Close(font); if (document != IntPtr.Zero) NativeMethods.FPDF_CloseDocument(document); Marshal.FreeCoTaskMem(name); }
            File.Move(temporary, path, true);
        }
    }
    private static IReadOnlyList<string> ReadingAlternatives(IntPtr document, int number)
    {
        var page = NativeMethods.FPDF_LoadPage(document, number - 1);
        if (page == IntPtr.Zero) throw CreatePdfException("読み方の検証用ページを開けませんでした");
        var tree = IntPtr.Zero; var result = new List<string>(); var visited = new HashSet<IntPtr>();
        try
        {
            tree = ReadingNative.FPDF_StructTree_GetForPage(page);
            if (tree == IntPtr.Zero) return result;
            var count = ReadingNative.FPDF_StructTree_CountChildren(tree);
            for (var index = 0; index < count; index++) Visit(ReadingNative.FPDF_StructTree_GetChildAtIndex(tree, index), 0);
            return result;
        }
        finally { if (tree != IntPtr.Zero) ReadingNative.FPDF_StructTree_Close(tree); NativeMethods.FPDF_ClosePage(page); }
        void Visit(IntPtr element, int depth)
        {
            if (element == IntPtr.Zero) return;
            if (depth > 64 || visited.Count > 100000 || !visited.Add(element)) throw new InvalidDataException("読み方のPDFタグ構造が不正です。");
            var size = ReadingNative.FPDF_StructElement_GetAltText(element, IntPtr.Zero, 0);
            if (size > 1024 * 1024) throw new InvalidDataException("読み方のPDFタグ文字列が大きすぎます。");
            if (size > 2)
            {
                var buffer = Marshal.AllocHGlobal(checked((int)size));
                try
                {
                    if (ReadingNative.FPDF_StructElement_GetAltText(element, buffer, size) != size) throw new InvalidDataException("PDFから読み方を再読込できませんでした。");
                    result.Add(Marshal.PtrToStringUni(buffer, checked((int)size / 2 - 1))!);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            var children = ReadingNative.FPDF_StructElement_CountChildren(element);
            for (var index = 0; index < children; index++) Visit(ReadingNative.FPDF_StructElement_GetChildAtIndex(element, index), depth + 1);
        }
    }

    internal static IReadOnlyList<string> ReadReadingAlternativesForDiagnostics(string path, int number)
    {
        lock (PdfiumSynchronization.Gate)
        {
            EnsureInitialized(); var name = Marshal.StringToCoTaskMemUTF8(path); var document = IntPtr.Zero;
            try
            {
                document = NativeMethods.FPDF_LoadDocument(name, IntPtr.Zero);
                if (document == IntPtr.Zero) throw CreatePdfException("読み方の検証用PDFを開けませんでした");
                return ReadingAlternatives(document, number);
            }
            finally { if (document != IntPtr.Zero) NativeMethods.FPDF_CloseDocument(document); Marshal.FreeCoTaskMem(name); }
        }
    }

    private static void VerifyReadingOutput(string path, PdfReadingPlan plan)
    {
        var name = Marshal.StringToCoTaskMemUTF8(path); var document = IntPtr.Zero;
        try
        {
            document = NativeMethods.FPDF_LoadDocument(name, IntPtr.Zero);
            if (document == IntPtr.Zero) throw CreatePdfException("読み方の検証用PDFを開けませんでした");
            foreach (var page in plan.Pages)
            {
                var expected = page.Contents.Where(content => content.AlternativeText is not null)
                    .GroupBy(content => content.AlternativeText!, StringComparer.Ordinal).ToArray();
                if (expected.Length == 0) continue;
                var actual = ReadingAlternatives(document, page.PageNumber).GroupBy(value => value, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
                if (expected.Any(group => actual.GetValueOrDefault(group.Key) < group.Count()))
                    throw new InvalidDataException($"{page.PageNumber}ページの読み方情報を出力PDFから再読込できなかったため、保存を中止しました。");
            }
        }
        finally { if (document != IntPtr.Zero) NativeMethods.FPDF_CloseDocument(document); Marshal.FreeCoTaskMem(name); }
    }
    private static class ReadingNative
    {
        [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFCatalog_IsTagged(IntPtr document);
        [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDF_GetSecurityHandlerRevision(IntPtr document);
        [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDF_StructTree_GetForPage(IntPtr page);
        [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDF_StructTree_Close(IntPtr tree);
        [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDF_StructTree_CountChildren(IntPtr tree);
        [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDF_StructTree_GetChildAtIndex(IntPtr tree, int index);
        [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDF_StructElement_CountChildren(IntPtr element);
        [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDF_StructElement_GetChildAtIndex(IntPtr element, int index);
        [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] internal static extern uint FPDF_StructElement_GetAltText(IntPtr element, IntPtr buffer, uint length);
        [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFPageObjMark_GetParamIntValue(IntPtr mark, IntPtr key, out int value);
        [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFPageObjMark_SetIntParam(IntPtr document, IntPtr pageObject, IntPtr mark, IntPtr key, int value);
    }
}
