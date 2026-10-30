using System.Globalization;
using System.Text;
using System.Text.Json;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.ProjectFormat;

internal static class SecurityRegressionChecks
{
    internal static async Task RunAsync(string outputDirectory)
    {
        var root = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(root)) throw new IOException("Use a new security-test output directory.");
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add("PASS: " + message);
        }
        async Task<NdlOcrDocument> Import(string name, string xml, NdlOcrCompanionService? service = null,
            CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(root, name + ".xml");
            await File.WriteAllTextAsync(path, xml);
            return await (service ?? new()).ImportAsync(path, cancellationToken);
        }
        async Task Reject(string name, string xml, NdlOcrCompanionService service)
        {
            var rejected = false;
            try { await Import(name, xml, service); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, name + " is rejected during bounded XML parsing.");
        }
        const string line = "<LINE X=\"2\" Y=\"3\" WIDTH=\"20\" HEIGHT=\"10\" STRING=\"abc\" IS_VERTICAL=\"true\" CONF=\"0.9\"/>";
        const string page = "<PAGE WIDTH=\"100\" HEIGHT=\"200\">" + line + "</PAGE>";
        var regular = await Import("regular", "<?xml version=\"1.0\"?><OCR>" + page + "</OCR>");
        Check(regular.Pages.Count == 1 && regular.Pages[1].Lines[0].Text == "abc", "Declared XML retains OCR text.");
        Check(regular.Pages[1].ImageWidth == 100 && regular.Pages[1].ImageHeight == 200, "Page dimensions are retained.");
        Check(regular.Pages[1].Lines[0] is { IsVertical: true, X: 2, Y: 3, Confidence: 0.9 }, "Geometry, confidence and explicit vertical metadata are retained.");
        var unicodePath = Path.Combine(root, "unicode.xml");
        await File.WriteAllTextAsync(unicodePath, "<?xml version=\"1.0\" encoding=\"utf-16\"?>" + page, Encoding.Unicode);
        Check((await new NdlOcrCompanionService().ImportAsync(unicodePath)).Pages.Count == 1, "UTF-16 BOM input remains readable.");
        var fragments = await Import("fragments", page + page);
        Check(fragments.Pages.Count == 2, "Rootless page fragments remain readable.");
        var declarations = await Import("declarations", "<?xml version=\"1.0\"?>" + page + "<?xml version=\"1.0\"?>" + page);
        Check(declarations.Pages.Count == 2, "Repeated XML declarations remain readable without whole-document copies.");
        var empty = await Import("empty-page", "<PAGE/>" + page);
        Check(empty.Pages.Count == 2 && empty.Pages[1].Lines.Count == 0 && empty.Pages[1].ImageWidth == 1, "Empty pages preserve sequential numbering.");
        var inner = await Import("inner-text", "<PAGE><LINE X=\"0\" Y=\"0\" WIDTH=\"1\" HEIGHT=\"1\">a<b>b</b><![CDATA[c]]>d</LINE></PAGE>");
        Check(inner.Pages[1].Lines[0].Text == "abcd", "Nested text and CDATA are combined as before.");
        var namespaceXml = await Import("namespace", "<n:PAGE xmlns:n=\"urn:ocr\" WIDTH=\"10\" HEIGHT=\"10\"><n:LINE X=\"0\" Y=\"0\" WIDTH=\"1\" HEIGHT=\"1\" STRING=\"x\" n:vertical=\"1\"/></n:PAGE>");
        Check(namespaceXml.Pages[1].Lines[0].IsVertical, "Namespaced page/line elements and vertical flags remain readable.");
        const string marker = "<?xml version=\"1.0\"?>";
        var cdata = await Import("cdata-declaration", "<PAGE><LINE X=\"0\" Y=\"0\" WIDTH=\"1\" HEIGHT=\"1\"><![CDATA[" + marker + "]]></LINE></PAGE>");
        Check(cdata.Pages[1].Lines[0].Text == marker, "Declaration-like OCR text inside CDATA is preserved.");
        foreach (var padding in new[] { 8185, 8188, 8191, 16382 })
        {
            var boundary = await Import("boundary-" + padding, new string(' ', padding) + marker + page + "<!--" + marker + "-->" + marker + page);
            Check(boundary.Pages.Count == 2, "XML declaration filtering handles a buffer boundary at " + padding + ".");
        }
        var largeText = new string('x', 20000);
        var chunked = await Import("chunked-text", "<PAGE><LINE WIDTH=\"1\" HEIGHT=\"1\">" + largeText + "</LINE></PAGE>");
        Check(chunked.Pages[1].Lines[0].Text == largeText, "Long valid text survives multiple value chunks.");
        await Reject("page-limit", page + page, new() { MaximumPageCount = 1 });
        await Reject("line-limit", "<PAGE>" + line + line + "</PAGE>", new() { MaximumLineCount = 1 });
        await Reject("attribute-text-limit", page, new() { MaximumLineTextCharacters = 2 });
        Check((await Import("exact-text-limit", page, new() { MaximumLineTextCharacters = 3, MaximumTotalTextCharacters = 3 })).Pages[1].Lines[0].Text == "abc",
            "Exact line and cumulative text limits remain readable.");
        await Reject("inner-text-limit", "<PAGE><LINE WIDTH=\"1\" HEIGHT=\"1\">" + largeText + "</LINE></PAGE>", new() { MaximumLineTextCharacters = 10 });
        await Reject("total-text-limit", page + page, new() { MaximumTotalTextCharacters = 5 });
        await Reject("node-limit", "<OCR>" + string.Concat(Enumerable.Repeat("<UNUSED/>", 50)) + page + "</OCR>", new() { MaximumXmlNodeCount = 20 });
        await Reject("depth-limit", "<a><b><c><d>" + page + "</d></c></b></a>", new() { MaximumXmlDepth = 2 });
        await Reject("xml-byte-limit", page, new() { MaximumXmlImportBytes = 32 });
        await Reject("invalid-line-count-limit", "<PAGE><LINE/><LINE/>" + line + "</PAGE>", new() { MaximumLineCount = 1 });
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var cancelled = false;
            try { await Import("cancelled", page, cancellationToken: cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "Cancelled imports stop without returning partial OCR.");
        }
        var secretPath = Path.Combine(root, "secret.txt");
        await File.WriteAllTextAsync(secretPath, "external-entity-secret");
        var entity = await Import("entity", "<!DOCTYPE OCR [<!ENTITY secret SYSTEM \"" + new Uri(secretPath).AbsoluteUri + "\">]><OCR><PAGE><LINE WIDTH=\"1\" HEIGHT=\"1\">&secret;</LINE></PAGE></OCR>");
        Check(entity.Pages.Count == 0, "DTD and external entity input does not produce OCR or expose a file.");
        var denseXml = "<OCR>" + string.Concat(Enumerable.Repeat("<UNUSED a=\"1\"/>", 100000)) + page + "</OCR>";
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var dense = await Import("dense", denseXml);
        var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
        Check(dense.Pages.Count == 1 && dense.Pages[1].Lines.Count == 1, "Dense irrelevant XML is scanned without retaining a DOM.");
        await File.WriteAllTextAsync(Path.Combine(root, "allocation.txt"), $"Dense input bytes: {Encoding.UTF8.GetByteCount(denseXml)}; cumulative allocated bytes including fixture IO: {allocated}\n");

        Check(PdfObjectReference.IsValid("1 0 R") && PdfObjectReference.IsValid("42 65535 R"), "Valid object references remain accepted.");
        foreach (var invalid in new[] { "x 0 R", "0 0 R", "-1 0 R", "+1 0 R", "1 -1 R", "1 65536 R", "2147483648 0 R", "1 0 R extra", "u:1 0 R" })
            Check(!PdfObjectReference.IsValid(invalid), "Invalid object reference is rejected: " + invalid);
        var savedOverride = Environment.GetEnvironmentVariable("PDFOCR_QPDF_PATH");
        try
        {
            var fixtureRoot = Path.Combine(root, "resolver");
            Directory.CreateDirectory(fixtureRoot);
            var overridePath = Path.Combine(root, "override.exe");
            await File.WriteAllTextAsync(overridePath, "not executed");
            Environment.SetEnvironmentVariable("PDFOCR_QPDF_PATH", overridePath);
            Check(QpdfExecutableResolver.Resolve(fixtureRoot) is null, "Release resolver ignores an environment-only executable.");
            var bundledPath = Path.Combine(fixtureRoot, "components", "qpdf", "qpdf.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(bundledPath)!);
            await File.WriteAllTextAsync(bundledPath, "not executed");
            Check(QpdfExecutableResolver.Resolve(fixtureRoot) == bundledPath, "Bundled qpdf wins over an existing environment override.");
        }
        finally { Environment.SetEnvironmentVariable("PDFOCR_QPDF_PATH", savedOverride); }

        var sourcePath = Path.Combine(root, "--input.pdf");
        WriteBlankPdf(sourcePath);
        var originalDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = root;
            var service = new PdfPageManagementService();
            await service.MaterializeAsync("--input.pdf", [new() { SourcePageNumber = 1 }], "--materialized.pdf");
            Check(File.Exists(Path.Combine(root, "--materialized.pdf")), "Materialize accepts hyphen-prefixed relative input and output paths.");
            await service.RotateAsync("--input.pdf", [1], 90, "--rotated.pdf");
            Check(File.Exists(Path.Combine(root, "--rotated.pdf")), "Rotate accepts hyphen-prefixed relative paths.");
            await service.ComposeAsync("--input.pdf", [new("--input.pdf", "1")], "--composed.pdf");
            Check(File.Exists(Path.Combine(root, "--composed.pdf")), "Compose accepts hyphen-prefixed relative paths.");
            var features = await PdfInputStructureService.ReadAsync("--input.pdf", CancellationToken.None);
            Check(!features.HasJavaScript && !features.HasForm, "Input inspection handles a normalized filename and valid object references.");
        }
        finally { Environment.CurrentDirectory = originalDirectory; }
        var packages = new ProjectPackageService();
        var packagePath = Path.Combine(root, "version.pdfocrproj");
        await packages.SaveAsync(packagePath, new PdfCorrectoriumProject { SourcePdf = await packages.CreateSourceReferenceAsync(sourcePath, root) });
        using (var archive = System.IO.Compression.ZipFile.OpenRead(packagePath))
        using (var document = await JsonDocument.ParseAsync(archive.GetEntry("manifest.json")!.Open()))
            Check(document.RootElement.GetProperty("applicationVersion").GetString() == ApplicationBuildInfo.Version,
                "Project manifest applicationVersion matches the current assembly.");
        Check(ApplicationBuildInfo.WindowTitle.Contains(ApplicationBuildInfo.Version) &&
              ApplicationBuildInfo.AboutText.Contains(ApplicationBuildInfo.InformationalVersion) &&
              ApplicationBuildInfo.AboutText.Contains(ApplicationBuildInfo.NumericVersion), "Title and About text use the same product and numeric versions.");
        await AdditionalSecurityRegressionChecks.RunAsync(root, Check);
        await FollowupSecurityRegressionChecks.RunAsync(root, Check);
        await File.WriteAllLinesAsync(Path.Combine(root, "checks.txt"), checks);
        Console.WriteLine($"Security regression checks passed: {checks.Count}. {root}");
    }

    private static void WriteBlankPdf(string path)
    {
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> >>" };
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(builder.Length);
            builder.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = builder.Length;
        builder.Append("xref\n0 4\n0000000000 65535 f \n");
        foreach (var offset in offsets) builder.Append(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        builder.Append($"trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, builder.ToString(), Encoding.ASCII);
    }
}
