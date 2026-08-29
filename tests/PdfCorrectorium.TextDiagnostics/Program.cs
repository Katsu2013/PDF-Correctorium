using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.ProjectFormat;
using System.Globalization;

if (args.Length is < 2 or > 3 ||
    !int.TryParse(args[1], out var pageNumber) ||
    pageNumber <= 0)
{
    throw new ArgumentException("Expected a PDF path, a positive page number, and optionally a .pdfocrproj path.");
}

var pdfPath = Path.GetFullPath(args[0]);
var preview = await new PdfPreviewService().RenderPageAsync(
    pdfPath,
    pageNumber,
    targetWidth: 1800);
var characters = preview.TextRegions
    .Where(region =>
        region.IsInvisible &&
        !string.IsNullOrWhiteSpace(region.Text) &&
        StringInfo.ParseCombiningCharacters(region.Text).Length == 1)
    .ToArray();
var overlaps = new List<(PdfTextOverlayRegion First, PdfTextOverlayRegion Second, double Amount)>();

for (var firstIndex = 0; firstIndex < characters.Length; firstIndex++)
{
    var first = characters[firstIndex];
    var firstVertical = IsVertical(first);
    for (var secondIndex = firstIndex + 1; secondIndex < characters.Length; secondIndex++)
    {
        var second = characters[secondIndex];
        if (firstVertical != IsVertical(second)) continue;
        if (AngleDistance(first.RotationDegrees, second.RotationDegrees) > 2d) continue;

        var crossOverlap = firstVertical
            ? Intersection(first.Left, first.Left + first.Width, second.Left, second.Left + second.Width)
            : Intersection(first.Top, first.Top + first.Height, second.Top, second.Top + second.Height);
        var minimumCrossSize = firstVertical
            ? Math.Min(first.Width, second.Width)
            : Math.Min(first.Height, second.Height);
        if (minimumCrossSize <= 0 || crossOverlap / minimumCrossSize < 0.75d) continue;

        var advanceOverlap = firstVertical
            ? Intersection(first.Top, first.Top + first.Height, second.Top, second.Top + second.Height)
            : Intersection(first.Left, first.Left + first.Width, second.Left, second.Left + second.Width);
        if (advanceOverlap > 0.05d) overlaps.Add((first, second, advanceOverlap));
    }
}

Console.WriteLine(
    $"Page={pageNumber}; InvisibleCharacters={characters.Length}; Overlaps={overlaps.Count}");
foreach (var overlap in overlaps.Take(20))
{
    Console.WriteLine(
        $"{overlap.First.Text} / {overlap.Second.Text}: {overlap.Amount:F3}px");
}

