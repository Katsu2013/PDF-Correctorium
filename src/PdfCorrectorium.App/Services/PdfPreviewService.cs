using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// プレビュー画像上へ重ねて表示する、1つのOCR文字領域を表します。
/// </summary>
/// <remarks>
/// 座標と寸法は、<see cref="PdfPreviewResult.Image"/> のピクセル座標系で保持します。
/// <paramref name="CharacterAdvances"/> は書字方向に沿った各テキスト要素の送り量です。
/// </remarks>
/// <param name="Text">領域に含まれるUnicode文字列。</param>
/// <param name="Left">プレビュー画像左端から領域左端までのピクセル位置。</param>
/// <param name="Top">プレビュー画像上端から領域上端までのピクセル位置。</param>
/// <param name="Width">プレビュー画像上の領域幅。</param>
/// <param name="Height">プレビュー画像上の領域高。</param>
/// <param name="IsInvisible">PDF上で不可視テキストとして描画されている場合は<c>true</c>。</param>
/// <param name="IsVertical">縦書き領域として扱う場合は<c>true</c>。</param>
/// <param name="ProviderId">領域を生成または取り込んだOCRプロバイダーの識別子。</param>
/// <param name="Confidence">OCR結果に信頼度がある場合の0～1の値。</param>
/// <param name="RotationDegrees">プレビュー画像上で時計回りに適用する回転角度。</param>
/// <param name="CharacterAdvances">書字方向に沿った各テキスト要素の送り量。</param>
public sealed record PdfTextOverlayRegion(
    string Text,
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsInvisible,
    bool IsVertical = false,
    string ProviderId = "imported-pdf",
    double? Confidence = null,
    double RotationDegrees = 0,
    IReadOnlyList<double>? CharacterAdvances = null);

/// <summary>
/// PDFページの描画画像と、その画像に対応するOCR領域をまとめた結果です。
/// </summary>
/// <param name="Image">指定ページを描画したWPF画像。</param>
/// <param name="PageCount">文書全体のページ数。</param>
/// <param name="PageNumber">1から始まる描画対象ページ番号。</param>
/// <param name="PageWidthPoints">PDFページの幅（ポイント）。</param>
/// <param name="PageHeightPoints">PDFページの高さ（ポイント）。</param>
/// <param name="TextRegions">画像座標へ変換済みのOCR文字領域。</param>
public sealed record PdfPreviewResult(
    BitmapSource Image,
    int PageCount,
    int PageNumber,
    double PageWidthPoints,
    double PageHeightPoints,
    IReadOnlyList<PdfTextOverlayRegion> TextRegions);

/// <summary>
/// PDFiumから取得した1文字分の境界と描画属性を表します。
/// </summary>
/// <remarks>
/// 境界はPDFページ座標であり、原点やY軸方向がプレビュー画像の座標系とは異なります。
/// </remarks>
/// <param name="Text">文字ボックスに対応するUnicodeテキスト要素。</param>
/// <param name="Left">PDF座標系での左端。</param>
/// <param name="Bottom">PDF座標系での下端。</param>
/// <param name="Right">PDF座標系での右端。</param>
/// <param name="Top">PDF座標系での上端。</param>
/// <param name="IsInvisible">不可視描画モードの文字である場合は<c>true</c>。</param>
/// <param name="RotationDegrees">PDF上の文字回転角度。</param>
public sealed record PdfCharacterBox(
    string Text,
    double Left,
    double Bottom,
    double Right,
    double Top,
    bool IsInvisible,
    double RotationDegrees);

/// <summary>
/// PDFiumを使用してPDFページのプレビューと文字位置を読み取ります。
/// </summary>
/// <remarks>
/// PDFiumのネイティブAPIは同時呼び出しを避けるため、内部で共通ロックを使用します。
/// 戻り値の画像はWPFの別スレッドからも参照できるように凍結されます。
/// </remarks>
public sealed class PdfPreviewService
{
    /// <summary>注釈をページ画像へ含めるPDFium描画フラグです。</summary>
    private const int RenderAnnotations = 0x01;
    /// <summary>画面表示向けのLCDテキスト描画を有効にするPDFiumフラグです。</summary>
    private const int RenderLcdText = 0x02;

