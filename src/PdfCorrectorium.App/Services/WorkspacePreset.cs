namespace PdfCorrectorium.App.Services;

/// <summary>固定パネルの幅・表示状態だけを保持します。文書、倍率、編集・復旧設定は含みません。</summary>
public sealed record WorkspacePreset
{
    public const int MaximumCount = 20;
    public const int MaximumNameLength = 64;
    public string Name { get; init; } = "";
    public bool ShowPageListPanel { get; init; } = true;
    public bool ShowBookmarksPanel { get; init; } = true;
    public bool ShowPropertiesPanel { get; init; } = true;
    public WorkspaceDockPosition NavigationDockPosition { get; init; } = WorkspaceDockPosition.Left;
    public WorkspaceDockPosition BookmarkDockPosition { get; init; } = WorkspaceDockPosition.Left;
    public WorkspaceDockPosition PropertiesDockPosition { get; init; } = WorkspaceDockPosition.Right;
    public WorkspacePaneGroupMode PaneGroupMode { get; init; } = WorkspacePaneGroupMode.Tabbed;
    public ToolbarDockSide MainToolbarDockSide { get; init; } = ToolbarDockSide.Top;
    public ToolbarDockSide PageToolbarDockSide { get; init; } = ToolbarDockSide.Top;
    public int MainToolbarBand { get; init; }
    public int MainToolbarBandIndex { get; init; }
    public int PageToolbarBand { get; init; } = 1;
    public int PageToolbarBandIndex { get; init; }
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
            Name = name.Trim(),
            ShowPageListPanel = normalized.ShowPageListPanel,
            ShowBookmarksPanel = normalized.ShowBookmarksPanel,
            ShowPropertiesPanel = normalized.ShowPropertiesPanel,
            NavigationDockPosition = normalized.NavigationDockPosition,
            BookmarkDockPosition = normalized.BookmarkDockPosition,
            PropertiesDockPosition = normalized.PropertiesDockPosition,
            PaneGroupMode = normalized.PaneGroupMode,
            MainToolbarDockSide = normalized.MainToolbarDockSide,
            PageToolbarDockSide = normalized.PageToolbarDockSide,
            MainToolbarBand = normalized.MainToolbarBand,
            MainToolbarBandIndex = normalized.MainToolbarBandIndex,
            PageToolbarBand = normalized.PageToolbarBand,
            PageToolbarBandIndex = normalized.PageToolbarBandIndex,
            ShowStatusBar = normalized.ShowStatusBar,
            ShowPageThumbnails = normalized.ShowPageThumbnails,
            PageListWidth = normalized.PageListWidth,
            PropertiesPanelWidth = normalized.PropertiesPanelWidth,
        };
    }

    public ApplicationSettings ApplyTo(ApplicationSettings settings) => (settings with
    {
        ShowPageListPanel = ShowPageListPanel,
        ShowBookmarksPanel = ShowBookmarksPanel,
        ShowPropertiesPanel = ShowPropertiesPanel,
        NavigationDockPosition = NavigationDockPosition,
        BookmarkDockPosition = BookmarkDockPosition,
        PropertiesDockPosition = PropertiesDockPosition,
        PaneGroupMode = PaneGroupMode,
        MainToolbarDockSide = MainToolbarDockSide,
        PageToolbarDockSide = PageToolbarDockSide,
        MainToolbarBand = MainToolbarBand,
        MainToolbarBandIndex = MainToolbarBandIndex,
        PageToolbarBand = PageToolbarBand,
        PageToolbarBandIndex = PageToolbarBandIndex,
        ShowStatusBar = ShowStatusBar,
        ShowPageThumbnails = ShowPageThumbnails,
        PageListWidth = PageListWidth,
        PropertiesPanelWidth = PropertiesPanelWidth,
    }).Normalize();

    // Local settings are tolerant of older/malformed entries; file import validates before normalizing.
    internal static IReadOnlyList<WorkspacePreset> NormalizeList(IReadOnlyList<WorkspacePreset>? presets) =>
        (presets ?? []).Where(p => p is not null && IsValidName(p.Name))
            .Select(p => p with
            {
                Name = p.Name.Trim(),
                PageListWidth = double.IsFinite(p.PageListWidth) ? Math.Clamp(p.PageListWidth, 160, 420) : 230,
                PropertiesPanelWidth = double.IsFinite(p.PropertiesPanelWidth) ? Math.Clamp(p.PropertiesPanelWidth, 240, 600) : 320,
                NavigationDockPosition = Enum.IsDefined(p.NavigationDockPosition) ? p.NavigationDockPosition : WorkspaceDockPosition.Left,
                BookmarkDockPosition = Enum.IsDefined(p.BookmarkDockPosition) ? p.BookmarkDockPosition : WorkspaceDockPosition.Left,
                PropertiesDockPosition = Enum.IsDefined(p.PropertiesDockPosition)
                    ? p.PropertiesDockPosition : WorkspaceDockPosition.Right,
                PaneGroupMode = Enum.IsDefined(p.PaneGroupMode) ? p.PaneGroupMode : WorkspacePaneGroupMode.Tabbed,
                MainToolbarDockSide = Enum.IsDefined(p.MainToolbarDockSide) ? p.MainToolbarDockSide : ToolbarDockSide.Top,
                PageToolbarDockSide = Enum.IsDefined(p.PageToolbarDockSide) ? p.PageToolbarDockSide : ToolbarDockSide.Top,
                MainToolbarBand = Math.Clamp(p.MainToolbarBand, 0, 1),
                MainToolbarBandIndex = Math.Clamp(p.MainToolbarBandIndex, 0, 1),
                PageToolbarBand = Math.Clamp(p.PageToolbarBand, 0, 1),
                PageToolbarBandIndex = Math.Clamp(p.PageToolbarBandIndex, 0, 1),
            })
            .DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Take(MaximumCount).ToArray();
}
