using System.IO;
using System.Text.Json;
using PdfCorrectorium.Core;

namespace PdfCorrectorium.App.Services;

/// <summary>設定の持ち運び用JSON。文書・パス・ログ・認証情報は含みません。</summary>
public static class SettingsTransferService
{
    public const string Format = "PdfCorrectoriumSettings";
    public const int TransferVersion = 1;
    public const int MaximumFileBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<ApplicationSettings> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        // Bounded read also protects against the input growing after its length was checked.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("The settings file exceeds 1 MB.");
        using var buffer = new MemoryStream();
        var bytes = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > MaximumFileBytes) throw new InvalidDataException("The settings file exceeds 1 MB.");
            buffer.Write(bytes, 0, count);
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String || format.GetString() != Format ||
            !root.TryGetProperty("formatVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var versionNumber) || versionNumber != TransferVersion ||
            !root.TryGetProperty("settings", out var settingsJson) || settingsJson.ValueKind != JsonValueKind.Object ||
            !settingsJson.TryGetProperty("formatVersion", out var settingsVersion) || settingsVersion.ValueKind != JsonValueKind.Number || !settingsVersion.TryGetInt32(out var settingsVersionNumber) ||
            settingsVersionNumber is < 1 or > ApplicationSettings.CurrentFormatVersion)
            throw new InvalidDataException("This is not a supported PDF Correctorium settings export.");
        RejectDuplicateProperties(root);
        var settings = settingsJson.Deserialize<ApplicationSettings>(Options)
            ?? throw new InvalidDataException("Settings are missing.");
        Validate(settings);
        return settings.Normalize();
    }

    public static Task ExportAsync(string path, ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        Validate(settings);
        var json = JsonSerializer.Serialize(new
        {
            format = Format, formatVersion = TransferVersion,
            applicationVersion = ApplicationBuildInfo.Version, settings = settings.Normalize(),
        }, Options);
        return WriteAtomicallyAsync(path, json, cancellationToken);
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON fields are not allowed.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    internal static void Validate(ApplicationSettings settings)
    {
        if (settings.WorkspacePresets is null || settings.WorkspacePresets.Count > WorkspacePreset.MaximumCount)
            throw new InvalidDataException("At most 20 workspace presets are supported.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in settings.WorkspacePresets)
            if (preset is null || !WorkspacePreset.IsValidName(preset.Name) || !names.Add(preset.Name.Trim()) ||
                !double.IsFinite(preset.PageListWidth) || !double.IsFinite(preset.PropertiesPanelWidth))
                throw new InvalidDataException("Workspace preset names must be unique and contain 1–64 characters; widths must be finite.");
        foreach (var property in typeof(ApplicationSettings).GetProperties().Where(p => p.PropertyType == typeof(double)))
            if (!double.IsFinite((double)property.GetValue(settings)!))
                throw new InvalidDataException("Settings contain a non-finite numeric value.");
        var gestures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in typeof(ApplicationSettings).GetProperties().Where(p => p.Name.EndsWith("Shortcut", StringComparison.Ordinal)))
        {
            if (!EditorShortcutService.TryNormalize((string?)property.GetValue(settings), out var gesture) || EditorShortcutService.IsReserved(gesture))
                throw new InvalidDataException("A shortcut is invalid or reserved by the application.");
            if (gesture.Length > 0 && !gestures.Add(gesture)) throw new InvalidDataException("Duplicate shortcuts are not allowed.");
        }
    }

    internal static async Task WriteAtomicallyAsync(string path, string json, CancellationToken cancellationToken)
    {
        var destination = Path.GetFullPath(path);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