    /// <summary>
    /// 指定ページを画像化し、同じページに含まれる文字領域を抽出します。
    /// </summary>
    /// <param name="pdfPath">読み取るPDFファイルのパス。</param>
    /// <param name="pageNumber">1から始まるページ番号。</param>
    /// <param name="targetWidth">生成するプレビュー画像のおおよその幅（ピクセル）。</param>
    /// <param name="cancellationToken">処理の取り消しを通知するトークン。</param>
    /// <returns>描画画像、ページ情報、およびOCRオーバーレイ候補。</returns>
    public Task<PdfPreviewResult> RenderPageAsync(
        string pdfPath,
        int pageNumber,
        int targetWidth = 1200,
        CancellationToken cancellationToken = default) =>
        PdfNativeWorkerClient.Shared.RenderPageAsync(pdfPath, pageNumber, targetWidth, cancellationToken);

    /// <summary>
    /// 指定ページに含まれる文字を、PDFページ座標の境界付きで読み取ります。
    /// </summary>
    /// <param name="pdfPath">読み取るPDFファイルのパス。</param>
    /// <param name="pageNumber">1から始まるページ番号。</param>
    /// <param name="cancellationToken">処理の取り消しを通知するトークン。</param>
    /// <returns>PDFiumが報告した文字境界の一覧。</returns>
    public Task<IReadOnlyList<PdfCharacterBox>> ReadCharacterBoxesAsync(
        string pdfPath,
        int pageNumber,
        CancellationToken cancellationToken = default) =>
        PdfNativeWorkerClient.Shared.ReadCharacterBoxesAsync(pdfPath, pageNumber, cancellationToken);

    internal static IReadOnlyList<PdfCharacterBox> ReadCharacterBoxesInProcess(
        string pdfPath,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        if (!File.Exists(pdfPath)) throw new FileNotFoundException("The PDF file was not found.", pdfPath);

        lock (PdfiumSynchronization.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureInitialized();

            var utf8Path = Marshal.StringToCoTaskMemUTF8(pdfPath);
            IntPtr document = IntPtr.Zero;
            IntPtr page = IntPtr.Zero;
            IntPtr textPage = IntPtr.Zero;
            try
            {
                document = NativeMethods.FPDF_LoadDocument(utf8Path, IntPtr.Zero);
                if (document == IntPtr.Zero) throw CreatePdfException("The PDF could not be opened");
                var pageCount = NativeMethods.FPDF_GetPageCount(document);
                if (pageNumber < 1 || pageNumber > pageCount)
                    throw new ArgumentOutOfRangeException(nameof(pageNumber), $"Page {pageNumber} is outside 1-{pageCount}.");

                page = NativeMethods.FPDF_LoadPage(document, pageNumber - 1);
                if (page == IntPtr.Zero) throw CreatePdfException($"Page {pageNumber} could not be loaded");
                textPage = NativeMethods.FPDFText_LoadPage(page);
                if (textPage == IntPtr.Zero) return [];

                var result = new List<PdfCharacterBox>();
                var count = NativeMethods.FPDFText_CountChars(textPage);
                for (var index = 0; index < count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var textObject = NativeMethods.FPDFText_GetTextObject(textPage, index);
                    if (textObject == IntPtr.Zero) continue;
                    var unicode = NativeMethods.FPDFText_GetUnicode(textPage, index);
                    if (unicode == 0 || unicode > 0x10FFFF) continue;
                    if (NativeMethods.FPDFText_GetCharBox(
                            textPage,
                            index,
                            out var left,
                            out var right,
                            out var bottom,
                            out var top) == 0 ||
                        right <= left ||
                        top <= bottom)
                        continue;

                    var alpha = 255u;
                    NativeMethods.FPDFText_GetFillColor(textPage, index, out _, out _, out _, out alpha);
                    result.Add(new PdfCharacterBox(
                        char.ConvertFromUtf32((int)unicode),
                        left,
                        bottom,
                        right,
                        top,
                        NativeMethods.FPDFTextObj_GetTextRenderMode(textObject) == 3 || alpha <= 5,
                        GetObjectRotation(textObject)));
                }
                return result;
            }
            finally
            {
                if (textPage != IntPtr.Zero) NativeMethods.FPDFText_ClosePage(textPage);
                if (page != IntPtr.Zero) NativeMethods.FPDF_ClosePage(page);
                if (document != IntPtr.Zero) NativeMethods.FPDF_CloseDocument(document);
                Marshal.FreeCoTaskMem(utf8Path);
            }
        }
    }

