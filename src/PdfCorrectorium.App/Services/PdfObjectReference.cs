using System.Globalization;

namespace PdfCorrectorium.App.Services;

internal static class PdfObjectReference
{
    internal static bool IsValid(string reference)
    {
        var parts = reference.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3 && parts[2] == "R" &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var generation) && generation is >= 0 and <= 65535;
    }
}
