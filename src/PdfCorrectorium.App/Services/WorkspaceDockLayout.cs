using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace PdfCorrectorium.App.Services;

/// <summary>Bounds and validates AvalonDock layout data before native deserialization.</summary>
public static class WorkspaceDockLayout
{
    public const int MaximumLength = 65536;
    public static readonly IReadOnlyList<string> ContentIds =
        ["navigation", "bookmarks", "properties", "link-properties", "redaction-properties", "comment-properties",
         "read-aloud", "main-toolbar", "page-toolbar", "editor"];

    internal static XmlReader Reader(string xml) => XmlReader.Create(new StringReader(xml), new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumLength,
    });

    public static string? Normalize(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        if (xml.Length > MaximumLength) return null;
        try
        {
            using var reader = Reader(xml);
            var root = XElement.Load(reader);
            if (root.Name.LocalName != "LayoutRoot" || root.Descendants().Count() > 200) return null;
            foreach (var name in new[] { "PdfCorrectoriumAutoHideLeft", "PdfCorrectoriumAutoHideRight" })
                if (root.Attribute(name) is { } sideMode && !bool.TryParse(sideMode.Value, out _)) return null;
            var pinned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in new[] { "PdfCorrectoriumFixedLeft", "PdfCorrectoriumFixedRight" })
                foreach (var id in ((string?)root.Attribute(name) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (id is "editor" or "main-toolbar" or "page-toolbar" || !ContentIds.Contains(id) || !pinned.Add(id)) return null;
            var ids = root.Descendants().Attributes("ContentId").Select(attribute => attribute.Value).ToArray();
            var allowed = ids.Contains("read-aloud", StringComparer.Ordinal) ? ContentIds : ContentIds.Where(id => id != "read-aloud").ToArray();
            if (ids.Length != allowed.Count || !ids.ToHashSet(StringComparer.Ordinal).SetEquals(allowed)) return null;
            foreach (var attribute in root.Descendants().Attributes())
                if (attribute.Name.LocalName.Contains("Width", StringComparison.Ordinal) ||
                    attribute.Name.LocalName.Contains("Height", StringComparison.Ordinal) ||
                    attribute.Name.LocalName is "FloatingLeft" or "FloatingTop")
                {
                    if (attribute.Value is "Auto" or "*") continue;
                    var value = attribute.Value.EndsWith('*') ? attribute.Value[..^1] : attribute.Value;
                    if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var number) ||
                        !double.IsFinite(number) || Math.Abs(number) > 100000) return null;
                }
            return xml;
        }
        catch (XmlException) { return null; }
    }
}