    internal static PdfPreviewResult RenderPageInProcess(
        string pdfPath,
        int pageNumber,
        int targetWidth,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        if (!File.Exists(pdfPath)) throw new FileNotFoundException("The PDF file was not found.", pdfPath);

        lock (PdfiumSynchronization.Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureInitialized();

            var utf8Path = Marshal.StringToCoTaskMemUTF8(pdfPath);
            IntPtr document = IntPtr.Zero;
            IntPtr page = IntPtr.Zero;
            IntPtr bitmap = IntPtr.Zero;
            IntPtr textPage = IntPtr.Zero;
            try
            {
                document = NativeMethods.FPDF_LoadDocument(utf8Path, IntPtr.Zero);
                if (document == IntPtr.Zero) throw CreatePdfException("The PDF could not be opened");

                var pageCount = NativeMethods.FPDF_GetPageCount(document);
                if (pageCount <= 0) throw new InvalidDataException("The PDF contains no pages.");
                if (pageNumber < 1 || pageNumber > pageCount)
                    throw new ArgumentOutOfRangeException(nameof(pageNumber), $"Page {pageNumber} is outside 1-{pageCount}.");

                page = NativeMethods.FPDF_LoadPage(document, pageNumber - 1);
                if (page == IntPtr.Zero) throw CreatePdfException($"Page {pageNumber} could not be loaded");
                textPage = NativeMethods.FPDFText_LoadPage(page);

                // widthPoints/heightPointsはPDF座標、pixelWidth/pixelHeightはWPFプレビュー座標の寸法です。
                var widthPoints = NativeMethods.FPDF_GetPageWidthF(page);
                var heightPoints = NativeMethods.FPDF_GetPageHeightF(page);
                if (widthPoints <= 0 || heightPoints <= 0)
                    throw new InvalidDataException($"Page {pageNumber} has an invalid size.");

                // アスペクト比を維持しつつ、過大な画像生成によるメモリ消費を上限値で抑えます。
                var pixelWidth = Math.Clamp(targetWidth, 96, 2400);
                var pixelHeight = Math.Clamp((int)Math.Ceiling(pixelWidth * heightPoints / widthPoints), 96, 3200);
                bitmap = NativeMethods.FPDFBitmap_Create(pixelWidth, pixelHeight, 1);
                if (bitmap == IntPtr.Zero) throw new InvalidOperationException("PDFium could not allocate the preview bitmap.");

                NativeMethods.FPDFBitmap_FillRect(bitmap, 0, 0, pixelWidth, pixelHeight, 0xFFFFFFFF);
                NativeMethods.FPDF_RenderPageBitmap(
                    bitmap,
                    page,
                    0,
                    0,
                    pixelWidth,
                    pixelHeight,
                    0,
                    RenderAnnotations | RenderLcdText);
                cancellationToken.ThrowIfCancellationRequested();

                var buffer = NativeMethods.FPDFBitmap_GetBuffer(bitmap);
                var stride = NativeMethods.FPDFBitmap_GetStride(bitmap);
                if (buffer == IntPtr.Zero || stride <= 0)
                    throw new InvalidOperationException("PDFium returned an empty preview bitmap.");

                var image = BitmapSource.Create(
                    pixelWidth,
                    pixelHeight,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null,
                    buffer,
                    checked(stride * pixelHeight),
                    stride);
                image.Freeze();
                var textRegions = textPage == IntPtr.Zero
                    ? []
                    : ExtractTextRegions(page, textPage, widthPoints, heightPoints, pixelWidth, pixelHeight);
                return new PdfPreviewResult(image, pageCount, pageNumber, widthPoints, heightPoints, textRegions);
            }
            finally
            {
                if (bitmap != IntPtr.Zero) NativeMethods.FPDFBitmap_Destroy(bitmap);
                if (textPage != IntPtr.Zero) NativeMethods.FPDFText_ClosePage(textPage);
                if (page != IntPtr.Zero) NativeMethods.FPDF_ClosePage(page);
                if (document != IntPtr.Zero) NativeMethods.FPDF_CloseDocument(document);
                Marshal.FreeCoTaskMem(utf8Path);
            }
        }
    }