var alignmentErrors = 0;
if (args.Length == 3)
{
    var project = await new ProjectPackageService().OpenAsync(Path.GetFullPath(args[2]));
    var page = project.Pages.Single(item => item.PageNumber == pageNumber);
    var boxes = (await new PdfPreviewService().ReadCharacterBoxesAsync(pdfPath, pageNumber))
        .Where(box => box.IsInvisible)
        .ToArray();
    foreach (var region in page.TextRegions.Where(region =>
                 region.IsModified &&
                 !region.IsDeleted &&
                 !string.IsNullOrWhiteSpace(region.EffectiveText) &&
                 region.EditedGeometry.CharacterAdvances.Count ==
                 StringInfo.ParseCombiningCharacters(region.EffectiveText).Length))
    {
        var target = region.EditedGeometry.LocalBounds;
        var textElements = StringInfo.ParseCombiningCharacters(region.EffectiveText);
        var candidates = boxes
            .Where(box =>
            {
                var boxIsVertical =
                    Math.Abs(NormalizeAngle(box.RotationDegrees)) is > 45d and < 135d;
                return (region.WritingMode == WritingMode.Vertical) == boxIsVertical;
            })
            .ToList();
        var ordered = new List<PdfCharacterBox>();
        var expectedOffset = 0d;
        var regionError = 0d;
        var lineCenterX = target.Left + target.Size.Width / 2d;
        var lineCenterY = target.Bottom + target.Size.Height / 2d;
        var layoutAngle = -region.EditedGeometry.RotationDegrees * Math.PI / 180d;
        var layoutCos = Math.Cos(layoutAngle);
        var layoutSin = Math.Sin(layoutAngle);
        for (var index = 0; index < textElements.Length; index++)
        {
            var advance = region.EditedGeometry.CharacterAdvances[index];
            var unrotatedExpectedX = region.WritingMode == WritingMode.Vertical
                ? target.Left + target.Size.Width / 2d
                : target.Left + expectedOffset + advance / 2d;
            var unrotatedExpectedY = region.WritingMode == WritingMode.Vertical
                ? target.Top - expectedOffset - advance / 2d
                : target.Bottom + target.Size.Height / 2d;
            var relativeX = unrotatedExpectedX - lineCenterX;
            var relativeY = unrotatedExpectedY - lineCenterY;
            var expectedX = lineCenterX + layoutCos * relativeX - layoutSin * relativeY;
            var expectedY = lineCenterY + layoutSin * relativeX + layoutCos * relativeY;
            var start = textElements[index];
            var end = index + 1 < textElements.Length ? textElements[index + 1] : region.EffectiveText.Length;
            var expectedText = region.EffectiveText[start..end];
            var best = candidates
                .Where(box => string.Equals(box.Text, expectedText, StringComparison.Ordinal))
                .Select(box => new
                {
                    Box = box,
                    Distance = Math.Sqrt(
                        Math.Pow((box.Left + box.Right) / 2d - expectedX, 2) +
                        Math.Pow((box.Bottom + box.Top) / 2d - expectedY, 2)),
                })
                .OrderBy(item => item.Distance)
                .FirstOrDefault();
            if (best is null || best.Distance > Math.Max(target.Size.Width, target.Size.Height) * 0.5d)
            {
                ordered.Clear();
                break;
            }
            ordered.Add(best.Box);
            candidates.Remove(best.Box);
            var actualX = (best.Box.Left + best.Box.Right) / 2d;
            var actualY = (best.Box.Bottom + best.Box.Top) / 2d;
            regionError = Math.Max(regionError, Math.Sqrt(
                (actualX - expectedX) * (actualX - expectedX) +
                (actualY - expectedY) * (actualY - expectedY)));
            expectedOffset += advance;
        }
        if (ordered.Count != textElements.Length) continue;
        var verticalObjects = ordered.Count(box =>
            Math.Abs(NormalizeAngle(box.RotationDegrees)) is > 45d and < 135d);
        if (regionError > 1d ||
            (region.WritingMode == WritingMode.Vertical && verticalObjects != ordered.Count))
        {
            alignmentErrors++;
            Console.WriteLine(
                $"ALIGN text={Abbreviate(region.EffectiveText)}; mode={region.WritingMode}; maxCenterError={regionError:F3}pt; verticalObjects={verticalObjects}/{ordered.Count}; target=({target.Left:F2},{target.Bottom:F2},{target.Size.Width:F2},{target.Size.Height:F2})");
            if (region.WritingMode == WritingMode.Vertical || alignmentErrors <= 3)
            {
                Console.WriteLine("  BOXES " + string.Join(
                    " | ",
                    ordered.Select(box =>
                        $"{box.Text}@{(box.Left + box.Right) / 2d:F1},{(box.Bottom + box.Top) / 2d:F1}:{box.RotationDegrees:F1}")));
            }
        }
    }
}

return overlaps.Count == 0 && alignmentErrors == 0 ? 0 : 2;

static bool IsVertical(PdfTextOverlayRegion region) =>
    region.IsVertical ||
    Math.Abs(NormalizeAngle(region.RotationDegrees)) is > 45d and < 135d;

static double Intersection(double firstStart, double firstEnd, double secondStart, double secondEnd) =>
    Math.Max(0d, Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart));

static double AngleDistance(double first, double second)
{
    var distance = Math.Abs(NormalizeAngle(first) - NormalizeAngle(second));
    return Math.Min(distance, 360d - distance);
}

static double NormalizeAngle(double angle)
{
    var normalized = angle % 360d;
    return normalized < 0 ? normalized + 360d : normalized;
}

static string Abbreviate(string text) =>
    text.Length <= 24 ? text : text[..24] + "...";
