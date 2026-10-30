using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Xml;
using PdfCorrectorium.Core.IO;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// NDLOCR-Liteが認識した1行の文字列と画像座標を表します。
/// </summary>
/// <param name="Text">認識された行文字列。</param>
/// <param name="X">OCR元画像左端からのX座標。</param>
/// <param name="Y">OCR元画像上端からのY座標。</param>
/// <param name="Width">OCR元画像上の行領域幅。</param>
/// <param name="Height">OCR元画像上の行領域高。</param>
/// <param name="IsVertical">縦書き行の場合は<c>true</c>。</param>
/// <param name="Confidence">NDLOCR-Liteが出力した認識信頼度。</param>
public sealed record NdlOcrLine(
    string Text,
    double X,
    double Y,
    double Width,
    double Height,
    bool IsVertical,
    double Confidence);

/// <summary>
/// NDLOCR-Liteの1ページ分の画像寸法と認識行を表します。
/// </summary>
/// <param name="PageNumber">1から始まるページ番号。</param>
/// <param name="ImageWidth">OCR処理に使われたページ画像の幅。</param>
/// <param name="ImageHeight">OCR処理に使われたページ画像の高さ。</param>
/// <param name="Lines">このページで認識された行領域。</param>
public sealed record NdlOcrPage(int PageNumber, double ImageWidth, double ImageHeight, IReadOnlyList<NdlOcrLine> Lines);

/// <summary>
/// NDLOCR-Liteの付随ファイルから統合した文書単位のOCR結果です。
/// </summary>
public sealed class NdlOcrDocument(
    string sourceKind,
    IReadOnlyDictionary<int, NdlOcrPage> pages,
    IReadOnlyList<string> companionFiles)
{
    /// <summary>採用した付随ファイル形式（JSON、XMLなど）の識別名です。</summary>
    public string SourceKind { get; } = sourceKind;
    /// <summary>ページ番号をキーとする統合済みOCRページです。</summary>
    public IReadOnlyDictionary<int, NdlOcrPage> Pages { get; } = pages;
    /// <summary>統合元として実際に読み込んだ付随ファイルのパスです。</summary>
    public IReadOnlyList<string> CompanionFiles { get; } = companionFiles;

    /// <summary>
    /// OCR元画像の座標を、現在のプレビュー画像のピクセル座標へ変換します。
    /// </summary>
    /// <param name="pageNumber">1から始まるページ番号。</param>
    /// <param name="pixelWidth">変換先プレビューの幅。</param>
    /// <param name="pixelHeight">変換先プレビューの高さ。</param>
    /// <returns>変換後のOCR領域。該当ページがなければ空の一覧。</returns>
    public IReadOnlyList<PdfTextOverlayRegion> GetScaledRegions(int pageNumber, int pixelWidth, int pixelHeight)
    {
        if (!Pages.TryGetValue(pageNumber, out var page) || page.ImageWidth <= 0 || page.ImageHeight <= 0) return [];
        return page.Lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Text) && line.Width > 0 && line.Height > 0)
            .Select(line => new PdfTextOverlayRegion(
                line.Text,
                line.X / page.ImageWidth * pixelWidth,
                line.Y / page.ImageHeight * pixelHeight,
                line.Width / page.ImageWidth * pixelWidth,
                line.Height / page.ImageHeight * pixelHeight,
                true,
                line.IsVertical,
                "ndlocr-lite",
                line.Confidence))
            .ToArray();
    }
}

