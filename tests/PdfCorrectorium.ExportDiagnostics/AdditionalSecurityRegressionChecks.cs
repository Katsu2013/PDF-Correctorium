using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfCorrectorium.App.Services;

internal static class AdditionalSecurityRegressionChecks
{
    internal static async Task RunAsync(string root, Action<bool, string> check)
    {
        async Task<NdlOcrDocument> Import(string name, string json, NdlOcrCompanionService? service = null, CancellationToken ct = default)
        {
            var path = Path.Combine(root, "json-" + name + ".json");
            await File.WriteAllTextAsync(path, json);
            return await (service ?? new()).ImportAsync(path, ct);
        }
        async Task Reject(string name, string json, NdlOcrCompanionService service)
        {
            var rejected = false;
            try { await Import(name, json, service); } catch (System.IO.InvalidDataException) { rejected = true; }
            check(rejected, "OCR JSON rejects " + name + " during streaming parsing.");
        }
        const string line = "{\"text\":\"abc\",\"boundingBox\":[[2,3],[22,13]],\"confidence\":0.9,\"isVertical\":true}";
        const string contents = "\"contents\":[[" + line + "]]";
        var regular = await Import("regular", "{\"pages\":[{\"img_width\":100,\"img_height\":200}]," + contents + "}");
        check(regular.Pages[1].Lines[0] is { Text: "abc", X: 2, Y: 3, Width: 20, Height: 10, IsVertical: true, Confidence: 0.9 }, "JSON preserves text, geometry, confidence and direction.");
        check(regular.Pages[1] is { ImageWidth: 100, ImageHeight: 200 }, "JSON retains explicit page dimensions.");
        var after = await Import("metadata-after", "{" + contents + ",\"pages\":[{\"img_width\":100,\"img_height\":200}]}");
        check(after.Pages[1] is { ImageWidth: 100, ImageHeight: 200 }, "JSON metadata may follow contents.");
        var fallback = await Import("imginfo", "{" + contents + ",\"imginfo\":{\"img_width\":50,\"img_height\":60}}");
        check(fallback.Pages[1] is { ImageWidth: 50, ImageHeight: 60 }, "JSON imginfo dimensions remain supported.");
        var inferred = await Import("inferred", "{" + contents + "}");
        check(inferred.Pages[1] is { ImageWidth: 22, ImageHeight: 13 }, "JSON missing dimensions are inferred from line bounds.");
        var unknown = await Import("unknown", "{\"unknown\":{\"a\":[1,{},null]}," + contents + "}");
        check(unknown.Pages[1].Lines.Count == 1, "Unknown JSON structures are skipped without retaining a DOM.");
        var exact = await Import("exact-text", "{" + contents + "}", new() { MaximumLineTextCharacters = 3, MaximumTotalTextCharacters = 3 });
        check(exact.Pages[1].Lines[0].Text == "abc", "Exact JSON line and total-text limits are accepted.");
        await Reject("line-text-limit", "{" + contents + "}", new() { MaximumLineTextCharacters = 2 });
        await Reject("total-text-limit", "{\"contents\":[[" + line + "," + line + "]]}", new() { MaximumTotalTextCharacters = 5 });
        await Reject("invalid-line-text-limit", "{\"contents\":[[{\"text\":\"123456789012\"}]]}", new() { MaximumTotalTextCharacters = 8 });
        await Reject("invalid-line-count", "{\"contents\":[[{},null,{}]]}", new() { MaximumLineCount = 2 });
        await Reject("page-count", "{\"contents\":[[],[]]}", new() { MaximumPageCount = 1 });
        await Reject("page-metadata-count", "{\"pages\":[{},{}],\"contents\":[]}", new() { MaximumPageCount = 1 });
        await Reject("point-count", "{\"contents\":[[{\"boundingBox\":[[0,0],[1,1],[2,2]]}]]}", new() { MaximumBoundingBoxPoints = 2 });
        await Reject("coordinate-count", "{\"contents\":[[{\"boundingBox\":[[0,0,0],[1,1]]}]]}", new() { MaximumCoordinateElements = 2 });
        await Reject("depth-limit", "{\"contents\":[[]]}", new() { MaximumJsonDepth = 2 });
        await Reject("token-count", "{\"unused\":[" + string.Join(',', Enumerable.Repeat("{}", 100)) + "],\"contents\":[]}", new() { MaximumJsonTokenCount = 20 });
        await Reject("scalar-bytes", "{\"unused\":\"" + new string('x', 200) + "\",\"contents\":[]}", new() { MaximumJsonTokenBytes = 32 });
        await Reject("byte-limit", "{" + contents + "}", new() { MaximumImportBytes = 32 });
        await Reject("duplicate-contents", "{\"contents\":[],\"contents\":[]}", new());
        await Reject("duplicate-line-text", "{\"contents\":[[{\"text\":\"x\",\"text\":\"y\"}]]}", new());
        await Reject("coordinate-overflow", "{\"contents\":[[{\"boundingBox\":[[-1e308,0],[1e308,1]]}]]}", new());
        var invalid = await Import("invalid-coordinates", "{\"contents\":[[{\"boundingBox\":[[1e400,0],[1,1]]}]]}");
        check(invalid.Pages[1].Lines.Count == 0, "Non-finite JSON coordinates do not create an OCR line.");
        foreach (var length in new[] { 8180, 8191, 16382, 20000 })
        {
            var text = new string('あ', length);
            var json = "{\"contents\":[[{\"text\":" + JsonSerializer.Serialize(text) + ",\"boundingBox\":[[0,0],[1,1]]}]]}";
            var imported = await Import("buffer-" + length, json);
            check(imported.Pages[1].Lines[0].Text == text, "JSON escaped Unicode survives token buffer growth at " + length + " characters.");
        }
        var numericDirection = await Import("numeric-direction", "{" + contents.Replace("true", "1") + "}");
        check(numericDirection.Pages[1].Lines[0].IsVertical, "Legacy integer vertical flags remain supported.");
        var fractionalDirection = await Import("fractional-direction", "{" + contents.Replace("true", "0.5") + "}");
        check(!fractionalDirection.Pages[1].Lines[0].IsVertical, "Fractional flags preserve the previous non-integer boolean interpretation.");
        var bomPath = Path.Combine(root, "json-bom.json");
        await File.WriteAllTextAsync(bomPath, "{" + contents + "}", new UTF8Encoding(true));
        check((await new NdlOcrCompanionService().ImportAsync(bomPath)).Pages[1].Lines.Count == 1, "JSON UTF-8 BOM is accepted.");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel(); var stopped = false;
            try { await Import("cancelled", "{" + contents + "}", ct: cancellation.Token); } catch (OperationCanceledException) { stopped = true; }
            check(stopped, "JSON cancellation produces no partial OCR document.");
        }
        var malformed = await Import("malformed", "{" + contents);
        check(malformed.Pages.Count == 0, "Malformed JSON retains the existing metadata-only fallback without partial lines.");
        var dense = "{\"unused\":[" + string.Join(',', Enumerable.Repeat("{}", 100000)) + "]," + contents + "}";
        var before = GC.GetTotalAllocatedBytes(true);
        var scanned = await Import("dense", dense);
        await File.WriteAllTextAsync(Path.Combine(root, "json-allocation.txt"), $"Input bytes={Encoding.UTF8.GetByteCount(dense)}; cumulative allocations including fixture IO={GC.GetTotalAllocatedBytes(true)-before}\n");
        check(scanned.Pages[1].Lines.Count == 1, "Dense irrelevant JSON is scanned incrementally without retained arrays.");

