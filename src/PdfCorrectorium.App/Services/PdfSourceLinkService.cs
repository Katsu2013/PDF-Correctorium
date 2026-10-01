using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;

namespace PdfCorrectorium.App.Services;

/// <summary>元PDFの安全なLink注釈だけを、物理ページからプロジェクトの不変ページIDへ対応付けます。</summary>
internal static class PdfSourceLinkService
{
    internal sealed record PageCoordinates(int Rotation, PdfPoint Origin);
    internal sealed record ScanResult(IReadOnlyList<PdfInternalLink> Links, int UnsupportedActions,
        IReadOnlyDictionary<int, PageCoordinates> PageCoordinates);

    internal static async Task<ScanResult> ReadAsync(
        string pdfPath, IReadOnlyList<ProjectPageReference> sequence, CancellationToken cancellationToken)
    {
        var qpdf = PdfBookmarkService.ResolveQpdfPath() ??
            throw new FileNotFoundException("リンクの確認に必要なqpdf.exeが見つかりません。");
        var pagesJson = JsonNode.Parse(await RunAsync(qpdf,
            ["--json=2", "--json-key=pages", pdfPath, "-"], cancellationToken))!;
        var pageReferences = pagesJson["pages"]!.AsArray()
            .ToDictionary(page => page!["pageposfrom1"]!.GetValue<int>(),
                page => page!["object"]!.GetValue<string>());
        var physicalByReference = pageReferences.ToDictionary(pair => pair.Value, pair => pair.Key);
        var logicalByPhysical = sequence.Select((page, index) => (page, index))
            .GroupBy(pair => pair.page.SourcePageNumber)
            .ToDictionary(group => group.Key, group => group.First().page.PageId);
        var pageValues = new JsonObject();
        var pageAnnotations = new Dictionary<int, List<JsonNode>>();
        var indirectArrays = new Dictionary<int, string>();
        foreach (var chunk in pageReferences.Chunk(64))
        {
            var values = await ReadObjectsAsync(qpdf, pdfPath, chunk.Select(pair => pair.Value), cancellationToken);
            foreach (var entry in values) pageValues[entry.Key] = entry.Value?.DeepClone();
            foreach (var pair in chunk)
            {
                if (values["obj:" + pair.Value]?["value"]?["/Annots"] is not { } annotations) continue;
                if (annotations is JsonArray array)
                    pageAnnotations[pair.Key] = array.Select(item => item!.DeepClone()).ToList();
                else if (TryReference(annotations, out var reference))
                    indirectArrays[pair.Key] = reference;
            }
        }
        foreach (var chunk in indirectArrays.Chunk(64))
        {
            var values = await ReadObjectsAsync(qpdf, pdfPath, chunk.Select(pair => pair.Value), cancellationToken);
            foreach (var pair in chunk)
                if (values["obj:" + pair.Value]?["value"] is JsonArray array)
                    pageAnnotations[pair.Key] = array.Select(item => item!.DeepClone()).ToList();
        }

        var annotationReferences = pageAnnotations.Values.SelectMany(items => items)
            .Select(item => TryReference(item, out var reference) ? reference : null)
            .Where(reference => reference is not null).Cast<string>().Distinct().ToArray();
        var annotationValues = new JsonObject();
        foreach (var chunk in annotationReferences.Chunk(64))
        {
            var values = await ReadObjectsAsync(qpdf, pdfPath, chunk, cancellationToken);
            foreach (var pair in values) annotationValues[pair.Key] = pair.Value?.DeepClone();
        }

        var reader = new PdfLinkObjectReader(qpdf, pdfPath, cancellationToken);
        reader.Seed(annotationValues);
        reader.Seed(pageValues);
        var coordinates = new Dictionary<int, PageCoordinates>();
        foreach (var pair in pageReferences)
        {
            var page = pageValues["obj:" + pair.Value]?["value"];
            var rotationNode = await reader.ReadInheritedAsync(page, "/Rotate");
            var rotation = TryNumber(rotationNode, out var degrees) && degrees % 90 == 0
                ? ProjectPageSequence.NormalizeRotation((int)degrees) : 0;
            var media = await reader.ReadInheritedAsync(page, "/MediaBox") as JsonArray;
            var crop = await reader.ReadInheritedAsync(page, "/CropBox") as JsonArray ?? media;
            var originX = crop is { Count: 4 } && TryNumber(crop[0], out var x) ? x : 0;
            var originY = crop is { Count: 4 } && TryNumber(crop[1], out var y) ? y : 0;
            if (media is { Count: 4 })
            {
                if (TryNumber(media[0], out var mediaX)) originX = Math.Max(originX, mediaX);
                if (TryNumber(media[1], out var mediaY)) originY = Math.Max(originY, mediaY);
            }
            coordinates[pair.Key] = new PageCoordinates(rotation, new PdfPoint(originX, originY));
        }
        var links = new List<PdfInternalLink>();
        var unsupported = 0;
        foreach (var (sourcePhysical, annotations) in pageAnnotations)
        {
            if (!logicalByPhysical.TryGetValue(sourcePhysical, out var sourceId)) continue;
            for (var annotationIndex = 0; annotationIndex < annotations.Count; annotationIndex++)
            {
                var annotationNode = annotations[annotationIndex];
                var reference = TryReference(annotationNode, out var objectReference) ? objectReference : null;
                var annotation = reference is null ? annotationNode : annotationValues["obj:" + reference]?["value"];
                if (annotation is not JsonObject value || ReadString(value["/Subtype"]) != "/Link") continue;
                if (value["/Rect"] is not JsonArray rect || rect.Count != 4 ||
                    !TryNumber(rect[0], out var left) || !TryNumber(rect[1], out var bottom) ||
                    !TryNumber(rect[2], out var right) || !TryNumber(rect[3], out var top) ||
                    right <= left || top <= bottom) continue;

                var destination = value["/Dest"];
                var action = await reader.ResolveAsync(value["/A"]);
                string? uri = null;
                string? unsupportedReason = value["/AA"] is not null ? "追加アクション付きリンク" : null;
                if (action is JsonObject actionValue)
                {
                    var kind = ReadString(actionValue["/S"]);
                    if (actionValue["/Next"] is not null) unsupportedReason = "連続アクション付きリンク";
                    if (kind == "/GoTo") destination = actionValue["/D"];
                    else if (kind == "/URI")
                    {
                        var candidate = DecodeString(ReadString(await reader.ResolveAsync(actionValue["/URI"])));
                        if (Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) &&
                            parsed.Scheme is "http" or "https") uri = parsed.AbsoluteUri;
                        else unsupportedReason ??= "HTTP(S)以外のアドレス";
                    }
                    else unsupportedReason ??= kind ?? "不明なアクション";
                }
                else if (value["/A"] is not null) unsupportedReason ??= "解決できないアクション";

                var destinationId = Guid.Empty;
                double? zoom = null;
                PdfPoint? destinationPosition = null;
                string? viewType = null;
                double?[] parameters = [];
                if (uri is null && unsupportedReason is null)
                {
                    var target = await reader.ResolveDestinationAsync(destination) as JsonArray;
                    if (target is null || target.Count < 2 ||
                        !TryReference(target[0], out var targetReference) ||
                        !physicalByReference.TryGetValue(targetReference, out var targetPhysical) ||
                        !logicalByPhysical.TryGetValue(targetPhysical, out destinationId))
                        unsupportedReason = "移動先ページを解決できないリンク";
                    else
                    {
                        viewType = ReadString(target[1]);
                        if (viewType is not ("/XYZ" or "/Fit" or "/FitH" or "/FitV" or "/FitR" or "/FitB" or "/FitBH" or "/FitBV") ||
                            target.Count > 6 || target.Skip(2).Any(item => item is not null && !TryNumber(item, out _)))
                        { unsupportedReason = "未対応の移動先表示方法"; viewType = null; }
                        else
                        {
                            parameters = target.Skip(2).Select(item => TryNumber(item, out var number) ? (double?)number : null).ToArray();
                            if (viewType == "/XYZ")
                            {
                                if (parameters.Length >= 2 && parameters[0] is { } x && parameters[1] is { } y)
                                    destinationPosition = new PdfPoint(x, y);
                                if (parameters.Length >= 3 && parameters[2] is { } factor && factor * 100 is >= 25 and <= 400)
                                    zoom = factor * 100;
                            }
                        }
                    }
                }
                if (unsupportedReason is not null) unsupported++;
                var sourceBounds = new PdfRectangle(new PdfPoint(left, bottom), new PdfSize(right - left, top - bottom));
                var marker = DecodeString(ReadString(value["/NM"]));
                var markerParts = marker.Split('-');
                var groupedId = marker.StartsWith("pdfcorrectorium-link-", StringComparison.Ordinal) &&
                    markerParts.Length >= 3 && Guid.TryParseExact(markerParts[2], "N", out var parsedId) ? parsedId : Guid.NewGuid();
                links.Add(new PdfInternalLink
                {
                    Id = groupedId,
                    SourcePageId = sourceId,
                    SourceBounds = new PdfRectangle(new PdfPoint(left, bottom), new PdfSize(right - left, top - bottom)),
                    DestinationPageId = destinationId,
                    ExternalUri = uri,
                    DestinationZoomPercent = zoom,
                    DestinationPosition = destinationPosition,
                    DestinationViewType = viewType,
                    DestinationViewParameters = parameters,
                    UnsupportedAction = unsupportedReason,
                    SourceAnnotations = [new PdfSourceLinkAnnotation { Reference = reference ?? $"direct:{annotationIndex}", Bounds = sourceBounds, Name = marker.Length is > 0 and <= 256 ? marker : null }],
                    Description = DecodeString(ReadString(value["/Contents"])),
                    SourceAnnotationReference = reference ?? $"direct:{annotationIndex}",
                    OriginalSourceBounds = new PdfRectangle(new PdfPoint(left, bottom), new PdfSize(right - left, top - bottom)),
                });
            }
        }
        // Our multiline output uses separate standard annotations, but remains one editable link.
        var grouped = links.GroupBy(link => (link.SourcePageId, link.Id, link.ExternalUri, link.DestinationPageId))
            .Select(group =>
            {
                var first = group.First();
                var areas = group.Select(link => link.SourceBounds!.Value).ToArray();
                var union = new PdfRectangle(
                    new PdfPoint(areas.Min(area => area.Left), areas.Min(area => area.Bottom)),
                    new PdfSize(areas.Max(area => area.Right) - areas.Min(area => area.Left),
                        areas.Max(area => area.Top) - areas.Min(area => area.Bottom)));
                return first with
                {
                    SourceBounds = union,
                    SourceAreas = areas.Length > 1 ? areas : [],
                    SourceAnnotations = group.SelectMany(link => link.SourceAnnotations).ToArray(),
                };
            }).ToArray();
        return new ScanResult(grouped, unsupported, coordinates);
    }

    private static async Task<JsonObject> ReadObjectsAsync(
        string qpdf, string pdfPath, IEnumerable<string> references, CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "--json-output=2" };
        foreach (var reference in references)
        {
            var parts = reference.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && parts[2] == "R") arguments.Add($"--json-object={parts[0]},{parts[1]}");
        }
        arguments.Add(pdfPath);
        arguments.Add("-");
        var root = JsonNode.Parse(await RunAsync(qpdf, arguments, cancellationToken))!;
        return root["qpdf"]![1]!.AsObject();
    }

    private static async Task<string> RunAsync(string qpdf, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await ExternalProcessRunner.RunAsync(qpdf, arguments,
            TimeSpan.FromMinutes(5), cancellationToken, 32 * 1024 * 1024);
        if (result.ExitCode != 0)
            throw new InvalidDataException("元PDFのリンクを確認できませんでした: " + result.StandardError);
        return result.StandardOutput;
    }

    internal static bool TryReference(JsonNode? node, out string reference)
    {
        reference = ReadString(node) ?? string.Empty;
        var parts = reference.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3 && parts[2] == "R" &&
            int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _);
    }

    internal static string? ReadString(JsonNode? node) => node is JsonValue value &&
        value.TryGetValue<string>(out var text) ? text : null;

    private static bool TryNumber(JsonNode? node, out double number)
    {
        number = 0;
        return node is JsonValue value && value.TryGetValue<double>(out number) && double.IsFinite(number);
    }

    internal static string DecodeString(string? value) => value switch
    {
        null => string.Empty,
        _ when value.StartsWith("u:", StringComparison.Ordinal) => value[2..],
        _ when value.StartsWith("b:", StringComparison.Ordinal) => DecodeBytes(value[2..]),
        _ => value,
    };

    private static string DecodeBytes(string hex)
    {
        try { return Encoding.Latin1.GetString(Convert.FromHexString(hex)); }
        catch (FormatException) { return string.Empty; }
    }
}