/// <summary>
/// PDFと同じ場所にあるNDLOCR-LiteのJSON、XML、TEI XMLなどを探索して取り込みます。
/// </summary>
/// <remarks>
/// 複数形式が存在する場合は、ページ番号、座標、文字列を統合し、利用したファイルを
/// <see cref="NdlOcrDocument.CompanionFiles"/> に記録します。
/// </remarks>
public sealed class NdlOcrCompanionService
{
    /// <summary>1つの付随ファイルに許可する最大バイト数です。</summary>
    public long MaximumImportBytes { get; init; } = 256L * 1024 * 1024;
    /// <summary>一度に統合する付随ファイル数です。</summary>
    public int MaximumCompanionCount { get; init; } = 32;
    /// <summary>1文書で許可するOCRページ数です。</summary>
    public int MaximumPageCount { get; init; } = 10_000;
    /// <summary>1文書で許可するOCR行数です。</summary>
    public int MaximumLineCount { get; init; } = 2_000_000;
    /// <summary>1行の認識文字列に許可する最大文字数です。</summary>
    public int MaximumLineTextCharacters { get; init; } = 32_768;
    /// <summary>XML専用のバイト数上限。全体上限より大きい値は使用しません。</summary>
    public long MaximumXmlImportBytes { get; init; } = 32L * 1024 * 1024;
    public int MaximumJsonTokenCount { get; init; } = 8_000_000;
    public int MaximumJsonDepth { get; init; } = 64;
    public int MaximumJsonTokenBytes { get; init; } = 1024 * 1024;
    public int MaximumBoundingBoxPoints { get; init; } = 64;
    public int MaximumCoordinateElements { get; init; } = 8;
    public int MaximumXmlNodeCount { get; init; } = 1_000_000;
    public int MaximumXmlDepth { get; init; } = 64;
    public long MaximumTotalTextCharacters { get; init; } = 16L * 1024 * 1024;
    /// <summary>NDLOCR-Liteが生成PDF名へ付加し得る接尾辞の候補です。</summary>
    private static readonly string[] GeneratedPdfSuffixes =
    [
        " - Unknown_text", "_Unknown_text", "-Unknown_text", "_text", "-text", " text", "_ocr", "-ocr", " ocr",
    ];

    /// <summary>
    /// PDF名に対応する付随OCRファイルを自動探索して取り込みます。
    /// </summary>
    /// <param name="pdfPath">付随ファイルの基準となるPDFパス。</param>
    /// <param name="cancellationToken">処理の取り消しを通知するトークン。</param>
    /// <returns>取り込んだOCR文書。候補が見つからない場合は<see langword="null"/>。</returns>
    public async Task<NdlOcrDocument?> TryImportAsync(string pdfPath, CancellationToken cancellationToken = default)
    {
        var companions = Discover(pdfPath);
        if (companions.Count == 0) return null;
        return await ImportCompanionsAsync(companions, cancellationToken);
    }

