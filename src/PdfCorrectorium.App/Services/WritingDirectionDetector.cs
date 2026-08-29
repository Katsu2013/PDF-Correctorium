using System.Globalization;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// OCR文字列の字種と領域形状から、縦書きである可能性を簡易判定します。
/// </summary>
public static class WritingDirectionDetector
{
    /// <summary>
    /// 文字列内容と領域の縦横比を組み合わせて縦書き候補を判定します。
    /// </summary>
    /// <param name="text">認識済み文字列。</param>
    /// <param name="width">OCR領域の幅。</param>
    /// <param name="height">OCR領域の高さ。</param>
    /// <returns>縦書きである可能性が高い場合は<see langword="true"/>。</returns>
    public static bool IsLikelyVertical(string text, double width, double height)
    {
        var indexes = StringInfo.ParseCombiningCharacters(text);
        var count = 0;
        for (var index = 0; index < indexes.Length; index++)
        {
            var end = index + 1 < indexes.Length ? indexes[index + 1] : text.Length;
            if (!string.IsNullOrWhiteSpace(text[indexes[index]..end])) count++;
        }
        if (count <= 0 || width <= 0 || height <= 0) return false;
        if (count == 1) return height > width * 2.4;

        var horizontalCellRatio = width / count / height;
        var verticalCellRatio = height / count / width;
        var horizontalScore = Math.Abs(Math.Log(Math.Clamp(horizontalCellRatio, 0.001, 1000)));
        var verticalScore = Math.Abs(Math.Log(Math.Clamp(verticalCellRatio, 0.001, 1000)));
        return height > width * 1.35 && verticalScore + 0.3 < horizontalScore;
    }
}
