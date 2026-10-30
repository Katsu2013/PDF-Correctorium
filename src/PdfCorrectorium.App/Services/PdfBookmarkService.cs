using PdfCorrectorium.Core.IO;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Xml;
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
public sealed partial class PdfBookmarkService
{
    private const string CurrentBookmarkFormat = "PdfCorrectoriumBookmarks";
    private const string LegacyBookmarkFormat = "PdfOcrEditorBookmarks";
    private sealed record BookmarkFile(string Format, int Version, IReadOnlyList<PdfBookmark> Bookmarks);

    /// <summary>交換ファイルの最大バイト数です。</summary>
    public long MaximumImportBytes { get; init; } = 64L * 1024 * 1024;
    /// <summary>1回に取り込めるしおり総数です。</summary>
    public int MaximumBookmarkCount { get; init; } = 100_000;
    /// <summary>しおり階層の最大深度です。</summary>
    public int MaximumBookmarkDepth { get; init; } = 256;
    /// <summary>しおりタイトル1件の最大文字数です。</summary>
    public int MaximumTitleCharacters { get; init; } = 32_768;
    public int MaximumImportNodes { get; init; } = 1_000_000;
    public int MaximumImportStructureDepth { get; init; } = 512;
    public long MaximumTotalImportCharacters { get; init; } = 16L * 1024 * 1024;
    /// <summary>qpdf 1回の処理期限です。</summary>
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>qpdfの標準出力・標準エラーそれぞれの最大文字数です。</summary>
    public int MaximumQpdfOutputCharacters { get; init; } = 64 * 1024 * 1024;

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
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
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

