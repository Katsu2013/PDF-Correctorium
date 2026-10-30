using System.Globalization;
using System.Windows.Data;

namespace PdfCorrectorium.App.Services;

public sealed class AutoHidePaneIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        "navigation" => "Thumbnail", "bookmarks" => "Bookmark", "properties" => "OcrEdit",
        "link-properties" => "Link", "redaction-properties" => "Redaction", "comment-properties" => "Comments",
        "read-aloud" => "ReadAloud", _ => "Info",
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
