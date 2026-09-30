using System.IO;
using System.Text.Json;

namespace PdfCorrectorium.App.Services;

/// <summary>qpdfの構造化情報から入力PDFの対話機能を調べます。ページ内容ストリームは読み込みません。</summary>
internal static class PdfInputStructureService
{
    internal sealed record Features(
        bool HasForm,
        bool HasXfa,
        bool HasSignature,
        bool HasAttachments,
        bool HasLayers,
        bool HasJavaScript,
        bool HasOpenAction,
        bool HasAdditionalActions,
        bool HasCollection,
        bool HasIncrementalUpdates);

    internal static async Task<Features> ReadAsync(string pdfPath, CancellationToken cancellationToken)
    {
        var qpdfPath = PdfBookmarkService.ResolveQpdfPath() ??
            throw new FileNotFoundException("PDF構造の検査に必要なqpdf.exeが見つかりません。");
        var summary = await RunAsync(qpdfPath,
            ["--json=2", "--json-key=acroform", "--json-key=attachments", pdfPath, "-"], cancellationToken);
        using var summaryJson = JsonDocument.Parse(summary);
        var root = summaryJson.RootElement;
        var hasForm = root.TryGetProperty("acroform", out var acroform) &&
            acroform.TryGetProperty("hasacroform", out var hasAcroform) && hasAcroform.ValueKind == JsonValueKind.True;
        var hasXfa = false;
        var hasSignature = false;
        if (hasForm && acroform.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in fields.EnumerateArray())
            {
                if (field.TryGetProperty("fieldtype", out var type) && type.ValueKind == JsonValueKind.String &&
                    type.GetString() is "Sig" or "/Sig") hasSignature = true;
            }
        }
        var hasAttachments = root.TryGetProperty("attachments", out var attachments) &&
            attachments.ValueKind == JsonValueKind.Object && attachments.EnumerateObject().Any();

        var trailer = await RunAsync(qpdfPath,
            ["--json-output=2", "--json-object=trailer", pdfPath, "-"], cancellationToken);
        using var trailerJson = JsonDocument.Parse(trailer);
        var objects = trailerJson.RootElement.GetProperty("qpdf")[1];
        var trailerValue = objects.GetProperty("trailer").GetProperty("value");
        var hasIncrementalUpdates = trailerValue.TryGetProperty("/Prev", out _);
        var catalogReference = trailerValue.GetProperty("/Root").GetString() ??
            throw new InvalidDataException("PDFカタログを特定できません。");
        var parts = catalogReference.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[2] != "R")
            throw new InvalidDataException("PDFカタログ参照が不正です。");
        var catalog = await RunAsync(qpdfPath,
            ["--json-output=2", $"--json-object={parts[0]},{parts[1]}", pdfPath, "-"], cancellationToken);
        using var catalogJson = JsonDocument.Parse(catalog);
        var catalogValue = catalogJson.RootElement.GetProperty("qpdf")[1]
            .GetProperty("obj:" + catalogReference).GetProperty("value");
        if (catalogValue.TryGetProperty("/AcroForm", out var form))
        {
            var formValue = await ResolveObjectAsync(qpdfPath, pdfPath, form, cancellationToken);
            hasXfa = formValue.ValueKind == JsonValueKind.Object && formValue.TryGetProperty("/XFA", out _);
        }
        var hasJavaScript = false;
        if (catalogValue.TryGetProperty("/Names", out var names))
        {
            var namesValue = await ResolveObjectAsync(qpdfPath, pdfPath, names, cancellationToken);
            hasJavaScript = namesValue.ValueKind == JsonValueKind.Object &&
                namesValue.TryGetProperty("/JavaScript", out _);
        }
        return new Features(
            hasForm, hasXfa, hasSignature, hasAttachments,
            catalogValue.TryGetProperty("/OCProperties", out _),
            hasJavaScript,
            catalogValue.TryGetProperty("/OpenAction", out _),
            catalogValue.TryGetProperty("/AA", out _),
            catalogValue.TryGetProperty("/Collection", out _),
            hasIncrementalUpdates);
    }

    private static async Task<JsonElement> ResolveObjectAsync(
        string qpdfPath, string pdfPath, JsonElement value, CancellationToken cancellationToken)
    {
        if (value.ValueKind != JsonValueKind.String) return value;
        var reference = value.GetString() ?? string.Empty;
        var parts = reference.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[2] != "R") return value;
        var objectJson = await RunAsync(qpdfPath,
            ["--json-output=2", $"--json-object={parts[0]},{parts[1]}", pdfPath, "-"], cancellationToken);
        using var document = JsonDocument.Parse(objectJson);
        return document.RootElement.GetProperty("qpdf")[1]
            .GetProperty("obj:" + reference).GetProperty("value").Clone();
    }

    private static async Task<string> RunAsync(string qpdfPath, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var result = await ExternalProcessRunner.RunAsync(
            qpdfPath, args, TimeSpan.FromMinutes(5), cancellationToken, 16 * 1024 * 1024);
        if (result.ExitCode != 0)
            throw new InvalidDataException($"PDF構造を確認できませんでした: {result.StandardError}");
        return result.StandardOutput;
    }
}
