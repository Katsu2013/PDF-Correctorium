using System.Globalization;
using System.Windows.Data;

namespace PdfCorrectorium.App.Services;

/// <summary>Two native grid columns when each control has 128 DIP plus its 8 DIP gutter; otherwise one.</summary>
public sealed class CompactPropertyColumnsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double width && double.IsFinite(width) && width >= 272 ? 2 : 1;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
