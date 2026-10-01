using System.IO;
using System.Text.Json.Nodes;

namespace PdfCorrectorium.App.Services;

/// <summary>リンク注釈の間接参照と名前付き移動先を、再取得を避けて解決します。</summary>
internal sealed class PdfLinkObjectReader(string qpdf, string pdfPath, CancellationToken cancellationToken)
{
    private readonly Dictionary<string, JsonNode?> _objects = [];
    private readonly Dictionary<string, JsonNode?> _destinations = new(StringComparer.Ordinal);
    private bool _namesLoaded;

    internal async Task<JsonNode?> ResolveAsync(JsonNode? node)
    {
        var visited = new HashSet<string>();
        for (var depth = 0; depth < 32 && PdfSourceLinkService.TryReference(node, out var reference); depth++)
        {
            if (!visited.Add(reference)) return null;
            if (!_objects.TryGetValue(reference, out node))
            {
                var values = await ReadAsync([reference]);
                node = values["obj:" + reference]?["value"]?.DeepClone();
                _objects[reference] = node;
            }
        }
        return PdfSourceLinkService.TryReference(node, out _) ? null : node;
    }

    internal async Task<JsonNode?> ReadInheritedAsync(JsonNode? page, string key)
    {
        var visited = new HashSet<string>();
        for (var depth = 0; depth < 32 && page is JsonObject value; depth++)
        {
            if (value[key] is { } property) return await ResolveAsync(property);
            if (PdfSourceLinkService.TryReference(value["/Parent"], out var reference) && !visited.Add(reference)) return null;
            page = await ResolveAsync(value["/Parent"]);
        }
        return null;
    }

    internal void Seed(JsonObject values)
    {
        foreach (var pair in values)
            if (pair.Key.StartsWith("obj:", StringComparison.Ordinal))
                _objects[pair.Key[4..]] = pair.Value?["value"]?.DeepClone();
    }

    internal async Task<JsonNode?> ResolveDestinationAsync(JsonNode? node)
    {
        var names = new HashSet<string>();
        for (var depth = 0; depth < 32; depth++)
        {
            node = await ResolveAsync(node);
            if (node is JsonArray) return node;
            if (node is JsonObject dictionary) { node = dictionary["/D"]; continue; }
            var name = ReadName(node);
            if (name is null || !names.Add(name)) return null;
            await LoadNamesAsync();
            if (!_destinations.TryGetValue(name, out node)) return null;
        }
        return null;
    }

    private async Task LoadNamesAsync()
    {
        if (_namesLoaded) return;
        _namesLoaded = true;
        var trailer = (await ReadAsync(["trailer"]))["trailer"]?["value"];
        var catalog = await ResolveAsync(trailer?["/Root"]);
        var oldNames = await ResolveAsync(catalog?["/Dests"]);
        if (oldNames is JsonObject dictionary)
            foreach (var entry in dictionary) _destinations[entry.Key.TrimStart('/')] = entry.Value?.DeepClone();
        var nameDictionary = await ResolveAsync(catalog?["/Names"]);
        var queue = new Queue<JsonNode?>();
        queue.Enqueue(nameDictionary?["/Dests"]);
        var visited = new HashSet<string>();
        var nodes = 0;
        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++nodes > 8192) throw new InvalidDataException("名前付きリンクの移動先ツリーが大きすぎます。");
            var node = queue.Dequeue();
            if (PdfSourceLinkService.TryReference(node, out var reference) && !visited.Add(reference)) continue;
            node = await ResolveAsync(node);
            if (node is not JsonObject branch) continue;
            if (await ResolveAsync(branch["/Names"]) is JsonArray pairs)
                for (var i = 0; i + 1 < pairs.Count; i += 2)
                {
                    var name = ReadName(pairs[i]);
                    if (name is not null) _destinations[name] = pairs[i + 1]?.DeepClone();
                }
            if (_destinations.Count > 100000) throw new InvalidDataException("名前付きリンクの移動先が多すぎます。");
            if (await ResolveAsync(branch["/Kids"]) is JsonArray kids)
                foreach (var kid in kids) queue.Enqueue(kid);
        }
    }

    private async Task<JsonObject> ReadAsync(IEnumerable<string> references)
    {
        var arguments = new List<string> { "--json-output=2" };
        foreach (var reference in references)
        {
            if (reference == "trailer") arguments.Add("--json-object=trailer");
            else
            {
                var parts = reference.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                arguments.Add($"--json-object={parts[0]},{parts[1]}");
            }
        }
        arguments.Add(pdfPath); arguments.Add("-");
        var result = await ExternalProcessRunner.RunAsync(qpdf, arguments, TimeSpan.FromMinutes(2),
            cancellationToken, 32 * 1024 * 1024);
        if (result.ExitCode != 0) throw new InvalidDataException("PDFリンクの参照を解決できませんでした: " + result.StandardError);
        return JsonNode.Parse(result.StandardOutput)!["qpdf"]![1]!.AsObject();
    }

    private static string? ReadName(JsonNode? node)
    {
        var text = PdfSourceLinkService.ReadString(node);
        return text is null ? null : text.StartsWith('/') ? text[1..] : PdfSourceLinkService.DecodeString(text);
    }
}
