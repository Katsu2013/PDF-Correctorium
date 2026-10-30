using System.IO;
using System.Text.Json;
using Token = PdfCorrectorium.App.Services.OcrJsonTokenReader.Token;

namespace PdfCorrectorium.App.Services;

/// <summary>ページ・行単位にOCR JSONを読み、DOMや座標配列を作らず入力予算を検査します。</summary>
internal sealed class NdlOcrJsonParser(NdlOcrCompanionService limits)
{
    private int _lineCount;
    private long _textCharacters;

    internal async Task<IReadOnlyDictionary<int, NdlOcrPage>> ReadAsync(Stream stream, CancellationToken ct)
    {
        var reader = new OcrJsonTokenReader(stream, limits.MaximumJsonTokenBytes, limits.MaximumJsonTokenCount, limits.MaximumJsonDepth);
        var token = await reader.RequireAsync(ct);
        if (token.Kind != JsonTokenType.StartObject) throw new JsonException("OCR JSON must be an object.");
        List<IReadOnlyList<NdlOcrLine>>? contents = null;
        var pageInfos = new List<(double Width, double Height)>();
        (double Width, double Height) imageInfo = default;
        var seen = new HashSet<string>();
        while ((token = await reader.RequireAsync(ct)).Kind != JsonTokenType.EndObject)
        {
            if (token.Kind != JsonTokenType.PropertyName) throw new JsonException();
            var name = token.Text;
            token = await reader.RequireAsync(ct);
            if (name is "contents" or "pages" or "imginfo" && !seen.Add(name))
                throw new InvalidDataException("OCR JSONに重複した主要プロパティがあります。");
            switch (name)
            {
                case "contents":
                    if (token.Kind != JsonTokenType.StartArray) throw new InvalidDataException("OCR JSONにcontents配列がありません。");
                    contents = await ReadContentsAsync(reader, ct);
                    break;
                case "pages" when token.Kind == JsonTokenType.StartArray:
                    while ((token = await reader.RequireAsync(ct)).Kind != JsonTokenType.EndArray)
                    {
                        if (pageInfos.Count >= limits.MaximumPageCount) throw new InvalidDataException("OCR JSONのページ情報数が上限を超えています。");
                        pageInfos.Add(await ReadDimensionsAsync(reader, token, ct));
                    }
                    break;
                case "imginfo": imageInfo = await ReadDimensionsAsync(reader, token, ct); break;
                default: await reader.SkipAsync(token, ct); break;
            }
        }
        if (await reader.ReadAsync(ct) is not null) throw new JsonException("Trailing OCR JSON.");
        if (contents is null) throw new InvalidDataException("OCR JSONにcontents配列がありません。");
        var result = new Dictionary<int, NdlOcrPage>();
        for (var index = 0; index < contents.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var lines = contents[index];
            var (width, height) = index < pageInfos.Count ? pageInfos[index] : imageInfo;
            if (width <= 0) width = lines.Count == 0 ? 1 : lines.Max(line => line.X + line.Width);
            if (height <= 0) height = lines.Count == 0 ? 1 : lines.Max(line => line.Y + line.Height);
            result[index + 1] = new(index + 1, width, height, lines);
        }
        return result;
    }

    private async Task<List<IReadOnlyList<NdlOcrLine>>> ReadContentsAsync(OcrJsonTokenReader reader, CancellationToken ct)
    {
        var pages = new List<IReadOnlyList<NdlOcrLine>>();
        Token token;
        while ((token = await reader.RequireAsync(ct)).Kind != JsonTokenType.EndArray)
        {
            if (pages.Count >= limits.MaximumPageCount) throw new InvalidDataException("OCR JSONのページ数が上限を超えています。");
            var lines = new List<NdlOcrLine>();
            if (token.Kind == JsonTokenType.StartArray)
            {
                while ((token = await reader.RequireAsync(ct)).Kind != JsonTokenType.EndArray)
                {
                    if (++_lineCount > limits.MaximumLineCount) throw new InvalidDataException("OCR JSONの入力行数が上限を超えています。");
                    var line = await ReadLineAsync(reader, token, ct);
                    if (line is not null) lines.Add(line);
                }
            }
            else await reader.SkipAsync(token, ct);
            pages.Add(lines);
        }
        return pages;
    }

