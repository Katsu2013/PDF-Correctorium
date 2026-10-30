using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;
using PdfCorrectorium.Core.IO;
using PdfCorrectorium.Infrastructure;
using PdfCorrectorium.ProjectFormat;

internal static class FollowupSecurityRegressionChecks
{
    internal static async Task RunAsync(string root, Action<bool, string> check)
    {
        async Task JsonReject(string name, string json, JsonInputBudget budget)
        {
            var rejected = false;
            try { await budget.ValidateAsync(new MemoryStream(Encoding.UTF8.GetBytes(json))); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException) { rejected = true; }
            check(rejected, "JSON preflight rejects " + name);
        }
        await new JsonInputBudget { MaximumStringCharacters = 3 }.ValidateAsync(new MemoryStream("{\"text\":\"abc\"}"u8.ToArray()));
        check(true, "JSON preflight accepts exact string boundary and ordinary property names.");
        await JsonReject("oversized string", "{\"x\":\"abcd\"}", new() { MaximumStringCharacters = 3 });
        await JsonReject("ignored tokens", "{\"ignored\":[{},{},{}]}", new() { MaximumTokens = 5 });
        await JsonReject("ignored depth", "{\"x\":[[[]]]}", new() { MaximumDepth = 2 });
        await JsonReject("case-folded duplicates", "{\"x\":1,\"X\":2}", new());
        await JsonReject("array cardinality", "{\"x\":[0,0,0]}", new() { MaximumArrayItems = 2 });
        await JsonReject("object cardinality", "[{},{}]", new() { MaximumObjects = 1 });
        await JsonReject("truncated input", "{\"x\":[", new());
        await JsonReject("large encoded token", "{\"x\":\"" + new string('x', 100) + "\"}", new() { MaximumTokenBytes = 32 });
        var truncationSample = Encoding.UTF8.GetBytes("{\"text\":\"校正\",\"items\":[0,{},null]}");
        var truncationsRejected = true;
        for (var cut = 0; cut < truncationSample.Length; cut++)
        {
            try { await new JsonInputBudget().ValidateAsync(new MemoryStream(truncationSample[..cut])); truncationsRejected = false; }
            catch (Exception ex) when (ex is InvalidDataException or JsonException) { }
        }
        check(truncationsRejected, "Every truncation of a generated UTF-8 document rejects without a partial model.");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); var stopped = false;
            try { await new JsonInputBudget().ValidateAsync(new MemoryStream("{}"u8.ToArray()), cancelled.Token); }
            catch (OperationCanceledException) { stopped = true; }
            check(stopped, "JSON preflight observes cancellation before allocation.");
        }
        foreach (var name in new[] { @"\\audit.invalid\share\file.pdf", "../file.pdf", "C:foo.pdf", "NUL.pdf", "COM1", "LPT¹.txt", "bad/name", "bad\\name", "trailing. ", new string('x', 241) })
            check(!SafeFileName.IsValid(name) && SafeFileName.ForDialog(name) == "document", "Unsafe document filename is not a dialog path: " + name[..Math.Min(name.Length, 40)]);
        check(SafeFileName.IsValid("校正結果 01.pdf"), "Ordinary Japanese filenames remain supported.");
        var source = new SourcePdfReference { FileName = "input.pdf", RelativePath = "input.pdf", FileSize = 0, Sha256 = new string('0',64), PageCount = 1 };
        var pageId = Guid.NewGuid();
        var geometry = new TextGeometry { LocalBounds = new(new(1,1),new(20,10)), RotationCenter = new(1,1) };
        var region = new OcrTextRegion { PageId = pageId, OriginalText = "abcd", OriginalGeometry = geometry, EditedGeometry = geometry };
        var project = new PdfCorrectoriumProject { SourcePdf = source, Pages = [new OcrPage { Id=pageId, PageNumber=1, WidthPoints=100, HeightPoints=100, TextRegions=[region] }] };
        async Task ProjectReject(string name, PdfCorrectoriumProject model, ProjectPackageLimits limits)
        {
            var path = Path.Combine(root, "project-"+name+".pdfocrproj"); await Package(path, model);
            var packages = new ProjectPackageService { Limits = limits };
            var validation = await packages.ValidateAsync(path); var stopped = false;
            try { _=await packages.OpenAsync(path); } catch(InvalidDataException) { stopped=true; }
            check(!validation.IsValid && stopped, "Project preflight rejects " + name + " before exposing a model.");
        }
        await ProjectReject("long-text", project, new() { MaximumOcrTextCharacters=3 });
        await ProjectReject("many-regions", project with { Pages=[project.Pages[0] with { TextRegions=[region,region with { Id=Guid.NewGuid() }] }] }, new() { MaximumOcrRegionsPerPage=1 });
        await ProjectReject("advances", project with { Pages=[project.Pages[0] with { TextRegions=[region with { EditedGeometry=geometry with { CharacterAdvances=[1,1,1,1,1] } }] }] }, new() { MaximumOcrTextCharacters=4 });
        await ProjectReject("filename", project with { SourcePdf=source with { FileName=@"\\audit.invalid\share\file.pdf" } }, new());
        await ProjectReject("project-name", project with { Name=@"\\audit.invalid\share\name" }, new());
        var bookmarks = new PdfBookmarkService { MaximumImportNodes=20, MaximumImportStructureDepth=8, MaximumBookmarkCount=2 };
        async Task BookmarkReject(string name, string content, string extension, PdfBookmarkService? service=null)
        {
            var path=Path.Combine(root,"bookmark-"+name+extension); await File.WriteAllTextAsync(path,content);
            var rejected=false; try{_=await (service??bookmarks).ImportAsync(path);}catch(Exception ex)when(ex is InvalidDataException or System.Xml.XmlException or JsonException){rejected=true;}
            check(rejected,"Bookmark preflight rejects "+name);
        }
        await BookmarkReject("ignored-nodes","<bookmark-tree><bookmark title=\"OK\"/>"+string.Concat(Enumerable.Repeat("<junk/>",25))+"</bookmark-tree>",".xml");
        await BookmarkReject("ignored-depth","<bookmark-tree><bookmark title=\"OK\"/>"+string.Concat(Enumerable.Repeat("<junk>",10))+string.Concat(Enumerable.Repeat("</junk>",10))+"</bookmark-tree>",".xml");
        await BookmarkReject("unknown-json-array","{\"unknown\":[0,0,0],\"bookmarks\":[]}",".json");
        await BookmarkReject("title","[{\"title\":\"abcd\"}]",".json",new(){MaximumTitleCharacters=3});
        await BookmarkReject("line-length",new string('x',100)+"/1",".txt",new(){MaximumTitleCharacters=4,MaximumBookmarkDepth=2});
        var validXml=Path.Combine(root,"bookmark-valid.xml"); await File.WriteAllTextAsync(validXml,"<bookmark-tree><bookmark title=\"校正\" page=\"1\"/></bookmark-tree>");
        check((await bookmarks.ImportAsync(validXml)).Single().Title=="校正","Ordinary XML bookmarks survive preflight.");
        var input=Path.Combine(root,"tagged.pdf"); MakeTagged(input,false);
        var packagesForPdf = new ProjectPackageService(); source=await packagesForPdf.CreateSourceReferenceAsync(input,root); source=source with{PageCount=1};
        var pdfProject=new PdfCorrectoriumProject { SourcePdf=source, Pages=[new OcrPage{Id=pageId,PageNumber=1,WidthPoints=100,HeightPoints=100}] };
        foreach(var shape in new[]{PdfRedactionShapeKind.Rectangle,PdfRedactionShapeKind.TextSelection})
        {
            var destination=Path.Combine(root,shape+"-redacted.pdf");
            var model=pdfProject with{Redactions=[new PdfRedaction{PageId=pageId,Bounds=new(new(0,0),new(100,100)),ShapeKind=shape}]};
            var result=await new PdfExportService().ExportAsync(input,destination,model);
            var qdf=Path.Combine(root,shape+"-redacted.qdf.pdf");
            var qpdf=QpdfExecutableResolver.Resolve()!;
            var rewritten=await ExternalProcessRunner.RunAsync(qpdf,["--qdf","--object-streams=disable",destination,qdf],TimeSpan.FromSeconds(30));
            check(rewritten.ExitCode==0 && !Encoding.Latin1.GetString(await File.ReadAllBytesAsync(qdf)).Contains("AUDIT_SECRET_220"),"Redaction removes structure-tree secrets from reachable and serialized output: "+shape);
            var preview=PdfPreviewService.RenderPageInProcess(destination,1,300,CancellationToken.None);
            check(preview.SelectableCharacters.Count==0 && result.Warnings.Any(w=>w.Contains("構造ツリー")),"Redaction confirms empty text and reports accessibility loss: "+shape);
        }
        var native = await new PdfPreviewService().RenderPageAsync(input,1,300);
        check(native.SelectableCharacters.Count==16 && PdfNativeWorkerClient.Shared.WorkerHasResourceJob,"Live PDF preview uses the resource-limited AppContainer and bounded broker output.");
        PdfNativeWorkerClient.Shared.Shutdown();
        var paths=new ApplicationPaths(StorageMode.Portable,root,root,Path.Combine(root,"cache"),root);
        var isolated=await new IsolatedPdfExportService(packagesForPdf,paths).ExportAsync(input,Path.Combine(root,"isolated-redacted.pdf"),pdfProject with{Redactions=[new PdfRedaction{PageId=pageId,Bounds=new(new(0,0),new(100,100))}]});
        check(isolated.Result.AppliedRedactions==1,"Live isolated PDF export and qpdf descendants operate without user-file access.");
        // Inline ActualText is a second carrier. Either remove it completely or fail closed.
        var marked=Path.Combine(root,"inline-marked.pdf");MakeTagged(marked,true);
        var markedOutput=Path.Combine(root,"inline-redacted.pdf");
        var markedSource=await packagesForPdf.CreateSourceReferenceAsync(marked,root);
        try
        {
            await new PdfExportService().ExportAsync(marked,markedOutput,pdfProject with{SourcePdf=markedSource with{PageCount=1},Redactions=[new PdfRedaction{PageId=pageId,Bounds=new(new(0,0),new(100,100)),ShapeKind=PdfRedactionShapeKind.TextSelection}]});
            var qdf=Path.Combine(root,"inline-redacted.qdf.pdf");
            await ExternalProcessRunner.RunAsync(QpdfExecutableResolver.Resolve()!,["--qdf","--object-streams=disable",markedOutput,qdf],TimeSpan.FromSeconds(30));
            check(!Encoding.Latin1.GetString(await File.ReadAllBytesAsync(qdf)).Contains("AUDIT_SECRET_220"),"Inline ActualText is removed after text-selection redaction.");
        }
        catch(InvalidDataException){check(!File.Exists(markedOutput),"Unsafe inline ActualText stops export before destination commit.");}
    }
    private static async Task Package(string path, PdfCorrectoriumProject project)
    {
        using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web){Converters={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)}};
        await Add("manifest.json",new ProjectManifest{ProjectId=project.ProjectId,FormatVersion="1.1"});await Add("project.json",project);await Add("source/source-reference.json",project.SourcePdf);
        async Task Add<T>(string name,T value){await using var stream=zip.CreateEntry(name).Open();await JsonSerializer.SerializeAsync(stream,value,options);}
    }
    internal static void MakeTagged(string path,bool inline)
    {
        var content="/P <</MCID 0"+(inline?" /ActualText (AUDIT_SECRET_220) /Alt (AUDIT_SECRET_220)":"")+">> BDC\nBT /F1 8 Tf 10 50 Td (AUDIT_SECRET_220) Tj ET\nEMC\n";
        var objects=new[]{"<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 6 0 R /MarkInfo << /Marked true >> >>","<< /Type /Pages /Kids [3 0 R] /Count 1 >>","<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R /StructParents 0 >>","<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",$"<< /Length {content.Length} >>\nstream\n{content}endstream","<< /Type /StructTreeRoot /K [7 0 R] /ParentTree 8 0 R >>","<< /Type /StructElem /S /P /P 6 0 R /Pg 3 0 R /K 0 /ActualText (AUDIT_SECRET_220) /Alt (AUDIT_SECRET_220) >>","<< /Nums [0 [7 0 R]] >>"};
        var pdf=new StringBuilder("%PDF-1.7\n");var offsets=new List<int>();
        foreach(var obj in objects){offsets.Add(pdf.Length);pdf.Append($"{offsets.Count} 0 obj\n{obj}\nendobj\n");}
        var xref=pdf.Length;pdf.Append($"xref\n0 {objects.Length+1}\n0000000000 65535 f \n");
        foreach(var offset in offsets)pdf.Append($"{offset:0000000000} 00000 n \n");
        pdf.Append($"trailer << /Size {objects.Length+1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");File.WriteAllText(path,pdf.ToString(),Encoding.ASCII);
    }
}