    private static IReadOnlyList<PdfTextOverlayRegion> ExtractTextRegions(
        IntPtr page,
        IntPtr textPage,
        double pageWidth,
        double pageHeight,
        int pixelWidth,
        int pixelHeight)
    {
        var regions = new List<PdfTextOverlayRegion>();
        var characterAdvances = CollectCharacterAdvances(textPage, pageWidth, pageHeight, pixelWidth, pixelHeight);
        VisitObjects(
            NativeMethods.FPDFPage_CountObjects(page),
            index => NativeMethods.FPDFPage_GetObject(page, index),
            textPage,
            pageWidth,
            pageHeight,
            pixelWidth,
            pixelHeight,
            characterAdvances,
            regions);
        if (regions.Count == 0)
            ExtractCharacterRegions(textPage, pageWidth, pageHeight, pixelWidth, pixelHeight, regions);
        var invisible = regions.Where(region => region.IsInvisible).ToArray();
        return invisible.Length > 0 ? invisible : regions;
    }

    private static void ExtractCharacterRegions(
        IntPtr textPage,
        double pageWidth,
        double pageHeight,
        int pixelWidth,
        int pixelHeight,
        List<PdfTextOverlayRegion> regions)
    {
        var count = NativeMethods.FPDFText_CountChars(textPage);
        if (count <= 0) return;
        var groups = new Dictionary<IntPtr, CharacterRegionBuilder>();
        var order = new List<IntPtr>();
        for (var index = 0; index < count; index++)
        {
            var textObject = NativeMethods.FPDFText_GetTextObject(textPage, index);
            if (textObject == IntPtr.Zero) continue;
            var unicode = NativeMethods.FPDFText_GetUnicode(textPage, index);
            if (unicode == 0 || unicode > 0x10FFFF) continue;
            if (NativeMethods.FPDFText_GetCharBox(textPage, index, out var left, out var right, out var bottom, out var top) == 0) continue;
            if (right <= left || top <= bottom) continue;
            if (!groups.TryGetValue(textObject, out var builder))
            {
                var alpha = 255u;
                NativeMethods.FPDFText_GetFillColor(textPage, index, out _, out _, out _, out alpha);
                var rotation = GetObjectRotation(textObject);
                builder = new CharacterRegionBuilder(NativeMethods.FPDFTextObj_GetTextRenderMode(textObject) == 3 || alpha <= 5, rotation);
                groups[textObject] = builder;
                order.Add(textObject);
            }
            builder.Add(char.ConvertFromUtf32((int)unicode), left, right, bottom, top);
        }

        foreach (var key in order)
        {
            var builder = groups[key];
            var text = builder.Text.ToString().Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            regions.Add(new PdfTextOverlayRegion(
                text,
                builder.Left / pageWidth * pixelWidth,
                (pageHeight - builder.Top) / pageHeight * pixelHeight,
                (builder.Right - builder.Left) / pageWidth * pixelWidth,
                (builder.Top - builder.Bottom) / pageHeight * pixelHeight,
                builder.IsInvisible,
                RotationDegrees: builder.RotationDegrees,
                CharacterAdvances: builder.CharacterAdvances(pageWidth, pageHeight, pixelWidth, pixelHeight)));
        }
    }

    private sealed class CharacterRegionBuilder(bool isInvisible, double rotationDegrees)
    {
        public StringBuilder Text { get; } = new();
        public bool IsInvisible { get; } = isInvisible;
        public double RotationDegrees { get; } = rotationDegrees;
        public double Left { get; private set; } = double.PositiveInfinity;
        public double Right { get; private set; } = double.NegativeInfinity;
        public double Bottom { get; private set; } = double.PositiveInfinity;
        public double Top { get; private set; } = double.NegativeInfinity;
        private List<(double Width, double Height)> CharacterSizes { get; } = [];

