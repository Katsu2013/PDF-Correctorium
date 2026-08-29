using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// PDFのアウトライン（しおり）と、アプリ独自のしおり交換ファイルを読み書きします。
/// </summary>
/// <remarks>
/// PDF内部のアウトライン操作にはqpdfを使用します。PDFへ適用する際は一時ファイルを生成し、
/// qpdfの処理が成功した場合だけ対象PDFを置き換えます。
/// </remarks>
public sealed class PdfBookmarkService
{
    private const string CurrentBookmarkFormat = "PdfCorrectoriumBookmarks";
    private const string LegacyBookmarkFormat = "PdfOcrEditorBookmarks";
    private sealed record BookmarkFile(string Format, int Version, IReadOnlyList<PdfBookmark> Bookmarks);

    /// <summary>階層型テキストを読み込む途中で、子要素を追加可能な形で保持します。</summary>
    private sealed class BookmarkBuilder(string title, int pageNumber)
    {
        public string Title { get; } = title;
        public int PageNumber { get; } = pageNumber;
        public bool IsExpanded { get; init; } = true;
        public List<BookmarkBuilder> Children { get; } = [];
        public PdfBookmark ToModel() => new()
        {
            Title = Title,
            PageNumber = PageNumber,
            IsExpanded = IsExpanded,
            Children = Children.Select(child => child.ToModel()).ToArray(),
        };
    }