    /// <summary>PDFカタログのLangから文書全体の読み上げ言語タグを読み取ります。</summary>
    /// <param name="pdfPath">読み取り対象PDF。</param>
    /// <param name="cancellationToken">処理の取り消しを通知するトークン。</param>
    /// <returns>BCP 47言語タグ。指定がない場合は空文字列。</returns>
    public async Task<string> ReadDocumentLanguageAsync(
        string pdfPath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(pdfPath);
        string? stagedPath = null;
        try
        {
            var qpdfInputPath = fullPath;
            if (fullPath.Length >= 220)
            {
                var operationDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "PDF-Correctorium",
                    "properties",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(operationDirectory);
                stagedPath = Path.Combine(operationDirectory, "input.pdf");
                File.Copy(fullPath, stagedPath, overwrite: true);
                qpdfInputPath = stagedPath;
            }

            var trailerJson = JsonNode.Parse(await RunQpdfForTextAsync(
                ["--json-output=2", "--json-object=trailer", qpdfInputPath, "-"],
                cancellationToken))!;
            var trailerObjects = trailerJson["qpdf"]![1]!.AsObject();
            var rootReference = trailerObjects["trailer"]!["value"]!["/Root"]!.GetValue<string>();
            var (rootObjectNumber, rootGeneration) = ParseReference(rootReference);
            var catalogJson = JsonNode.Parse(await RunQpdfForTextAsync(
                ["--json-output=2", $"--json-object={rootObjectNumber},{rootGeneration}", qpdfInputPath, "-"],
                cancellationToken))!;
            var catalogValue = catalogJson["qpdf"]![1]![$"obj:{rootReference}"]!["value"]!.AsObject();
            return catalogValue["/Lang"] is JsonValue languageNode &&
                   languageNode.TryGetValue<string>(out var encodedLanguage)
                ? DecodeQpdfString(encodedLanguage)
                : string.Empty;
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

    /// <summary>PDF Catalogから初期ページ配置と綴じ方向を読み取ります。</summary>
    public async Task<ViewerSettings> ReadViewerSettingsAsync(
        string pdfPath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(pdfPath);
        string? stagedPath = null;
        try
        {
            var qpdfInputPath = fullPath;
            if (fullPath.Length >= 220)
            {
                var operationDirectory = Path.Combine(
                    Path.GetTempPath(), "PDF-Correctorium", "viewer-settings", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(operationDirectory);
                stagedPath = Path.Combine(operationDirectory, "input.pdf");
                File.Copy(fullPath, stagedPath, overwrite: true);
                qpdfInputPath = stagedPath;
            }

            var trailerJson = JsonNode.Parse(await RunQpdfForTextAsync(
                ["--json-output=2", "--json-object=trailer", qpdfInputPath, "-"], cancellationToken))!;
            var trailerObjects = trailerJson["qpdf"]![1]!.AsObject();
            var rootReference = trailerObjects["trailer"]!["value"]!["/Root"]!.GetValue<string>();
            var (rootObjectNumber, rootGeneration) = ParseReference(rootReference);
            var catalogJson = JsonNode.Parse(await RunQpdfForTextAsync(
                ["--json-output=2", $"--json-object={rootObjectNumber},{rootGeneration}", qpdfInputPath, "-"],
                cancellationToken))!;
            var catalogValue = catalogJson["qpdf"]![1]![$"obj:{rootReference}"]!["value"]!.AsObject();
            var pageLayout = catalogValue["/PageLayout"] is JsonValue layoutNode &&
                             layoutNode.TryGetValue<string>(out var layoutName)
                ? layoutName
                : null;
            string? direction = null;
            if (catalogValue["/ViewerPreferences"] is JsonObject directPreferences)
            {
                direction = ReadName(directPreferences["/Direction"]);
            }
            else if (catalogValue["/ViewerPreferences"] is JsonValue referenceNode &&
                     referenceNode.TryGetValue<string>(out var preferencesReference))
            {
                var (objectNumber, generation) = ParseReference(preferencesReference);
                var preferencesJson = JsonNode.Parse(await RunQpdfForTextAsync(
                    ["--json-output=2", $"--json-object={objectNumber},{generation}", qpdfInputPath, "-"],
                    cancellationToken))!;
                var preferences = preferencesJson["qpdf"]![1]![$"obj:{preferencesReference}"]!["value"]!.AsObject();
                direction = ReadName(preferences["/Direction"]);
            }
            return PdfViewerSettingsMapping.FromCatalogNames(pageLayout, direction);
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

    private static string? ReadName(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var name) ? name : null;

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
    public Task<IReadOnlyList<PdfBookmark>> ImportAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => ImportCoreAsync(path, cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<PdfBookmark>> ImportCoreAsync(
        string path,
        CancellationToken cancellationToken)
    {
        EnsureImportFileWithinLimits(path);
        var extension = Path.GetExtension(path);
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            return await ImportTextAsync(path, cancellationToken);
        if (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            return await ImportXmlAsync(path, cancellationToken);

        await using var stream = File.OpenRead(path);
        if (stream.Length > MaximumImportBytes) throw new InvalidDataException("しおりファイルが大きすぎます。");
        await new JsonInputBudget
        {
            MaximumTokens = MaximumImportNodes, MaximumDepth = Math.Min(64, MaximumImportStructureDepth),
            MaximumObjects = MaximumBookmarkCount + 1, MaximumArrayItems = MaximumBookmarkCount,
            MaximumStringCharacters = Math.Max(32_768, MaximumTitleCharacters), MaximumTotalStringCharacters = MaximumTotalImportCharacters,
            StringLimits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["title"] = MaximumTitleCharacters },
        }.ValidateAsync(stream, cancellationToken);
        stream.Position = 0;
        int first;
        using (var peek = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true))
            do { first = peek.Read(); } while (first >= 0 && char.IsWhiteSpace((char)first));
        stream.Position = 0;
        if (first == '[')
        {
            var result = await JsonSerializer.DeserializeAsync<IReadOnlyList<PdfBookmark>>(stream, JsonOptions, cancellationToken) ?? [];
            ValidateBookmarks(result); return result;
        }
        var file = await JsonSerializer.DeserializeAsync<BookmarkFile>(stream, JsonOptions, cancellationToken)
                   ?? throw new InvalidDataException("しおりファイルが空です。");
        var supportedFormat =
            string.Equals(file.Format, CurrentBookmarkFormat, StringComparison.Ordinal) ||
            string.Equals(file.Format, LegacyBookmarkFormat, StringComparison.Ordinal);
        if (!supportedFormat || file.Version != 1)
            throw new InvalidDataException("対応していないしおりファイルです。");
        ValidateBookmarks(file.Bookmarks);
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
    private async Task<IReadOnlyList<PdfBookmark>> ImportTextAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(path);
        if (input.Length > MaximumImportBytes) throw new InvalidDataException("しおりファイルが大きすぎます。");
        using var textReader = new StreamReader(input, Encoding.UTF8, true);
        var lines = new List<string>();
        var line = new StringBuilder(); var totalCharacters = 0L; var lineCount = 0;
        var buffer = new char[4096]; int read;
        while ((read = await textReader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
        {
            totalCharacters += read;
            if (totalCharacters > MaximumTotalImportCharacters) throw new InvalidDataException("しおり文字総量が上限を超えています。");
            foreach (var c in buffer.AsSpan(0, read))
            {
                if (c == '\n')
                {
                    if (++lineCount > MaximumImportNodes) throw new InvalidDataException("しおり行数が上限を超えています。");
                    lines.Add(line.ToString().TrimEnd('\r')); line.Clear();
                    if (lines.Count > MaximumBookmarkCount) throw new InvalidDataException("しおり行数が上限を超えています。");
                }
                else
                {
                    if (line.Length >= MaximumTitleCharacters + MaximumBookmarkDepth + 32)
                        throw new InvalidDataException("しおり行が長すぎます。");
                    line.Append(c);
                }
            }
        }
        if (line.Length > 0) lines.Add(line.ToString().TrimEnd('\r'));
        var roots = new List<BookmarkBuilder>();
        var latestAtDepth = new List<BookmarkBuilder>();
        var count = 0;
        foreach (var rawLine in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(rawLine)) continue;
            var depth = rawLine.TakeWhile(character => character == '\t').Count();
            if (depth >= MaximumBookmarkDepth)
                throw new InvalidDataException($"しおりの階層が上限 {MaximumBookmarkDepth:N0} を超えています。");
            var value = rawLine[depth..].Trim();
            var separator = value.LastIndexOf('/');
            if (separator <= 0 || !int.TryParse(value[(separator + 1)..].Trim(), out var pageNumber))
                throw new InvalidDataException($"しおり行「{value}」の末尾に /ページ番号 がありません。");

            var node = new BookmarkBuilder(value[..separator].Trim(), Math.Max(1, pageNumber));
            if (node.Title.Length > MaximumTitleCharacters)
                throw new InvalidDataException($"しおりタイトルが上限 {MaximumTitleCharacters:N0} 文字を超えています。");
            depth = Math.Min(depth, latestAtDepth.Count);
            if (depth == 0) roots.Add(node);
            else latestAtDepth[depth - 1].Children.Add(node);
            if (latestAtDepth.Count > depth) latestAtDepth.RemoveRange(depth, latestAtDepth.Count - depth);
            latestAtDepth.Add(node);
            if (++count > MaximumBookmarkCount)
                throw new InvalidDataException($"しおり数が上限 {MaximumBookmarkCount:N0} 件を超えています。");
        }
        var result = roots.Select(root => root.ToModel()).ToArray();
        ValidateBookmarks(result);
        return result;
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
    private async Task<IReadOnlyList<PdfBookmark>> ImportXmlAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length > MaximumImportBytes) throw new InvalidDataException("しおりファイルが大きすぎます。");
        var settings = new XmlReaderSettings
        {
            Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = Math.Min(MaximumImportBytes, MaximumTotalImportCharacters),
            CloseInput = false,
        };
        using (var scan = XmlReader.Create(stream, settings))
        {
            var nodes = 0; var bookmarks = 0; var characters = 0L;
            var chunk = new char[4096];
            while (await scan.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++nodes > MaximumImportNodes || scan.Depth >= MaximumImportStructureDepth)
                    throw new InvalidDataException("しおりXMLの構造量が上限を超えています。");
                if (scan.NodeType == XmlNodeType.Element)
                {
                    if ((scan.LocalName.Equals("bookmark", StringComparison.OrdinalIgnoreCase) ||
                         scan.LocalName.Equals("outline", StringComparison.OrdinalIgnoreCase) ||
                         scan.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase)) && ++bookmarks > MaximumBookmarkCount)
                        throw new InvalidDataException("しおり数が上限を超えています。");
                    nodes = checked(nodes + scan.AttributeCount);
                    if (nodes > MaximumImportNodes) throw new InvalidDataException("しおりXMLの属性数が上限を超えています。");
                    while (scan.MoveToNextAttribute())
                        await CountValue();
                    scan.MoveToElement();
                }
                else if (scan.HasValue) await CountValue();
            }
            async Task CountValue()
            {
                var valueCharacters = 0; int count;
                while ((count = await scan.ReadValueChunkAsync(chunk, 0, chunk.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    valueCharacters += count; characters += count;
                    if (valueCharacters > MaximumTitleCharacters || characters > MaximumTotalImportCharacters)
                        throw new InvalidDataException("しおりXMLの文字量が上限を超えています。");
                }
            }
        }
        stream.Position = 0;
        using var reader = XmlReader.Create(stream, settings);
        var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        var root = document.Root ?? throw new InvalidDataException("しおりXMLにルート要素がありません。");
        var rootElements = IsBookmarkElement(root)
            ? [root]
            : BookmarkChildren(root).ToArray();
        if (rootElements.Length == 0)
            throw new InvalidDataException("しおりXMLにbookmark、outline、またはitem要素がありません。");
        var bookmarkElements = document.Descendants().Where(IsBookmarkElement).ToArray();
        if (bookmarkElements.Length > MaximumBookmarkCount)
            throw new InvalidDataException($"しおり数が上限 {MaximumBookmarkCount:N0} 件を超えています。");
        if (bookmarkElements.Any(element => element.Ancestors().Count(IsBookmarkElement) >= MaximumBookmarkDepth))
            throw new InvalidDataException($"しおりの階層が上限 {MaximumBookmarkDepth:N0} を超えています。");
        var result = rootElements.Select(ParseXmlBookmark).ToArray();
        ValidateBookmarks(result);
        return result;
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
        await ApplyToPdfAsync(
            pdfPath,
            bookmarks,
            null,
            null,
            PdfOutputVersion.Automatic,
            null,
            true,
            null,
            null,
            cancellationToken);
    }

    /// <summary>
    /// PDFのアウトライン、文書情報、および開いたときのページレイアウトを一度のqpdf更新で適用します。
    /// </summary>
    /// <param name="pdfPath">更新するPDFファイル。</param>
    /// <param name="bookmarks">適用するしおり。</param>
    /// <param name="viewerSettings">PDFカタログへ反映する初期表示設定。変更しない場合は<see langword="null"/>。</param>
    /// <param name="documentMetadata">PDF文書情報辞書へ反映する編集値。変更しない場合は<see langword="null"/>。</param>
    /// <param name="outputPdfVersion">出力PDFへ明示する仕様バージョン。</param>
    /// <param name="documentLanguage">PDFカタログのLangへ反映する言語タグ。変更しない場合はnull。</param>
    /// <param name="replaceBookmarks">既存アウトラインを引数のしおりで置き換えるかどうか。</param>
    /// <param name="cancellationToken">処理の取り消しを通知するトークン。</param>
    public async Task ApplyToPdfAsync(
        string pdfPath,
        IReadOnlyList<PdfBookmark> bookmarks,
        ViewerSettings? viewerSettings,
        PdfDocumentMetadata? documentMetadata,
        PdfOutputVersion outputPdfVersion,
        string? documentLanguage,
        bool replaceBookmarks,
        IReadOnlyList<PdfInternalLink>? internalLinks,
        IReadOnlyList<OcrPage>? projectPages,
        CancellationToken cancellationToken = default,
        IReadOnlyList<ProjectPageReference>? pageSequence = null,
        bool removeDocumentStructure = false, PdfReadingPlan? pronunciationPlan = null)
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
            var trailerValue = trailerObjects["trailer"]!["value"]!.DeepClone().AsObject();
            var rootReference = trailerValue["/Root"]!.GetValue<string>();
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
                    // AssignReferences receives the starting value by value. Advance the shared allocator too,
                    // otherwise a newly-created /Info dictionary can reuse the first bookmark object number.
                    nextObjectId += CountDescendants(bookmarks);
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

            if (documentLanguage is not null)
            {
                if (string.IsNullOrWhiteSpace(documentLanguage))
                    catalogValue.Remove("/Lang");
                else
                    catalogValue["/Lang"] = "u:" + documentLanguage.Trim();
            }

            if (documentMetadata is not null)
            {
                JsonObject infoValue;
                string infoReference;
                if (trailerValue["/Info"] is JsonValue infoReferenceNode &&
                    infoReferenceNode.TryGetValue<string>(out var existingInfoReference))
                {
                    infoReference = existingInfoReference;
                    var (infoObjectNumber, infoGeneration) = ParseReference(infoReference);
                    var infoJson = JsonNode.Parse(await RunQpdfForTextAsync(
                        ["--json-output=2", $"--json-object={infoObjectNumber},{infoGeneration}", qpdfInputPath, "-"],
                        cancellationToken))!;
                    infoValue = infoJson["qpdf"]![1]![$"obj:{infoReference}"]!["value"]!.DeepClone().AsObject();
                }
                else
                {
                    infoReference = $"{nextObjectId++} 0 R";
                    infoValue = new JsonObject();
                    trailerValue["/Info"] = infoReference;
                    updateObjects["trailer"] = new JsonObject { ["value"] = trailerValue };
                }

                SetDocumentMetadataValue(infoValue, "/Title", documentMetadata.Title);
                SetDocumentMetadataValue(infoValue, "/Author", documentMetadata.Author);
                SetDocumentMetadataValue(infoValue, "/Subject", documentMetadata.Subject);
                SetDocumentMetadataValue(infoValue, "/Keywords", documentMetadata.Keywords);
                SetDocumentMetadataValue(infoValue, "/Creator", documentMetadata.Creator);
                SetDocumentMetadataValue(infoValue, "/Producer", documentMetadata.Producer);
                updateObjects[$"obj:{infoReference}"] = new JsonObject { ["value"] = infoValue };
            }

            if (internalLinks is { Count: > 0 } &&
                (projectPages is { Count: > 0 } || pageSequence is { Count: > 0 }))
            {
                nextObjectId = await WriteInternalLinkObjectsAsync(
                    qpdfInputPath,
                    internalLinks,
                    projectPages ?? [],
                    pageReferences,
                    updateObjects,
                    nextObjectId,
                    cancellationToken,
                    pageSequence);
            }

            if (removeDocumentStructure)
            {
                catalogValue.Remove("/StructTreeRoot");
                catalogValue.Remove("/MarkInfo");
                catalogValue.Remove("/SpiderInfo");
            }
            if (pronunciationPlan is { ReadingCount: > 0 })
                nextObjectId = await WriteReadingStructureAsync(qpdfInputPath, pronunciationPlan, pageReferences,
                    catalogValue, updateObjects, nextObjectId, removeDocumentStructure, cancellationToken);
            updateObjects[$"obj:{rootReference}"] = new JsonObject { ["value"] = catalogValue };
            var update = new JsonObject
            {
                ["qpdf"] = new JsonArray(
                    new JsonObject { ["jsonversion"] = 2 },
                    updateObjects),
            };

            await File.WriteAllTextAsync(updatePath, update.ToJsonString(JsonOptions), new UTF8Encoding(false), cancellationToken);
            var outputArguments = new List<string>
            {
                qpdfInputPath,
                $"--update-from-json={updatePath}",
            };
            var requestedVersion = PdfOutputVersionMapping.GetVersionString(outputPdfVersion);
            if (requestedVersion is not null)
            {
                outputArguments.Add($"--force-version={requestedVersion}");
                if (outputPdfVersion == PdfOutputVersion.Pdf14)
                    outputArguments.Add("--object-streams=disable");
            }
            outputArguments.Add(outputPath);
            await RunQpdfAsync(outputArguments, cancellationToken);
            if (!File.Exists(outputPath))
                throw new InvalidDataException("qpdfがしおり更新後のPDFを生成しませんでした。");
            if (removeDocumentStructure && pronunciationPlan is not { ReadingCount: > 0 })
            {
                var verifiedTrailer = JsonNode.Parse(await RunQpdfForTextAsync(
                    ["--json-output=2", "--json-object=trailer", outputPath, "-"], cancellationToken))!;
                var verifiedRoot = verifiedTrailer["qpdf"]![1]!["trailer"]!["value"]!["/Root"]!.GetValue<string>();
                var (verifiedNumber, verifiedGeneration) = ParseReference(verifiedRoot);
                var verifiedCatalog = JsonNode.Parse(await RunQpdfForTextAsync(
                    ["--json-output=2", $"--json-object={verifiedNumber},{verifiedGeneration}", outputPath, "-"], cancellationToken))!;
                var verifiedValue = verifiedCatalog["qpdf"]![1]![$"obj:{verifiedRoot}"]!["value"]!.AsObject();
                if (verifiedValue.ContainsKey("/StructTreeRoot") || verifiedValue.ContainsKey("/MarkInfo") || verifiedValue.ContainsKey("/SpiderInfo"))
                    throw new InvalidDataException("墨消し後の文書構造に機密情報が残る可能性があるため、出力を中止しました。");
            }
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

    /// <summary>アプリで設定した文書内リンクまたは外部URIリンクをLink注釈としてページ辞書へ追加します。</summary>
    private async Task<int> WriteInternalLinkObjectsAsync(
        string pdfPath,
        IReadOnlyList<PdfInternalLink> links,
        IReadOnlyList<OcrPage> projectPages,
        IReadOnlyDictionary<int, string> pageReferences,
        JsonObject updateObjects,
        int nextObjectId,
        CancellationToken cancellationToken,
        IReadOnlyList<ProjectPageReference>? pageSequence)
    {
        var pagesById = projectPages.ToDictionary(page => page.Id);
        // Link targets must not depend on whether their OCR data has been lazily rendered.
        if (pageSequence is not null)
            for (var index = 0; index < pageSequence.Count; index++)
                pagesById.TryAdd(pageSequence[index].PageId,
                    new OcrPage { Id = pageSequence[index].PageId, PageNumber = index + 1 });
        // Imported, unchanged annotations already exist in the output. Inspect only
        // pages that actually add, replace or remove a link; large OCR books may
        // contain hundreds of untouched link pages.
        var changes = links.GroupBy(link => link.SourcePageId)
            .Where(group => group.Any(link => link.IsModified ||
                link.IsEnabled && link.SourceAnnotationReference is null && link.UnsupportedAction is null)).ToArray();
        foreach (var sourceGroup in changes)
        {
            if (!pagesById.TryGetValue(sourceGroup.Key, out var sourcePage) ||
                !pageReferences.TryGetValue(sourcePage.PageNumber, out var sourcePageReference)) continue;
            var (pageObjectNumber, pageGeneration) = ParseReference(sourcePageReference);
            var pageObjectJson = JsonNode.Parse(await RunQpdfForTextAsync(
                ["--json-output=2", $"--json-object={pageObjectNumber},{pageGeneration}", pdfPath, "-"], cancellationToken))!;
            var pageValue = pageObjectJson["qpdf"]![1]![$"obj:{sourcePageReference}"]!["value"]!.DeepClone().AsObject();

            JsonArray annotations;
            string? indirectAnnotationsReference = null;
            if (pageValue["/Annots"] is JsonArray directAnnotations)
            {
                annotations = directAnnotations.DeepClone().AsArray();
            }
            else if (pageValue["/Annots"] is JsonValue annotsReferenceNode &&
                     annotsReferenceNode.TryGetValue<string>(out var annotsReference) && annotsReference.EndsWith(" R", StringComparison.Ordinal))
            {
                indirectAnnotationsReference = annotsReference;
                var (annotsObjectNumber, annotsGeneration) = ParseReference(annotsReference);
                var annotsJson = JsonNode.Parse(await RunQpdfForTextAsync(
                    ["--json-output=2", $"--json-object={annotsObjectNumber},{annotsGeneration}", pdfPath, "-"], cancellationToken))!;
                annotations = annotsJson["qpdf"]![1]![$"obj:{annotsReference}"]!["value"]!.DeepClone().AsArray();
            }
            else
            {
                annotations = [];
            }

            // Resolve annotations once per page, in batches. Extraction may renumber objects,
            // so a reference alone is not sufficient evidence that this is the original link.
            var annotationValues = await LoadLinkAnnotationValuesAsync(pdfPath, annotations, cancellationToken);
            foreach (var link in sourceGroup.Where(link => link.IsModified && link.SourceAnnotationReference is not null))
            {
                var originals = link.SourceAnnotations.Count > 0 ? link.SourceAnnotations :
                    new[] { new PdfSourceLinkAnnotation { Reference = link.SourceAnnotationReference!,
                        Bounds = link.OriginalSourceBounds ?? link.SourceBounds ?? default } };
                foreach (var original in originals)
                {
                    var index = FindOriginalAnnotationIndex(annotations, annotationValues, original);
                    if (index < 0)
                        throw new InvalidDataException($"元PDFのリンク注釈を特定できませんでした（{sourcePage.PageNumber}ページ）。重複リンクを防ぐため出力を中止しました。");
                    annotations.RemoveAt(index);
                }
            }

            foreach (var link in sourceGroup.Where(link => link.IsEnabled && link.UnsupportedAction is null &&
                         (link.SourceAnnotationReference is null || link.IsModified)))
            {
                if (!link.IsGeometryLocked && link.SourceRegionId is { } regionId && sourcePage.TextRegions.FirstOrDefault(region => region.Id == regionId) is { IsDeleted: true }) continue;
                var bounds = link.SourceBounds ?? sourcePage.TextRegions.FirstOrDefault(region => region.Id == link.SourceRegionId)?.EditedGeometry.LocalBounds;
                var areas = link.SourceAreas.Count > 0 ? link.SourceAreas :
                    bounds is { IsValid: true } sourceRectangle ? new[] { sourceRectangle } : [];
                for (var areaIndex = 0; areaIndex < areas.Count; areaIndex++)
                {
                    var rectangle = areas[areaIndex];
                    if (!rectangle.IsValid) continue;
                    var annotationReference = $"{nextObjectId++} 0 R";
                    var annotation = new JsonObject
                    {
                        ["/Type"] = "/Annot", ["/Subtype"] = "/Link",
                        ["/Rect"] = new JsonArray(rectangle.Left, rectangle.Bottom, rectangle.Right, rectangle.Top),
                        ["/Border"] = new JsonArray(0, 0, 0),
                        ["/NM"] = "u:pdfcorrectorium-link-" + link.Id.ToString("N") + "-" + areaIndex,
                    };
                    if (!string.IsNullOrWhiteSpace(link.ExternalUri) &&
                        Uri.TryCreate(link.ExternalUri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                        annotation["/A"] = new JsonObject { ["/S"] = "/URI", ["/URI"] = "u:" + uri.AbsoluteUri };
                    else
                    {
                        if (!pagesById.TryGetValue(link.DestinationPageId, out var destinationPage) ||
                            !pageReferences.TryGetValue(destinationPage.PageNumber, out var destinationPageReference)) continue;
                        annotation["/Dest"] = CreateLinkDestination(link, destinationPageReference);
                    }
                    if (!string.IsNullOrWhiteSpace(link.Description)) annotation["/Contents"] = "u:" + link.Description;
                    updateObjects[$"obj:{annotationReference}"] = new JsonObject { ["value"] = annotation };
                    annotations.Add(annotationReference);
                }
            }

            if (indirectAnnotationsReference is not null)
                updateObjects[$"obj:{indirectAnnotationsReference}"] = new JsonObject { ["value"] = annotations };
            else
            {
                pageValue["/Annots"] = annotations;
                updateObjects[$"obj:{sourcePageReference}"] = new JsonObject { ["value"] = pageValue };
            }
        }
        return nextObjectId;
    }

    private static JsonArray CreateLinkDestination(PdfInternalLink link, string pageReference)
    {
        if (link.DestinationViewType is { } type && link.DestinationViewParameters.Count > 0)
        {
            var destination = new JsonArray(pageReference, type);
            foreach (var value in link.DestinationViewParameters)
                destination.Add(value is { } number ? JsonValue.Create(number) : null);
            return destination;
        }
        if (link.DestinationViewType is "/Fit" or "/FitB")
            return new JsonArray(pageReference, link.DestinationViewType);
        return link.DestinationZoomPercent is > 0 || link.DestinationPosition is not null
            ? new JsonArray(pageReference, "/XYZ",
                link.DestinationPosition?.X is { } x ? JsonValue.Create(x) : null,
                link.DestinationPosition?.Y is { } y ? JsonValue.Create(y) : null,
                link.DestinationZoomPercent is { } zoom ? JsonValue.Create(zoom / 100d) : null)
            : new JsonArray(pageReference, "/Fit");
    }

    private async Task<Dictionary<string, JsonNode?>> LoadLinkAnnotationValuesAsync(
        string pdfPath, JsonArray annotations, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var references = annotations.OfType<JsonValue>().Select(node =>
            node.TryGetValue<string>(out var value) ? value : null)
            .Where(value => value?.EndsWith(" R", StringComparison.Ordinal) == true).Cast<string>().Distinct();
        foreach (var batch in references.Chunk(64))
        {
            var args = new List<string> { "--json-output=2" };
            foreach (var reference in batch)
            {
                var (number, generation) = ParseReference(reference);
                args.Add($"--json-object={number},{generation}");
            }
            args.Add(pdfPath); args.Add("-");
            var json = JsonNode.Parse(await RunQpdfForTextAsync(args.ToArray(), cancellationToken))!;
            foreach (var reference in batch)
                result[reference] = json["qpdf"]![1]![$"obj:{reference}"]?["value"];
        }
        return result;
    }

    private static int FindOriginalAnnotationIndex(JsonArray annotations,
        IReadOnlyDictionary<string, JsonNode?> values, PdfSourceLinkAnnotation original)
    {
        var matches = new List<int>();
        for (var index = 0; index < annotations.Count; index++)
        {
            var reference = annotations[index] is JsonValue node && node.TryGetValue<string>(out var value) ? value : null;
            var candidate = reference is not null && values.TryGetValue(reference, out var resolved)
                ? resolved as JsonObject : annotations[index] as JsonObject;
            if (candidate?["/Subtype"]?.GetValue<string>() != "/Link" ||
                !AnnotationRectMatches(candidate["/Rect"], original.Bounds)) continue;
            var name = candidate["/NM"] is JsonValue nameValue && nameValue.TryGetValue<string>(out var nameText)
                ? DecodeQpdfString(nameText) : null;
            if (original.Name is not null && name != original.Name) continue;
            // A renumbered reference can coincidentally match a different annotation.
            // Only a unique rectangle/name match is safe to replace.
            matches.Add(index);
        }
        return matches.Count == 1 ? matches[0] : -1;
    }

    private static bool AnnotationRectMatches(JsonNode? node, PdfCorrectorium.Core.Geometry.PdfRectangle? bounds)
    {
        if (node is not JsonArray rectangle || rectangle.Count != 4 || bounds is null) return false;
        var expected = new[] { bounds.Value.Left, bounds.Value.Bottom, bounds.Value.Right, bounds.Value.Top };
        for (var i = 0; i < 4; i++)
        {
            if (rectangle[i] is not JsonValue value || !value.TryGetValue<double>(out var actual) ||
                Math.Abs(actual - expected[i]) > 1d) return false;
        }
        return true;
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

    /// <summary>PDF文書情報辞書の文字列を更新し、空欄の場合は既存項目を削除します。</summary>
    private static void SetDocumentMetadataValue(JsonObject info, string key, string value)
    {
        if (string.IsNullOrEmpty(value))
            info.Remove(key);
        else
            info[key] = "u:" + value;
    }

    /// <summary>qpdf JSONの文字列表現を画面で使用する通常の文字列へ戻します。</summary>
    private static string DecodeQpdfString(string value)
    {
        if (value.StartsWith("u:", StringComparison.Ordinal)) return value[2..];
        if (!value.StartsWith("b:", StringComparison.Ordinal)) return value;
        var hex = value[2..];
        try
        {
            return Encoding.ASCII.GetString(Convert.FromHexString(hex));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
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
    /// 配布フォルダーを優先する共通ポリシーでqpdf実行ファイルを探索します。
    /// </summary>
    /// <returns>見つかったqpdfの絶対パス。利用できない場合は<see langword="null"/>。</returns>
    public static string? ResolveQpdfPath() => QpdfExecutableResolver.Resolve();
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

    private async Task<string> RunQpdfForTextAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var (output, _) = await RunQpdfAsync(arguments, cancellationToken);
        return output;
    }

    internal Action<IReadOnlyList<string>>? QpdfInvocationObserver { get; init; }

    private async Task<(string Output, string Error)> RunQpdfAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var qpdfPath = ResolveQpdfPath() ??
                       throw new FileNotFoundException("しおり処理に必要なqpdf.exeが見つかりません。");
        QpdfInvocationObserver?.Invoke(arguments);
        var result = await ExternalProcessRunner.RunAsync(
            qpdfPath, arguments, OperationTimeout, cancellationToken, MaximumQpdfOutputCharacters);
        if (result.ExitCode != 0)
            throw new InvalidDataException(
                $"qpdfによるしおり処理に失敗しました（終了コード: {result.ExitCode}）。\n" +
                string.Join("\n", new[] { result.StandardError, result.StandardOutput }.Where(value => !string.IsNullOrWhiteSpace(value))));
        return (result.StandardOutput, result.StandardError);
    }

    private void EnsureImportFileWithinLimits(string path)
    {
        if (MaximumImportBytes <= 0 || MaximumBookmarkCount <= 0 || MaximumBookmarkDepth <= 0 ||
            MaximumTitleCharacters <= 0 || MaximumImportNodes <= 0 || MaximumImportStructureDepth <= 0 || MaximumTotalImportCharacters <= 0)
            throw new InvalidOperationException("しおり取込上限は正の値で指定してください。");
        var length = new FileInfo(path).Length;
        if (length > MaximumImportBytes)
            throw new InvalidDataException($"しおり交換ファイルが上限 {MaximumImportBytes:N0} バイトを超えています。");
    }

    private void ValidateBookmarks(IReadOnlyList<PdfBookmark> roots)
    {
        var pending = new Stack<(PdfBookmark Bookmark, int Depth)>(roots.Reverse().Select(item => (item, 1)));
        var count = 0;
        while (pending.TryPop(out var item))
        {
            if (++count > MaximumBookmarkCount)
                throw new InvalidDataException($"しおり数が上限 {MaximumBookmarkCount:N0} 件を超えています。");
            if (item.Depth > MaximumBookmarkDepth)
                throw new InvalidDataException($"しおりの階層が上限 {MaximumBookmarkDepth:N0} を超えています。");
            if ((item.Bookmark.Title ?? string.Empty).Length > MaximumTitleCharacters)
                throw new InvalidDataException($"しおりタイトルが上限 {MaximumTitleCharacters:N0} 文字を超えています。");
            foreach (var child in item.Bookmark.Children.Reverse()) pending.Push((child, item.Depth + 1));
        }
    }
}