        public void Add(string text, double left, double right, double bottom, double top)
        {
            Text.Append(text);
            Left = Math.Min(Left, left);
            Right = Math.Max(Right, right);
            Bottom = Math.Min(Bottom, bottom);
            Top = Math.Max(Top, top);
            CharacterSizes.Add((right - left, top - bottom));
        }

        public IReadOnlyList<double> CharacterAdvances(double pageWidth, double pageHeight, int pixelWidth, int pixelHeight)
        {
            var verticalOnPage = Math.Abs(RotationDegrees) is > 45 and < 135;
            return CharacterSizes.Select(size => verticalOnPage
                ? size.Height / pageHeight * pixelHeight
                : size.Width / pageWidth * pixelWidth).ToArray();
        }
    }

    private static IReadOnlyDictionary<IntPtr, IReadOnlyList<double>> CollectCharacterAdvances(
        IntPtr textPage,
        double pageWidth,
        double pageHeight,
        int pixelWidth,
        int pixelHeight)
    {
        var result = new Dictionary<IntPtr, List<double>>();
        var count = NativeMethods.FPDFText_CountChars(textPage);
        for (var index = 0; index < count; index++)
        {
            var textObject = NativeMethods.FPDFText_GetTextObject(textPage, index);
            if (textObject == IntPtr.Zero ||
                NativeMethods.FPDFText_GetCharBox(textPage, index, out var left, out var right, out var bottom, out var top) == 0 ||
                right <= left || top <= bottom)
                continue;
            if (!result.TryGetValue(textObject, out var advances))
            {
                advances = [];
                result[textObject] = advances;
            }
            var rotation = Math.Abs(GetObjectRotation(textObject));
            advances.Add(rotation is > 45 and < 135
                ? (top - bottom) / pageHeight * pixelHeight
                : (right - left) / pageWidth * pixelWidth);
        }
        return result.ToDictionary(item => item.Key, item => (IReadOnlyList<double>)item.Value);
    }

    private static void VisitObjects(
        int count,
        Func<int, IntPtr> getObject,
        IntPtr textPage,
        double pageWidth,
        double pageHeight,
        int pixelWidth,
        int pixelHeight,
        IReadOnlyDictionary<IntPtr, IReadOnlyList<double>> characterAdvances,
        List<PdfTextOverlayRegion> regions)
    {
        for (var index = 0; index < count; index++)
        {
            var pageObject = getObject(index);
            if (pageObject == IntPtr.Zero) continue;
            var objectType = NativeMethods.FPDFPageObj_GetType(pageObject);
            if (objectType == 5)
            {
                var childCount = NativeMethods.FPDFFormObj_CountObjects(pageObject);
                VisitObjects(
                    childCount,
                    childIndex => NativeMethods.FPDFFormObj_GetObject(pageObject, (uint)childIndex),
                    textPage,
                    pageWidth,
                    pageHeight,
                    pixelWidth,
                    pixelHeight,
                    characterAdvances,
                    regions);
                continue;
            }
            if (objectType != 1) continue;
            if (NativeMethods.FPDFPageObj_GetBounds(pageObject, out var left, out var bottom, out var right, out var top) == 0) continue;
            if (right <= left || top <= bottom) continue;

            var byteLength = NativeMethods.FPDFTextObj_GetText(pageObject, textPage, IntPtr.Zero, 0);
            if (byteLength < 2 || byteLength > 8_388_608) continue;
            var textBuffer = Marshal.AllocHGlobal(checked((int)byteLength));
            string text;
            try
            {
                if (NativeMethods.FPDFTextObj_GetText(pageObject, textPage, textBuffer, byteLength) == 0) continue;
                text = Marshal.PtrToStringUni(textBuffer, checked((int)byteLength / 2 - 1))?.Trim() ?? string.Empty;
            }
            finally { Marshal.FreeHGlobal(textBuffer); }
            if (string.IsNullOrWhiteSpace(text)) continue;

            var renderMode = NativeMethods.FPDFTextObj_GetTextRenderMode(pageObject);
            var alpha = 255u;
            NativeMethods.FPDFPageObj_GetFillColor(pageObject, out _, out _, out _, out alpha);
            regions.Add(new PdfTextOverlayRegion(
                text,
                left / pageWidth * pixelWidth,
                (pageHeight - top) / pageHeight * pixelHeight,
                (right - left) / pageWidth * pixelWidth,
                (top - bottom) / pageHeight * pixelHeight,
                renderMode == 3 || alpha <= 5,
                RotationDegrees: GetObjectRotation(pageObject),
                CharacterAdvances: characterAdvances.GetValueOrDefault(pageObject)));
        }
    }