    /// <summary>しおり交換ファイルとqpdf更新JSONに共通で使う整形設定です。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// PDFに保存されている階層付きしおりを読み取ります。
    /// </summary>
    /// <param name="pdfPath">読み取り対象PDF。</param>
    /// <param name="cancellationToken">処理の取り消しを通知するトークン。</param>
    /// <returns>ルート階層のしおり一覧。</returns>
    public async Task<IReadOnlyList<PdfBookmark>> ReadFromPdfAsync(
        string pdfPath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(pdfPath);
        string? stagedPath = null;
        try
        {
            // qpdf for Windows can fail before opening a valid PDF when the complete path is long.
            // Read through a short temporary copy; the source PDF remains untouched.
            var qpdfPath = fullPath;
            if (fullPath.Length >= 220)
            {
                var operationDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "PDF-Correctorium",
                    "bookmarks",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(operationDirectory);
                stagedPath = Path.Combine(operationDirectory, "input.pdf");
                File.Copy(fullPath, stagedPath, overwrite: true);
                qpdfPath = stagedPath;
            }

            var json = await RunQpdfForTextAsync(
                ["--json=2", "--json-key=outlines", qpdfPath, "-"],
                cancellationToken);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("outlines", out var outlines) ||
                outlines.ValueKind != JsonValueKind.Array)
                return [];
            return ReadOutlineArray(outlines);
        }
        finally
        {
            if (stagedPath is not null)
            {
                var operationDirectory = Path.GetDirectoryName(stagedPath)!;
                TryDeleteFile(stagedPath);
                try
                {
                    if (Directory.Exists(operationDirectory)) Directory.Delete(operationDirectory);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Best-effort cleanup; a scanner may transiently retain the temporary file.
                }
            }
        }
    }

    /// <summary>
    /// しおりをPDF CorrectoriumのJSON交換形式へ書き出します。
    /// </summary>
    /// <param name="path">保存先ファイル。</param>
    /// <param name="bookmarks">保存する階層付きしおり。</param>
    /// <param name="cancellationToken">処理の取り消しを通知するトークン。</param>
    public async Task ExportAsync(
        string path,
        IReadOnlyList<PdfBookmark> bookmarks,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            await ExportTextAsync(path, bookmarks, cancellationToken);
            return;
        }
        if (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            await ExportXmlAsync(path, bookmarks, cancellationToken);
            return;
        }

        var file = new BookmarkFile(CurrentBookmarkFormat, 1, bookmarks);
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await JsonSerializer.SerializeAsync(stream, file, JsonOptions, cancellationToken);
    }

    /// <summary>
    /// JSON交換ファイルから階層付きしおりを読み込みます。
    /// </summary>
    /// <param name="path">読み込む交換ファイル。</param>
    /// <param name="cancellationToken">処理の取り消しを通知するトークン。</param>
    /// <returns>読み込んだルート階層のしおり一覧。</returns>
    /// <exception cref="InvalidDataException">形式名またはバージョンが未対応の場合。</exception>
    public async Task<IReadOnlyList<PdfBookmark>> ImportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            return await ImportTextAsync(path, cancellationToken);
        if (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            return await ImportXmlAsync(path, cancellationToken);

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
            return JsonSerializer.Deserialize<IReadOnlyList<PdfBookmark>>(document.RootElement.GetRawText(), JsonOptions) ?? [];

        var file = JsonSerializer.Deserialize<BookmarkFile>(document.RootElement.GetRawText(), JsonOptions)
                   ?? throw new InvalidDataException("しおりファイルが空です。");
        var supportedFormat =
            string.Equals(file.Format, CurrentBookmarkFormat, StringComparison.Ordinal) ||
            string.Equals(file.Format, LegacyBookmarkFormat, StringComparison.Ordinal);
        if (!supportedFormat || file.Version != 1)
            throw new InvalidDataException("対応していないしおりファイルです。");
        return file.Bookmarks;
    }

    /// <summary>
    /// pdf_asで使われる「先頭タブ＝階層、タイトル/ページ番号」のテキスト形式を書き出します。
    /// </summary>
    private static async Task ExportTextAsync(
        string path,
        IReadOnlyList<PdfBookmark> bookmarks,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        void Append(IEnumerable<PdfBookmark> items, int depth)
        {
            foreach (var bookmark in items)
            {
                builder.Append('\t', depth)
                    .Append(bookmark.Title.Replace("\r", " ").Replace("\n", " "))
                    .Append('/')
                    .Append(bookmark.PageNumber)
                    .AppendLine();
                Append(bookmark.Children, depth + 1);
            }
        }
        Append(bookmarks, 0);
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
    }

    /// <summary>pdf_as形式の階層付きテキストを読み込みます。</summary>
    private static async Task<IReadOnlyList<PdfBookmark>> ImportTextAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        var roots = new List<BookmarkBuilder>();
        var latestAtDepth = new List<BookmarkBuilder>();
        foreach (var rawLine in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(rawLine)) continue;
            var depth = rawLine.TakeWhile(character => character == '\t').Count();
            var value = rawLine[depth..].Trim();
            var separator = value.LastIndexOf('/');
            if (separator <= 0 || !int.TryParse(value[(separator + 1)..].Trim(), out var pageNumber))
                throw new InvalidDataException($"しおり行「{value}」の末尾に /ページ番号 がありません。");

            var node = new BookmarkBuilder(value[..separator].Trim(), Math.Max(1, pageNumber));
            depth = Math.Min(depth, latestAtDepth.Count);
            if (depth == 0) roots.Add(node);
            else latestAtDepth[depth - 1].Children.Add(node);
            if (latestAtDepth.Count > depth) latestAtDepth.RemoveRange(depth, latestAtDepth.Count - depth);
            latestAtDepth.Add(node);
        }
        return roots.Select(root => root.ToModel()).ToArray();
    }

    /// <summary>
    /// pdf_as等が扱うbookmark-tree XMLを書き出します。ページ番号は
    /// <c>destination-page-number</c>属性へ1始まりで保存します。
    /// </summary>
    private static async Task ExportXmlAsync(
        string path,
        IReadOnlyList<PdfBookmark> bookmarks,
        CancellationToken cancellationToken)
    {
        XElement CreateElement(PdfBookmark bookmark) =>
            new("bookmark",
                new XAttribute("expand", bookmark.IsExpanded ? "true" : "false"),
                new XAttribute("action-type", "gotor"),
                new XAttribute("destination-type", "fit"),
                new XAttribute("destination-page-number", bookmark.PageNumber),
                new XElement("bookmark-title", new XAttribute("color", "#000000"), bookmark.Title),
                bookmark.Children.Select(CreateElement));

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("bookmark-tree", bookmarks.Select(CreateElement)));
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await document.SaveAsync(stream, SaveOptions.None, cancellationToken);
    }

    /// <summary>
    /// bookmark-tree形式に加え、Bookmark／Outline／Itemを入れ子にする一般的なXMLも読み込みます。
    /// </summary>
    private static async Task<IReadOnlyList<PdfBookmark>> ImportXmlAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        var root = document.Root ?? throw new InvalidDataException("しおりXMLにルート要素がありません。");
        var rootElements = IsBookmarkElement(root)
            ? [root]
            : BookmarkChildren(root).ToArray();
        if (rootElements.Length == 0)
            throw new InvalidDataException("しおりXMLにbookmark、outline、またはitem要素がありません。");
        return rootElements.Select(ParseXmlBookmark).ToArray();
    }

    private static PdfBookmark ParseXmlBookmark(XElement element)
    {
        var title = AttributeValue(element, "title", "name", "text")
                    ?? element.Elements().FirstOrDefault(child =>
                        NameEquals(child, "bookmark-title") || NameEquals(child, "title"))?.Value;
        var pageText = AttributeValue(
            element,
            "destination-page-number",
            "page-number",
            "pagenumber",
            "page");
        _ = int.TryParse(pageText, out var pageNumber);
        var expandedText = AttributeValue(element, "expand", "expanded", "open");
        var expanded = !bool.TryParse(expandedText, out var parsedExpanded) || parsedExpanded;
        return new PdfBookmark
        {
            Title = string.IsNullOrWhiteSpace(title) ? "無題のしおり" : title.Trim(),
            PageNumber = Math.Max(1, pageNumber),
            IsExpanded = expanded,
            Children = BookmarkChildren(element).Select(ParseXmlBookmark).ToArray(),
        };
    }

    private static IEnumerable<XElement> BookmarkChildren(XElement parent) =>
        parent.Elements().Where(IsBookmarkElement)
            .Concat(parent.Elements()
                .Where(element => NameEquals(element, "children") || NameEquals(element, "bookmarks"))
                .SelectMany(container => container.Elements().Where(IsBookmarkElement)));

    private static bool IsBookmarkElement(XElement element) =>
        NameEquals(element, "bookmark") || NameEquals(element, "outline") || NameEquals(element, "item");

    private static bool NameEquals(XElement element, string name) =>
        element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static string? AttributeValue(XElement element, params string[] names) =>
        element.Attributes().FirstOrDefault(attribute =>
            names.Any(name => attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)))?.Value;

    /// <summary>
    /// 指定したしおり階層でPDFのアウトラインを置き換えます。
    /// </summary>
    /// <param name="pdfPath">更新するPDFファイル。</param>
    /// <param name="bookmarks">適用するしおり。空の場合は既存アウトラインを削除します。</param>
    /// <param name="cancellationToken">処理の取り消しを通知するトークン。</param>
    public async Task ApplyToPdfAsync(
        string pdfPath,
        IReadOnlyList<PdfBookmark> bookmarks,
        CancellationToken cancellationToken = default)
    {
        await ApplyToPdfAsync(pdfPath, bookmarks, null, true, cancellationToken);
    }

    /// <summary>
    /// PDFのアウトラインと、文書を開いたときのページレイアウトを一度のqpdf更新で適用します。
    /// </summary>
    /// <param name="pdfPath">更新するPDFファイル。</param>
    /// <param name="bookmarks">適用するしおり。</param>
    /// <param name="viewerSettings">PDFカタログへ反映する初期表示設定。変更しない場合は<see langword="null"/>。</param>
    /// <param name="replaceBookmarks">既存アウトラインを引数のしおりで置き換えるかどうか。</param>
    /// <param name="cancellationToken">処理の取り消しを通知するトークン。</param>
    public async Task ApplyToPdfAsync(
        string pdfPath,
        IReadOnlyList<PdfBookmark> bookmarks,
        ViewerSettings? viewerSettings,
        bool replaceBookmarks,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(pdfPath);
        var operationDirectory = Path.Combine(
            Path.GetTempPath(),
            "PDF-Correctorium",
            "bookmarks",
            Guid.NewGuid().ToString("N"));
        var stagedInputPath = Path.Combine(operationDirectory, "input.pdf");
        var updatePath = Path.Combine(operationDirectory, "update.json");
        var outputPath = Path.Combine(operationDirectory, "output.pdf");
        Directory.CreateDirectory(operationDirectory);
        try
        {
            // qpdf on Windows may reject paths close to MAX_PATH. The generated bookmark JSON used
            // to repeat the complete PDF file name and could therefore exceed the limit. Only the
            // source PDF is staged when necessary; qpdf's generated files always use short names.
            var qpdfInputPath = fullPath;
            if (fullPath.Length >= 220)
            {
                File.Copy(fullPath, stagedInputPath, true);
                qpdfInputPath = stagedInputPath;
            }

            var trailerJson = JsonNode.Parse(await RunQpdfForTextAsync(
                ["--json-output=2", "--json-object=trailer", qpdfInputPath, "-"],
                cancellationToken))!;
            var qpdf = trailerJson["qpdf"]!.AsArray();
            var header = qpdf[0]!.AsObject();
            var maxObjectId = header["maxobjectid"]!.GetValue<int>();
            var trailerObjects = qpdf[1]!.AsObject();
            var rootReference = trailerObjects["trailer"]!["value"]!["/Root"]!.GetValue<string>();
            var (rootObjectNumber, rootGeneration) = ParseReference(rootReference);

            var catalogJson = JsonNode.Parse(await RunQpdfForTextAsync(
                ["--json-output=2", $"--json-object={rootObjectNumber},{rootGeneration}", qpdfInputPath, "-"],
                cancellationToken))!;
            var catalogValue = catalogJson["qpdf"]![1]![$"obj:{rootReference}"]!["value"]!.DeepClone().AsObject();

            var pageJson = JsonNode.Parse(await RunQpdfForTextAsync(
                ["--json=2", "--json-key=pages", qpdfInputPath, "-"],
                cancellationToken))!;
            var pageReferences = pageJson["pages"]!.AsArray()
                .ToDictionary(
                    page => page!["pageposfrom1"]!.GetValue<int>(),
                    page => page!["object"]!.GetValue<string>());

            var updateObjects = new JsonObject();
            var nextObjectId = maxObjectId + 1;
            if (replaceBookmarks)
            {
                if (bookmarks.Count == 0)
                {
                    catalogValue.Remove("/Outlines");
                }
                else
                {
                    var outlineRootReference = $"{nextObjectId++} 0 R";
                    catalogValue["/Outlines"] = outlineRootReference;
                    var nodeReferences = AssignReferences(bookmarks, nextObjectId);
                    updateObjects[$"obj:{outlineRootReference}"] = new JsonObject
                    {
                        ["value"] = new JsonObject
                        {
                            ["/Type"] = "/Outlines",
                            ["/Count"] = CountDescendants(bookmarks),
                            ["/First"] = nodeReferences[bookmarks[0].Id],
                            ["/Last"] = nodeReferences[bookmarks[^1].Id],
                        },
                    };
                    WriteBookmarkObjects(bookmarks, outlineRootReference, nodeReferences, pageReferences, updateObjects);
                }
            }

            if (viewerSettings is not null)
                ApplyViewerSettings(catalogValue, viewerSettings);

            updateObjects[$"obj:{rootReference}"] = new JsonObject { ["value"] = catalogValue };
            var update = new JsonObject
            {
                ["qpdf"] = new JsonArray(
                    new JsonObject { ["jsonversion"] = 2 },
                    updateObjects),
            };

            await File.WriteAllTextAsync(updatePath, update.ToJsonString(JsonOptions), new UTF8Encoding(false), cancellationToken);
            await RunQpdfAsync(
                [qpdfInputPath, $"--update-from-json={updatePath}", outputPath],
                cancellationToken);
            if (!File.Exists(outputPath))
                throw new InvalidDataException("qpdfがしおり更新後のPDFを生成しませんでした。");
            File.Copy(outputPath, fullPath, true);
        }
        finally
        {
            TryDeleteFile(stagedInputPath);
            TryDeleteFile(updatePath);
            TryDeleteFile(outputPath);
            try
            {
                if (Directory.Exists(operationDirectory)) Directory.Delete(operationDirectory);
            }
            catch (IOException)
            {
                // Temporary files are already removed. A transient directory handle must not hide
                // a successful bookmark update or the original qpdf error.
            }
            catch (UnauthorizedAccessException)
            {
                // Antivirus scanners can briefly retain the empty directory; the OS may clean it later.
            }
        }
    }

    /// <summary>アプリの表示設定をPDF CatalogのPageLayoutとViewerPreferencesへ変換します。</summary>
    private static void ApplyViewerSettings(JsonObject catalog, ViewerSettings settings)
    {
        catalog["/PageLayout"] = PdfViewerSettingsMapping.GetPageLayoutName(settings);

        // 既存値が間接参照の場合でも、編集対象のDirectionだけを確実に表現できる直接辞書へ置換します。
        // ViewerPreferences内の未知の設定を保存する機能は、カタログ参照解決を追加する後続実装で扱います。
        catalog["/ViewerPreferences"] = new JsonObject
        {
            ["/Direction"] = PdfViewerSettingsMapping.GetDirectionName(settings),
        };
    }

    /// <summary>一時ファイルの後始末失敗によって、本来の処理結果を上書きしないよう削除を試みます。</summary>
    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // A scanner may still hold the temporary file. It is safe to leave it in the temp folder.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup is best-effort and must not convert a successful export into an error.
        }
    }

    /// <summary>
    /// 環境変数、配布フォルダー、開発時の配置先の順にqpdf実行ファイルを探索します。
    /// </summary>
    /// <returns>見つかったqpdfの絶対パス。利用できない場合は<see langword="null"/>。</returns>
    public static string? ResolveQpdfPath()
    {
        var configured = Environment.GetEnvironmentVariable("PDFOCR_QPDF_PATH");
        var candidates = new[]
        {
            configured,
            Path.Combine(AppContext.BaseDirectory, "tools", "qpdf", "bin", "qpdf.exe"),
            Path.Combine(AppContext.BaseDirectory, "qpdf", "bin", "qpdf.exe"),
            Path.Combine(AppContext.BaseDirectory, "qpdf.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "qpdf", "bin", "qpdf.exe")),
        };
        foreach (var candidate in candidates)
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return Path.GetFullPath(candidate);
        return null;
    }

    private static IReadOnlyList<PdfBookmark> ReadOutlineArray(JsonElement outlines)
    {
        var result = new List<PdfBookmark>();
        foreach (var outline in outlines.EnumerateArray())
        {
            var title = outline.TryGetProperty("title", out var titleValue) ? titleValue.GetString() : null;
            var page = outline.TryGetProperty("destpageposfrom1", out var pageValue) &&
                       pageValue.ValueKind == JsonValueKind.Number
                ? pageValue.GetInt32()
                : 1;
            var children = outline.TryGetProperty("kids", out var kids) && kids.ValueKind == JsonValueKind.Array
                ? ReadOutlineArray(kids)
                : [];
            result.Add(new PdfBookmark
            {
                Title = string.IsNullOrWhiteSpace(title) ? "無題のしおり" : title,
                PageNumber = Math.Max(1, page),
                IsExpanded = !outline.TryGetProperty("open", out var open) || open.GetBoolean(),
                Children = children,
            });
        }
        return result;
    }

    private static Dictionary<Guid, string> AssignReferences(
        IReadOnlyList<PdfBookmark> bookmarks,
        int nextObjectId)
    {
        var references = new Dictionary<Guid, string>();
        void Visit(IEnumerable<PdfBookmark> items)
        {
            foreach (var item in items)
            {
                references[item.Id] = $"{nextObjectId++} 0 R";
                Visit(item.Children);
            }
        }
        Visit(bookmarks);
        return references;
    }

    private static void WriteBookmarkObjects(
        IReadOnlyList<PdfBookmark> bookmarks,
        string parentReference,
        IReadOnlyDictionary<Guid, string> references,
        IReadOnlyDictionary<int, string> pageReferences,
        JsonObject objects)
    {
        for (var index = 0; index < bookmarks.Count; index++)
        {
            var bookmark = bookmarks[index];
            var value = new JsonObject
            {
                ["/Title"] = "u:" + bookmark.Title,
                ["/Parent"] = parentReference,
            };
            var pageNumber = Math.Clamp(bookmark.PageNumber, 1, pageReferences.Count);
            if (pageReferences.TryGetValue(pageNumber, out var pageReference))
                value["/Dest"] = new JsonArray(pageReference, "/Fit");
            if (index > 0) value["/Prev"] = references[bookmarks[index - 1].Id];
            if (index + 1 < bookmarks.Count) value["/Next"] = references[bookmarks[index + 1].Id];
            if (bookmark.Children.Count > 0)
            {
                var descendants = CountDescendants(bookmark.Children);
                value["/Count"] = bookmark.IsExpanded ? descendants : -descendants;
                value["/First"] = references[bookmark.Children[0].Id];
                value["/Last"] = references[bookmark.Children[^1].Id];
            }

            var reference = references[bookmark.Id];
            objects[$"obj:{reference}"] = new JsonObject { ["value"] = value };
            WriteBookmarkObjects(bookmark.Children, reference, references, pageReferences, objects);
        }
    }

    private static int CountDescendants(IEnumerable<PdfBookmark> bookmarks) =>
        bookmarks.Sum(bookmark => 1 + CountDescendants(bookmark.Children));

    private static (int ObjectNumber, int Generation) ParseReference(string reference)
    {
        var parts = reference.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out var objectNumber) ||
            !int.TryParse(parts[1], out var generation) ||
            parts[2] != "R")
            throw new InvalidDataException($"不正なPDFオブジェクト参照です: {reference}");
        return (objectNumber, generation);
    }

    private static async Task<string> RunQpdfForTextAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var (output, _) = await RunQpdfAsync(arguments, cancellationToken);
        return output;
    }

    private static async Task<(string Output, string Error)> RunQpdfAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var qpdfPath = ResolveQpdfPath() ??
                       throw new FileNotFoundException("しおり処理に必要なqpdf.exeが見つかりません。");
        var startInfo = new ProcessStartInfo
        {
            FileName = qpdfPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("qpdfを起動できませんでした。");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidDataException(
                $"qpdfによるしおり処理に失敗しました（終了コード: {process.ExitCode}）。\n" +
                string.Join("\n", new[] { error, output }.Where(value => !string.IsNullOrWhiteSpace(value))));
        return (output, error);
    }
}
