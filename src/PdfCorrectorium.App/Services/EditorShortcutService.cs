using System.Windows.Input;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// 利用者が設定したショートカット文字列とWPFのキー入力を相互変換します。
/// </summary>
public static class EditorShortcutService
{
    /// <summary>終了、保存、コピーなどと競合するため、利用者割当を禁止するキー一覧です。</summary>
    private static readonly HashSet<string> ReservedShortcuts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl+Z",
        "Ctrl+Y",
        "Ctrl+D0",
        "Ctrl+NumPad0",
        "Ctrl+Add",
        "Ctrl+Subtract",
        "Ctrl+S",
        "Ctrl+Shift+S",
    };

    /// <summary>
    /// キーイベントが指定ショートカットと一致するかを判定します。
    /// </summary>
    public static bool Matches(KeyEventArgs e, string? shortcut)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return Matches(key, Keyboard.Modifiers, shortcut);
    }

    public static bool Matches(Key key, ModifierKeys modifiers, string? shortcut) =>
        TryParse(shortcut, out var configuredKey, out var configuredModifiers) &&
        key == configuredKey &&
        modifiers == configuredModifiers;

    /// <summary>
    /// ショートカット文字列を一定の修飾キー順序とキー名へ正規化します。
    /// </summary>
    /// <param name="shortcut">正規化する文字列。</param>
    /// <param name="normalized">成功時の正規化済み表現。</param>
    /// <returns>有効な1つのキー操作として解釈できた場合は<see langword="true"/>。</returns>
    public static bool TryNormalize(string? shortcut, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(shortcut)) return true;
        if (!TryParse(shortcut, out var key, out var modifiers)) return false;
        normalized = Format(key, modifiers);
        return true;
    }

    /// <summary>
    /// 入力が無効な場合に既定値へ戻し、正規化済みショートカットを返します。
    /// </summary>
    public static string NormalizeOrDefault(string? shortcut, string fallback)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) return string.Empty;
        return TryNormalize(shortcut, out var normalized)
            ? normalized
            : TryNormalize(fallback, out var normalizedFallback) ? normalizedFallback : fallback;
    }

    /// <summary>
    /// OSや一般的な編集操作のために予約している組み合わせかを判定します。
    /// </summary>
    public static bool IsReserved(string? shortcut) =>
        TryNormalize(shortcut, out var normalized) &&
        !string.IsNullOrEmpty(normalized) &&
        ReservedShortcuts.Contains(normalized);

    /// <summary>
    /// 設定画面で押されたキーを保存可能なショートカット文字列へ変換します。
    /// </summary>
    public static bool TryCapture(KeyEventArgs e, out string shortcut)
    {
        shortcut = string.Empty;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
            return false;
        var modifiers = Keyboard.Modifiers;
        if (!IsAllowed(key, modifiers)) return false;
        shortcut = Format(key, modifiers);
        return true;
    }

    private static bool TryParse(string? shortcut, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;
        if (string.IsNullOrWhiteSpace(shortcut)) return false;
        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        foreach (var part in parts[..^1])
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Control;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Alt;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Shift;
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                     part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                modifiers |= ModifierKeys.Windows;
            else
                return false;
        }
        if (!Enum.TryParse(parts[^1], ignoreCase: true, out key) || key == Key.None) return false;
        return IsAllowed(key, modifiers);
    }

    private static bool IsAllowed(Key key, ModifierKeys modifiers) =>
        modifiers.HasFlag(ModifierKeys.Control) ||
        modifiers.HasFlag(ModifierKeys.Alt) ||
        modifiers.HasFlag(ModifierKeys.Windows) ||
        key is >= Key.F1 and <= Key.F24;

    private static string Format(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join('+', parts);
    }
}
