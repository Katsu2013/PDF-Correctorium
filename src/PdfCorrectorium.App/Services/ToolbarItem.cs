using System.Windows;

namespace PdfCorrectorium.App.Services;

/// <summary>表示設定に使う操作ID。表示名や画面上の順番に依存しません。</summary>
public static class ToolbarItem
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(ToolbarItem), new PropertyMetadata(string.Empty));
    public static void SetKey(DependencyObject target, string value) => target.SetValue(KeyProperty, value);
    public static string GetKey(DependencyObject target) => (string)target.GetValue(KeyProperty);
}