        var thumbnail = ThumbnailDecoder.Decode(Image(2048, 2048));
        check(thumbnail is { PixelWidth: 220, PixelHeight: 220 } && thumbnail.IsFrozen && thumbnail.Format == PixelFormats.Bgra32, "The audited 2048-square PNG thumbnail is decoded to 220 pixels and frozen.");
        var small = ThumbnailDecoder.Decode(Image(20, 30, jpeg: true));
        check(small is { PixelWidth: 20, PixelHeight: 30 }, "Small legitimate JPEG thumbnails are not enlarged.");
        var tall = ThumbnailDecoder.Decode(Image(100, 4096));
        check(tall is not null && tall.PixelHeight == 220 && tall.PixelWidth <= 220, "Portrait thumbnails are bounded by their long edge.");
        check(ThumbnailDecoder.Decode(Image(4097, 1)) is null, "Thumbnail header dimensions above the limit are rejected.");
        check(ThumbnailDecoder.Decode(Image(2049, 2048)) is null, "Thumbnail pixel counts above the limit are rejected.");
        check(ThumbnailDecoder.Decode([1, 2, 3]) is null && ThumbnailDecoder.Decode([]) is null, "Corrupt and empty thumbnail caches fall back to regeneration.");
    }

    private static byte[] Image(int width, int height, bool jpeg = false)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, new byte[checked(width * height * 4)], width * 4);
        BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder() : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
    }
}
