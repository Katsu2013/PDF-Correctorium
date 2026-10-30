namespace PdfCorrectorium.Core.IO;

/// <summary>Names from documents never become paths or shell-dialog navigation targets.</summary>
public static class SafeFileName
{
    public static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 240 &&
        value is not ("." or "..") && !value.EndsWith('.') && !value.EndsWith(' ') &&
        !value.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)) && !IsDevice(value);
    private static bool IsDevice(string value)
    {
        var stem = value.Split('.')[0].TrimEnd(' ', '.');
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
             (stem[3] is >= '1' and <= '9' || "¹²³".Contains(stem[3])));
    }
    public static string ForDialog(string? value, string fallback = "document") => IsValid(value) ? value! : fallback;
}
