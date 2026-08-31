using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Data;
using System.Windows.Input;

namespace PdfCorrectorium.App.Services;

/// <summary>ラベルを持たないアイコン・一覧にも標準WPFのアクセスキーを提供します。</summary>
public static class KeyboardAccess
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(KeyboardAccess), new PropertyMetadata(null, OnKeyChanged));

    public static string? GetKey(DependencyObject element) => (string?)element.GetValue(KeyProperty);
    public static void SetKey(DependencyObject element, string value) => element.SetValue(KeyProperty, value);

    private static readonly DependencyProperty RegisteredKeyProperty = DependencyProperty.RegisterAttached(
        "RegisteredKey", typeof(string), typeof(KeyboardAccess));
    private static readonly DependencyProperty HasHintProperty = DependencyProperty.RegisterAttached(
        "HasHint", typeof(bool), typeof(KeyboardAccess), new PropertyMetadata(false));

    private static void OnKeyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        if (args.NewValue is string key && (key.Length != 1 || !char.IsAsciiLetterOrDigit(key[0])))
            throw new ArgumentException("An access key must be one ASCII letter or digit.");
        Unregister(element);
        element.Loaded -= OnLoaded;
        element.Unloaded -= OnUnloaded;
        AccessKeyManager.RemoveAccessKeyPressedHandler(element, OnAccessKeyPressed);
        if (args.NewValue is null)
        {
            AutomationProperties.SetAccessKey(element, string.Empty);
            BindingOperations.GetMultiBindingExpression(element, FrameworkElement.ToolTipProperty)?.UpdateTarget();
            return;
        }
        element.Loaded += OnLoaded;
        element.Unloaded += OnUnloaded;
        AccessKeyManager.AddAccessKeyPressedHandler(element, OnAccessKeyPressed);
        AutomationProperties.SetAccessKey(element, "Alt+" + args.NewValue);
        if (element.IsLoaded) Register(element);
    }

    private static void OnLoaded(object sender, RoutedEventArgs args) => Register((FrameworkElement)sender);
    private static void OnUnloaded(object sender, RoutedEventArgs args) => Unregister((FrameworkElement)sender);

    private static void OnAccessKeyPressed(object sender, AccessKeyPressedEventArgs args)
    {
        // Inputs do not nominate themselves the way Button/Label do. Keep popup scopes intact.
        if (!args.Handled && args.Scope is null && args.Target is null && sender is UIElement element)
            args.Target = element;
    }

    private static void Register(FrameworkElement element)
    {
        Unregister(element);
        if (GetKey(element) is not { } key) return;
        AccessKeyManager.Register(key, element);
        element.SetValue(RegisteredKeyProperty, key);
        RefreshHint(element);
    }

    private static void Unregister(FrameworkElement element)
    {
        if (element.GetValue(RegisteredKeyProperty) is string key)
            AccessKeyManager.Unregister(key, element);
        element.ClearValue(RegisteredKeyProperty);
    }

    /// <summary>既存の倍率などのバインドを保持したまま、アクセスキーをヒントへ追加します。</summary>
    public static void RefreshHint(FrameworkElement element)
    {
        if (GetKey(element) is null) return;
        if (!(bool)element.GetValue(HasHintProperty))
        {
            var original = BindingOperations.GetBindingBase(element, FrameworkElement.ToolTipProperty);
            // Do not replace rich tooltips or nest an existing MultiBinding.
            if (original is MultiBinding or PriorityBinding || element.ToolTip is FrameworkElement) return;
            var binding = new MultiBinding { Converter = new AccessHintConverter() };
            binding.Bindings.Add(original ?? new Binding
            {
                Source = element.ToolTip ?? AutomationProperties.GetName(element),
            });
            binding.Bindings.Add(new Binding { Source = element, Path = new PropertyPath(KeyProperty) });
            BindingOperations.SetBinding(element, FrameworkElement.ToolTipProperty, binding);
            element.SetValue(HasHintProperty, true);
        }
        BindingOperations.GetMultiBindingExpression(element, FrameworkElement.ToolTipProperty)?.UpdateTarget();
    }

    private sealed class AccessHintConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var caption = LocalizationService.Translate(values[0]?.ToString() ?? string.Empty);
            return values[1] is string key ? $"{caption} (Alt+{key})".Trim() : caption;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
