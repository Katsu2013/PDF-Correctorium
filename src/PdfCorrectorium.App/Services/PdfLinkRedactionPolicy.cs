using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;

namespace PdfCorrectorium.App.Services;

/// <summary>墨消し済みページへリンク情報を再投入しないための、出力専用・非破壊の調整。</summary>
internal static class PdfLinkRedactionPolicy
{
    internal static IReadOnlyList<PdfInternalLink> Apply(IReadOnlyList<PdfInternalLink> links, Guid pageId,
        IReadOnlyList<PdfRedaction> redactions, bool annotationsRemoved, out int excluded)
    {
        var removed = 0;
        bool Overlaps(PdfRectangle bounds) => redactions.Any(redaction => PdfRedactionGeometry.IntersectsRectangle(
            redaction, bounds.Left, bounds.Bottom, bounds.Right, bounds.Top, PdfRedactionGeometry.GetSafetyPadding(redaction)));
        var result = new List<PdfInternalLink>(links.Count);
        foreach (var link in links)
        {
            if (link.SourcePageId != pageId) { result.Add(link); continue; }
            var originals = link.SourceAnnotations.Count > 0 ? link.SourceAnnotations :
                link.SourceAnnotationReference is { } reference && (link.OriginalSourceBounds ?? link.SourceBounds) is { } oldBounds
                    ? [new PdfSourceLinkAnnotation { Reference = reference, Bounds = oldBounds }] : Array.Empty<PdfSourceLinkAnnotation>();
            var areas = link.SourceAreas.Count > 0 ? link.SourceAreas : link.SourceBounds is { } bounds ? [bounds] : Array.Empty<PdfRectangle>();
            var overlaps = areas.Any(Overlaps) || originals.Any(original => Overlaps(original.Bounds));
            if (overlaps || (annotationsRemoved && link.UnsupportedAction is not null))
            {
                if (link.IsEnabled) removed++;
                // Some multiline annotations can remain outside the redaction. Remove those too,
                // so their shared URI/description cannot disclose the redacted information.
                var remaining = annotationsRemoved ? [] : originals.Where(original => !Overlaps(original.Bounds)).ToArray();
                if (remaining.Length > 0)
                    result.Add(link with { IsEnabled = false, IsModified = true, SourceAnnotations = remaining,
                        SourceAnnotationReference = remaining[0].Reference, OriginalSourceBounds = remaining[0].Bounds });
                continue;
            }
            if (annotationsRemoved)
            {
                if (link.IsEnabled)
                    result.Add(link with { SourceAnnotationReference = null, SourceAnnotations = [],
                        OriginalSourceBounds = null, IsModified = false });
            }
            else result.Add(link);
        }
        excluded = removed;
        return result;
    }
}
