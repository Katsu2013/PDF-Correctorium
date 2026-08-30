using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// PDF文書から、文書プロパティ画面に表示する情報を読み取ります。
/// </summary>
internal static class PdfDocumentPropertiesService
{
    /// <summary>PDFiumのネイティブライブラリ名です。</summary>
    private const string PdfiumLibrary = "pdfium";

    /// <summary>
    /// PDF文書のプロパティをバックグラウンドで読み取ります。
    /// </summary>
    /// <param name="pdfPath">読み取るPDFファイルの絶対パスです。</param>
    /// <param name="cancellationToken">読み取りを中止するためのトークンです。</param>
    /// <returns>画面表示用に整形した文書情報です。</returns>
    public static Task<PdfDocumentPropertiesInfo> ReadAsync(
        string pdfPath,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => Read(pdfPath, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// 情報を取得できないときに表示する既定値を作成します。
    /// </summary>
    /// <param name="pdfPath">対象PDFのパスです。</param>
    /// <param name="message">取得できなかった理由です。</param>
    /// <returns>「不明」を中心とした表示用情報です。</returns>
    public static PdfDocumentPropertiesInfo CreateUnavailable(
        string? pdfPath,
        string? message = null)
    {
        return new PdfDocumentPropertiesInfo
        {
            FileName = string.IsNullOrWhiteSpace(pdfPath) ? "不明" : Path.GetFileName(pdfPath),
            FileLocation = string.IsNullOrWhiteSpace(pdfPath)
                ? "不明"
                : Path.GetDirectoryName(pdfPath) ?? "不明",
            ErrorMessage = message ?? string.Empty,
        };
    }

    /// <summary>PDFiumを使用してPDF文書の情報を読み取ります。</summary>
    private static PdfDocumentPropertiesInfo Read(
        string pdfPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            return CreateUnavailable(pdfPath, "元PDFが見つかりません。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var fileInfo = new FileInfo(pdfPath);
        string documentLanguage;
        try
        {
            documentLanguage = new PdfBookmarkService()
                .ReadDocumentLanguageAsync(pdfPath, cancellationToken)
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Langは補助情報です。qpdfが利用できない場合も、その他の文書情報は表示します。
            documentLanguage = string.Empty;
        }

        lock (PdfiumSynchronization.Gate)
        {
            PdfiumSynchronization.EnsureInitialized(NativeMethods.FPDF_InitLibrary);

            var utf8Path = Marshal.StringToCoTaskMemUTF8(pdfPath);
            IntPtr document = IntPtr.Zero;
            try
            {
                document = NativeMethods.FPDF_LoadDocument(utf8Path, IntPtr.Zero);
                if (document == IntPtr.Zero)
                {
                    return CreateUnavailable(pdfPath, "PDFを開けませんでした。");
                }

                cancellationToken.ThrowIfCancellationRequested();
                var pageCount = NativeMethods.FPDF_GetPageCount(document);
                var version = ReadPdfVersion(document);
                var securityRevision = NativeMethods.FPDF_GetSecurityHandlerRevision(document);
                var permissions = NativeMethods.FPDF_GetDocPermissions(document);
                var firstPageSize = ReadFirstPageSize(document);
                IReadOnlyList<PdfFontPropertyInfo> fonts;
                try
                {
                    fonts = ReadFonts(document, pageCount, cancellationToken);
                }
                catch (EntryPointNotFoundException)
                {
                    // 古い PDFium では文字オブジェクト列挙 API が公開されていないため、
                    // 文書情報の読み込みを継続し、フォント一覧だけを空にします。
                    fonts = Array.Empty<PdfFontPropertyInfo>();
                }

                return new PdfDocumentPropertiesInfo
                {
                    FileName = fileInfo.Name,
                    Title = ReadMetadata(document, "Title"),
                    Author = ReadMetadata(document, "Author"),
                    Subject = ReadMetadata(document, "Subject"),
                    Keywords = ReadMetadata(document, "Keywords"),
                    CreationDateText = FormatPdfDate(ReadMetadata(document, "CreationDate")),
                    ModifiedDateText = FormatPdfDate(ReadMetadata(document, "ModDate")),
                    Creator = ReadMetadata(document, "Creator"),
                    Producer = ReadMetadata(document, "Producer"),
                    PdfVersionText = version,
                    CompatibleVersionText = string.Equals(version, "不明", StringComparison.Ordinal)
                        ? "不明"
                        : $"PDF {version} 以降",
                    FileLocation = fileInfo.DirectoryName ?? "不明",
                    FileSizeText = FormatFileSize(fileInfo.Length),
                    PageSizeText = firstPageSize,
                    PageCountText = pageCount.ToString(CultureInfo.CurrentCulture),
                    TaggedPdfText = ReadTaggedStatus(document),
                    FastWebViewText = IsLinearized(pdfPath) ? "はい" : "いいえ",
                    SecurityMethodText = securityRevision < 0
                        ? "セキュリティなし"
                        : $"Standard Security Handler（Revision {securityRevision}）",
                    PrintPermissionText = PermissionText(securityRevision < 0 || (permissions & 0x0004) != 0),
                    AssemblyPermissionText = PermissionText(securityRevision < 0 || (permissions & 0x0400) != 0),
                    CopyPermissionText = PermissionText(securityRevision < 0 || (permissions & 0x0010) != 0),
                    AccessibilityPermissionText = PermissionText(securityRevision < 0 || (permissions & 0x0200) != 0),
                    ExtractPermissionText = PermissionText(securityRevision < 0 || (permissions & 0x0010) != 0),
                    AnnotatePermissionText = PermissionText(securityRevision < 0 || (permissions & 0x0020) != 0),
                    FormFillPermissionText = PermissionText(securityRevision < 0 || (permissions & 0x0100) != 0),
                    SignPermissionText = PermissionText(securityRevision < 0 || (permissions & 0x0020) != 0),
                    TemplatePermissionText = PermissionText(securityRevision < 0 || (permissions & 0x0008) != 0),
                    LanguageText = documentLanguage,
                    Fonts = fonts,
                };
            }
            catch (EntryPointNotFoundException exception)
            {
                return CreateUnavailable(pdfPath, $"PDF情報の一部を取得できませんでした: {exception.Message}");
            }
            finally
            {
                if (document != IntPtr.Zero)
                {
                    NativeMethods.FPDF_CloseDocument(document);
                }

                Marshal.FreeCoTaskMem(utf8Path);
            }
        }
    }

    /// <summary>PDFメタデータをUTF-16文字列として読み取ります。</summary>
    private static string ReadMetadata(IntPtr document, string tag)
    {
        var byteCount = NativeMethods.FPDF_GetMetaText(document, tag, IntPtr.Zero, 0);
        if (byteCount <= 2)
        {
            return string.Empty;
        }

        var buffer = Marshal.AllocHGlobal(checked((int)byteCount));
        try
        {
            var written = NativeMethods.FPDF_GetMetaText(document, tag, buffer, byteCount);
            if (written <= 2)
            {
                return string.Empty;
            }

            var bytes = new byte[checked((int)written - 2)];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>PDFバージョン番号を読みやすい形式へ変換します。</summary>
    private static string ReadPdfVersion(IntPtr document)
    {
        if (NativeMethods.FPDF_GetFileVersion(document, out var version) == 0)
        {
            return "不明";
        }

        return version switch
        {
            >= 10 and <= 17 => $"1.{version - 10}",
            20 => "2.0",
            _ => version.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>先頭ページの大きさをミリメートル単位で返します。</summary>
    private static string ReadFirstPageSize(IntPtr document)
    {
        if (NativeMethods.FPDF_GetPageCount(document) <= 0)
        {
            return "不明";
        }

        var page = NativeMethods.FPDF_LoadPage(document, 0);
        if (page == IntPtr.Zero)
        {
            return "不明";
        }

        try
        {
            const double millimetersPerPoint = 25.4 / 72.0;
            var width = NativeMethods.FPDF_GetPageWidthF(page) * millimetersPerPoint;
            var height = NativeMethods.FPDF_GetPageHeightF(page) * millimetersPerPoint;
            return $"{width:0.0} × {height:0.0} mm";
        }
        finally
        {
            NativeMethods.FPDF_ClosePage(page);
        }
    }

    /// <summary>文書内で使用されているフォントを重複なく収集します。</summary>
    private static IReadOnlyList<PdfFontPropertyInfo> ReadFonts(
        IntPtr document,
        int pageCount,
        CancellationToken cancellationToken)
    {
        var fonts = new Dictionary<string, PdfFontPropertyInfo>(StringComparer.OrdinalIgnoreCase);
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = NativeMethods.FPDF_LoadPage(document, pageIndex);
            if (page == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                var objectCount = NativeMethods.FPDFPage_CountObjects(page);
                for (var objectIndex = 0; objectIndex < objectCount; objectIndex++)
                {
                    var pageObject = NativeMethods.FPDFPage_GetObject(page, objectIndex);
                    CollectFonts(pageObject, fonts);
                }
            }
            finally
            {
                NativeMethods.FPDF_ClosePage(page);
            }
        }

        return fonts.Values
            .OrderBy(font => font.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>ページオブジェクトと、その配下のフォームからフォントを収集します。</summary>
    private static void CollectFonts(
        IntPtr pageObject,
        IDictionary<string, PdfFontPropertyInfo> fonts)
    {
        if (pageObject == IntPtr.Zero)
        {
            return;
        }

        var objectType = NativeMethods.FPDFPageObj_GetType(pageObject);
        if (objectType == 1)
        {
            var font = NativeMethods.FPDFTextObj_GetFont(pageObject);
            if (font == IntPtr.Zero)
            {
                return;
            }

            var baseName = ReadUtf8Name(font, NativeMethods.FPDFFont_GetBaseFontName);
            var familyName = ReadUtf8Name(font, NativeMethods.FPDFFont_GetFamilyName);
            var displayName = string.IsNullOrWhiteSpace(baseName)
                ? (string.IsNullOrWhiteSpace(familyName) ? "不明なフォント" : familyName)
                : baseName;
            var key = $"{displayName}\u001f{familyName}";
            if (!fonts.ContainsKey(key))
            {
                fonts[key] = new PdfFontPropertyInfo
                {
                    Name = displayName,
                    FamilyName = string.IsNullOrWhiteSpace(familyName) ? "不明" : familyName,
                    EmbeddedText = NativeMethods.FPDFFont_GetIsEmbedded(font) != 0 ? "はい" : "いいえ",
                };
            }

            return;
        }

        // PDFiumではフォームXObjectが種別5として返されます。中の文字も再帰的に確認します。
        if (objectType != 5)
        {
            return;
        }

        var childCount = NativeMethods.FPDFFormObj_CountObjects(pageObject);
        for (var childIndex = 0; childIndex < childCount; childIndex++)
        {
            CollectFonts(NativeMethods.FPDFFormObj_GetObject(pageObject, childIndex), fonts);
        }
    }

    /// <summary>PDFiumのUTF-8フォント名取得関数を呼び出します。</summary>
    private static string ReadUtf8Name(IntPtr font, FontNameReader reader)
    {
        var length = reader(font, IntPtr.Zero, 0);
        if (length <= 1)
        {
            return string.Empty;
        }

        var buffer = Marshal.AllocHGlobal(checked((int)length));
        try
        {
            var written = reader(font, buffer, length);
            if (written <= 1)
            {
                return string.Empty;
            }

            var bytes = new byte[checked((int)written - 1)];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>PDFの日付文字列をローカル表示用に整形します。</summary>
    private static string FormatPdfDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "不明";
        }

        var digits = value.StartsWith("D:", StringComparison.Ordinal) ? value[2..] : value;
        if (digits.Length >= 4 &&
            int.TryParse(digits[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            var month = ParseDatePart(digits, 4, 2, 1);
            var day = ParseDatePart(digits, 6, 2, 1);
            var hour = ParseDatePart(digits, 8, 2, 0);
            var minute = ParseDatePart(digits, 10, 2, 0);
            var second = ParseDatePart(digits, 12, 2, 0);
            try
            {
                return new DateTime(year, month, day, hour, minute, second)
                    .ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.CurrentCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                // 不正な日付の場合は、情報を失わないよう元の値を表示します。
            }
        }

        return value;
    }

    /// <summary>PDF日付文字列から指定位置の数値を取り出します。</summary>
    private static int ParseDatePart(string value, int start, int length, int defaultValue)
    {
        return value.Length >= start + length &&
               int.TryParse(
                   value.AsSpan(start, length),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out var result)
            ? result
            : defaultValue;
    }

    /// <summary>ファイルサイズをMB表記とバイト数で返します。</summary>
    private static string FormatFileSize(long byteCount)
    {
        var megabytes = byteCount / (1024d * 1024d);
        return $"{megabytes:0.00} MB（{byteCount:N0} バイト）";
    }

    /// <summary>PDFがタグ付き文書かどうかを取得します。</summary>
    private static string ReadTaggedStatus(IntPtr document)
    {
        try
        {
            return NativeMethods.FPDFCatalog_IsTagged(document) != 0 ? "はい" : "いいえ";
        }
        catch (EntryPointNotFoundException)
        {
            return "不明";
        }
    }

    /// <summary>PDF先頭付近のLinearized辞書の有無を確認します。</summary>
    private static bool IsLinearized(string pdfPath)
    {
        using var stream = File.OpenRead(pdfPath);
        var buffer = new byte[Math.Min(8192, checked((int)Math.Min(stream.Length, 8192)))];
        var read = stream.Read(buffer, 0, buffer.Length);
        var header = Encoding.ASCII.GetString(buffer, 0, read);
        return header.Contains("/Linearized", StringComparison.Ordinal);
    }

    /// <summary>権限フラグを日本語表示へ変換します。</summary>
    private static string PermissionText(bool allowed) => allowed ? "許可" : "許可しない";

    /// <summary>PDFiumのフォント名取得関数を表します。</summary>
    private delegate nuint FontNameReader(IntPtr font, IntPtr buffer, nuint length);

    /// <summary>PDFiumのネイティブAPI宣言です。</summary>
    private static class NativeMethods
    {
        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FPDF_InitLibrary();

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FPDF_LoadDocument(IntPtr filePath, IntPtr password);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FPDF_CloseDocument(IntPtr document);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDF_GetPageCount(IntPtr document);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDF_GetFileVersion(IntPtr document, out int fileVersion);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint FPDF_GetDocPermissions(IntPtr document);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDF_GetSecurityHandlerRevision(IntPtr document);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern uint FPDF_GetMetaText(
            IntPtr document,
            string tag,
            IntPtr buffer,
            uint bufferLength);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDFCatalog_IsTagged(IntPtr document);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FPDF_ClosePage(IntPtr page);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern float FPDF_GetPageWidthF(IntPtr page);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern float FPDF_GetPageHeightF(IntPtr page);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDFPage_CountObjects(IntPtr page);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FPDFPage_GetObject(IntPtr page, int index);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDFPageObj_GetType(IntPtr pageObject);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FPDFTextObj_GetFont(IntPtr textObject);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern nuint FPDFFont_GetBaseFontName(IntPtr font, IntPtr buffer, nuint length);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern nuint FPDFFont_GetFamilyName(IntPtr font, IntPtr buffer, nuint length);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDFFont_GetIsEmbedded(IntPtr font);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDFFormObj_CountObjects(IntPtr formObject);

        [DllImport(PdfiumLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FPDFFormObj_GetObject(IntPtr formObject, int index);
    }
}

/// <summary>
/// 文書プロパティ画面に表示するPDF文書情報です。
/// </summary>
public sealed class PdfDocumentPropertiesInfo
{
    private const string Unknown = "不明";

    public string FileName { get; init; } = Unknown;
    public string Title { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Keywords { get; init; } = string.Empty;
    public string CreationDateText { get; init; } = Unknown;
    public string ModifiedDateText { get; init; } = Unknown;
    public string Creator { get; init; } = Unknown;
    public string Producer { get; init; } = Unknown;
    public string PdfVersionText { get; init; } = Unknown;
    public string CompatibleVersionText { get; init; } = Unknown;
    public string FileLocation { get; init; } = Unknown;
    public string FileSizeText { get; init; } = Unknown;
    public string PageSizeText { get; init; } = Unknown;
    public string PageCountText { get; init; } = Unknown;
    public string TaggedPdfText { get; init; } = Unknown;
    public string FastWebViewText { get; init; } = Unknown;
    public string SecurityMethodText { get; init; } = Unknown;
    public string PrintPermissionText { get; init; } = Unknown;
    public string AssemblyPermissionText { get; init; } = Unknown;
    public string CopyPermissionText { get; init; } = Unknown;
    public string AccessibilityPermissionText { get; init; } = Unknown;
    public string ExtractPermissionText { get; init; } = Unknown;
    public string AnnotatePermissionText { get; init; } = Unknown;
    public string FormFillPermissionText { get; init; } = Unknown;
    public string SignPermissionText { get; init; } = Unknown;
    public string TemplatePermissionText { get; init; } = Unknown;
    public string BaseUrl { get; init; } = string.Empty;
    public string SearchIndexFile { get; init; } = string.Empty;
    public string TrappingText { get; init; } = Unknown;
    public string LanguageText { get; init; } = Unknown;
    public string ErrorMessage { get; init; } = string.Empty;
    public IReadOnlyList<PdfFontPropertyInfo> Fonts { get; init; } = Array.Empty<PdfFontPropertyInfo>();
}

/// <summary>
/// 文書で使用されている1種類のフォント情報です。
/// </summary>
public sealed class PdfFontPropertyInfo
{
    public string Name { get; init; } = "不明なフォント";
    public string FamilyName { get; init; } = "不明";
    public string TypeText { get; init; } = "不明";
    public string EncodingText { get; init; } = "不明";
    public string EmbeddedText { get; init; } = "不明";
}
