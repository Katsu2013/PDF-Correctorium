using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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

    private static async Task<NdlOcrDocument> ImportCompanionsAsync(IReadOnlyList<string> companions, CancellationToken cancellationToken)
    {

        var jsonPath = companions.FirstOrDefault(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        if (jsonPath is not null)
        {
            try
            {
                var pages = await ReadJsonAsync(jsonPath, cancellationToken);
                if (pages.Count > 0) return new NdlOcrDocument("NDLOCR-Lite JSON", pages, companions);
            }
            catch (JsonException) { }
            catch (InvalidDataException) { }
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
            catch (InvalidDataException) { }
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

    private static async Task<IReadOnlyDictionary<int, NdlOcrPage>> ReadJsonAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("contents", out var contents) || contents.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The NDLOCR JSON does not contain a contents array.");

        var pageInfos = root.TryGetProperty("pages", out var pagesElement) && pagesElement.ValueKind == JsonValueKind.Array
            ? pagesElement.EnumerateArray().ToArray()
            : [];
        JsonElement? imageInfo = root.TryGetProperty("imginfo", out var imgInfoElement) ? imgInfoElement : null;
        var result = new Dictionary<int, NdlOcrPage>();
        var pageIndex = 0;
        foreach (var pageContents in contents.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lines = new List<NdlOcrLine>();
            if (pageContents.ValueKind == JsonValueKind.Array)
            {
                foreach (var lineElement in pageContents.EnumerateArray())
                {
                    if (!TryReadJsonLine(lineElement, out var line)) continue;
                    lines.Add(line);
                }
            }

            var info = pageIndex < pageInfos.Length ? pageInfos[pageIndex] : imageInfo;
            var width = info.HasValue ? ReadDouble(info.Value, "img_width") : 0;
            var height = info.HasValue ? ReadDouble(info.Value, "img_height") : 0;
            if (width <= 0) width = lines.Count == 0 ? 1 : lines.Max(line => line.X + line.Width);
            if (height <= 0) height = lines.Count == 0 ? 1 : lines.Max(line => line.Y + line.Height);
            result[pageIndex + 1] = new NdlOcrPage(pageIndex + 1, width, height, lines);
            pageIndex++;
        }
        return result;
    }

    private static bool TryReadJsonLine(JsonElement element, out NdlOcrLine line)
    {
        line = default!;
        if (!element.TryGetProperty("boundingBox", out var box) || box.ValueKind != JsonValueKind.Array) return false;
        var points = new List<(double X, double Y)>();
        foreach (var point in box.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Array) continue;
            var values = point.EnumerateArray().ToArray();
            if (values.Length >= 2 && values[0].TryGetDouble(out var x) && values[1].TryGetDouble(out var y)) points.Add((x, y));
        }
        if (points.Count < 2) return false;
        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X);
        var bottom = points.Max(point => point.Y);
        var text = element.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty;
        var hasVertical = element.TryGetProperty("isVertical", out var verticalElement);
        var isVertical = hasVertical
            ? ReadBoolean(verticalElement)
            : WritingDirectionDetector.IsLikelyVertical(text, right - left, bottom - top);
        var confidence = element.TryGetProperty("confidence", out var confidenceElement) && confidenceElement.TryGetDouble(out var value) ? value : 0;
        line = new NdlOcrLine(text, left, top, right - left, bottom - top, isVertical, confidence);
        return true;
    }

    private static async Task<IReadOnlyDictionary<int, NdlOcrPage>> ReadXmlAsync(string path, CancellationToken cancellationToken)
    {
        var xml = await File.ReadAllTextAsync(path, cancellationToken);
        XDocument document;
        try { document = XDocument.Parse(xml, LoadOptions.None); }
        catch (System.Xml.XmlException)
        {
            var withoutDeclaration = Regex.Replace(xml, @"<\?xml[^?]*\?>", string.Empty, RegexOptions.IgnoreCase);
            document = XDocument.Parse("<NDLOCR>" + withoutDeclaration + "</NDLOCR>");
        }

        var result = new Dictionary<int, NdlOcrPage>();
        var pageNumber = 1;
        foreach (var pageElement in document.Descendants().Where(element => element.Name.LocalName.Equals("PAGE", StringComparison.OrdinalIgnoreCase)))
        {
            var lines = pageElement.Descendants().Where(element => element.Name.LocalName.Equals("LINE", StringComparison.OrdinalIgnoreCase))
                .Select(ReadXmlLine)
                .Where(line => line is not null)
                .Cast<NdlOcrLine>()
                .ToArray();
            var width = ReadAttribute(pageElement, "WIDTH");
            var height = ReadAttribute(pageElement, "HEIGHT");
            if (width <= 0) width = lines.Length == 0 ? 1 : lines.Max(line => line.X + line.Width);
            if (height <= 0) height = lines.Length == 0 ? 1 : lines.Max(line => line.Y + line.Height);
            result[pageNumber] = new NdlOcrPage(pageNumber, width, height, lines);
            pageNumber++;
        }
        return result;
    }

    private static NdlOcrLine? ReadXmlLine(XElement element)
    {
        var x = ReadAttribute(element, "X");
        var y = ReadAttribute(element, "Y");
        var width = ReadAttribute(element, "WIDTH");
        var height = ReadAttribute(element, "HEIGHT");
        if (width <= 0 || height <= 0) return null;
        var text = element.Attribute("STRING")?.Value ?? element.Value;
        var explicitVertical = ReadOptionalBooleanAttribute(element, "IS_VERTICAL") ??
                               ReadOptionalBooleanAttribute(element, "VERTICAL");
        return new NdlOcrLine(
            text,
            x,
            y,
            width,
            height,
            explicitVertical ?? WritingDirectionDetector.IsLikelyVertical(text, width, height),
            ReadAttribute(element, "CONF"));
    }

    private static bool? ReadOptionalBooleanAttribute(XElement element, string name)
    {
        var value = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        if (value is null) return null;
        if (bool.TryParse(value, out var parsed)) return parsed;
        return value.Trim() switch { "1" => true, "0" => false, _ => null };
    }

    private static double ReadDouble(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.TryGetDouble(out var number)
            ? number
            : 0;

    private static bool ReadBoolean(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => bool.TryParse(element.GetString(), out var value) && value,
        JsonValueKind.Number => element.TryGetInt32(out var value) && value != 0,
        _ => false,
    };

    private static double ReadAttribute(XElement element, string name) =>
        double.TryParse(element.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