    private async Task<(double Width, double Height)> ReadDimensionsAsync(OcrJsonTokenReader reader, Token token, CancellationToken ct)
    {
        double width = 0, height = 0;
        if (token.Kind != JsonTokenType.StartObject) { await reader.SkipAsync(token, ct); return default; }
        while ((token = await reader.RequireAsync(ct)).Kind != JsonTokenType.EndObject)
        {
            var name = token.Text;
            token = await reader.RequireAsync(ct);
            if (name == "img_width") width = token.Number ?? 0;
            else if (name == "img_height") height = token.Number ?? 0;
            await reader.SkipAsync(token, ct);
        }
        return (width, height);
    }

    private async Task<NdlOcrLine?> ReadLineAsync(OcrJsonTokenReader reader, Token token, CancellationToken ct)
    {
        if (token.Kind != JsonTokenType.StartObject) { await reader.SkipAsync(token, ct); return null; }
        string text = "";
        bool? vertical = null;
        double confidence = 0;
        (double Left, double Top, double Width, double Height)? bounds = null;
        var seen = new HashSet<string>();
        while ((token = await reader.RequireAsync(ct)).Kind != JsonTokenType.EndObject)
        {
            var name = token.Text;
            token = await reader.RequireAsync(ct);
            if (name is "text" or "boundingBox" or "isVertical" or "confidence" && !seen.Add(name))
                throw new InvalidDataException("OCR JSONの行に重複プロパティがあります。");
            switch (name)
            {
                case "boundingBox": bounds = await ReadBoxAsync(reader, token, ct); break;
                case "text":
                    if (token.Kind is not (JsonTokenType.String or JsonTokenType.Null)) throw new InvalidDataException("OCR JSONの本文が文字列ではありません。");
                    text = token.Text ?? "";
                    _textCharacters = checked(_textCharacters + text.Length);
                    if (text.Length > limits.MaximumLineTextCharacters || _textCharacters > limits.MaximumTotalTextCharacters)
                        throw new InvalidDataException("OCR JSONの本文文字数が上限を超えています。");
                    break;
                case "isVertical":
                    vertical = token.Kind == JsonTokenType.True || token.Kind == JsonTokenType.Number && token.Number is { } number && number != 0 &&
                        number >= int.MinValue && number <= int.MaxValue && number == Math.Truncate(number) ||
                        token.Kind == JsonTokenType.String && bool.TryParse(token.Text, out var flag) && flag;
                    await reader.SkipAsync(token, ct);
                    break;
                case "confidence": confidence = token.Number ?? 0; await reader.SkipAsync(token, ct); break;
                default: await reader.SkipAsync(token, ct); break;
            }
        }
        if (bounds is not { } box) return null;
        return new(text, box.Left, box.Top, box.Width, box.Height,
            vertical ?? WritingDirectionDetector.IsLikelyVertical(text, box.Width, box.Height), confidence);
    }

    private async Task<(double Left, double Top, double Width, double Height)?> ReadBoxAsync(OcrJsonTokenReader reader, Token token, CancellationToken ct)
    {
        if (token.Kind != JsonTokenType.StartArray) { await reader.SkipAsync(token, ct); return null; }
        var points = 0;
        var valid = 0;
        double left = double.PositiveInfinity, top = double.PositiveInfinity, right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        while ((token = await reader.RequireAsync(ct)).Kind != JsonTokenType.EndArray)
        {
            if (++points > limits.MaximumBoundingBoxPoints) throw new InvalidDataException("OCR JSONの座標点数が上限を超えています。");
            if (token.Kind != JsonTokenType.StartArray) { await reader.SkipAsync(token, ct); continue; }
            var coordinates = 0;
            double? x = null, y = null;
            while ((token = await reader.RequireAsync(ct)).Kind != JsonTokenType.EndArray)
            {
                if (++coordinates > limits.MaximumCoordinateElements) throw new InvalidDataException("OCR JSONの座標要素数が上限を超えています。");
                if (coordinates == 1) x = token.Number;
                else if (coordinates == 2) y = token.Number;
                await reader.SkipAsync(token, ct);
            }
            if (x is not { } px || y is not { } py) continue;
            left = Math.Min(left, px); right = Math.Max(right, px); top = Math.Min(top, py); bottom = Math.Max(bottom, py);
            valid++;
        }
        if (valid < 2) return null;
        if (!double.IsFinite(right - left) || !double.IsFinite(bottom - top)) throw new InvalidDataException("OCR JSONの座標が範囲外です。");
        return (left, top, right - left, bottom - top);
    }
}
