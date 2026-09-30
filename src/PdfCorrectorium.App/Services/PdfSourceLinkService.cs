using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;

namespace PdfCorrectorium.App.Services;

/// <summary>元PDFの安全なLink注釈だけを、物理ページからプロジェクトの不変ページIDへ対応付けます。</summary>
internal static class PdfSourceLinkService
{
    internal sealed record ScanResult(IReadOnlyList<PdfInternalLink> Links, int UnsupportedActions);

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
        var pageAnnotations = new Dictionary<int, List<JsonNode>>();
        var indirectArrays = new Dictionary<int, string>();
        foreach (var chunk in pageReferences.Chunk(64))
        {
            var values = await ReadObjectsAsync(qpdf, pdfPath, chunk.Select(pair => pair.Value), cancellationToken);
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
                var action = value["/A"];
                string? uri = null;
                if (action is JsonObject actionValue)
                {
                    var kind = ReadString(actionValue["/S"]);
                    if (kind == "/GoTo") destination = actionValue["/D"];
                    else if (kind == "/URI")
                    {
                        var candidate = DecodeString(ReadString(actionValue["/URI"]));
                        if (Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) &&
                            parsed.Scheme is "http" or "https") uri = parsed.AbsoluteUri;
                        else { unsupported++; continue; }
                    }
                    else { unsupported++; continue; }
                }
                else if (action is not null) { unsupported++; continue; }

                var destinationId = Guid.Empty;
                double? zoom = null;
                if (uri is null)
                {
                    if (destination is not JsonArray target || target.Count == 0 ||
                        !TryReference(target[0], out var targetReference) ||
                        !physicalByReference.TryGetValue(targetReference, out var targetPhysical) ||
                        !logicalByPhysical.TryGetValue(targetPhysical, out destinationId))
                    { unsupported++; continue; }
                    if (target.Count >= 5 && ReadString(target[1]) == "/XYZ" &&
                        TryNumber(target[4], out var factor) && factor > 0) zoom = factor * 100d;
                }
                links.Add(new PdfInternalLink
                {
                    SourcePageId = sourceId,
                    SourceBounds = new PdfRectangle(new PdfPoint(left, bottom), new PdfSize(right - left, top - bottom)),
                    DestinationPageId = destinationId,
                    ExternalUri = uri,
                    DestinationZoomPercent = zoom,
                    Description = DecodeString(ReadString(value["/Contents"])),
                    SourceAnnotationReference = reference ?? $"direct:{annotationIndex}",
                    OriginalSourceBounds = new PdfRectangle(new PdfPoint(left, bottom), new PdfSize(right - left, top - bottom)),
                });
            }
        }
        return new ScanResult(links, unsupported);
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

    private static bool TryReference(JsonNode? node, out string reference)
    {
        reference = ReadString(node) ?? string.Empty;
        var parts = reference.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3 && parts[2] == "R" &&
            int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _);
    }

    private static string? ReadString(JsonNode? node) => node is JsonValue value &&
        value.TryGetValue<string>(out var text) ? text : null;

    private static bool TryNumber(JsonNode? node, out double number)
    {
        number = 0;
        return node is JsonValue value && value.TryGetValue<double>(out number) && double.IsFinite(number);
    }

    private static string DecodeString(string? value) => value switch
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