    /// <summary>
    /// 指定したOCR付随ファイルと、同じ文書名を持つ関連ファイルをまとめて取り込みます。
    /// </summary>
    /// <param name="companionPath">JSON、XML、TEI XMLなどの付随ファイル。</param>
    /// <param name="cancellationToken">処理の取り消しを通知するトークン。</param>
    /// <returns>統合済みOCR文書。</returns>
    /// <exception cref="FileNotFoundException">指定ファイルが存在しない場合。</exception>
    public async Task<NdlOcrDocument> ImportAsync(string companionPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(companionPath)) throw new FileNotFoundException("The OCR companion file was not found.", companionPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(companionPath))!;
        var name = Path.GetFileName(companionPath);
        var stem = name.EndsWith(".tei.xml", StringComparison.OrdinalIgnoreCase)
            ? name[..^".tei.xml".Length]
            : name.EndsWith("_tei.xml", StringComparison.OrdinalIgnoreCase)
                ? name[..^"_tei.xml".Length]
                : Path.GetFileNameWithoutExtension(name);
        var companions = Directory.EnumerateFiles(directory)
            .Where(path => IsCompanionExtension(path) && MatchesStem(path, stem))
            .OrderBy(CompanionPriority)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!companions.Contains(Path.GetFullPath(companionPath), StringComparer.OrdinalIgnoreCase))
            companions = [Path.GetFullPath(companionPath), .. companions];
        return await ImportCompanionsAsync(companions, cancellationToken);
    }

    private async Task<NdlOcrDocument> ImportCompanionsAsync(IReadOnlyList<string> companions, CancellationToken cancellationToken)
    {
        ValidateCompanions(companions);
        // JSON/XMLは座標と本文を持つため優先して解析し、TXT/TEIはメタデータだけでも取り込めるようにする。
        var jsonPath = companions.FirstOrDefault(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        if (jsonPath is not null)
        {
            try
            {
                var pages = await ReadJsonAsync(jsonPath, cancellationToken);
                if (pages.Count > 0) return new NdlOcrDocument("NDLOCR-Lite JSON", pages, companions);
            }
            catch (JsonException) { }
        }

        var xmlPath = companions.FirstOrDefault(path =>
            path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".tei.xml", StringComparison.OrdinalIgnoreCase));
        if (xmlPath is not null)
        {
            try
            {
                var pages = await ReadXmlAsync(xmlPath, cancellationToken);
                if (pages.Count > 0) return new NdlOcrDocument("NDLOCR-Lite XML", pages, companions);
            }
            catch (System.Xml.XmlException) { }
        }

        return new NdlOcrDocument("NDLOCR-Lite TXT/TEI metadata", new Dictionary<int, NdlOcrPage>(), companions);
    }

    private static IReadOnlyList<string> Discover(string pdfPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(pdfPath));
        if (directory is null || !Directory.Exists(directory)) return [];
        var stems = GetCandidateStems(Path.GetFileNameWithoutExtension(pdfPath));
        var files = Directory.EnumerateFiles(directory)
            .Where(path => IsCompanionExtension(path))
            .ToArray();
        return files
            .Where(path => stems.Any(stem => MatchesStem(path, stem)))
            .OrderBy(CompanionPriority)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> GetCandidateStems(string originalStem)
    {
        var stems = new List<string>();
        var pending = new Queue<string>();
        pending.Enqueue(originalStem);
        while (pending.TryDequeue(out var current))
        {
            if (string.IsNullOrWhiteSpace(current) || stems.Contains(current, StringComparer.OrdinalIgnoreCase)) continue;
            stems.Add(current);
            foreach (var suffix in GeneratedPdfSuffixes)
            {
                if (!current.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                var shortened = current[..^suffix.Length].TrimEnd();
                if (!string.IsNullOrWhiteSpace(shortened)) pending.Enqueue(shortened);
            }
        }
        return stems;
    }

    private static bool MatchesStem(string path, string stem)
    {
        var name = Path.GetFileName(path);
        return name.Equals(stem + ".json", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(stem + ".xml", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(stem + ".txt", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(stem + ".tei.xml", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(stem + "_tei.xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompanionExtension(string path) =>
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

    private static int CompanionPriority(string path) =>
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? 0 :
        path.EndsWith(".tei.xml", StringComparison.OrdinalIgnoreCase) ? 3 :
        path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? 1 : 2;

    private async Task<IReadOnlyDictionary<int, NdlOcrPage>> ReadJsonAsync(string path, CancellationToken cancellationToken)
    {
        EnsureFileWithinLimits(path);
        await using var stream = new BoundedReadStream(File.OpenRead(path), MaximumImportBytes);
        return await new NdlOcrJsonParser(this).ReadAsync(stream, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<int, NdlOcrPage>> ReadXmlAsync(string path, CancellationToken cancellationToken)
    {
        EnsureFileWithinLimits(path);
        var maximumBytes = Math.Min(MaximumImportBytes, MaximumXmlImportBytes);
        if (new FileInfo(path).Length > maximumBytes)
            throw new InvalidDataException($"OCR XMLが上限 {maximumBytes:N0} バイトを超えています。");
        await using var stream = new BoundedReadStream(File.OpenRead(path), maximumBytes);
        using var textReader = new OcrXmlFragmentReader(new StreamReader(stream));
        using var reader = XmlReader.Create(textReader, new XmlReaderSettings
        {
            Async = true,
            ConformanceLevel = ConformanceLevel.Fragment,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maximumBytes,
        });
        var result = new Dictionary<int, NdlOcrPage>();
        var lines = new List<NdlOcrLine>();
        var pageDepth = -1;
        var pageWidth = 0d;
        var pageHeight = 0d;
        var totalLines = 0;
        long totalText = 0;
        long nodes = 0;
        XmlLineState? line = null;
        var valueBuffer = new char[4096];

        void FinishLine()
        {
            var current = line!;
            var value = current.AttributeText ?? current.Text.ToString();
            totalText = checked(totalText + value.Length);
            if (totalText > MaximumTotalTextCharacters)
                throw new InvalidDataException("OCR本文の合計文字数が上限を超えています。");
            if (current.Width > 0 && current.Height > 0)
                lines.Add(new NdlOcrLine(value, current.X, current.Y, current.Width, current.Height,
                    current.Vertical ?? WritingDirectionDetector.IsLikelyVertical(value, current.Width, current.Height), current.Confidence));
            line = null;
        }

        void FinishPage()
        {
            var width = pageWidth > 0 ? pageWidth : lines.Count == 0 ? 1 : lines.Max(item => item.X + item.Width);
            var height = pageHeight > 0 ? pageHeight : lines.Count == 0 ? 1 : lines.Max(item => item.Y + item.Height);
            var number = result.Count + 1;
            result.Add(number, new NdlOcrPage(number, width, height, lines.ToArray()));
            lines.Clear();
            pageDepth = -1;
        }

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            nodes = checked(nodes + 1 + reader.AttributeCount);
            if (nodes > MaximumXmlNodeCount || reader.Depth > MaximumXmlDepth)
                throw new InvalidDataException("OCR XMLのノード数または深さが上限を超えています。");
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.LocalName.Equals("PAGE", StringComparison.OrdinalIgnoreCase))
                {
                    if (pageDepth >= 0) throw new InvalidDataException("OCR PAGE要素の入れ子には対応していません。");
                    if (result.Count >= MaximumPageCount)
                        throw new InvalidDataException($"OCRページ数が上限 {MaximumPageCount:N0} を超えています。");
                    pageDepth = reader.Depth;
                    pageWidth = ReadAttribute(reader, "WIDTH");
                    pageHeight = ReadAttribute(reader, "HEIGHT");
                    if (reader.IsEmptyElement) FinishPage();
                }
                else if (pageDepth >= 0 && reader.LocalName.Equals("LINE", StringComparison.OrdinalIgnoreCase))
                {
                    if (line is not null) throw new InvalidDataException("OCR LINE要素の入れ子には対応していません。");
                    if (++totalLines > MaximumLineCount)
                        throw new InvalidDataException($"OCR行数が上限 {MaximumLineCount:N0} を超えています。");
                    var attributeText = reader.GetAttribute("STRING");
                    if (attributeText?.Length > MaximumLineTextCharacters)
                        throw new InvalidDataException($"OCR行の文字数が上限 {MaximumLineTextCharacters:N0} を超えています。");
                    line = new XmlLineState(reader.Depth, ReadAttribute(reader, "X"), ReadAttribute(reader, "Y"),
                        ReadAttribute(reader, "WIDTH"), ReadAttribute(reader, "HEIGHT"), ReadAttribute(reader, "CONF"),
                        ReadOptionalBooleanAttribute(reader, "IS_VERTICAL") ?? ReadOptionalBooleanAttribute(reader, "VERTICAL"), attributeText);
                    if (reader.IsEmptyElement) FinishLine();
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (line is not null && reader.Depth == line.Depth) FinishLine();
                if (pageDepth >= 0 && reader.Depth == pageDepth) FinishPage();
            }
            else if (line is not null && line.AttributeText is null &&
                     reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
            {
                int read;
                while ((read = await reader.ReadValueChunkAsync(valueBuffer, 0, valueBuffer.Length).ConfigureAwait(false)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (line.Text.Length + read > MaximumLineTextCharacters)
                        throw new InvalidDataException($"OCR行の文字数が上限 {MaximumLineTextCharacters:N0} を超えています。");
                    line.Text.Append(valueBuffer, 0, read);
                }
            }
        }
        return result;
    }

    private sealed record XmlLineState(int Depth, double X, double Y, double Width, double Height,
        double Confidence, bool? Vertical, string? AttributeText)
    {
        public StringBuilder Text { get; } = new();
    }

    private static bool? ReadOptionalBooleanAttribute(XmlReader reader, string name)
    {
        string? value = null;
        if (reader.MoveToFirstAttribute())
        {
            do
            {
                if (reader.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = reader.Value; break; }
            } while (reader.MoveToNextAttribute());
            reader.MoveToElement();
        }
        if (value is null) return null;
        if (bool.TryParse(value, out var parsed)) return parsed;
        return value.Trim() switch { "1" => true, "0" => false, _ => null };
    }
    private static double ReadAttribute(XmlReader reader, string name) =>
        double.TryParse(reader.GetAttribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private void ValidateCompanions(IReadOnlyList<string> companions)
    {
        if (MaximumImportBytes <= 0 || MaximumCompanionCount <= 0 || MaximumPageCount <= 0 ||
            MaximumLineCount <= 0 || MaximumLineTextCharacters <= 0 || MaximumXmlImportBytes <= 0 ||
            MaximumXmlNodeCount <= 0 || MaximumXmlDepth <= 0 || MaximumTotalTextCharacters <= 0 ||
            MaximumJsonTokenCount <= 0 || MaximumJsonDepth <= 0 || MaximumJsonDepth > 128 || MaximumJsonTokenBytes < 16 ||
            MaximumBoundingBoxPoints < 2 || MaximumCoordinateElements < 2)
            throw new InvalidOperationException("OCR取込上限は正の値で指定してください。");
        if (companions.Count > MaximumCompanionCount)
            throw new InvalidDataException($"OCR付随ファイル数が上限 {MaximumCompanionCount:N0} を超えています。");
        foreach (var path in companions) EnsureFileWithinLimits(path);
    }

    private void EnsureFileWithinLimits(string path)
    {
        var length = new FileInfo(path).Length;
        if (length > MaximumImportBytes)
            throw new InvalidDataException($"OCR付随ファイルが上限 {MaximumImportBytes:N0} バイトを超えています: {Path.GetFileName(path)}");
    }

}