    private static double GetObjectRotation(IntPtr pageObject)
    {
        if (NativeMethods.FPDFPageObj_GetMatrix(pageObject, out var matrix) == 0) return 0;
        // PDF coordinates use an upward Y axis and positive angles rotate
        // counter-clockwise. WPF screen coordinates use a downward Y axis,
        // where RotateTransform's positive angle appears clockwise.
        return -Math.Atan2(matrix.B, matrix.A) * 180d / Math.PI;
    }

    private static void EnsureInitialized()
        => PdfiumSynchronization.EnsureInitialized(NativeMethods.FPDF_InitLibrary);

    private static Exception CreatePdfException(string message)
    {
        var error = NativeMethods.FPDF_GetLastError();
        var detail = error switch
        {
            1 => "unknown error",
            2 => "file not found or inaccessible",
            3 => "invalid PDF format",
            4 => "password is required or incorrect",
            5 => "unsupported security scheme",
            6 => "page error",
            _ => $"PDFium error {error}",
        };
        return new InvalidDataException($"{message}: {detail}.");
    }

    private static class NativeMethods
    {
        /// <summary>PDFiumネイティブライブラリを指定するP/Invoke用の論理名です。</summary>
        private const string Pdfium = "pdfium";

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FPDF_InitLibrary();

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FPDF_LoadDocument(IntPtr filePathUtf8, IntPtr passwordUtf8);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint FPDF_GetLastError();

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDF_GetPageCount(IntPtr document);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FPDFText_LoadPage(IntPtr page);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FPDFText_ClosePage(IntPtr textPage);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDFText_CountChars(IntPtr textPage);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint FPDFText_GetUnicode(IntPtr textPage, int index);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FPDFText_GetTextObject(IntPtr textPage, int index);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDFText_GetCharBox(IntPtr textPage, int index, out double left, out double right, out double bottom, out double top);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDFText_GetFillColor(IntPtr textPage, int index, out uint red, out uint green, out uint blue, out uint alpha);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDFPage_CountObjects(IntPtr page);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FPDFPage_GetObject(IntPtr page, int index);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDFPageObj_GetType(IntPtr pageObject);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDFFormObj_CountObjects(IntPtr formObject);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FPDFFormObj_GetObject(IntPtr formObject, uint index);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDFPageObj_GetBounds(IntPtr pageObject, out float left, out float bottom, out float right, out float top);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDFPageObj_GetMatrix(IntPtr pageObject, out FsMatrix matrix);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDFTextObj_GetTextRenderMode(IntPtr textObject);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint FPDFTextObj_GetText(IntPtr textObject, IntPtr textPage, IntPtr buffer, uint length);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDFPageObj_GetFillColor(IntPtr pageObject, out uint red, out uint green, out uint blue, out uint alpha);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern float FPDF_GetPageWidthF(IntPtr page);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern float FPDF_GetPageHeightF(IntPtr page);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FPDFBitmap_GetStride(IntPtr bitmap);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FPDFBitmap_Destroy(IntPtr bitmap);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FPDF_ClosePage(IntPtr page);

        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FPDF_CloseDocument(IntPtr document);

        [StructLayout(LayoutKind.Sequential)]
        internal struct FsMatrix
        {
            internal float A;
            internal float B;
            internal float C;
            internal float D;
            internal float E;
            internal float F;
        }
    }
}
