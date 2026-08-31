namespace PdfCorrectorium.App.Services;

/// <summary>固定パネルの幅・表示状態だけを保持します。文書、倍率、編集・復旧設定は含みません。</summary>
public sealed record WorkspacePreset
{
    public const int MaximumCount = 20;
    public const int MaximumNameLength = 64;
    public string Name { get; init; } = "";
    public bool ShowPageListPanel { get; init; } = true;
    public bool ShowPropertiesPanel { get; init; } = true;
    public bool ShowStatusBar { get; init; } = true;
    public bool ShowPageThumbnails { get; init; }
    public double PageListWidth { get; init; } = 230;
    public double PropertiesPanelWidth { get; init; } = 320;

    public static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name) &&
        name.Trim().Length <= MaximumNameLength && !name.Any(char.IsControl);

    public static WorkspacePreset Capture(string name, ApplicationSettings settings)
    {
        if (!IsValidName(name)) throw new ArgumentException("Use a nonempty preset name of at most 64 characters.", nameof(name));
        var normalized = settings.Normalize();
        return new WorkspacePreset
        {
            Name = name.Trim(), ShowPageListPanel = normalized.ShowPageListPanel,
            ShowPropertiesPanel = normalized.ShowPropertiesPanel, ShowStatusBar = normalized.ShowStatusBar,
            ShowPageThumbnails = normalized.ShowPageThumbnails,
            PageListWidth = normalized.PageListWidth, PropertiesPanelWidth = normalized.PropertiesPanelWidth,
        };
    }

    public ApplicationSettings ApplyTo(ApplicationSettings settings) => (settings with
    {
        ShowPageListPanel = ShowPageListPanel, ShowPropertiesPanel = ShowPropertiesPanel,
        ShowStatusBar = ShowStatusBar, ShowPageThumbnails = ShowPageThumbnails,
        PageListWidth = PageListWidth, PropertiesPanelWidth = PropertiesPanelWidth,
    }).Normalize();

    // Local settings are tolerant of older/malformed entries; file import validates before normalizing.
    internal static IReadOnlyList<WorkspacePreset> NormalizeList(IReadOnlyList<WorkspacePreset>? presets) =>
        (presets ?? []).Where(p => p is not null && IsValidName(p.Name))
            .Select(p => p with
            {
                Name = p.Name.Trim(),
                PageListWidth = double.IsFinite(p.PageListWidth) ? Math.Clamp(p.PageListWidth, 160, 420) : 230,
                PropertiesPanelWidth = double.IsFinite(p.PropertiesPanelWidth) ? Math.Clamp(p.PropertiesPanelWidth, 240, 600) : 320,
            })
            .DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Take(MaximumCount).ToArray();
}
