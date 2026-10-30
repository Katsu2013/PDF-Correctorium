namespace PdfCorrectorium.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public void SetToolbarItemVisible(string id, bool visible)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var hidden = _applicationSettings.HiddenToolbarItems.ToHashSet(StringComparer.Ordinal);
        var changed = visible ? hidden.Remove(id) : hidden.Add(id);
        UpdateDisplaySetting(!changed, _applicationSettings with { HiddenToolbarItems = hidden.Order(StringComparer.Ordinal).ToArray() });
    }
    public void ResetToolbarItems(IEnumerable<string> ids)
    {
        var targets = ids.ToHashSet(StringComparer.Ordinal);
        var hidden = _applicationSettings.HiddenToolbarItems.Where(id => !targets.Contains(id)).ToArray();
        UpdateDisplaySetting(hidden.Length == _applicationSettings.HiddenToolbarItems.Count,
            _applicationSettings with { HiddenToolbarItems = hidden });
    }
}
