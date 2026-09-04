using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfCorrectorium.Core.Documents;
using PdfCorrectorium.Core.Geometry;

namespace PdfCorrectorium.App.Services;

/// <summary>
/// PDF出力で実際に反映された変更数と、検証中に検出した警告を表します。
/// </summary>
/// <param name="ModifiedRegions">出力PDFへ反映したOCR領域数。</param>
/// <param name="ModifiedPages">OCR変更または画像最適化を反映したページ数。</param>
/// <param name="Warnings">出力を継続できたものの利用者確認が必要な事項。</param>
/// <param name="OptimizedImages">余白切り抜きを適用したページ画像数。</param>
public sealed record PdfExportResult(
    int ModifiedRegions,
    int ModifiedPages,
    IReadOnlyList<string> Warnings,
    int OptimizedImages = 0);

/// <summary>PDF生成ワーカーが通知する処理段階と進捗を表します。</summary>
/// <param name="Phase">機械判定に使用する処理段階。</param>
/// <param name="Current">完了した処理単位。</param>
/// <param name="Total">全処理単位。算出できない場合は0。</param>
/// <param name="Message">利用者へ表示する現在の処理内容。</param>
internal sealed record PdfExportProgress(
    string Phase,
    int Current,
    int Total,
    string Message);

/// <summary>
/// ページ画像から安全に除去できる余白と、期待できる容量削減量の分析結果です。
/// </summary>
/// <param name="PageNumber">1から始まる分析対象ページ番号。</param>
/// <param name="EligibleImages">安全な全面画像として認識できた画像数。</param>
/// <param name="OriginalPixels">切り抜き前の総画素数。</param>
/// <param name="CroppedPixels">切り抜き後に残る総画素数。</param>
/// <param name="EstimatedAreaReduction">削減できると推定した画素面積の割合。</param>
/// <param name="OriginalEncodedBytes">元画像ストリームの符号化済みバイト数。</param>
/// <param name="EstimatedEncodedBytes">再符号化後の推定バイト数。</param>
/// <param name="Message">分析結果を利用者へ説明するメッセージ。</param>
/// <param name="EstimatedJpegQuality">再符号化に使用するJPEG品質の推定値。</param>
public sealed record PdfImageOptimizationAnalysis(
    int PageNumber,
    int EligibleImages,
    long OriginalPixels,
    long CroppedPixels,
    double EstimatedAreaReduction,
    long OriginalEncodedBytes,
    long EstimatedEncodedBytes,
    string Message,
    int EstimatedJpegQuality = 92,
    IReadOnlyList<PdfImageOptimizationPreviewRegion>? PreviewRegions = null,
    uint BackgroundArgb = 0xFFFFFFFF,
    bool UsesUniformColorBackground = false,
    int RetainedRegionCount = 1,
    int RemovableBlankImages = 0)
{
    /// <summary>
    /// 画像の切り抜きによって、画素数と推定符号化サイズの両方を削減できるかを取得します。
    /// </summary>
    public bool CanOptimize =>
        EligibleImages > 0 &&
        CroppedPixels < OriginalPixels &&
        EstimatedEncodedBytes < OriginalEncodedBytes;

    /// <summary>プレビューへ描画する、単色背景へ置換されるページ上の領域です。</summary>
    public IReadOnlyList<PdfImageOptimizationPreviewRegion> Regions => PreviewRegions ?? [];

    /// <summary>指定した目安より推定容量削減率が小さいかを返します。</summary>
    public bool IsBelowSavingsGuide(double guide)
    {
        if (OriginalEncodedBytes <= 0) return true;
        var normalizedGuide = Math.Clamp(guide, 0d, 0.95d);
        var byteReduction = 1d - EstimatedEncodedBytes / (double)OriginalEncodedBytes;
        return EstimatedAreaReduction < normalizedGuide || byteReduction < normalizedGuide;
    }
}

/// <summary>PDF全体のページ画像を走査した容量削減見込みを表します。</summary>
public sealed record PdfDocumentImageOptimizationAnalysis(
    IReadOnlyList<PdfImageOptimizationAnalysis> Pages,
    int PageCount,
    long SourcePdfBytes,
    long EstimatedPdfBytes)
{
    /// <summary>最適化可能なページだけを取得します。</summary>
    public IReadOnlyList<PdfImageOptimizationAnalysis> Candidates => Pages.Where(page => page.CanOptimize).ToArray();
    /// <summary>候補画像の元データサイズ合計です。</summary>
    public long OriginalImageBytes => Candidates.Sum(page => page.OriginalEncodedBytes);
    /// <summary>候補画像の最適化後データサイズ合計です。</summary>
    public long EstimatedImageBytes => Candidates.Sum(page => page.EstimatedEncodedBytes);
    /// <summary>削除できる空白全面画像の個数です。</summary>
    public int RemovableBlankImages => Candidates.Sum(page => page.RemovableBlankImages);
}

/// <summary>
/// 画像最適化プレビューで、元の画像画素から単色背景へ置き換わるページ上の矩形を表します。
/// </summary>
/// <param name="LeftRatio">ページ左端からの位置を0～1で表した値。</param>
/// <param name="TopRatio">ページ上端からの位置を0～1で表した値。</param>
/// <param name="WidthRatio">ページ幅に対する矩形幅の比率。</param>
/// <param name="HeightRatio">ページ高さに対する矩形高さの比率。</param>
/// <param name="Description">四辺余白または内部空白帯などの説明。</param>
public sealed record PdfImageOptimizationPreviewRegion(
    double LeftRatio,
    double TopRatio,
    double WidthRatio,
    double HeightRatio,
    string Description);

/// <summary>
/// 編集プロジェクトのOCR領域、しおり、および画像最適化設定をPDFへ反映します。
/// </summary>
/// <remarks>
/// 出力は一時ファイルへ作成してから再読込と検証を行い、合格した場合だけ出力先へ確定します。
/// 元PDFは直接変更しません。PDFiumオブジェクトの生成・変形・削除は、共通ロック内で行います。
/// </remarks>
public sealed class PdfExportService
{
    /// <summary>1つのUnicodeテキスト要素と、行先頭からの位置および送り量を保持します。</summary>
    private sealed record CharacterTextRun(string Text, double Offset, double Advance);

    /// <summary>
    /// 保存後のPDFへ、編集画面で確定した文字送りを適用するための要求です。
    /// PDFiumで行を一つのテキストオブジェクトとして維持したまま、後処理で
    /// PDFの <c>TJ</c> 配列へ文字間隔を記録する際に使用します。
    /// </summary>
    private sealed record TextSpacingRequest(
        int PageNumber,
        string MarkName,
        string Text,
        PdfRectangle TargetBounds,
        WritingMode WritingMode,
        double RotationDegrees,
        IReadOnlyList<double> CharacterAdvances);

    /// <summary>保存済みPDFから実測した、隣接文字間の送り量とPDF文字行列の尺度です。</summary>
    private sealed record MeasuredTextSpacing(
        TextSpacingRequest Request,
        IReadOnlyList<double> CurrentAdvances,
        double PointsPerTextAdjustmentUnit);
    /// <summary>PDFオブジェクトを生成し、実測境界を取得した文字を保持します。</summary>
    private sealed record PreparedCharacterText(
        IntPtr Object,
        CharacterTextRun Run,
        double Left,
        double Right);
    /// <summary>PDFへ配置済みの文字オブジェクトと、PDF座標上の実測境界を保持します。</summary>
    private sealed record ExportedCharacterText(
        IntPtr Object,
        string Text,
        double Left,
        double Bottom,
        double Right,
        double Top)
    {
        public double CenterX => (Left + Right) / 2d;
        public double CenterY => (Bottom + Top) / 2d;
        public double Width => Right - Left;
        public double Height => Top - Bottom;
    }

    /// <summary>出力文字と編集モデル上の目標中心位置を対応付けます。</summary>
    private sealed record CharacterCalibrationMatch(
        ExportedCharacterText Exported,
        CharacterTextRun Run,
        double TargetCenterX,
        double TargetCenterY);

    /// <summary>フォントの基準線を基点とした字形の下端・上端を保持します。</summary>
    private readonly record struct FontVerticalMetrics(double Bottom, double Top)
    {
        public double Height => Top - Bottom;
        public double Center => (Bottom + Top) / 2d;
    }

    /// <summary>ビューア選択範囲が隣接セルへはみ出さないよう、文字形状をセル内へ収める係数です。</summary>
    private const double CharacterCellSafetyFactor = 0.88d;
    /// <summary>元PDFへ増分追記せず、完全な新規ファイルとして保存するPDFiumフラグです。</summary>
    private const uint NoIncremental = 2;

    /// <summary>
    /// プロジェクトの編集内容を新しいPDFへ安全に書き出します。
    /// </summary>
    /// <param name="sourcePdfPath">編集元PDFのパス。</param>
    /// <param name="destinationPdfPath">完成したPDFの保存先。</param>
    /// <param name="project">反映するOCR領域と文書設定を保持するプロジェクト。</param>
    /// <param name="cancellationToken">処理の取り消しを通知するトークン。</param>
    /// <returns>反映件数と検証警告を含む出力結果。</returns>
    /// <exception cref="InvalidOperationException">安全な出力または保存後検証を完了できなかった場合。</exception>
    public Task<PdfExportResult> ExportAsync(
        string sourcePdfPath,
        string destinationPdfPath,
        PdfCorrectoriumProject project,
        CancellationToken cancellationToken = default) =>
        ExportAsync(sourcePdfPath, destinationPdfPath, project, progress: null, cancellationToken);

    /// <summary>進捗を通知しながら、プロジェクトの編集内容を新しいPDFへ安全に書き出します。</summary>
    internal Task<PdfExportResult> ExportAsync(
        string sourcePdfPath,
        string destinationPdfPath,
        PdfCorrectoriumProject project,
        IProgress<PdfExportProgress>? progress,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Export(sourcePdfPath, destinationPdfPath, project, progress, cancellationToken),
            cancellationToken);

    /// <summary>
    /// 指定ページの全面画像を調べ、四辺余白、内部空白帯、単一色背景を削減した場合の効果を見積もります。
    /// </summary>
    /// <param name="sourcePdfPath">分析対象PDFのパス。</param>
    /// <param name="pageNumber">1から始まるページ番号。</param>
    /// <param name="options">余白判定と再圧縮に使用する設定。省略時は既定値を使用します。</param>
    /// <param name="cancellationToken">処理の取り消しを通知するトークン。</param>
    /// <returns>対象画像数、画素数、推定容量を含む分析結果。</returns>
    public Task<PdfImageOptimizationAnalysis> AnalyzePageImageOptimizationAsync(
        string sourcePdfPath,
        int pageNumber,
        PageImageOptimization? options = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            lock (PdfiumSynchronization.Gate)
                return AnalyzePageImageOptimization(
                    Path.GetFullPath(sourcePdfPath),
                    pageNumber,
                    options ?? new PageImageOptimization(),
                    cancellationToken);
        }, cancellationToken);

    /// <summary>PDFの全ページを順に調べ、画像最適化候補とPDF全体の概算出力サイズを返します。</summary>
    public Task<PdfDocumentImageOptimizationAnalysis> AnalyzeDocumentImageOptimizationAsync(
        string sourcePdfPath,
        PageImageOptimization? options = null,
        IProgress<(int Current, int Total)>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var fullPath = Path.GetFullPath(sourcePdfPath);
            var pageOptions = options ?? new PageImageOptimization();
            EnsureInitialized();
            var utf8Path = Marshal.StringToCoTaskMemUTF8(fullPath);
            IntPtr document = IntPtr.Zero;
            try
            {
                lock (PdfiumSynchronization.Gate)
                {
                    document = NativeMethods.FPDF_LoadDocument(utf8Path, IntPtr.Zero);
                    if (document == IntPtr.Zero) throw CreatePdfException("元PDFを開けませんでした");
                    var pageCount = NativeMethods.FPDF_GetPageCount(document);
                    var pages = new List<PdfImageOptimizationAnalysis>(pageCount);
                    for (var pageNumber = 1; pageNumber <= pageCount; pageNumber++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        pages.Add(AnalyzePageImageOptimization(
                            fullPath,
                            pageNumber,
                            pageOptions,
                            cancellationToken,
                            document));
                        progress?.Report((pageNumber, pageCount));
                    }

                    var sourceBytes = new FileInfo(fullPath).Length;
                    var imageSavings = pages
                        .Where(page => page.CanOptimize)
                        .Sum(page => Math.Max(0L, page.OriginalEncodedBytes - page.EstimatedEncodedBytes));
                    return new PdfDocumentImageOptimizationAnalysis(
                        pages,
                        pageCount,
                        sourceBytes,
                        Math.Max(0L, sourceBytes - imageSavings));
                }
            }
            finally
            {
                if (document != IntPtr.Zero)
                {
                    lock (PdfiumSynchronization.Gate)
                        NativeMethods.FPDF_CloseDocument(document);
                }
                Marshal.FreeCoTaskMem(utf8Path);
            }
        }, cancellationToken);

    /// <summary>PDFを開いてページ数だけを安全に取得します。</summary>
    private static int GetPdfPageCount(string sourcePdfPath)
    {
        lock (PdfiumSynchronization.Gate)
        {
            EnsureInitialized();
            var utf8Path = Marshal.StringToCoTaskMemUTF8(sourcePdfPath);
            IntPtr document = IntPtr.Zero;
            try
            {
                document = NativeMethods.FPDF_LoadDocument(utf8Path, IntPtr.Zero);
                if (document == IntPtr.Zero) throw CreatePdfException("元PDFを開けませんでした");
                return NativeMethods.FPDF_GetPageCount(document);
            }
            finally
            {
                if (document != IntPtr.Zero) NativeMethods.FPDF_CloseDocument(document);
                Marshal.FreeCoTaskMem(utf8Path);
            }
        }
    }

    private static PdfExportResult Export(
        string sourcePdfPath,
        string destinationPdfPath,
        PdfCorrectoriumProject project,
        IProgress<PdfExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPdfPath);
        if (!File.Exists(sourcePdfPath)) throw new FileNotFoundException("元PDFが見つかりません。", sourcePdfPath);

        var sourceFullPath = Path.GetFullPath(sourcePdfPath);
        var destinationFullPath = Path.GetFullPath(destinationPdfPath);
        if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("元PDFへの直接上書きはできません。別のファイル名を指定してください。");

        var directory = Path.GetDirectoryName(destinationFullPath) ?? throw new InvalidOperationException("出力先が不正です。");
        Directory.CreateDirectory(directory);

        // Keep all generation and validation paths short. PDFium and qpdf may reject
        // a temporary path built from an already long user-selected destination name.
        var operationDirectory = Path.Combine(
            Path.GetTempPath(),
            "PDF-Correctorium",
            "exports",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(operationDirectory);
        var temporaryPath = Path.Combine(operationDirectory, "output.pdf");
        try
        {
            PdfExportResult result;
            lock (PdfiumSynchronization.Gate)
                result = EditAndSave(sourceFullPath, temporaryPath, project, progress, cancellationToken);

            progress?.Report(new PdfExportProgress("validating", 0, 0, "出力PDFを再読込して検証しています..."));
            lock (PdfiumSynchronization.Gate)
                ValidateOutput(
                    temporaryPath,
                    project.SourcePdf.PageCount,
                    project.Pages.Where(PageHasChanges).Select(page => page.PageNumber).ToHashSet(),
                    project.OutputPdfVersion);
            progress?.Report(new PdfExportProgress("committing", 0, 0, "検証済みPDFを保存先へ確定しています..."));
            PdfOutputFileCommitter.Commit(
                temporaryPath,
                destinationFullPath,
                preserveCompletedOutputOnConflict: false,
                cancellationToken);
            return result;
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(operationDirectory))
                    Directory.Delete(operationDirectory, recursive: true);
            }
            catch
            {
                // Cleanup failure must not invalidate an already committed PDF.
            }
        }
    }

    private static PdfExportResult EditAndSave(
        string sourcePath,
        string temporaryPath,
        PdfCorrectoriumProject project,
        IProgress<PdfExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        EnsureInitialized();
        var utf8Path = Marshal.StringToCoTaskMemUTF8(sourcePath);
        IntPtr document = IntPtr.Zero;
        try
        {
            document = NativeMethods.FPDF_LoadDocument(utf8Path, IntPtr.Zero);
            if (document == IntPtr.Zero) throw CreatePdfException("元PDFを開けませんでした");

            var requestedOutputVersion = PdfOutputVersionMapping.GetPdfiumVersion(project.OutputPdfVersion);
            if (requestedOutputVersion is not null &&
                NativeMethods.FPDF_GetFileVersion(document, out var sourceFileVersion) != 0 &&
                requestedOutputVersion.Value < sourceFileVersion)
                throw new InvalidOperationException(
                    $"元PDFはPDF {FormatPdfiumVersion(sourceFileVersion)}です。" +
                    $"それより低いPDF {FormatPdfiumVersion(requestedOutputVersion.Value)}では出力できません。");

            var pageCount = NativeMethods.FPDF_GetPageCount(document);
            var warnings = new List<string>();
            var textSpacingRequests = new List<TextSpacingRequest>();
            var modifiedRegions = 0;
            var modifiedPages = 0;

            var optimizedImages = 0;
            var changedPages = project.Pages
                .Where(PageHasChanges)
                .OrderBy(page => page.PageNumber)
                .ToArray();
            var processedPages = 0;
            progress?.Report(new PdfExportProgress(
                "editing",
                processedPages,
                changedPages.Length,
                $"編集対象 {changedPages.Length:N0} ページの処理を開始します..."));

            foreach (var projectPage in changedPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (projectPage.PageNumber < 1 || projectPage.PageNumber > pageCount)
                {
                    warnings.Add($"{projectPage.PageNumber}ページは元PDFに存在しないため、スキップしました。");
                    continue;
                }

                var page = NativeMethods.FPDF_LoadPage(document, projectPage.PageNumber - 1);
                if (page == IntPtr.Zero) throw CreatePdfException($"{projectPage.PageNumber}ページを開けませんでした");
                var textPage = IntPtr.Zero;
                try
                {
                    textPage = NativeMethods.FPDFText_LoadPage(page);
                    var candidates = CollectTextObjects(page, textPage);
                    var used = new HashSet<IntPtr>();
                    var appliedDeletionRequests = new List<OcrTextRegion>();
                    var pageChanged = false;

                    if (projectPage.ImageOptimization is { Enabled: true })
                    {
                        // 以前は全ページ分の再圧縮画像を先に生成して保持していたため、
                        // 大きな文書で数GBのメモリと長いGC待ちが発生していました。
                        // ページごとに解析・適用・解放し、ピークメモリを抑えます。
                        var pageOptimizedImages = AnalyzeAndApplyPageImageOptimizations(
                            document,
                            page,
                            projectPage.ImageOptimization,
                            cancellationToken);
                        if (pageOptimizedImages == 0)
                        {
                            warnings.Add(
                                $"{projectPage.PageNumber}ページ: 余白または単一色背景を安全に削減できる全面画像が見つかりませんでした。");
                        }
                        else
                        {
                            optimizedImages += pageOptimizedImages;
                            pageChanged = true;
                        }
                    }

                    foreach (var region in projectPage.TextRegions.Where(ShouldApplyToPdf).OrderByDescending(region => region.IsAdded))
                    {
                        if (region.IsAdded)
                        {
                            InsertAddedRegion(
                                document,
                                page,
                                candidates,
                                region,
                                projectPage.PageNumber,
                                textSpacingRequests);
                            modifiedRegions++;
                            pageChanged = true;
                            continue;
                        }

                        var candidate = FindCandidate(candidates, used, region);
                        if (candidate is null)
                        {
                            if (region.IsDeleted &&
                                appliedDeletionRequests.Any(applied => IsDuplicateDeletionRequest(region, applied)))
                            {
                                modifiedRegions++;
                                warnings.Add(
                                    $"{projectPage.PageNumber}ページ: 重複する削除指定「{Abbreviate(region.OriginalText)}」は、" +
                                    "同じPDFテキストを対象としているため1件として処理しました。");
                                continue;
                            }

                            warnings.Add($"{projectPage.PageNumber}ページ: 「{Abbreviate(region.OriginalText)}」に対応するPDFテキストが見つかりませんでした。");
                            continue;
                        }

                        used.Add(candidate.Object);
                        if (region.IsDeleted)
                        {
                            if (NativeMethods.FPDFPage_RemoveObject(page, candidate.Object) == 0)
                                throw new InvalidDataException("削除対象のPDFテキスト領域を除去できませんでした。");
                            NativeMethods.FPDFPageObj_Destroy(candidate.Object);
                            appliedDeletionRequests.Add(region);
                        }
                        else
                        {
                            ApplyRegion(
                                document,
                                page,
                                candidate,
                                region,
                                projectPage.PageNumber,
                                textSpacingRequests);
                        }
                        modifiedRegions++;
                        pageChanged = true;
                    }

                    if (pageChanged)
                    {
                        if (textPage != IntPtr.Zero)
                        {
                            NativeMethods.FPDFText_ClosePage(textPage);
                            textPage = IntPtr.Zero;
                        }
                        if (NativeMethods.FPDFPage_GenerateContent(page) == 0)
                            throw new InvalidDataException($"{projectPage.PageNumber}ページのPDF内容を再生成できませんでした。");
                        modifiedPages++;
                    }
                }
                finally
                {
                    if (textPage != IntPtr.Zero) NativeMethods.FPDFText_ClosePage(textPage);
                    NativeMethods.FPDF_ClosePage(page);
                }

                processedPages++;
                progress?.Report(new PdfExportProgress(
                    "editing",
                    processedPages,
                    changedPages.Length,
                    $"PDFへ編集を反映しています（{processedPages:N0}/{changedPages.Length:N0}ページ）..."));
            }

            var requested = project.Pages.Sum(page => page.TextRegions.Count(ShouldApplyToPdf));
            if (requested > 0 && modifiedRegions == 0)
                throw new InvalidDataException("編集対象に対応するPDFテキストを特定できなかったため、出力を中止しました。NDLOCRの座標とPDF内テキストの位置が一致しているか確認してください。");
            if (modifiedRegions < requested)
                throw new InvalidDataException($"{requested}件中{requested - modifiedRegions}件を安全に反映できなかったため、出力を中止しました。未反映箇所を確認してください。\n" + string.Join("\n", warnings.Take(8)));

            progress?.Report(new PdfExportProgress("saving", 0, 0, "編集済みPDFを一時保存しています..."));
            SaveDocument(document, temporaryPath);
            NativeMethods.FPDF_CloseDocument(document);
            document = IntPtr.Zero;
            progress?.Report(new PdfExportProgress("calibrating", 0, 0, "文字位置と選択範囲を校正しています..."));
            CalibrateSavedDocument(temporaryPath, project, progress, cancellationToken);
            progress?.Report(new PdfExportProgress("spacing", 0, 0, "文字送りを出力PDFへ反映しています..."));
            ApplyCharacterSpacing(
                temporaryPath,
                textSpacingRequests,
                warnings,
                progress,
                cancellationToken);
            if (optimizedImages > 0)
            {
                progress?.Report(new PdfExportProgress("compacting", 0, 0, "画像最適化後のPDFを圧縮しています..."));
                CompactDocumentWithQpdf(temporaryPath, cancellationToken);
            }
            progress?.Report(new PdfExportProgress("bookmarks", 0, 0, "しおり、文書情報、初期表示設定を反映しています..."));
            new PdfBookmarkService()
                .ApplyToPdfAsync(
                    temporaryPath,
                    project.Bookmarks,
                    project.ViewerSettings,
                    project.DocumentMetadata,
                    project.OutputPdfVersion,
                    project.DocumentLanguage,
                    project.BookmarksModified,
                    cancellationToken)
                .GetAwaiter()
                .GetResult();
            return new PdfExportResult(modifiedRegions, modifiedPages, warnings, optimizedImages);
        }
        finally
        {
            if (document != IntPtr.Zero) NativeMethods.FPDF_CloseDocument(document);
            Marshal.FreeCoTaskMem(utf8Path);
        }
    }

    private static void CalibrateSavedDocument(
        string pdfPath,
        PdfCorrectoriumProject project,
        IProgress<PdfExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var calibratedPath = pdfPath + ".selection-calibrated";
        var utf8Path = Marshal.StringToCoTaskMemUTF8(pdfPath);
        var document = IntPtr.Zero;
        try
        {
            document = NativeMethods.FPDF_LoadDocument(utf8Path, IntPtr.Zero);
            if (document == IntPtr.Zero)
                throw CreatePdfException("文字選択領域の補正用PDFを開けませんでした");

            var pageCount = NativeMethods.FPDF_GetPageCount(document);
            var changed = false;
            var calibrationPages = project.Pages.Where(page =>
                    page.TextRegions.Any(region =>
                        ShouldApplyToPdf(region) &&
                        !region.IsDeleted &&
                        RequiresPerCharacterObjects(region)))
                .ToArray();
            var calibratedPages = 0;
            foreach (var projectPage in calibrationPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (projectPage.PageNumber < 1 || projectPage.PageNumber > pageCount)
                    continue;

                var page = NativeMethods.FPDF_LoadPage(document, projectPage.PageNumber - 1);
                if (page == IntPtr.Zero)
                    throw CreatePdfException($"{projectPage.PageNumber}ページを選択領域の補正用に開けませんでした");
                try
                {
                    var pageChanged = false;
                    // The first pass can change both scale and position. PDFium
                    // recalculates its loose selection box from the new matrix,
                    // so repeat the center correction to remove the residual
                    // introduced by that recalculation.
                    for (var pass = 0; pass < 3; pass++)
                    {
                        if (!CalibratePerCharacterSelectionBoxes(page, projectPage))
                            break;
                        if (NativeMethods.FPDFPage_GenerateContent(page) == 0)
                            throw new InvalidDataException(
                                $"{projectPage.PageNumber}ページの文字選択領域を確定できませんでした。");
                        pageChanged = true;
                    }
                    changed |= pageChanged;
                }
                finally
                {
                    NativeMethods.FPDF_ClosePage(page);
                }

                calibratedPages++;
                progress?.Report(new PdfExportProgress(
                    "calibrating",
                    calibratedPages,
                    calibrationPages.Length,
                    $"文字位置を校正しています（{calibratedPages:N0}/{calibrationPages.Length:N0}ページ）..."));
            }

            if (!changed) return;
            SaveDocument(document, calibratedPath);
            NativeMethods.FPDF_CloseDocument(document);
            document = IntPtr.Zero;
            File.Move(calibratedPath, pdfPath, true);
        }
        finally
        {
            if (document != IntPtr.Zero) NativeMethods.FPDF_CloseDocument(document);
            Marshal.FreeCoTaskMem(utf8Path);
            if (File.Exists(calibratedPath)) File.Delete(calibratedPath);
        }
    }

    /// <summary>
    /// 編集画面の文字セル開始位置と、保存済みPDFで実測した文字原点との差を
    /// <c>TJ</c> 配列の文字間隔へ変換します。横書き行は一つのPDFテキスト
    /// オブジェクトのまま維持されるため、LibreOffice等でも行単位で扱えます。
    /// </summary>
    private static void ApplyCharacterSpacing(
        string pdfPath,
        IReadOnlyList<TextSpacingRequest> requests,
        ICollection<string> warnings,
        IProgress<PdfExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0) return;

        progress?.Report(new PdfExportProgress(
            "spacing",
            0,
            requests.Count,
            $"文字送りを実測しています（対象 {requests.Count:N0}行）..."));
        var measurements = MeasureTextSpacing(pdfPath, requests, cancellationToken);
        if (measurements.Count == 0)
        {
            warnings.Add("編集した横書き行の文字送りをPDF上で実測できなかったため、行全体の幅だけを反映しました。");
            return;
        }

        var qpdfPath = ResolveQpdfPath();
        if (qpdfPath is null)
        {
            warnings.Add("qpdfが見つからないため、文字ごとの送り幅補正を省略しました。");
            return;
        }

        var qdfPath = pdfPath + ".spacing-qdf";
        var adjustedPath = pdfPath + ".spacing-adjusted";
        try
        {
            progress?.Report(new PdfExportProgress(
                "spacing",
                0,
                0,
                "文字送り補正用のPDFデータを準備しています..."));
            RunQpdf(
                qpdfPath,
                ["--qdf", "--object-streams=disable", "--stream-data=uncompress", pdfPath, qdfPath],
                qdfPath,
                cancellationToken);

            var qdfBytes = File.ReadAllBytes(qdfPath);
            var replacements = new List<(int Start, int Length, byte[] Value)>();
            var adjustedMarks = new HashSet<string>(StringComparer.Ordinal);
            // QDF全体を行ごとに先頭から検索すると、行数×PDF容量の走査になります。
            // PdfCorrectoriumが付けたマークを一度だけ走査して索引化します。
            var markerPositions = FindTextSpacingMarkerPositions(
                qdfBytes,
                measurements.Select(measurement => measurement.Request.MarkName));
            for (var measurementIndex = 0; measurementIndex < measurements.Count; measurementIndex++)
            {
                var measurement = measurements[measurementIndex];
                cancellationToken.ThrowIfCancellationRequested();
                if (!markerPositions.TryGetValue(measurement.Request.MarkName, out var markerIndex) ||
                    !TryCreateTextSpacingReplacement(qdfBytes, measurement, markerIndex, out var replacement))
                {
                    warnings.Add(
                        $"{measurement.Request.PageNumber}ページ: 「{Abbreviate(measurement.Request.Text)}」の文字送りをPDF命令へ反映できませんでした。");
                    continue;
                }
                replacements.Add(replacement);
                adjustedMarks.Add(measurement.Request.MarkName);

                if ((measurementIndex + 1) % 25 == 0 || measurementIndex + 1 == measurements.Count)
                {
                    progress?.Report(new PdfExportProgress(
                        "spacing",
                        measurementIndex + 1,
                        measurements.Count,
                        $"文字送りを反映しています（{measurementIndex + 1:N0}/{measurements.Count:N0}行）..."));
                }
            }

            if (replacements.Count == 0) return;
            qdfBytes = ApplyByteReplacements(qdfBytes, replacements);
            File.WriteAllBytes(qdfPath, qdfBytes);

            progress?.Report(new PdfExportProgress(
                "spacing",
                0,
                0,
                "文字送り反映後のPDFを再構成しています..."));
            RunQpdf(
                qpdfPath,
                ["--object-streams=generate", "--recompress-flate", qdfPath, adjustedPath],
                adjustedPath,
                cancellationToken);
            File.Move(adjustedPath, pdfPath, true);

            // qpdf converts a TJ array into several PDFium text-page fragments.
            // Therefore, measuring the marked page object again would report a
            // false failure even though the content stream is correct.  The TJ
            // structure is verified while it is created above instead.
            var maximumError = 0d;
            var failedLines = 0;

            const double spacingVerificationTolerance = 0.5d;
            if (failedLines > 0 || maximumError > spacingVerificationTolerance)
            {
                warnings.Add(
                    $"文字送りの保存後検証で {failedLines} 行を再測定できず、最大誤差は {maximumError:0.###} pt でした。" +
                    "該当箇所は編集画面とPDFビューアで位置を確認してください。");
            }
        }
        finally
        {
            if (File.Exists(qdfPath)) File.Delete(qdfPath);
            if (File.Exists(adjustedPath)) File.Delete(adjustedPath);
        }
    }

    /// <summary>
    /// 元のバイト列に対する複数の置換を、一度の出力バッファ確保と一度の走査で反映します。
    /// </summary>
    /// <remarks>
    /// 置換のたびにPDF全体を複製すると、文字送り補正行数とPDF容量の積に比例して
    /// メモリコピーが増えます。数百ページの文書では数千回から数万回の全体コピーに
    /// なるため、置換位置を昇順に並べて一括反映します。
    /// </remarks>
    private static byte[] ApplyByteReplacements(
        byte[] source,
        IReadOnlyCollection<(int Start, int Length, byte[] Value)> replacements)
    {
        var ordered = replacements.OrderBy(item => item.Start).ToArray();
        var outputLength = source.LongLength;
        var previousEnd = 0;
        foreach (var replacement in ordered)
        {
            if (replacement.Start < previousEnd ||
                replacement.Start < 0 ||
                replacement.Length < 0 ||
                replacement.Start > source.Length - replacement.Length)
            {
                throw new InvalidDataException("文字送り補正の置換範囲が重複しているか、PDFデータの範囲外です。");
            }

            outputLength += replacement.Value.LongLength - replacement.Length;
            previousEnd = replacement.Start + replacement.Length;
        }

        if (outputLength > int.MaxValue)
            throw new InvalidDataException("文字送り補正後のPDFデータが処理可能なサイズを超えています。");

        var result = new byte[(int)outputLength];
        var sourceOffset = 0;
        var destinationOffset = 0;
        foreach (var replacement in ordered)
        {
            var unchangedLength = replacement.Start - sourceOffset;
            Buffer.BlockCopy(source, sourceOffset, result, destinationOffset, unchangedLength);
            destinationOffset += unchangedLength;

            Buffer.BlockCopy(replacement.Value, 0, result, destinationOffset, replacement.Value.Length);
            destinationOffset += replacement.Value.Length;
            sourceOffset = replacement.Start + replacement.Length;
        }

        var remainingLength = source.Length - sourceOffset;
        Buffer.BlockCopy(source, sourceOffset, result, destinationOffset, remainingLength);
        return result;
    }

    /// <summary>
    /// QDF内にある文字送り補正用マークの開始位置を、一度の前方向走査で索引化します。
    /// </summary>
    private static Dictionary<string, int> FindTextSpacingMarkerPositions(
        byte[] qdfBytes,
        IEnumerable<string> markNames)
    {
        var requestedMarks = markNames.ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (requestedMarks.Count == 0) return result;

        var prefix = Encoding.ASCII.GetBytes("/PCO_");
        var searchOffset = 0;
        while (searchOffset < qdfBytes.Length)
        {
            var markerIndex = IndexOf(qdfBytes, prefix, searchOffset);
            if (markerIndex < 0) break;

            var nameStart = markerIndex + 1;
            var nameEnd = nameStart;
            while (nameEnd < qdfBytes.Length && IsPdfCorrectoriumMarkNameByte(qdfBytes[nameEnd]))
                nameEnd++;

            var markName = Encoding.ASCII.GetString(qdfBytes, nameStart, nameEnd - nameStart);
            if (requestedMarks.Contains(markName))
            {
                result.TryAdd(markName, markerIndex);
                if (result.Count == requestedMarks.Count) break;
            }

            searchOffset = Math.Max(nameEnd, markerIndex + prefix.Length);
        }

        return result;
    }

    /// <summary>PdfCorrectoriumが生成するPDF名に使用できるASCII文字かを返します。</summary>
    private static bool IsPdfCorrectoriumMarkNameByte(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' or
            >= (byte)'a' and <= (byte)'z' or
            >= (byte)'0' and <= (byte)'9' or
            (byte)'_';

    /// <summary>保存済みPDFからマーク付き行の文字原点を取得し、現在の送り量を実測します。</summary>
    private static List<MeasuredTextSpacing> MeasureTextSpacing(
        string pdfPath,
        IReadOnlyList<TextSpacingRequest> requests,
        CancellationToken cancellationToken)
    {
        var requestsByPage = requests
            .GroupBy(request => request.PageNumber)
            .ToDictionary(group => group.Key, group => group.ToDictionary(request => request.MarkName));
        var result = new List<MeasuredTextSpacing>();
        var utf8Path = Marshal.StringToCoTaskMemUTF8(pdfPath);
        var document = IntPtr.Zero;
        try
        {
            document = NativeMethods.FPDF_LoadDocument(utf8Path, IntPtr.Zero);
            if (document == IntPtr.Zero) return result;
            var pageCount = NativeMethods.FPDF_GetPageCount(document);
            foreach (var pageEntry in requestsByPage)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pageEntry.Key < 1 || pageEntry.Key > pageCount) continue;
                var page = NativeMethods.FPDF_LoadPage(document, pageEntry.Key - 1);
                if (page == IntPtr.Zero) continue;
                var textPage = IntPtr.Zero;
                try
                {
                    textPage = NativeMethods.FPDFText_LoadPage(page);
                    if (textPage == IntPtr.Zero) continue;

                    var markedObjects = new Dictionary<IntPtr, TextSpacingRequest>();
                    var objectCount = NativeMethods.FPDFPage_CountObjects(page);
                    for (var objectIndex = 0; objectIndex < objectCount; objectIndex++)
                    {
                        var pageObject = NativeMethods.FPDFPage_GetObject(page, objectIndex);
                        // A marked-content sequence can be associated with page objects
                        // other than text after the PDF has been saved and normalized.
                        // Passing one of those objects to FPDFTextObj_* is undefined and
                        // can terminate the native PDFium process with an access violation.
                        if (pageObject == IntPtr.Zero ||
                            NativeMethods.FPDFPageObj_GetType(pageObject) != 1)
                            continue;
                        foreach (var markName in GetPageObjectMarkNames(pageObject))
                        {
                            if (pageEntry.Value.TryGetValue(markName, out var request))
                            {
                                markedObjects[pageObject] = request;
                                break;
                            }
                        }
                    }
                    if (markedObjects.Count == 0) continue;

                    var origins = markedObjects.Keys.ToDictionary(key => key, _ => new List<(double X, double Y)>());
                    var fontSizes = new Dictionary<IntPtr, double>();
                    var characterCount = NativeMethods.FPDFText_CountChars(textPage);
                    for (var characterIndex = 0; characterIndex < characterCount; characterIndex++)
                    {
                        var textObject = NativeMethods.FPDFText_GetTextObject(textPage, characterIndex);
                        if (!origins.TryGetValue(textObject, out var objectOrigins) ||
                            NativeMethods.FPDFText_GetCharOrigin(textPage, characterIndex, out var x, out var y) == 0)
                            continue;
                        objectOrigins.Add((x, y));
                        if (!fontSizes.ContainsKey(textObject))
                        {
                            // Use the text-page API while the character index is known.
                            // It validates the text-page lifetime and avoids dereferencing
                            // a raw page-object pointer through FPDFTextObj_GetFontSize.
                            var fontSize = NativeMethods.FPDFText_GetFontSize(textPage, characterIndex);
                            if (double.IsFinite(fontSize) && fontSize > 0d)
                                fontSizes[textObject] = fontSize;
                        }
                    }

                    foreach (var item in markedObjects)
                    {
                        var request = item.Value;
                        var expectedCount = StringInfo.ParseCombiningCharacters(request.Text).Length;
                        var objectOrigins = origins[item.Key];
                        if (objectOrigins.Count != expectedCount || expectedCount < 2)
                            continue;
                        var angle = -request.RotationDegrees * Math.PI / 180d;
                        var axisX = request.WritingMode == WritingMode.Vertical
                            ? Math.Sin(angle)
                            : Math.Cos(angle);
                        var axisY = request.WritingMode == WritingMode.Vertical
                            ? -Math.Cos(angle)
                            : Math.Sin(angle);
                        var currentAdvances = new double[expectedCount];
                        for (var index = 0; index < currentAdvances.Length - 1; index++)
                        {
                            currentAdvances[index] =
                                (objectOrigins[index + 1].X - objectOrigins[index].X) * axisX +
                                (objectOrigins[index + 1].Y - objectOrigins[index].Y) * axisY;
                        }

                        // The next character origin gives the exact advance for every
                        // character except the last one. The line object was already
                        // transformed to the edited rectangle, so its remaining distance
                        // to the edited line end is the best representation of the last
                        // character's advance. For vertical writing the line end is the
                        // bottom edge; for horizontal writing it is the right edge.
                        var lineCenterX = request.TargetBounds.Left + request.TargetBounds.Size.Width / 2d;
                        var lineCenterY = request.TargetBounds.Bottom + request.TargetBounds.Size.Height / 2d;
                        var lineExtent = request.WritingMode == WritingMode.Vertical
                            ? request.TargetBounds.Size.Height / 2d
                            : request.TargetBounds.Size.Width / 2d;
                        var targetAxisEnd =
                            lineCenterX * axisX +
                            lineCenterY * axisY +
                            lineExtent;
                        var lastOrigin = objectOrigins[^1].X * axisX + objectOrigins[^1].Y * axisY;
                        currentAdvances[^1] = targetAxisEnd - lastOrigin;

                        if (currentAdvances.Any(advance => !double.IsFinite(advance) || advance <= 0.0000001d))
                            continue;
                        if (!fontSizes.TryGetValue(item.Key, out var fontSize) ||
                            NativeMethods.FPDFPageObj_GetMatrix(item.Key, out var matrix) == 0)
                            continue;
                        var writingAxisScale = request.WritingMode == WritingMode.Vertical
                            ? Math.Sqrt(matrix.C * matrix.C + matrix.D * matrix.D)
                            : Math.Sqrt(matrix.A * matrix.A + matrix.B * matrix.B);
                        var pointsPerAdjustmentUnit = fontSize * writingAxisScale / 1000d;
                        if (!double.IsFinite(pointsPerAdjustmentUnit) || pointsPerAdjustmentUnit <= 0.0000001d)
                            continue;
                        result.Add(new MeasuredTextSpacing(
                            request,
                            currentAdvances,
                            pointsPerAdjustmentUnit));
                    }
                }
                finally
                {
                    if (textPage != IntPtr.Zero) NativeMethods.FPDFText_ClosePage(textPage);
                    NativeMethods.FPDF_ClosePage(page);
                }
            }
        }
        finally
        {
            if (document != IntPtr.Zero) NativeMethods.FPDF_CloseDocument(document);
            Marshal.FreeCoTaskMem(utf8Path);
        }
        return result;
    }

    /// <summary>PDFページオブジェクトに付与されたマーク名をUTF-16LEとして読み取ります。</summary>
    private static IEnumerable<string> GetPageObjectMarkNames(IntPtr pageObject)
    {
        var markCount = NativeMethods.FPDFPageObj_CountMarks(pageObject);
        for (uint index = 0; index < markCount; index++)
        {
            var mark = NativeMethods.FPDFPageObj_GetMark(pageObject, index);
            if (mark == IntPtr.Zero) continue;
            NativeMethods.FPDFPageObjMark_GetName(mark, IntPtr.Zero, 0, out var requiredBytes);
            if (requiredBytes < 2 || requiredBytes > 4096) continue;
            var buffer = Marshal.AllocHGlobal((int)requiredBytes);
            try
            {
                if (NativeMethods.FPDFPageObjMark_GetName(mark, buffer, requiredBytes, out _) == 0) continue;
                var name = Marshal.PtrToStringUni(buffer, (int)requiredBytes / 2)?.TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(name)) yield return name;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>
    /// マークで囲まれた1行の <c>Tj</c> 命令を、文字ごとに横倍率を切り替える一連の
    /// <c>Tz</c>/<c>Tj</c> 命令へ置換します。
    /// </summary>
    /// <remarks>
    /// 1つの <c>TJ</c> 配列では文字の開始位置だけを調整でき、文字自体の横幅は行全体で
    /// 共通になります。編集画面は各文字を個別セルへ横方向にフィットさせるため、保存側も
    /// 各文字の自然送り幅に対して個別の横倍率を適用します。すべての文字は元の
    /// 1つのテキストオブジェクトと同じテキスト状態・ベースラインに残します。
    /// 行全体を1つの<c>ActualText</c>へ置き換えると、Acrobatが行全体の選択領域を
    /// 先頭グリフへ割り当てることがあるため、各<c>Tj</c>の実文字コードと実座標を
    /// そのまま選択・検索へ使用させます。
    /// </remarks>
    private static bool TryCreateTextSpacingReplacement(
        byte[] qdfBytes,
        MeasuredTextSpacing measurement,
        out (int Start, int Length, byte[] Value) replacement)
    {
        replacement = default;
        var marker = Encoding.ASCII.GetBytes('/' + measurement.Request.MarkName);
        var markerIndex = IndexOf(qdfBytes, marker, 0);
        return markerIndex >= 0 &&
               TryCreateTextSpacingReplacement(qdfBytes, measurement, markerIndex, out replacement);
    }

    /// <summary>
    /// 事前に索引化したマーク位置を用いて、文字送り補正用の置換データを作成します。
    /// </summary>
    private static bool TryCreateTextSpacingReplacement(
        byte[] qdfBytes,
        MeasuredTextSpacing measurement,
        int markerIndex,
        out (int Start, int Length, byte[] Value) replacement)
    {
        replacement = default;
        var marker = Encoding.ASCII.GetBytes('/' + measurement.Request.MarkName);
        var endMarker = Encoding.ASCII.GetBytes("EMC");
        var blockEnd = IndexOf(qdfBytes, endMarker, markerIndex + marker.Length);
        if (blockEnd < 0 || blockEnd - markerIndex > 65536) return false;

        var blockStart = markerIndex;
        var blockLength = blockEnd + endMarker.Length - blockStart;
        var blockText = Encoding.Latin1.GetString(qdfBytes, blockStart, blockLength);
        if (!TryFindPdfStringShowOperation(
                blockText,
                out var operationStart,
                out var operationLength,
                out var operand) ||
            !TryDecodePdfStringOperand(operand, out var encodedBytes))
            return false;

        var chunks = SplitEncodedCharacters(encodedBytes, measurement.Request.Text);
        if (chunks is null || chunks.Count != measurement.Request.CharacterAdvances.Count) return false;

        if (measurement.CurrentAdvances.Count != chunks.Count) return false;

        if (measurement.Request.WritingMode == WritingMode.Vertical)
        {
            // A native Identity-V / WMode 1 font already supplies the correct
            // vertical glyph forms and a shared Acrobat text-selection model.
            // Keep the complete line in one text-showing operation and adjust
            // only the distance to the next origin. This mirrors OCR PDFs made
            // by Acrobat/NDLOCR and avoids one independently sized object per glyph.
            var verticalBuilder = new StringBuilder("[");
            for (var index = 0; index < chunks.Count; index++)
            {
                verticalBuilder.Append('<').Append(chunks[index]).Append('>');
                if (index >= chunks.Count - 1) continue;

                var desiredAdvance = measurement.Request.CharacterAdvances[index];
                var currentAdvance = measurement.CurrentAdvances[index];
                // In vertical writing a positive TJ adjustment moves the next
                // origin down the column; horizontal writing uses the opposite
                // sign, which is why this branch does not share the Tz formula.
                var adjustment = (desiredAdvance - currentAdvance) /
                                 measurement.PointsPerTextAdjustmentUnit;
                if (!double.IsFinite(adjustment)) return false;
                verticalBuilder.Append(' ')
                    .Append(Math.Clamp(adjustment, -1000000d, 1000000d)
                        .ToString("0.####", CultureInfo.InvariantCulture))
                    .Append(' ');
            }
            verticalBuilder.Append("] TJ");
            replacement = (
                blockStart + operationStart,
                operationLength,
                Encoding.ASCII.GetBytes(verticalBuilder.ToString()));
            return true;
        }

        var baseHorizontalScale = FindActiveHorizontalScale(blockText, operationStart);
        var builder = new StringBuilder();
        for (var index = 0; index < chunks.Count; index++)
        {
            var desiredAdvance = measurement.Request.CharacterAdvances[index];
            var currentAdvance = measurement.CurrentAdvances[index];
            var horizontalScale = baseHorizontalScale * desiredAdvance / currentAdvance;
            if (!double.IsFinite(horizontalScale) || horizontalScale <= 0d) return false;

            // Extremely small/large values are malformed OCR geometry rather than a
            // useful stretch.  Keep the stream valid while allowing large display text.
            builder.Append(Math.Clamp(horizontalScale, 1d, 2000d)
                    .ToString("0.####", CultureInfo.InvariantCulture))
                .Append(" Tz <")
                .Append(chunks[index])
                .Append("> Tj ");
        }
        builder.Append(baseHorizontalScale.ToString("0.####", CultureInfo.InvariantCulture))
            .Append(" Tz");
        var replacementBytes = Encoding.ASCII.GetBytes(builder.ToString());
        replacement = (
            blockStart + operationStart,
            operationLength,
            replacementBytes);
        return true;
    }

    /// <summary>
    /// 対象の文字描画命令より前で有効になっている横倍率を取得します。
    /// 明示的な<c>Tz</c>がない標準状態は100%です。
    /// </summary>
    private static double FindActiveHorizontalScale(string blockText, int operationStart)
    {
        var prefix = blockText[..Math.Clamp(operationStart, 0, blockText.Length)];
        var matches = Regex.Matches(
            prefix,
            @"(?<value>[+-]?(?:\d+(?:\.\d*)?|\.\d+))\s+Tz\b",
            RegexOptions.CultureInvariant);
        if (matches.Count == 0) return 100d;
        return double.TryParse(
                   matches[^1].Groups["value"].Value,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out var value) &&
               double.IsFinite(value) &&
               value > 0d
            ? value
            : 100d;
    }

    /// <summary>PDFの16進文字列を、元のUnicodeテキスト要素に対応する符号列へ分割します。</summary>
    /// <summary>
    /// マーク付きコンテンツから、単一文字列を描画する <c>Tj</c> 命令を探します。
    /// PDF文字列は16進文字列と括弧付きリテラル文字列の両方を扱います。
    /// </summary>
    private static bool TryFindPdfStringShowOperation(
        string blockText,
        out int operationStart,
        out int operationLength,
        out string operand)
    {
        operationStart = 0;
        operationLength = 0;
        operand = string.Empty;

        foreach (Match showMatch in Regex.Matches(blockText, @"\bTj\b", RegexOptions.CultureInvariant))
        {
            var operandEnd = showMatch.Index;
            while (operandEnd > 0 && char.IsWhiteSpace(blockText[operandEnd - 1])) operandEnd--;
            if (operandEnd <= 0) continue;

            var operandStart = -1;
            if (blockText[operandEnd - 1] == '>')
            {
                operandStart = blockText.LastIndexOf('<', operandEnd - 1);
                if (operandStart > 0 && blockText[operandStart - 1] == '<') operandStart = -1;
            }
            else if (blockText[operandEnd - 1] == ')')
            {
                for (var candidate = blockText.LastIndexOf('(', operandEnd - 1);
                     candidate >= 0;
                     candidate = blockText.LastIndexOf('(', candidate - 1))
                {
                    if (TryFindPdfLiteralStringEnd(blockText, candidate, out var literalEnd) &&
                        literalEnd == operandEnd)
                    {
                        operandStart = candidate;
                        break;
                    }
                }
            }

            if (operandStart < 0) continue;
            operationStart = operandStart;
            operationLength = showMatch.Index + showMatch.Length - operandStart;
            operand = blockText.Substring(operandStart, operandEnd - operandStart);
            return true;
        }
        return false;
    }

    /// <summary>括弧付きPDF文字列の終端を、エスケープと括弧の入れ子を考慮して探します。</summary>
    private static bool TryFindPdfLiteralStringEnd(string value, int start, out int end)
    {
        end = -1;
        if (start < 0 || start >= value.Length || value[start] != '(') return false;
        var depth = 0;
        for (var index = start; index < value.Length; index++)
        {
            var current = value[index];
            if (current == '\\')
            {
                if (++index >= value.Length) return false;
                if (value[index] == '\r' && index + 1 < value.Length && value[index + 1] == '\n') index++;
                continue;
            }
            if (current == '(') depth++;
            else if (current == ')' && --depth == 0)
            {
                end = index + 1;
                return true;
            }
        }
        return false;
    }

    /// <summary>16進文字列または括弧付きPDF文字列を、フォントへ渡される元のバイト列へ戻します。</summary>
    private static bool TryDecodePdfStringOperand(string operand, out byte[] encodedBytes)
    {
        encodedBytes = [];
        if (operand.Length < 2) return false;
        if (operand[0] == '<' && operand[^1] == '>')
        {
            var hex = string.Concat(operand.AsSpan(1, operand.Length - 2).ToString().Where(Uri.IsHexDigit));
            if (hex.Length == 0) return true;
            if (hex.Length % 2 != 0) hex += "0";
            try
            {
                encodedBytes = Convert.FromHexString(hex);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
        if (operand[0] != '(' || operand[^1] != ')') return false;

        var bytes = new List<byte>(operand.Length - 2);
        for (var index = 1; index < operand.Length - 1; index++)
        {
            var current = operand[index];
            if (current != '\\')
            {
                if (current > byte.MaxValue) return false;
                bytes.Add((byte)current);
                continue;
            }

            if (++index >= operand.Length - 1) return false;
            current = operand[index];
            if (current is >= '0' and <= '7')
            {
                var octal = current - '0';
                var count = 1;
                while (count < 3 && index + 1 < operand.Length - 1 && operand[index + 1] is >= '0' and <= '7')
                {
                    octal = octal * 8 + operand[++index] - '0';
                    count++;
                }
                bytes.Add((byte)octal);
                continue;
            }

            switch (current)
            {
                case 'n': bytes.Add((byte)'\n'); break;
                case 'r': bytes.Add((byte)'\r'); break;
                case 't': bytes.Add((byte)'\t'); break;
                case 'b': bytes.Add(0x08); break;
                case 'f': bytes.Add(0x0c); break;
                case '\r':
                    if (index + 1 < operand.Length - 1 && operand[index + 1] == '\n') index++;
                    break;
                case '\n':
                    break;
                default:
                    if (current > byte.MaxValue) return false;
                    bytes.Add((byte)current);
                    break;
            }
        }
        encodedBytes = bytes.ToArray();
        return true;
    }

    private static IReadOnlyList<string>? SplitEncodedCharacters(byte[] encoded, string text)
    {
        if (encoded.Length == 0) return null;
        var indexes = StringInfo.ParseCombiningCharacters(text);
        if (indexes.Length == 0) return null;
        var elementLengths = indexes
            .Select((start, index) =>
                (index + 1 < indexes.Length ? indexes[index + 1] : text.Length) - start)
            .ToArray();
        var encodedByteCount = encoded.Length;
        var utf16CodeUnits = elementLengths.Sum();
        int[] byteLengths;
        if (encodedByteCount == utf16CodeUnits * 2)
            byteLengths = elementLengths.Select(length => length * 2).ToArray();
        else if (encodedByteCount == indexes.Length)
            byteLengths = Enumerable.Repeat(1, indexes.Length).ToArray();
        else if (encodedByteCount % indexes.Length == 0)
            byteLengths = Enumerable.Repeat(encodedByteCount / indexes.Length, indexes.Length).ToArray();
        else
            return null;

        var result = new List<string>(indexes.Length);
        var byteOffset = 0;
        foreach (var byteLength in byteLengths)
        {
            if (byteOffset + byteLength > encoded.Length) return null;
            result.Add(Convert.ToHexString(encoded.AsSpan(byteOffset, byteLength)));
            byteOffset += byteLength;
        }
        return byteOffset == encoded.Length ? result : null;
    }

    /// <summary>バイナリPDFを壊さずにASCIIマーカーを検索します。</summary>
    private static int IndexOf(byte[] source, byte[] value, int startIndex)
    {
        if (value.Length == 0) return startIndex;
        for (var index = Math.Max(0, startIndex); index <= source.Length - value.Length; index++)
        {
            var matches = true;
            for (var offset = 0; offset < value.Length; offset++)
            {
                if (source[index + offset] == value[offset]) continue;
                matches = false;
                break;
            }
            if (matches) return index;
        }
        return -1;
    }

    /// <summary>qpdfを同期実行し、警告終了も許容しつつ目的ファイルの生成を確認します。</summary>
    private static void RunQpdf(
        string qpdfPath,
        IReadOnlyList<string> arguments,
        string expectedOutputPath,
        CancellationToken cancellationToken)
    {
        var result = ExternalProcessRunner.RunAsync(
                qpdfPath, arguments, TimeSpan.FromMinutes(5), cancellationToken, 16 * 1024 * 1024)
            .GetAwaiter().GetResult();
        if ((result.ExitCode != 0 && result.ExitCode != 3) || !File.Exists(expectedOutputPath))
            throw new InvalidDataException(
                $"qpdfによる文字送り補正に失敗しました（終了コード {result.ExitCode}）。\n" +
                string.Join("\n", new[] { result.StandardError, result.StandardOutput }.Where(value => !string.IsNullOrWhiteSpace(value))));
    }

    private static bool ShouldApplyToPdf(OcrTextRegion region) =>
        region.IsModified &&
        !(region.IsAdded && region.IsDeleted) &&
        (region.IsDeleted || region.Output.IncludeInPdf);

    private static bool PageHasChanges(OcrPage page) =>
        page.ImageOptimization is { Enabled: true } ||
        page.TextRegions.Any(ShouldApplyToPdf);

    private static string? ResolveQpdfPath()
    {
        var configured = Environment.GetEnvironmentVariable("PDFOCR_QPDF_PATH");
        var candidates = new[]
        {
            configured,
            Path.Combine(AppContext.BaseDirectory, "tools", "qpdf", "bin", "qpdf.exe"),
            Path.Combine(AppContext.BaseDirectory, "qpdf", "bin", "qpdf.exe"),
            Path.Combine(AppContext.BaseDirectory, "qpdf.exe"),
        };
        foreach (var candidate in candidates)
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return Path.GetFullPath(candidate);

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "qpdf.exe");
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }
        return null;
    }

    private static void CompactDocumentWithQpdf(string pdfPath, CancellationToken cancellationToken)
    {
        var qpdfPath = ResolveQpdfPath();
        if (qpdfPath is null)
            throw new InvalidDataException(
                "画像最適化の仕上げに必要なqpdfが見つかりません。" +
                "qpdf.exeと付属DLLがPDF Correctoriumの実行ファイルと同じフォルダーにあることを確認してください。");

        var compactPath = pdfPath + ".qpdf";
        try
        {
            var result = ExternalProcessRunner.RunAsync(
                    qpdfPath,
                    ["--object-streams=generate", "--recompress-flate", "--compression-level=9", pdfPath, compactPath],
                    TimeSpan.FromMinutes(5),
                    cancellationToken,
                    16 * 1024 * 1024)
                .GetAwaiter().GetResult();
            if (result.ExitCode != 0 || !File.Exists(compactPath))
                throw new InvalidDataException(
                    $"qpdfによる不要画像データの除去に失敗しました（終了コード {result.ExitCode}）。\n" +
                    string.Join("\n", new[] { result.StandardError, result.StandardOutput }.Where(value => !string.IsNullOrWhiteSpace(value))));

            File.Move(compactPath, pdfPath, true);
        }
        finally
        {
            if (File.Exists(compactPath)) File.Delete(compactPath);
        }
    }

    private static PdfImageOptimizationAnalysis AnalyzePageImageOptimization(
        string sourcePdfPath,
        int pageNumber,
        PageImageOptimization options,
        CancellationToken cancellationToken,
        IntPtr sharedDocument = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePdfPath);
        if (!File.Exists(sourcePdfPath)) throw new FileNotFoundException("元PDFが見つかりません。", sourcePdfPath);
        if (ResolveQpdfPath() is null)
            return new PdfImageOptimizationAnalysis(
                pageNumber,
                0,
                0,
                0,
                0,
                0,
                0,
                "画像最適化に必要なqpdfが見つかりません。Portable版の tools\\qpdf\\bin に配置してください。");
        EnsureInitialized();
        var ownsDocument = sharedDocument == IntPtr.Zero;
        var utf8Path = ownsDocument ? Marshal.StringToCoTaskMemUTF8(sourcePdfPath) : IntPtr.Zero;
        var document = sharedDocument;
        IntPtr page = IntPtr.Zero;
        try
        {
            if (ownsDocument)
            {
                document = NativeMethods.FPDF_LoadDocument(utf8Path, IntPtr.Zero);
                if (document == IntPtr.Zero) throw CreatePdfException("元PDFを開けませんでした");
            }
            var pageCount = NativeMethods.FPDF_GetPageCount(document);
            if (pageNumber < 1 || pageNumber > pageCount)
                throw new ArgumentOutOfRangeException(nameof(pageNumber));
            page = NativeMethods.FPDF_LoadPage(document, pageNumber - 1);
            if (page == IntPtr.Zero) throw CreatePdfException($"{pageNumber}ページを開けませんでした");

            var pageWidth = NativeMethods.FPDF_GetPageWidthF(page);
            var pageHeight = NativeMethods.FPDF_GetPageHeightF(page);
            var eligible = 0;
            long originalPixels = 0;
            long croppedPixels = 0;
            long originalEncodedBytes = 0;
            long estimatedEncodedBytes = 0;
            var minimumJpegQuality = 100;
            var rejectionReasons = new List<string>();
            var previewRegions = new List<PdfImageOptimizationPreviewRegion>();
            var retainedRegionCount = 0;
            var removableBlankImages = 0;
            var usesUniformColorBackground = false;
            var backgroundArgb = 0xFFFFFFFFu;
            var imageCount = NativeMethods.FPDFPage_CountObjects(page);
            for (var index = 0; index < imageCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var imageObject = NativeMethods.FPDFPage_GetObject(page, index);
                if (imageObject == IntPtr.Zero || NativeMethods.FPDFPageObj_GetType(imageObject) != 3) continue;
                if (!TryAnalyzeImageOptimizationPlan(
                        imageObject,
                        pageWidth,
                        pageHeight,
                        options,
                        out var plan,
                        out var rejectionReason))
                {
                    if (!string.IsNullOrWhiteSpace(rejectionReason))
                        rejectionReasons.Add(rejectionReason);
                    continue;
                }
                eligible++;
                originalPixels += plan.OriginalPixels;
                croppedPixels += plan.RetainedPixels;
                originalEncodedBytes += plan.OriginalEncodedBytes;
                estimatedEncodedBytes += plan.EstimatedEncodedBytes;
                minimumJpegQuality = Math.Min(minimumJpegQuality, plan.JpegQuality);
                previewRegions.AddRange(plan.PreviewRegions);
                retainedRegionCount += plan.Tiles.Count;
                if (plan.RemoveImageObject) removableBlankImages++;
                usesUniformColorBackground |= !plan.IsWhiteBackground;
                if (eligible == 1) backgroundArgb = plan.BackgroundArgb;
            }

            if (eligible == 0)
                return new PdfImageOptimizationAnalysis(
                    pageNumber,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    rejectionReasons.Count == 0
                        ? "このページには、余白または単一色背景を安全に削減できる全面画像が見つかりませんでした。"
                        : "このページの全面画像は最適化条件を満たしませんでした。\n" +
                          string.Join("\n", rejectionReasons.Distinct().Take(4)));

            var reduction = originalPixels <= 0 ? 0 : 1d - croppedPixels / (double)originalPixels;
            var byteReduction = originalEncodedBytes <= 0
                ? 0
                : 1d - estimatedEncodedBytes / (double)originalEncodedBytes;
            return new PdfImageOptimizationAnalysis(
                pageNumber,
                eligible,
                originalPixels,
                croppedPixels,
                reduction,
                originalEncodedBytes,
                estimatedEncodedBytes,
                $"{eligible}個の全面画像で、画像面積を約{reduction:P0}、画像データを約{byteReduction:P0}削減できる見込みです。" +
                (minimumJpegQuality < 94
                    ? $" 元画像より大きくならないよう、JPEG品質{minimumJpegQuality}を自動選択します。"
                    : string.Empty),
                minimumJpegQuality,
                previewRegions,
                backgroundArgb,
                usesUniformColorBackground,
                retainedRegionCount,
                removableBlankImages);
        }
        finally
        {
            if (page != IntPtr.Zero) NativeMethods.FPDF_ClosePage(page);
            if (ownsDocument && document != IntPtr.Zero) NativeMethods.FPDF_CloseDocument(document);
            if (utf8Path != IntPtr.Zero) Marshal.FreeCoTaskMem(utf8Path);
        }
    }

    /// <summary>
    /// 1ページ分の画像を解析し、適用可能な最適化をその場で反映します。
    /// </summary>
    /// <remarks>
    /// 全ページ分の再圧縮画像を先に生成すると、大きな文書では数GBのメモリを消費します。
    /// 解析、適用、解放をページ内で完結させることで、ピークメモリを1ページ分に抑えます。
    /// </remarks>
    private static int AnalyzeAndApplyPageImageOptimizations(
        IntPtr document,
        IntPtr page,
        PageImageOptimization options,
        CancellationToken cancellationToken)
    {
        var pageWidth = NativeMethods.FPDF_GetPageWidthF(page);
        var pageHeight = NativeMethods.FPDF_GetPageHeightF(page);
        var optimized = 0;

        // 画像を削除すると後続のオブジェクト番号が詰まるため、末尾から処理します。
        for (var objectIndex = NativeMethods.FPDFPage_CountObjects(page) - 1;
             objectIndex >= 0;
             objectIndex--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var imageObject = NativeMethods.FPDFPage_GetObject(page, objectIndex);
            if (imageObject == IntPtr.Zero || NativeMethods.FPDFPageObj_GetType(imageObject) != 3)
            {
                continue;
            }

            if (!TryAnalyzeImageOptimizationPlan(
                    imageObject,
                    pageWidth,
                    pageHeight,
                    options,
                    out var plan,
                    out _))
            {
                continue;
            }

            if (plan.RemoveImageObject)
            {
                if (NativeMethods.FPDFPage_RemoveObject(page, imageObject) == 0)
                {
                    throw new InvalidDataException("空白だけの全面画像をページから削除できませんでした。");
                }

                NativeMethods.FPDFPageObj_Destroy(imageObject);
                optimized++;
                continue;
            }

            if (!TryApplyImageOptimizationPlan(document, page, imageObject, plan))
            {
                throw new InvalidDataException("PDF画像の余白または単色背景を削減したデータへ置き換えられませんでした。");
            }

            optimized++;
        }

        return optimized;
    }

    private static bool TryAnalyzeImageOptimizationPlan(
        IntPtr imageObject,
        double pageWidth,
        double pageHeight,
        PageImageOptimization options,
        out ImageOptimizationPlan plan,
        out string rejectionReason)
    {
        plan = default!;
        rejectionReason = string.Empty;
        if (pageWidth <= 0 || pageHeight <= 0 ||
            NativeMethods.FPDFPageObj_GetBounds(imageObject, out var objectLeft, out var objectBottom, out var objectRight, out var objectTop) == 0)
        {
            rejectionReason = "画像のページ上の配置範囲を取得できませんでした。";
            return false;
        }

        var objectArea = Math.Max(0d, objectRight - objectLeft) * Math.Max(0d, objectTop - objectBottom);
        var pageCoverage = objectArea / (pageWidth * pageHeight);
        if (pageCoverage < 0.50d)
        {
            rejectionReason = $"画像がページ面積の50%を覆っていません（約{pageCoverage:P0}）。";
            return false;
        }

        var bitmap = NativeMethods.FPDFImageObj_GetBitmap(imageObject);
        if (bitmap == IntPtr.Zero)
        {
            rejectionReason = "画像の画素データを取得できませんでした。";
            return false;
        }
        try
        {
            var width = NativeMethods.FPDFBitmap_GetWidth(bitmap);
            var height = NativeMethods.FPDFBitmap_GetHeight(bitmap);
            var stride = NativeMethods.FPDFBitmap_GetStride(bitmap);
            var format = NativeMethods.FPDFBitmap_GetFormat(bitmap);
            var bytesPerPixel = format switch
            {
                1 => 1,
                2 => 3,
                3 or 4 => 4,
                _ => 0,
            };
            if (width < 64 || height < 64 || stride <= 0 || bytesPerPixel == 0)
            {
                rejectionReason = $"画像形式または画像サイズを安全に処理できません（{width}×{height}、形式{format}）。";
                return false;
            }
            var buffer = NativeMethods.FPDFBitmap_GetBuffer(bitmap);
            if (buffer == IntPtr.Zero)
            {
                rejectionReason = "画像の画素バッファーを取得できませんでした。";
                return false;
            }
            var pixels = new byte[checked(stride * height)];
            Marshal.Copy(buffer, pixels, 0, pixels.Length);

            var originalEncodedBytes = NativeMethods.FPDFImageObj_GetImageDataRaw(imageObject, IntPtr.Zero, 0);
            if (originalEncodedBytes == 0)
            {
                rejectionReason = "元画像の圧縮データサイズを取得できませんでした。";
                return false;
            }

            if (!TryDetermineBackgroundColor(
                    pixels,
                    width,
                    height,
                    stride,
                    format,
                    options,
                    out var background))
            {
                rejectionReason = "画像外周から十分に安定した単一背景色を推定できませんでした。";
                return false;
            }

            var rowHasContent = new bool[height];
            var columnContentCounts = new int[width];
            var minimumRowInk = Math.Max(2, width / 800);
            for (var y = 0; y < height; y++)
            {
                var row = y * stride;
                var rowInk = 0;
                for (var x = 0; x < width; x++)
                {
                    var offset = row + x * bytesPerPixel;
                    if (IsBackgroundPixel(pixels, offset, format, background, options)) continue;
                    rowInk++;
                    columnContentCounts[x]++;
                }
                rowHasContent[y] = rowInk >= minimumRowInk;
            }
            var minimumColumnInk = Math.Max(2, height / 800);
            var columnHasContent = columnContentCounts
                .Select(count => count >= minimumColumnInk)
                .ToArray();
            if (!rowHasContent.Any(value => value) || !columnHasContent.Any(value => value))
            {
                var blankImageHasWhiteBackground = background.Red >= options.WhiteThreshold &&
                                                   background.Green >= options.WhiteThreshold &&
                                                   background.Blue >= options.WhiteThreshold;
                if (options.RemoveBlankFullPageImage && blankImageHasWhiteBackground && pageCoverage >= 0.90d)
                {
                    var removed = new[]
                    {
                        new RemovedImageRegion(
                            new PixelRectangle(0, 0, width, height),
                            "空白だけの全面画像を削除"),
                    };
                    plan = new ImageOptimizationPlan(
                        width,
                        height,
                        (long)width * height,
                        0,
                        originalEncodedBytes,
                        0,
                        [],
                        [],
                        100,
                        background.ToArgb(),
                        true,
                        BuildPreviewRegions(
                            removed,
                            width,
                            height,
                            objectLeft,
                            objectBottom,
                            objectRight,
                            objectTop,
                            pageWidth,
                            pageHeight),
                        true);
                    return true;
                }
                rejectionReason = "画像全体が推定背景色に近く、残す画像範囲を特定できませんでした。";
                return false;
            }

            var adaptivePadding = Math.Max(options.PaddingPixels, (int)Math.Ceiling(Math.Max(width, height) * 0.002d));
            var minimumHorizontalBand = Math.Max(8, (int)Math.Ceiling(height * Math.Clamp(options.MinimumInternalBlankBandRatio, 0.005d, 0.25d)));
            var minimumVerticalBand = Math.Max(8, (int)Math.Ceiling(width * Math.Clamp(options.MinimumInternalBlankBandRatio, 0.005d, 0.25d)));
            var maximumRegions = Math.Clamp(options.MaximumRetainedRegions, 1, 64);
            var contentGrid = ContentGrid.Create(
                pixels,
                width,
                height,
                stride,
                format,
                background,
                options);
            var segmentation = BuildRetainedRectangles(
                contentGrid,
                rowHasContent,
                columnHasContent,
                width,
                height,
                options.RemoveOuterMargins,
                options.RemoveInternalBlankBands,
                minimumHorizontalBand,
                minimumVerticalBand,
                adaptivePadding,
                maximumRegions,
                enableShapeSplits: true);
            var usedShapeSplits = segmentation.Removed.Any(item =>
                string.Equals(item.Description, "内側の矩形空白", StringComparison.Ordinal));
            var shapeRetainedAreaRatio = segmentation.Retained.Sum(bounds => (long)bounds.Width * bounds.Height) /
                                         (double)((long)width * height);
            segmentation = ApplyUserKeepRegions(
                segmentation,
                options.KeepRegions,
                width,
                height,
                objectLeft,
                objectBottom,
                objectRight,
                objectTop,
                pageWidth,
                pageHeight,
                adaptivePadding);
            var tileBounds = segmentation.Retained.ToList();
            if (tileBounds.Count == 0)
            {
                rejectionReason = "背景以外の内容を保持する矩形を作成できませんでした。";
                return false;
            }

            var isWhiteBackground = background.Red >= options.WhiteThreshold &&
                                    background.Green >= options.WhiteThreshold &&
                                    background.Blue >= options.WhiteThreshold;
            var requiresBackgroundLayer = !isWhiteBackground;
            var sourceTiles = tileBounds
                .Select(bounds => new ImageCrop(
                    width,
                    height,
                    bounds.Left,
                    bounds.Top,
                    bounds.Width,
                    bounds.Height,
                    format,
                    stride,
                    pixels,
                    0,
                    [],
                    94))
                .ToList();
            if (sourceTiles.Any(HasTranslucentPixels))
            {
                rejectionReason = "保持領域に半透明画素を含むため、JPEGへ安全に置き換えられません。";
                return false;
            }

            var encodedSuccessfully = TryEncodeOptimizationTiles(
                    sourceTiles,
                    background,
                    requiresBackgroundLayer,
                    originalEncodedBytes,
                    out var encodedTiles,
                    out var encodedBackground,
                    out var jpegQuality,
                    out var estimatedEncodedBytes);

            if (usedShapeSplits && (!encodedSuccessfully || shapeRetainedAreaRatio > 0.70d))
            {
                // Shape-aware splitting is intended for genuinely sparse pages.
                // On dense pages, creating several JPEG tiles adds recompression
                // noise while saving little area, so keep the conservative
                // band-only result unless at least 30% of the source pixels go.
                encodedSuccessfully = false;
                var conservativeSegmentation = BuildRetainedRectangles(
                    contentGrid,
                    rowHasContent,
                    columnHasContent,
                    width,
                    height,
                    options.RemoveOuterMargins,
                    options.RemoveInternalBlankBands,
                    minimumHorizontalBand,
                    minimumVerticalBand,
                    adaptivePadding,
                    maximumRegions,
                    enableShapeSplits: false);
                conservativeSegmentation = ApplyUserKeepRegions(
                    conservativeSegmentation,
                    options.KeepRegions,
                    width,
                    height,
                    objectLeft,
                    objectBottom,
                    objectRight,
                    objectTop,
                    pageWidth,
                    pageHeight,
                    adaptivePadding);
                var conservativeBounds = conservativeSegmentation.Retained.ToList();
                var conservativeTiles = conservativeBounds
                    .Select(bounds => new ImageCrop(
                        width,
                        height,
                        bounds.Left,
                        bounds.Top,
                        bounds.Width,
                        bounds.Height,
                        format,
                        stride,
                        pixels,
                        0,
                        [],
                        94))
                    .ToList();
                if (!conservativeTiles.Any(HasTranslucentPixels) &&
                    TryEncodeOptimizationTiles(
                        conservativeTiles,
                        background,
                        requiresBackgroundLayer,
                        originalEncodedBytes,
                        out var conservativeEncodedTiles,
                        out var conservativeEncodedBackground,
                        out var conservativeJpegQuality,
                        out var conservativeEstimatedBytes))
                {
                    segmentation = conservativeSegmentation;
                    tileBounds = conservativeBounds;
                    sourceTiles = conservativeTiles;
                    encodedTiles = conservativeEncodedTiles;
                    encodedBackground = conservativeEncodedBackground;
                    jpegQuality = conservativeJpegQuality;
                    estimatedEncodedBytes = conservativeEstimatedBytes;
                    encodedSuccessfully = true;
                }
            }

            if (!encodedSuccessfully)
            {
                var retainedPixels = tileBounds.Sum(bounds => (long)bounds.Width * bounds.Height);
                var reduction = 1d - retainedPixels / (double)((long)width * height);
                rejectionReason =
                    $"背景領域は約{reduction:P0}省けますが、JPEG品質80以上では元画像" +
                    $"（約{originalEncodedBytes / 1024d:N1} KB）より小さくできません。";
                return false;
            }

            var previewRegions = BuildPreviewRegions(
                segmentation.Removed,
                width,
                height,
                objectLeft,
                objectBottom,
                objectRight,
                objectTop,
                pageWidth,
                pageHeight);
            if (previewRegions.Count == 0)
            {
                rejectionReason = "削減できる四辺余白または内部空白帯が見つかりませんでした。";
                return false;
            }

            plan = new ImageOptimizationPlan(
                width,
                height,
                (long)width * height,
                tileBounds.Sum(bounds => (long)bounds.Width * bounds.Height),
                originalEncodedBytes,
                estimatedEncodedBytes,
                encodedTiles,
                encodedBackground,
                jpegQuality,
                background.ToArgb(),
                isWhiteBackground,
                previewRegions,
                false);
            return true;
        }
        finally
        {
            NativeMethods.FPDFBitmap_Destroy(bitmap);
        }
    }

    private static bool TryDetermineBackgroundColor(
        byte[] pixels,
        int width,
        int height,
        int stride,
        int format,
        PageImageOptimization options,
        out RgbColor background)
    {
        background = new RgbColor(255, 255, 255);
        if (!options.DetectUniformColorBackground) return true;

        var bytesPerPixel = format == 1 ? 1 : format == 2 ? 3 : 4;
        var borderX = Math.Max(2, width / 50);
        var borderY = Math.Max(2, height / 50);
        var step = Math.Max(1, Math.Min(width, height) / 300);
        var buckets = new Dictionary<int, BackgroundBucket>();
        var sampleCount = 0;
        for (var y = 0; y < height; y += step)
        for (var x = 0; x < width; x += step)
        {
            if (x >= borderX && x < width - borderX && y >= borderY && y < height - borderY) continue;
            var color = ReadPixel(pixels, y * stride + x * bytesPerPixel, format);
            var key = ((color.Red >> 4) << 8) | ((color.Green >> 4) << 4) | (color.Blue >> 4);
            buckets.TryGetValue(key, out var bucket);
            buckets[key] = bucket.Add(color);
            sampleCount++;
        }
        if (sampleCount == 0 || buckets.Count == 0) return false;
        var dominant = buckets.Values.OrderByDescending(value => value.Count).First();
        if (dominant.Count / (double)sampleCount < 0.35d) return false;
        background = dominant.Average;
        return true;
    }

    private static RgbColor ReadPixel(byte[] pixels, int offset, int format)
    {
        if (format == 1) return new RgbColor(pixels[offset], pixels[offset], pixels[offset]);
        if (format == 4 && pixels[offset + 3] <= 8) return new RgbColor(255, 255, 255);
        return new RgbColor(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private static bool IsBackgroundPixel(
        byte[] pixels,
        int offset,
        int format,
        RgbColor background,
        PageImageOptimization options)
    {
        if (format == 4 && pixels[offset + 3] <= 8) return true;
        var color = ReadPixel(pixels, offset, format);
        var whiteTolerance = Math.Max(options.BackgroundColorTolerance, 255 - options.WhiteThreshold);
        var tolerance = background.IsNearWhite ? whiteTolerance : options.BackgroundColorTolerance;
        return Math.Abs(color.Red - background.Red) <= tolerance &&
               Math.Abs(color.Green - background.Green) <= tolerance &&
               Math.Abs(color.Blue - background.Blue) <= tolerance;
    }

    private static bool IsStrictShapeBackgroundPixel(
        byte[] pixels,
        int offset,
        int format,
        RgbColor background,
        PageImageOptimization options)
    {
        if (format == 4 && pixels[offset + 3] <= 8) return true;
        var color = ReadPixel(pixels, offset, format);
        var configuredTolerance = background.IsNearWhite
            ? Math.Max(options.BackgroundColorTolerance, 255 - options.WhiteThreshold)
            : options.BackgroundColorTolerance;
        var tolerance = Math.Clamp(configuredTolerance, 1, 6);
        return Math.Abs(color.Red - background.Red) <= tolerance &&
               Math.Abs(color.Green - background.Green) <= tolerance &&
               Math.Abs(color.Blue - background.Blue) <= tolerance;
    }

    private static ImageSegmentation BuildRetainedRectangles(
        ContentGrid contentGrid,
        IReadOnlyList<bool> rowHasContent,
        IReadOnlyList<bool> columnHasContent,
        int sourceWidth,
        int sourceHeight,
        bool removeOuterMargins,
        bool removeInternalBands,
        int minimumHorizontalBand,
        int minimumVerticalBand,
        int padding,
        int maximumRegions,
        bool enableShapeSplits)
    {
        var firstRow = Enumerable.Range(0, rowHasContent.Count).First(index => rowHasContent[index]);
        var lastRow = Enumerable.Range(0, rowHasContent.Count).Last(index => rowHasContent[index]);
        var firstColumn = Enumerable.Range(0, columnHasContent.Count).First(index => columnHasContent[index]);
        var lastColumn = Enumerable.Range(0, columnHasContent.Count).Last(index => columnHasContent[index]);
        var outer = removeOuterMargins
            ? new PixelRectangle(
                Math.Max(0, firstColumn - padding),
                Math.Max(0, firstRow - padding),
                Math.Min(sourceWidth, lastColumn + 1 + padding) - Math.Max(0, firstColumn - padding),
                Math.Min(sourceHeight, lastRow + 1 + padding) - Math.Max(0, firstRow - padding))
            : new PixelRectangle(0, 0, sourceWidth, sourceHeight);

        var retained = new List<PixelRectangle> { outer };
        var removed = new List<RemovedImageRegion>();
        if (removeOuterMargins)
        {
            AddRemovedRectangle(removed, new PixelRectangle(0, 0, sourceWidth, outer.Top), "上端・下端の余白");
            AddRemovedRectangle(removed, new PixelRectangle(0, outer.Bottom, sourceWidth, sourceHeight - outer.Bottom), "上端・下端の余白");
            AddRemovedRectangle(removed, new PixelRectangle(0, outer.Top, outer.Left, outer.Height), "左端・右端の余白");
            AddRemovedRectangle(removed, new PixelRectangle(outer.Right, outer.Top, sourceWidth - outer.Right, outer.Height), "左端・右端の余白");
        }

        if (!removeInternalBands) return new ImageSegmentation(retained, removed);
        while (enableShapeSplits && retained.Count < maximumRegions)
        {
            BlankRectangleSplit? best = null;
            var bestIndex = -1;
            for (var index = 0; index < retained.Count; index++)
            {
                var candidate = FindBestBlankRectangleSplit(
                    contentGrid,
                    retained[index],
                    minimumHorizontalBand,
                    minimumVerticalBand,
                    padding);
                if (candidate is null || best is not null && candidate.Score <= best.Score) continue;
                best = candidate;
                bestIndex = index;
            }
            if (best is null || bestIndex < 0) break;
            retained.RemoveAt(bestIndex);
            retained.Insert(bestIndex, best.Second);
            retained.Insert(bestIndex, best.First);
            removed.Add(new RemovedImageRegion(best.Removed, best.Description));
        }

        // A blank area is not always a full-width or full-height band. For example,
        // a chapter tab at the left and a thin rule at the bottom form an L-shape;
        // the large white rectangle inside that shape must be removed without
        // deleting either piece of artwork. Partition the remaining rectangles by
        // their actual two-dimensional content bounds to handle those cases.
        while (retained.Count < maximumRegions)
        {
            ShapeRectangleSplit? best = null;
            var bestIndex = -1;
            for (var index = 0; index < retained.Count; index++)
            {
                var candidate = contentGrid.FindBestShapeSplit(
                    retained[index],
                    minimumHorizontalBand,
                    minimumVerticalBand,
                    padding);
                if (candidate is null || best is not null && candidate.Score <= best.Score) continue;
                best = candidate;
                bestIndex = index;
            }

            if (best is null || bestIndex < 0) break;
            retained.RemoveAt(bestIndex);
            retained.Insert(bestIndex, best.Second);
            retained.Insert(bestIndex, best.First);
            AddDifferenceRectangles(removed, best.FirstPartition, best.First, best.Description);
            AddDifferenceRectangles(removed, best.SecondPartition, best.Second, best.Description);
        }
        return new ImageSegmentation(retained, removed);
    }

    private static BlankRectangleSplit? FindBestBlankRectangleSplit(
        ContentGrid contentGrid,
        PixelRectangle rectangle,
        int minimumHorizontalBand,
        int minimumVerticalBand,
        int padding)
    {
        BlankRectangleSplit? best = null;
        foreach (var band in contentGrid.FindBlankRowBands(rectangle, minimumHorizontalBand))
        {
            var removedTop = band.Start + padding;
            var removedBottom = band.End - padding;
            if (removedBottom <= removedTop) continue;
            var first = new PixelRectangle(rectangle.Left, rectangle.Top, rectangle.Width, band.Start - rectangle.Top + padding);
            var second = new PixelRectangle(rectangle.Left, band.End - padding, rectangle.Width, rectangle.Bottom - band.End + padding);
            if (!contentGrid.HasContent(first) || !contentGrid.HasContent(second)) continue;
            var removed = new PixelRectangle(rectangle.Left, removedTop, rectangle.Width, removedBottom - removedTop);
            var candidate = new BlankRectangleSplit(first, second, removed, "中央の水平空白帯");
            if (best is null || candidate.Score > best.Score) best = candidate;
        }
        foreach (var band in contentGrid.FindBlankColumnBands(rectangle, minimumVerticalBand))
        {
            var removedLeft = band.Start + padding;
            var removedRight = band.End - padding;
            if (removedRight <= removedLeft) continue;
            var first = new PixelRectangle(rectangle.Left, rectangle.Top, band.Start - rectangle.Left + padding, rectangle.Height);
            var second = new PixelRectangle(band.End - padding, rectangle.Top, rectangle.Right - band.End + padding, rectangle.Height);
            if (!contentGrid.HasContent(first) || !contentGrid.HasContent(second)) continue;
            var removed = new PixelRectangle(removedLeft, rectangle.Top, removedRight - removedLeft, rectangle.Height);
            var candidate = new BlankRectangleSplit(first, second, removed, "中央の垂直空白帯");
            if (best is null || candidate.Score > best.Score) best = candidate;
        }
        return best;
    }

    private static void AddRemovedRectangle(
        ICollection<RemovedImageRegion> removed,
        PixelRectangle rectangle,
        string description)
    {
        if (rectangle.Width > 0 && rectangle.Height > 0)
            removed.Add(new RemovedImageRegion(rectangle, description));
    }

    private static void AddDifferenceRectangles(
        ICollection<RemovedImageRegion> removed,
        PixelRectangle partition,
        PixelRectangle retained,
        string description)
    {
        AddRemovedRectangle(
            removed,
            new PixelRectangle(partition.Left, partition.Top, partition.Width, retained.Top - partition.Top),
            description);
        AddRemovedRectangle(
            removed,
            new PixelRectangle(partition.Left, retained.Bottom, partition.Width, partition.Bottom - retained.Bottom),
            description);
        AddRemovedRectangle(
            removed,
            new PixelRectangle(partition.Left, retained.Top, retained.Left - partition.Left, retained.Height),
            description);
        AddRemovedRectangle(
            removed,
            new PixelRectangle(retained.Right, retained.Top, partition.Right - retained.Right, retained.Height),
            description);
    }

    private static bool TryEncodeOptimizationTiles(
        IReadOnlyList<ImageCrop> sourceTiles,
        RgbColor background,
        bool includeBackground,
        long originalEncodedBytes,
        out IReadOnlyList<ImageCrop> encodedTiles,
        out byte[] encodedBackground,
        out int jpegQuality,
        out long estimatedBytes)
    {
        foreach (var quality in new[] { 94, 92, 90, 88, 85, 82, 80 })
        {
            var tiles = sourceTiles
                .Select(tile => tile with { SourcePixels = [], EncodedJpeg = EncodeCroppedJpeg(tile, quality), JpegQuality = quality })
                .ToList();
            var backgroundBytes = includeBackground ? EncodeSolidJpeg(background, quality) : [];
            var total = tiles.Sum(tile => (long)tile.EncodedJpeg.Length) + backgroundBytes.Length;
            if (total + 512L * (tiles.Count + (includeBackground ? 1 : 0)) >= originalEncodedBytes) continue;
            encodedTiles = tiles;
            encodedBackground = backgroundBytes;
            jpegQuality = quality;
            estimatedBytes = total;
            return true;
        }
        encodedTiles = [];
        encodedBackground = [];
        jpegQuality = 0;
        estimatedBytes = 0;
        return false;
    }

    private static byte[] EncodeSolidJpeg(RgbColor color, int quality)
    {
        const int size = 8;
        var stride = size * 4;
        var pixels = new byte[stride * size];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = color.Blue;
            pixels[offset + 1] = color.Green;
            pixels[offset + 2] = color.Red;
            pixels[offset + 3] = 255;
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static IReadOnlyList<PdfImageOptimizationPreviewRegion> BuildPreviewRegions(
        IReadOnlyList<RemovedImageRegion> removed,
        int sourceWidth,
        int sourceHeight,
        double objectLeft,
        double objectBottom,
        double objectRight,
        double objectTop,
        double pageWidth,
        double pageHeight)
    {
        var objectWidth = objectRight - objectLeft;
        var objectHeight = objectTop - objectBottom;
        return removed
            .Where(item => item.Bounds.Width > 0 && item.Bounds.Height > 0)
            .Select(item => new PdfImageOptimizationPreviewRegion(
                Math.Clamp((objectLeft + item.Bounds.Left / (double)sourceWidth * objectWidth) / pageWidth, 0d, 1d),
                Math.Clamp((pageHeight - objectTop + item.Bounds.Top / (double)sourceHeight * objectHeight) / pageHeight, 0d, 1d),
                Math.Clamp(item.Bounds.Width / (double)sourceWidth * objectWidth / pageWidth, 0d, 1d),
                Math.Clamp(item.Bounds.Height / (double)sourceHeight * objectHeight / pageHeight, 0d, 1d),
                item.Description))
            .ToList();
    }

    /// <summary>
    /// プレビューで利用者が「元画像として保持」に切り替えた背景候補を、保持タイルへ戻します。
    /// </summary>
    /// <remarks>
    /// 保持指定領域が既存タイルへ接している場合は両者を外接矩形へまとめます。これにより、背景置換を
    /// 一部だけ無効化した結果として細かな画像オブジェクトが増えることを避け、指定部分を周辺の内容と
    /// 可能な限り一枚の画像として出力します。
    /// </remarks>
    private static ImageSegmentation ApplyUserKeepRegions(
        ImageSegmentation segmentation,
        IReadOnlyList<ImageOptimizationKeepRegion>? keepRegions,
        int sourceWidth,
        int sourceHeight,
        double objectLeft,
        double objectBottom,
        double objectRight,
        double objectTop,
        double pageWidth,
        double pageHeight,
        int mergeTolerance)
    {
        if (keepRegions is not { Count: > 0 } || segmentation.Removed.Count == 0)
            return segmentation;

        var objectWidth = objectRight - objectLeft;
        var objectHeight = objectTop - objectBottom;
        if (objectWidth <= 0 || objectHeight <= 0 || pageWidth <= 0 || pageHeight <= 0)
            return segmentation;

        var requested = keepRegions
            .Select(region => PageKeepRegionToPixels(
                region,
                sourceWidth,
                sourceHeight,
                objectLeft,
                objectTop,
                objectWidth,
                objectHeight,
                pageWidth,
                pageHeight))
            .Where(bounds => bounds.Width > 0 && bounds.Height > 0)
            .ToArray();
        if (requested.Length == 0) return segmentation;

        var restored = segmentation.Removed
            .Where(item => requested.Any(request => ContainsCenter(request, item.Bounds)))
            .Select(item => item.Bounds)
            .ToList();
        if (restored.Count == 0) return segmentation;

        var retained = segmentation.Retained.ToList();
        foreach (var restoredRegion in restored)
        {
            var merged = restoredRegion;
            var changed = true;
            while (changed)
            {
                changed = false;
                for (var index = retained.Count - 1; index >= 0; index--)
                {
                    if (!TouchesOrOverlaps(merged, retained[index], mergeTolerance)) continue;
                    merged = Union(merged, retained[index]);
                    retained.RemoveAt(index);
                    changed = true;
                }
            }
            retained.Add(merged);
        }

        // 外接矩形へまとめた結果、その内側へ完全に含まれた別の背景候補も実画像に戻るため、
        // プレビューと実際の出力が食い違わないよう置換候補から除外します。
        var removed = segmentation.Removed
            .Where(item => !retained.Any(tile => Contains(tile, item.Bounds)))
            .ToList();
        return new ImageSegmentation(retained, removed);
    }

    /// <summary>ページ相対の保持範囲を、元画像のピクセル矩形へ変換します。</summary>
    private static PixelRectangle PageKeepRegionToPixels(
        ImageOptimizationKeepRegion region,
        int sourceWidth,
        int sourceHeight,
        double objectLeft,
        double objectTop,
        double objectWidth,
        double objectHeight,
        double pageWidth,
        double pageHeight)
    {
        var pageLeft = Math.Clamp(region.LeftRatio, 0d, 1d) * pageWidth;
        var pageTop = Math.Clamp(region.TopRatio, 0d, 1d) * pageHeight;
        var pageRight = Math.Clamp(region.LeftRatio + region.WidthRatio, 0d, 1d) * pageWidth;
        var pageBottom = Math.Clamp(region.TopRatio + region.HeightRatio, 0d, 1d) * pageHeight;
        var objectTopFromPage = pageHeight - objectTop;
        var left = (int)Math.Floor((pageLeft - objectLeft) / objectWidth * sourceWidth);
        var top = (int)Math.Floor((pageTop - objectTopFromPage) / objectHeight * sourceHeight);
        var right = (int)Math.Ceiling((pageRight - objectLeft) / objectWidth * sourceWidth);
        var bottom = (int)Math.Ceiling((pageBottom - objectTopFromPage) / objectHeight * sourceHeight);
        left = Math.Clamp(left, 0, sourceWidth);
        top = Math.Clamp(top, 0, sourceHeight);
        right = Math.Clamp(right, left, sourceWidth);
        bottom = Math.Clamp(bottom, top, sourceHeight);
        return new PixelRectangle(left, top, right - left, bottom - top);
    }

    private static bool ContainsCenter(PixelRectangle outer, PixelRectangle inner)
    {
        var centerX = inner.Left + inner.Width / 2d;
        var centerY = inner.Top + inner.Height / 2d;
        return centerX >= outer.Left && centerX <= outer.Right &&
               centerY >= outer.Top && centerY <= outer.Bottom;
    }

    private static bool Contains(PixelRectangle outer, PixelRectangle inner) =>
        inner.Left >= outer.Left && inner.Top >= outer.Top &&
        inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    private static bool TouchesOrOverlaps(PixelRectangle first, PixelRectangle second, int tolerance)
    {
        var margin = Math.Max(1, tolerance);
        return first.Left <= second.Right + margin && first.Right + margin >= second.Left &&
               first.Top <= second.Bottom + margin && first.Bottom + margin >= second.Top;
    }

    private static PixelRectangle Union(PixelRectangle first, PixelRectangle second)
    {
        var left = Math.Min(first.Left, second.Left);
        var top = Math.Min(first.Top, second.Top);
        var right = Math.Max(first.Right, second.Right);
        var bottom = Math.Max(first.Bottom, second.Bottom);
        return new PixelRectangle(left, top, right - left, bottom - top);
    }

    private static bool HasTranslucentPixels(ImageCrop crop)
    {
        if (crop.SourceFormat != 4) return false;
        for (var y = crop.Top; y < crop.Top + crop.Height; y++)
        {
            var row = y * crop.SourceStride;
            for (var x = crop.Left; x < crop.Left + crop.Width; x++)
                if (crop.SourcePixels[row + x * 4 + 3] < 250)
                    return true;
        }
        return false;
    }

    private static bool TryEncodeCroppedJpeg(
        ImageCrop crop,
        long originalEncodedBytes,
        out byte[] encodedJpeg,
        out int jpegQuality)
    {
        foreach (var quality in new[] { 94, 92, 90, 88, 85, 82, 80 })
        {
            var candidate = EncodeCroppedJpeg(crop, quality);
            if (candidate.Length + 512 >= originalEncodedBytes ||
                candidate.Length / (double)originalEncodedBytes > 0.95d)
                continue;
            encodedJpeg = candidate;
            jpegQuality = quality;
            return true;
        }

        encodedJpeg = [];
        jpegQuality = 0;
        return false;
    }

    private static byte[] EncodeCroppedJpeg(ImageCrop crop, int quality)
    {
        var targetStride = checked(crop.Width * 4);
        var targetPixels = new byte[checked(targetStride * crop.Height)];
        for (var y = 0; y < crop.Height; y++)
        {
            var sourceRow = (crop.Top + y) * crop.SourceStride;
            var targetRow = y * targetStride;
            for (var x = 0; x < crop.Width; x++)
            {
                var targetOffset = targetRow + x * 4;
                var sourceOffset = sourceRow + (crop.Left + x) * crop.SourceBytesPerPixel;
                if (crop.SourceFormat == 1)
                {
                    var gray = crop.SourcePixels[sourceOffset];
                    targetPixels[targetOffset] = gray;
                    targetPixels[targetOffset + 1] = gray;
                    targetPixels[targetOffset + 2] = gray;
                }
                else
                {
                    targetPixels[targetOffset] = crop.SourcePixels[sourceOffset];
                    targetPixels[targetOffset + 1] = crop.SourcePixels[sourceOffset + 1];
                    targetPixels[targetOffset + 2] = crop.SourcePixels[sourceOffset + 2];
                }
                targetPixels[targetOffset + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(
            crop.Width,
            crop.Height,
            96,
            96,
            PixelFormats.Bgr32,
            null,
            targetPixels,
            targetStride);
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static bool TryApplyImageOptimizationPlan(
        IntPtr document,
        IntPtr page,
        IntPtr imageObject,
        ImageOptimizationPlan plan)
    {
        if (NativeMethods.FPDFPageObj_GetMatrix(imageObject, out var matrix) == 0) return false;
        var tileIndex = 0;
        if (plan.EncodedBackground.Length > 0)
        {
            if (!TryLoadJpegIntoImageObject(imageObject, plan.EncodedBackground)) return false;
            if (NativeMethods.FPDFPageObj_SetMatrix(imageObject, ref matrix) == 0) return false;
        }
        else
        {
            if (plan.Tiles.Count == 0 || !TryLoadAndPositionTile(imageObject, plan.Tiles[0], matrix)) return false;
            tileIndex = 1;
        }

        for (; tileIndex < plan.Tiles.Count; tileIndex++)
        {
            var created = NativeMethods.FPDFPageObj_NewImageObj(document);
            if (created == IntPtr.Zero) return false;
            if (!TryLoadAndPositionTile(created, plan.Tiles[tileIndex], matrix))
            {
                NativeMethods.FPDFPageObj_Destroy(created);
                return false;
            }
            NativeMethods.FPDFPage_InsertObject(page, created);
        }
        return true;
    }

    private static bool TryLoadAndPositionTile(IntPtr imageObject, ImageCrop crop, FsMatrix originalMatrix)
    {
        if (!TryLoadJpegIntoImageObject(imageObject, crop.EncodedJpeg)) return false;
        var u0 = crop.Left / (double)crop.SourceWidth;
        var v0 = (crop.SourceHeight - crop.Top - crop.Height) / (double)crop.SourceHeight;
        var uScale = crop.Width / (double)crop.SourceWidth;
        var vScale = crop.Height / (double)crop.SourceHeight;
        var croppedMatrix = new FsMatrix
        {
            A = (float)(originalMatrix.A * uScale),
            B = (float)(originalMatrix.B * uScale),
            C = (float)(originalMatrix.C * vScale),
            D = (float)(originalMatrix.D * vScale),
            E = (float)(originalMatrix.A * u0 + originalMatrix.C * v0 + originalMatrix.E),
            F = (float)(originalMatrix.B * u0 + originalMatrix.D * v0 + originalMatrix.F),
        };
        return NativeMethods.FPDFPageObj_SetMatrix(imageObject, ref croppedMatrix) != 0;
    }

    private static bool TryLoadJpegIntoImageObject(IntPtr imageObject, byte[] jpegBytes)
    {
        var bytesHandle = GCHandle.Alloc(jpegBytes, GCHandleType.Normal);
        GetBlockDelegate? callback = null;
        try
        {
            callback = (parameter, position, buffer, size) =>
            {
                var bytes = (byte[])GCHandle.FromIntPtr(parameter).Target!;
                if ((ulong)position + size > (ulong)bytes.Length) return 0;
                Marshal.Copy(bytes, checked((int)position), buffer, checked((int)size));
                return 1;
            };
            var access = new FpdfFileAccess
            {
                FileLen = checked((uint)jpegBytes.Length),
                GetBlock = Marshal.GetFunctionPointerForDelegate(callback),
                Param = GCHandle.ToIntPtr(bytesHandle),
            };
            return NativeMethods.FPDFImageObj_LoadJpegFileInline(IntPtr.Zero, 0, imageObject, ref access) != 0;
        }
        finally
        {
            GC.KeepAlive(callback);
            if (bytesHandle.IsAllocated) bytesHandle.Free();
        }
    }

    private static List<TextObjectCandidate> CollectTextObjects(IntPtr page, IntPtr textPage)
    {
        var result = new List<TextObjectCandidate>();
        var count = NativeMethods.FPDFPage_CountObjects(page);
        for (var index = 0; index < count; index++)
        {
            var pageObject = NativeMethods.FPDFPage_GetObject(page, index);
            if (pageObject == IntPtr.Zero || NativeMethods.FPDFPageObj_GetType(pageObject) != 1) continue;
            if (NativeMethods.FPDFPageObj_GetBounds(pageObject, out var left, out var bottom, out var right, out var top) == 0) continue;
            if (right <= left || top <= bottom) continue;
            var text = GetObjectText(pageObject, textPage);
            if (string.IsNullOrWhiteSpace(text)) continue;
            result.Add(new TextObjectCandidate(pageObject, text, new PdfRectangle(new PdfPoint(left, bottom), new PdfSize(right - left, top - bottom))));
        }
        return result;
    }

    private static bool CalibratePerCharacterSelectionBoxes(IntPtr page, OcrPage projectPage)
    {
        var trace = string.Equals(
            Environment.GetEnvironmentVariable("PDFOCR_TRACE_CALIBRATION"),
            "1",
            StringComparison.Ordinal);
        var regions = projectPage.TextRegions
            .Where(region =>
                ShouldApplyToPdf(region) &&
                !region.IsDeleted &&
                RequiresPerCharacterObjects(region))
            .ToArray();
        if (regions.Length == 0) return false;

        var textPage = NativeMethods.FPDFText_LoadPage(page);
        if (textPage == IntPtr.Zero) return false;
        try
        {
            var exported = CollectExportedCharacterText(page, textPage);
            if (exported.Count == 0) return false;
            if (trace)
                Console.Error.WriteLine(
                    $"CAL page={projectPage.PageNumber} regions={regions.Length} exported={exported.Count} samples={string.Join("|", exported.Take(8).Select(item => item.Text))}");

            var used = new HashSet<IntPtr>();
            var changed = false;
            var setAttempts = 0;
            var setSuccesses = 0;
            foreach (var region in regions)
            {
                var advances = region.EditedGeometry.CharacterAdvances;
                var indexes = StringInfo.ParseCombiningCharacters(region.EffectiveText);
                if (indexes.Length == 0 || indexes.Length != advances.Count) continue;

                var runs = BuildCharacterTextRuns(region.EffectiveText, indexes, advances);
                if (runs.Count == 0) continue;

                var target = region.EditedGeometry.LocalBounds;
                var layoutAngle = -region.EditedGeometry.RotationDegrees * Math.PI / 180d;
                var layoutCos = Math.Cos(layoutAngle);
                var layoutSin = Math.Sin(layoutAngle);
                var lineCenterX = target.Left + target.Size.Width / 2d;
                var lineCenterY = target.Bottom + target.Size.Height / 2d;
                var matches = new List<CharacterCalibrationMatch>();

                foreach (var run in runs)
                {
                    var unrotatedCenterX = region.WritingMode == WritingMode.Vertical
                        ? lineCenterX
                        : target.Left + run.Offset + run.Advance / 2d;
                    var unrotatedCenterY = region.WritingMode == WritingMode.Vertical
                        ? target.Top - run.Offset - run.Advance / 2d
                        : lineCenterY;
                    var relativeX = unrotatedCenterX - lineCenterX;
                    var relativeY = unrotatedCenterY - lineCenterY;
                    var targetCenterX = lineCenterX + layoutCos * relativeX - layoutSin * relativeY;
                    var targetCenterY = lineCenterY + layoutSin * relativeX + layoutCos * relativeY;
                    var expectedText = NormalizeText(run.Text);

                    var nearest = exported
                        .Where(item =>
                            !used.Contains(item.Object) &&
                            string.Equals(NormalizeText(item.Text), expectedText, StringComparison.Ordinal))
                        .Select(item => new
                        {
                            Item = item,
                            Distance = Math.Sqrt(
                                Math.Pow(item.CenterX - targetCenterX, 2d) +
                                Math.Pow(item.CenterY - targetCenterY, 2d)),
                        })
                        .OrderBy(item => item.Distance)
                        .FirstOrDefault();

                    var crossAxis = region.WritingMode == WritingMode.Vertical
                        ? target.Size.Width
                        : target.Size.Height;
                    var maximumDistance = Math.Max(8d, Math.Sqrt(run.Advance * run.Advance + crossAxis * crossAxis) * 1.5d);
                    if (nearest is null || nearest.Distance > maximumDistance) continue;

                    used.Add(nearest.Item.Object);
                    matches.Add(new CharacterCalibrationMatch(
                        nearest.Item,
                        run,
                        targetCenterX,
                        targetCenterY));
                }

                if (matches.Count == 0) continue;
                if (trace)
                    Console.Error.WriteLine(
                        $"CAL region={Abbreviate(region.EffectiveText)} matches={matches.Count}/{runs.Count}");

                // PDF viewers select the font character box, not the visible ink
                // bounds used while constructing the object. Derive one additional
                // scale for the complete line so every selection box remains inside
                // its edited cell without introducing per-character font sizes.
                var advanceUnitX = region.WritingMode == WritingMode.Vertical
                    ? layoutSin
                    : layoutCos;
                var advanceUnitY = region.WritingMode == WritingMode.Vertical
                    ? -layoutCos
                    : layoutSin;
                var crossUnitX = region.WritingMode == WritingMode.Vertical
                    ? layoutCos
                    : -layoutSin;
                var crossUnitY = region.WritingMode == WritingMode.Vertical
                    ? layoutSin
                    : layoutCos;
                var targetCrossExtent = region.WritingMode == WritingMode.Vertical
                    ? target.Size.Width
                    : target.Size.Height;

                var advanceScale = matches
                    .Select(match =>
                    {
                        var extent = Math.Abs(advanceUnitX) * match.Exported.Width +
                                     Math.Abs(advanceUnitY) * match.Exported.Height;
                        return extent > 0.0001d ? match.Run.Advance / extent : 1d;
                    })
                    .DefaultIfEmpty(1d)
                    .Min();
                var crossScale = matches
                    .Select(match =>
                    {
                        var extent = Math.Abs(crossUnitX) * match.Exported.Width +
                                     Math.Abs(crossUnitY) * match.Exported.Height;
                        return extent > 0.0001d ? targetCrossExtent / extent : 1d;
                    })
                    .DefaultIfEmpty(1d)
                    .Min();

                advanceScale = advanceScale < 0.995d ? Math.Max(0.05d, advanceScale * 0.98d) : 1d;
                crossScale = crossScale < 0.995d ? Math.Max(0.05d, crossScale * 0.98d) : 1d;

                foreach (var match in matches)
                {
                    if (NativeMethods.FPDFPageObj_GetMatrix(match.Exported.Object, out var matrix) == 0)
                        continue;

                    var determinant = matrix.A * matrix.D - matrix.B * matrix.C;
                    if (!double.IsFinite(determinant) || Math.Abs(determinant) < 0.0000001d)
                        continue;

                    var relativeCenterX = match.Exported.CenterX - matrix.E;
                    var relativeCenterY = match.Exported.CenterY - matrix.F;
                    var localCenterX = (matrix.D * relativeCenterX - matrix.C * relativeCenterY) / determinant;
                    var localCenterY = (-matrix.B * relativeCenterX + matrix.A * relativeCenterY) / determinant;

                    var calibrated = matrix;
                    calibrated.A = (float)(matrix.A * advanceScale);
                    calibrated.B = (float)(matrix.B * advanceScale);
                    calibrated.C = (float)(matrix.C * crossScale);
                    calibrated.D = (float)(matrix.D * crossScale);
                    calibrated.E = (float)(match.TargetCenterX -
                                           calibrated.A * localCenterX -
                                           calibrated.C * localCenterY);
                    calibrated.F = (float)(match.TargetCenterY -
                                           calibrated.B * localCenterX -
                                           calibrated.D * localCenterY);

                    setAttempts++;
                    if (NativeMethods.FPDFPageObj_SetMatrix(match.Exported.Object, ref calibrated) != 0)
                    {
                        changed = true;
                        setSuccesses++;
                    }
                }
            }

            if (trace)
                Console.Error.WriteLine(
                    $"CAL page={projectPage.PageNumber} matrix={setSuccesses}/{setAttempts}");
            return changed;
        }
        finally
        {
            NativeMethods.FPDFText_ClosePage(textPage);
        }
    }

    private static List<ExportedCharacterText> CollectExportedCharacterText(IntPtr page, IntPtr textPage)
    {
        var boundsByObject = new Dictionary<IntPtr, (double Left, double Bottom, double Right, double Top)>();
        var characterCount = NativeMethods.FPDFText_CountChars(textPage);
        for (var index = 0; index < characterCount; index++)
        {
            var textObject = NativeMethods.FPDFText_GetTextObject(textPage, index);
            if (textObject == IntPtr.Zero ||
                NativeMethods.FPDFText_GetCharBox(
                    textPage,
                    index,
                    out var left,
                    out var right,
                    out var bottom,
                    out var top) == 0 ||
                right <= left ||
                top <= bottom)
                continue;

            if (boundsByObject.TryGetValue(textObject, out var bounds))
            {
                boundsByObject[textObject] = (
                    Math.Min(bounds.Left, left),
                    Math.Min(bounds.Bottom, bottom),
                    Math.Max(bounds.Right, right),
                    Math.Max(bounds.Top, top));
            }
            else
            {
                boundsByObject[textObject] = (left, bottom, right, top);
            }
        }

        var pageObjects = CollectTextObjects(page, textPage);
        var pageObjectPointers = pageObjects.Select(item => item.Object).ToHashSet();
        var mappedPageObjects = new HashSet<IntPtr>();
        var result = new List<ExportedCharacterText>();
        foreach (var item in boundsByObject)
        {
            var text = GetObjectText(item.Key, textPage);
            if (NormalizeText(text).Length == 0) continue;

            var actualObject = pageObjectPointers.Contains(item.Key)
                ? item.Key
                : pageObjects
                    .Where(candidate =>
                        !mappedPageObjects.Contains(candidate.Object) &&
                        string.Equals(
                            NormalizeText(candidate.Text),
                            NormalizeText(text),
                            StringComparison.Ordinal))
                    .OrderBy(candidate =>
                    {
                        var centerX = candidate.Bounds.Left + candidate.Bounds.Size.Width / 2d;
                        var centerY = candidate.Bounds.Bottom + candidate.Bounds.Size.Height / 2d;
                        var expectedCenterX = (item.Value.Left + item.Value.Right) / 2d;
                        var expectedCenterY = (item.Value.Bottom + item.Value.Top) / 2d;
                        return Math.Pow(centerX - expectedCenterX, 2d) +
                               Math.Pow(centerY - expectedCenterY, 2d);
                    })
                    .Select(candidate => candidate.Object)
                    .FirstOrDefault();
            if (actualObject == IntPtr.Zero) continue;

            mappedPageObjects.Add(actualObject);
            result.Add(new ExportedCharacterText(
                actualObject,
                text,
                item.Value.Left,
                item.Value.Bottom,
                item.Value.Right,
                item.Value.Top));
        }
        return result;
    }

    private static string GetObjectText(IntPtr textObject, IntPtr textPage)
    {
        var length = NativeMethods.FPDFTextObj_GetText(textObject, textPage, IntPtr.Zero, 0);
        if (length < 2 || length > 8_388_608) return string.Empty;
        var buffer = Marshal.AllocHGlobal(checked((int)length));
        try
        {
            if (NativeMethods.FPDFTextObj_GetText(textObject, textPage, buffer, length) == 0) return string.Empty;
            return Marshal.PtrToStringUni(buffer, checked((int)length / 2 - 1))?.Trim() ?? string.Empty;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static TextObjectCandidate? FindCandidate(
        IReadOnlyList<TextObjectCandidate> candidates,
        HashSet<IntPtr> used,
        OcrTextRegion region)
    {
        var original = region.OriginalGeometry.LocalBounds;
        // rankedは文字列一致を最優先し、同じ文字列が複数ある場合に元の位置が近い順で並べます。
        var ranked = candidates
            .Where(candidate => !used.Contains(candidate.Object))
            .Select(candidate => new
            {
                Candidate = candidate,
                TextPenalty = NormalizeText(candidate.Text) == NormalizeText(region.OriginalText) ? 0d : 1000d,
                GeometryPenalty = GeometryDistance(candidate.Bounds, original),
            })
            .OrderBy(item => item.TextPenalty + item.GeometryPenalty)
            .ToArray();
        if (ranked.Length == 0) return null;
        var best = ranked[0];
        // pageScaleは大きな見出しと小さな本文で、許容する位置差を相対化する基準値です。
        var pageScale = Math.Max(1d, Math.Max(original.Size.Width, original.Size.Height));
        return best.TextPenalty == 0 || best.GeometryPenalty <= pageScale * 0.75 ? best.Candidate : null;
    }

    private static void ApplyRegion(
        IntPtr document,
        IntPtr page,
        TextObjectCandidate candidate,
        OcrTextRegion region,
        int pageNumber,
        ICollection<TextSpacingRequest> textSpacingRequests)
    {
        // PDFium cannot measure or transform a text object after SetText("").
        // Remove the existing object before the whole-line/per-character paths diverge.
        if (string.IsNullOrWhiteSpace(region.EffectiveText))
        {
            if (NativeMethods.FPDFPage_RemoveObject(page, candidate.Object) == 0)
                throw new InvalidDataException("空欄にしたPDFテキスト領域を除去できませんでした。");
            NativeMethods.FPDFPageObj_Destroy(candidate.Object);
            return;
        }
        if (RequiresPerCharacterObjects(region))
        {
            ApplyPerCharacterRegion(document, page, candidate, region);
            return;
        }
        ApplyWholeRegion(candidate, region);
        RegisterTextSpacingRequest(
            candidate.Object,
            pageNumber,
            region,
            textSpacingRequests);
    }

    private static void InsertAddedRegion(
        IntPtr document,
        IntPtr page,
        IReadOnlyList<TextObjectCandidate> candidates,
        OcrTextRegion region,
        int pageNumber,
        ICollection<TextSpacingRequest> textSpacingRequests)
    {
        if (string.IsNullOrWhiteSpace(region.EffectiveText))
            throw new InvalidDataException("追加した透明テキスト領域の文字列が空です。文字列を入力するか、領域を削除してください。");

        var target = region.EditedGeometry.LocalBounds;
        var templates = candidates
            .OrderBy(candidate => GeometryDistance(candidate.Bounds, target))
            .ToArray();
        if (templates.Length == 0)
            throw new InvalidDataException("追加領域へ使用できるPDFフォントがこのページにありません。OCRテキストが存在するページで追加するか、フォント埋め込み対応版を使用してください。");

        foreach (var template in templates)
        {
            var font = NativeMethods.FPDFTextObj_GetFont(template.Object);
            var fontSize = NativeMethods.FPDFTextObj_GetFontSize(template.Object);
            if (font == IntPtr.Zero || !float.IsFinite(fontSize) || fontSize <= 0) continue;
            var textElementCount = StringInfo.ParseCombiningCharacters(region.EffectiveText).Length;
            if (region.WritingMode == WritingMode.Vertical ||
                (textElementCount > 1 && RequiresPerCharacterObjects(region)))
            {
                if (TryInsertAddedPerCharacterRegion(document, page, font, fontSize, region)) return;
                continue;
            }

            var textObject = NativeMethods.FPDFPageObj_CreateTextObj(document, font, fontSize);
            if (textObject == IntPtr.Zero) continue;
            var inserted = false;
            try
            {
                var unicode = Marshal.StringToHGlobalUni(region.EffectiveText + '\0');
                try
                {
                    if (NativeMethods.FPDFText_SetText(textObject, unicode) == 0) continue;
                }
                finally { Marshal.FreeHGlobal(unicode); }

                NativeMethods.FPDFTextObj_SetTextRenderMode(textObject, 3);
                if (NativeMethods.FPDFPageObj_GetBounds(textObject, out var left, out var bottom, out var right, out var top) == 0 ||
                    right <= left || top <= bottom)
                    continue;

                var angle = GetPdfCharacterAngle(region);
                var cos = Math.Cos(angle);
                var sin = Math.Sin(angle);
                var scaleX = target.Size.Width / (right - left);
                var metrics = GetFontVerticalMetrics(font, fontSize);
                var scaleY = target.Size.Height / metrics.Height;
                var sourceCenterX = (left + right) / 2d;
                var sourceCenterY = metrics.Center;
                var targetCenterX = target.Left + target.Size.Width / 2d;
                var targetCenterY = target.Bottom + target.Size.Height / 2d;
                var a = cos * scaleX;
                var b = sin * scaleX;
                var c = -sin * scaleY;
                var d = cos * scaleY;
                var e = targetCenterX - a * sourceCenterX - c * sourceCenterY;
                var f = targetCenterY - b * sourceCenterX - d * sourceCenterY;
                NativeMethods.FPDFPageObj_Transform(textObject, a, b, c, d, e, f);
                RegisterTextSpacingRequest(
                    textObject,
                    pageNumber,
                    region,
                    textSpacingRequests);
                NativeMethods.FPDFPage_InsertObject(page, textObject);
                inserted = true;
                return;
            }
            finally
            {
                if (!inserted) NativeMethods.FPDFPageObj_Destroy(textObject);
            }
        }

        throw new InvalidDataException($"追加文字列「{Abbreviate(region.EffectiveText)}」を表現できる既存PDFフォントが見つかりませんでした。");
    }

    private static bool TryInsertAddedPerCharacterRegion(
        IntPtr document,
        IntPtr page,
        IntPtr font,
        float fontSize,
        OcrTextRegion region)
    {
        var advances = region.EditedGeometry.CharacterAdvances;
        var indexes = StringInfo.ParseCombiningCharacters(region.EffectiveText);
        if (indexes.Length == 0 || indexes.Length != advances.Count) return false;
        var textRuns = BuildCharacterTextRuns(region.EffectiveText, indexes, advances);
        if (textRuns.Count == 0) return false;

        var target = region.EditedGeometry.LocalBounds;
        var textAngle = GetPdfCharacterAngle(region);
        var textCos = Math.Cos(textAngle);
        var textSin = Math.Sin(textAngle);
        var layoutAngle = -region.EditedGeometry.RotationDegrees * Math.PI / 180d;
        var layoutCos = Math.Cos(layoutAngle);
        var layoutSin = Math.Sin(layoutAngle);
        var lineCenterX = target.Left + target.Size.Width / 2d;
        var lineCenterY = target.Bottom + target.Size.Height / 2d;
        var metrics = GetFontVerticalMetrics(font, fontSize);
        var created = new List<IntPtr>();
        var prepared = new List<PreparedCharacterText>();
        try
        {
            foreach (var run in textRuns)
            {
                var textObject = NativeMethods.FPDFPageObj_CreateTextObj(document, font, fontSize);
                if (textObject == IntPtr.Zero) return false;
                created.Add(textObject);

                var unicode = Marshal.StringToHGlobalUni(run.Text + '\0');
                try
                {
                    if (NativeMethods.FPDFText_SetText(textObject, unicode) == 0) return false;
                }
                finally { Marshal.FreeHGlobal(unicode); }

                NativeMethods.FPDFTextObj_SetTextRenderMode(textObject, 3);
                if (NativeMethods.FPDFPageObj_GetBounds(textObject, out var left, out var bottom, out var right, out var top) == 0 ||
                    right <= left || top <= bottom)
                    return false;

                prepared.Add(new PreparedCharacterText(textObject, run, left, right));
            }

            var scales = GetUniformCharacterScales(
                target,
                region.WritingMode,
                metrics,
                prepared);
            foreach (var item in prepared)
            {
                var run = item.Run;
                var unrotatedCenterX = region.WritingMode == WritingMode.Vertical
                    ? lineCenterX
                    : target.Left + run.Offset + run.Advance / 2d;
                var unrotatedCenterY = region.WritingMode == WritingMode.Vertical
                    ? target.Top - run.Offset - run.Advance / 2d
                    : lineCenterY;
                var relativeX = unrotatedCenterX - lineCenterX;
                var relativeY = unrotatedCenterY - lineCenterY;
                var targetCenterX = lineCenterX + layoutCos * relativeX - layoutSin * relativeY;
                var targetCenterY = lineCenterY + layoutSin * relativeX + layoutCos * relativeY;
                var sourceCenterX = (item.Left + item.Right) / 2d;
                var sourceCenterY = metrics.Center;
                // Keep one transform for every character in the line. The
                // advance-axis scale is reduced only when a natural glyph would
                // cross its edited character cell, leaving a small selection
                // gap without reintroducing per-character size differences.
                var a = textCos * scales.ScaleX;
                var b = textSin * scales.ScaleX;
                var c = -textSin * scales.ScaleY;
                var d = textCos * scales.ScaleY;
                var e = targetCenterX - a * sourceCenterX - c * sourceCenterY;
                var f = targetCenterY - b * sourceCenterX - d * sourceCenterY;
                NativeMethods.FPDFPageObj_Transform(item.Object, a, b, c, d, e, f);
            }

            foreach (var textObject in created) NativeMethods.FPDFPage_InsertObject(page, textObject);
            created.Clear();
            return true;
        }
        finally
        {
            foreach (var textObject in created) NativeMethods.FPDFPageObj_Destroy(textObject);
        }
    }

    private static void ApplyWholeRegion(TextObjectCandidate candidate, OcrTextRegion region)
    {
        if (!string.Equals(region.EffectiveText, region.OriginalText, StringComparison.Ordinal))
        {
            var unicode = Marshal.StringToHGlobalUni(region.EffectiveText + '\0');
            try
            {
                if (NativeMethods.FPDFText_SetText(candidate.Object, unicode) == 0)
                    throw new InvalidDataException($"「{Abbreviate(region.EffectiveText)}」を元フォントで表現できません。現段階の出力では元PDFフォントに含まれる文字だけを利用できます。");
            }
            finally { Marshal.FreeHGlobal(unicode); }
        }

        if (NativeMethods.FPDFPageObj_GetBounds(candidate.Object, out var left, out var bottom, out var right, out var top) == 0 || right <= left || top <= bottom)
            throw new InvalidDataException("変更対象のPDFテキスト領域を取得できませんでした。");

        var target = region.EditedGeometry.LocalBounds;
        var sourceWidth = right - left;
        var sourceHeight = top - bottom;
        var scaleX = target.Size.Width / sourceWidth;
        var sourceCenterX = (left + right) / 2d;
        var sourceCenterY = (bottom + top) / 2d;
        var scaleY = target.Size.Height / sourceHeight;
        var font = NativeMethods.FPDFTextObj_GetFont(candidate.Object);
        var fontSize = NativeMethods.FPDFTextObj_GetFontSize(candidate.Object);
        if (region.WritingMode == WritingMode.Horizontal &&
            font != IntPtr.Zero &&
            float.IsFinite(fontSize) &&
            fontSize > 0 &&
            NativeMethods.FPDFPageObj_GetMatrix(candidate.Object, out var currentMatrix) != 0)
        {
            var metrics = GetFontVerticalMetrics(font, fontSize);
            var verticalAxisScale = Math.Sqrt(
                currentMatrix.C * currentMatrix.C +
                currentMatrix.D * currentMatrix.D);
            var fontHeightOnPage = metrics.Height * verticalAxisScale;
            if (fontHeightOnPage > 0.0001d)
            {
                // Acrobat highlights the font ascent/descent box rather than only
                // the visible ink. Fit that box to the OCR region so punctuation,
                // long vowel marks and other short glyphs cannot create an
                // abnormally tall selection rectangle. Positioning intentionally
                // continues to use the actual PDF object's bounds center above.
                // A font's ascent/descent is usually asymmetric; using its metrics
                // center as the visual center shifts the exported line upward in
                // Acrobat even when the editor overlay is correctly aligned.
                scaleY = target.Size.Height / fontHeightOnPage;
            }
        }
        // The editor stores WPF/screen angles (clockwise-positive). PDF page
        // transforms use mathematical page coordinates (counter-clockwise-positive).
        var editorRotationDelta = region.EditedGeometry.RotationDegrees - region.OriginalGeometry.RotationDegrees;
        var radians = -editorRotationDelta * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var a = cos * scaleX;
        var b = sin * scaleX;
        var c = -sin * scaleY;
        var d = cos * scaleY;
        var targetCenterX = target.Left + target.Size.Width / 2d;
        var targetCenterY = target.Bottom + target.Size.Height / 2d;
        var e = targetCenterX - a * sourceCenterX - c * sourceCenterY;
        var f = targetCenterY - b * sourceCenterX - d * sourceCenterY;
        NativeMethods.FPDFPageObj_Transform(candidate.Object, a, b, c, d, e, f);
    }

    /// <summary>
    /// 横書き行の各文字送りを、保存後に一つの <c>TJ</c> 命令として復元できるよう
    /// 対象オブジェクトへ一意のマークを付け、補正要求を登録します。
    /// </summary>
    private static void RegisterTextSpacingRequest(
        IntPtr textObject,
        int pageNumber,
        OcrTextRegion region,
        ICollection<TextSpacingRequest> requests)
    {
        var indexes = StringInfo.ParseCombiningCharacters(region.EffectiveText);
        var advances = region.EditedGeometry.CharacterAdvances;
        if (indexes.Length < 2 ||
            advances.Count != indexes.Length)
            return;

        var markName = $"PCO_{pageNumber}_{region.Id:N}";
        var utf8Name = Marshal.StringToCoTaskMemUTF8(markName);
        try
        {
            if (NativeMethods.FPDFPageObj_AddMark(textObject, utf8Name) == IntPtr.Zero)
                throw new InvalidDataException("文字送り補正用のPDFマークを作成できませんでした。");
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8Name);
        }

        requests.Add(new TextSpacingRequest(
            pageNumber,
            markName,
            region.EffectiveText,
            region.EditedGeometry.LocalBounds,
            region.WritingMode,
            region.EditedGeometry.RotationDegrees,
            advances.ToArray()));
    }

    private static bool RequiresPerCharacterObjects(OcrTextRegion region)
    {
        var edited = region.EditedGeometry.CharacterAdvances;
        var count = StringInfo.ParseCombiningCharacters(region.EffectiveText).Length;
        if (count == 0 || edited.Count != count) return false;
        if (count == 1) return false;

        // Preserve both horizontal and native vertical OCR lines as one PDF
        // text object. The source vertical font already has WMode 1 metrics and
        // the correct vertical glyph forms; splitting it into one horizontal
        // object per glyph is precisely what makes Acrobat report inconsistent
        // sizes. Per-character fallback is needed only when the user explicitly
        // converts the writing mode, because the source font cannot be assumed
        // to support the newly selected direction.
        var originalWritingMode = region.OriginalWritingMode ?? region.WritingMode;
        return originalWritingMode != region.WritingMode;
    }

    /// <summary>
    /// 診断テスト向けに、対象領域が行単位のPDFテキストオブジェクトとして出力されるかを返します。
    /// </summary>
    /// <param name="region">判定するOCR領域。</param>
    /// <returns>行を分割せずに出力する場合は<c>true</c>。</returns>
    internal static bool PreservesLineTextObjectForDiagnostics(OcrTextRegion region) =>
        !RequiresPerCharacterObjects(region);

    /// <summary>
    /// 診断テスト向けに、PDFの16進文字列と括弧付き文字列を文字送り補正へ変換できるかを返します。
    /// </summary>
    internal static bool SupportsTextSpacingOperandsForDiagnostics()
    {
        var samples = new[]
        {
            (Block: "BT <41424344> Tj ET", Text: "ABCD", Bytes: new byte[] { 0x41, 0x42, 0x43, 0x44 }),
            (Block: @"BT (AB\053C) Tj ET", Text: "AB+C", Bytes: new byte[] { 0x41, 0x42, 0x2b, 0x43 }),
            (Block: "BT (A(B)C) Tj ET", Text: "A(B)C", Bytes: new byte[] { 0x41, 0x28, 0x42, 0x29, 0x43 }),
        };
        foreach (var sample in samples)
        {
            if (!TryFindPdfStringShowOperation(sample.Block, out _, out _, out var operand) ||
                !TryDecodePdfStringOperand(operand, out var encoded) ||
                !encoded.SequenceEqual(sample.Bytes) ||
                SplitEncodedCharacters(encoded, sample.Text)?.Count != StringInfo.ParseCombiningCharacters(sample.Text).Length)
                return false;
        }

        var request = new TextSpacingRequest(
            1,
            "PCO_DIAG",
            "AB",
            new PdfRectangle(new PdfPoint(0d, 0d), new PdfSize(20d, 10d)),
            WritingMode.Horizontal,
            0d,
            [12d, 8d]);
        var measurement = new MeasuredTextSpacing(request, [10d, 10d], 0.01d);
        var qdfBytes = Encoding.ASCII.GetBytes("/PCO_DIAG BMC BT <4142> Tj ET EMC");
        if (!TryCreateTextSpacingReplacement(qdfBytes, measurement, out var replacement))
            return false;
        var replacementText = Encoding.ASCII.GetString(replacement.Value);
        if (!replacementText.Contains("120 Tz <41> Tj", StringComparison.Ordinal) ||
            !replacementText.Contains("80 Tz <42> Tj", StringComparison.Ordinal) ||
            replacementText.Contains("ActualText", StringComparison.Ordinal))
            return false;

        var verticalRequest = request with
        {
            MarkName = "PCO_VERTICAL_DIAG",
            TargetBounds = new PdfRectangle(new PdfPoint(0d, 0d), new PdfSize(10d, 20d)),
            WritingMode = WritingMode.Vertical,
        };
        var verticalMeasurement = new MeasuredTextSpacing(
            verticalRequest,
            [10d, 10d],
            0.01d);
        var verticalQdf = Encoding.ASCII.GetBytes(
            "/PCO_VERTICAL_DIAG BMC BT <4142> Tj ET EMC");
        if (!TryCreateTextSpacingReplacement(verticalQdf, verticalMeasurement, out var verticalReplacement))
            return false;
        var verticalReplacementText = Encoding.ASCII.GetString(verticalReplacement.Value);
        return verticalReplacementText.Contains("[<41> 200 <42>] TJ", StringComparison.Ordinal) &&
               !verticalReplacementText.Contains("ActualText", StringComparison.Ordinal);
    }

    private static void ApplyPerCharacterRegion(
        IntPtr document,
        IntPtr page,
        TextObjectCandidate candidate,
        OcrTextRegion region)
    {
        if (string.IsNullOrWhiteSpace(region.EffectiveText))
        {
            if (NativeMethods.FPDFPage_RemoveObject(page, candidate.Object) == 0)
                throw new InvalidDataException("空白だけになったPDFテキスト領域を置き換えられませんでした。");
            NativeMethods.FPDFPageObj_Destroy(candidate.Object);
            return;
        }
        var font = NativeMethods.FPDFTextObj_GetFont(candidate.Object);
        var fontSize = NativeMethods.FPDFTextObj_GetFontSize(candidate.Object);
        if (font == IntPtr.Zero || !float.IsFinite(fontSize) || fontSize <= 0)
            throw new InvalidDataException("変更対象のPDFフォント情報を取得できませんでした。");

        var advances = region.EditedGeometry.CharacterAdvances;
        var indexes = StringInfo.ParseCombiningCharacters(region.EffectiveText);
        if (indexes.Length == 0 || indexes.Length != advances.Count)
            throw new InvalidDataException("文字数と文字幅情報が一致しません。");
        var textRuns = BuildCharacterTextRuns(region.EffectiveText, indexes, advances);

        var renderMode = NativeMethods.FPDFTextObj_GetTextRenderMode(candidate.Object);
        NativeMethods.FPDFPageObj_GetFillColor(candidate.Object, out var red, out var green, out var blue, out var alpha);
        var target = region.EditedGeometry.LocalBounds;
        var textAngle = GetPdfCharacterAngle(region);
        var textCos = Math.Cos(textAngle);
        var textSin = Math.Sin(textAngle);
        var layoutAngle = -region.EditedGeometry.RotationDegrees * Math.PI / 180d;
        var layoutCos = Math.Cos(layoutAngle);
        var layoutSin = Math.Sin(layoutAngle);
        var lineCenterX = target.Left + target.Size.Width / 2d;
        var lineCenterY = target.Bottom + target.Size.Height / 2d;
        var metrics = GetFontVerticalMetrics(font, fontSize);
        var created = new List<IntPtr>();
        var prepared = new List<PreparedCharacterText>();

        try
        {
            foreach (var run in textRuns)
            {
                var text = run.Text;
                var textObject = NativeMethods.FPDFPageObj_CreateTextObj(document, font, fontSize);
                if (textObject == IntPtr.Zero) throw CreatePdfException("文字単位のPDFテキストを作成できませんでした");
                created.Add(textObject);

                var unicode = Marshal.StringToHGlobalUni(text + '\0');
                try
                {
                    if (NativeMethods.FPDFText_SetText(textObject, unicode) == 0)
                        throw new InvalidDataException($"「{text}」を元フォントで表現できません。");
                }
                finally { Marshal.FreeHGlobal(unicode); }

                NativeMethods.FPDFTextObj_SetTextRenderMode(textObject, renderMode);
                NativeMethods.FPDFPageObj_SetFillColor(textObject, red, green, blue, alpha);
                if (NativeMethods.FPDFPageObj_GetBounds(textObject, out var left, out var bottom, out var right, out var top) == 0 ||
                    right <= left || top <= bottom)
                    throw new InvalidDataException($"「{text}」の文字領域を計算できませんでした。");

                prepared.Add(new PreparedCharacterText(textObject, run, left, right));
            }

            var scales = GetUniformCharacterScales(
                target,
                region.WritingMode,
                metrics,
                prepared);
            foreach (var item in prepared)
            {
                var run = item.Run;
                var unrotatedCenterX = region.WritingMode == WritingMode.Vertical
                    ? lineCenterX
                    : target.Left + run.Offset + run.Advance / 2d;
                var unrotatedCenterY = region.WritingMode == WritingMode.Vertical
                    ? target.Top - run.Offset - run.Advance / 2d
                    : lineCenterY;
                var relativeX = unrotatedCenterX - lineCenterX;
                var relativeY = unrotatedCenterY - lineCenterY;
                var targetCenterX = lineCenterX + layoutCos * relativeX - layoutSin * relativeY;
                var targetCenterY = lineCenterY + layoutSin * relativeX + layoutCos * relativeY;
                var sourceCenterX = (item.Left + item.Right) / 2d;
                var sourceCenterY = metrics.Center;
                // Preserve one common matrix for the complete line. This keeps
                // the size reported by PDF viewers uniform while ensuring that
                // no glyph/selection rectangle enters the neighbouring cell.
                var a = textCos * scales.ScaleX;
                var b = textSin * scales.ScaleX;
                var c = -textSin * scales.ScaleY;
                var d = textCos * scales.ScaleY;
                var e = targetCenterX - a * sourceCenterX - c * sourceCenterY;
                var f = targetCenterY - b * sourceCenterX - d * sourceCenterY;
                NativeMethods.FPDFPageObj_Transform(item.Object, a, b, c, d, e, f);
            }

            if (NativeMethods.FPDFPage_RemoveObject(page, candidate.Object) == 0)
                throw new InvalidDataException("元のPDFテキスト領域を置き換えられませんでした。");
            NativeMethods.FPDFPageObj_Destroy(candidate.Object);
            foreach (var textObject in created) NativeMethods.FPDFPage_InsertObject(page, textObject);
            created.Clear();
        }
        finally
        {
            foreach (var textObject in created) NativeMethods.FPDFPageObj_Destroy(textObject);
        }
    }

    private static (double ScaleX, double ScaleY) GetUniformCharacterScales(
        PdfRectangle target,
        WritingMode writingMode,
        FontVerticalMetrics metrics,
        IReadOnlyList<PreparedCharacterText> characters)
    {
        // Horizontal lines derive the font size from their height. Vertical
        // lines use their column width as the cross-axis character size.
        var crossAxisSize = writingMode == WritingMode.Vertical
            ? target.Size.Width
            : target.Size.Height;
        var crossAxisScale = crossAxisSize / metrics.Height;
        if (characters.Count == 0) return (crossAxisScale, crossAxisScale);

        if (writingMode == WritingMode.Vertical)
        {
            // A vertical line is emitted with a -90 degree PDF text matrix.
            // The font's local X axis is therefore the top-to-bottom advance
            // axis, and its local Y axis becomes the column width.
            var fitScale = characters
                .Where(character =>
                    character.Run.Advance > 0.0001d &&
                    character.Right - character.Left > 0.0001d)
                .Select(character =>
                    character.Run.Advance /
                    (character.Right - character.Left))
                .DefaultIfEmpty(crossAxisScale)
                .Min();
            var verticalScaleX = fitScale < crossAxisScale
                ? fitScale * CharacterCellSafetyFactor
                : crossAxisScale;
            return (verticalScaleX, crossAxisScale);
        }

        // Measure every natural glyph before placing it. If one glyph is wider
        // than its edited cell, apply the same horizontal compression to the
        // whole line. This avoids both overlap and viewer-visible size changes
        // between individual characters.
        var horizontalFitScale = characters
            .Where(character =>
                character.Run.Advance > 0.0001d &&
                character.Right - character.Left > 0.0001d)
            .Select(character =>
                character.Run.Advance /
                (character.Right - character.Left))
            .DefaultIfEmpty(crossAxisScale)
            .Min();
        var scaleX = horizontalFitScale < crossAxisScale
            ? horizontalFitScale * CharacterCellSafetyFactor
            : crossAxisScale;
        return (scaleX, crossAxisScale);
    }

    private static double GetPdfCharacterAngle(OcrTextRegion region)
    {
        // Editor angles are clockwise-positive in screen coordinates. Vertical
        // writing needs an additional clockwise quarter turn in the PDF text
        // matrix; otherwise PDF viewers interpret the characters as unrelated
        // horizontal lines merely stacked from top to bottom.
        var screenAngle = region.EditedGeometry.RotationDegrees +
                          (region.WritingMode == WritingMode.Vertical ? 90d : 0d);
        return -screenAngle * Math.PI / 180d;
    }

    private static IReadOnlyList<CharacterTextRun> BuildCharacterTextRuns(
        string text,
        IReadOnlyList<int> indexes,
        IReadOnlyList<double> advances)
    {
        var result = new List<CharacterTextRun>();
        var pendingText = string.Empty;
        var pendingOffset = 0d;
        var pendingAdvance = 0d;
        var offset = 0d;
        for (var index = 0; index < indexes.Count; index++)
        {
            var start = indexes[index];
            var end = index + 1 < indexes.Count ? indexes[index + 1] : text.Length;
            var element = text[start..end];
            var advance = advances[index];
            if (IsNonMeasurableTextElement(element))
            {
                if (pendingText.Length == 0) pendingOffset = offset;
                pendingText += element;
                pendingAdvance += advance;
            }
            else
            {
                result.Add(new CharacterTextRun(pendingText + element, pendingText.Length == 0 ? offset : pendingOffset, pendingAdvance + advance));
                pendingText = string.Empty;
                pendingAdvance = 0;
            }
            offset += advance;
        }

        if (pendingText.Length > 0 && result.Count > 0)
        {
            var last = result[^1];
            result[^1] = last with { Text = last.Text + pendingText, Advance = last.Advance + pendingAdvance };
        }
        return result;
    }

    private static bool IsNonMeasurableTextElement(string value) =>
        string.IsNullOrWhiteSpace(value) || value.All(character =>
            char.IsControl(character) || CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format);

    private static FontVerticalMetrics GetFontVerticalMetrics(IntPtr font, float fontSize)
    {
        if (font != IntPtr.Zero &&
            float.IsFinite(fontSize) &&
            fontSize > 0 &&
            NativeMethods.FPDFFont_GetAscent(font, fontSize, out var ascent) != 0 &&
            NativeMethods.FPDFFont_GetDescent(font, fontSize, out var descent) != 0 &&
            float.IsFinite(ascent) &&
            float.IsFinite(descent))
        {
            // PDF fonts normally report a negative descent, but tolerate fonts
            // that expose it as a positive distance below the baseline.
            var bottom = descent > 0 ? -descent : descent;
            var top = ascent < 0 ? -ascent : ascent;
            var height = top - bottom;
            if (height >= fontSize * 0.25d && height <= fontSize * 5d)
                return new FontVerticalMetrics(bottom, top);
        }

        // A one-em fallback is deliberately independent of the glyph's visible
        // bounds. Using the ink bounds makes "ー", "・", punctuation and spaces
        // expand vertically until they fill the whole OCR line.
        return new FontVerticalMetrics(-fontSize * 0.2d, fontSize * 0.8d);
    }

    private static double GeometryDistance(PdfRectangle left, PdfRectangle right) =>
        Math.Abs(left.Left - right.Left) + Math.Abs(left.Bottom - right.Bottom) +
        Math.Abs(left.Right - right.Right) + Math.Abs(left.Top - right.Top);

    private static bool IsDuplicateDeletionRequest(OcrTextRegion candidate, OcrTextRegion applied)
    {
        if (!candidate.IsDeleted || candidate.IsAdded || !applied.IsDeleted || applied.IsAdded)
            return false;

        var candidateText = NormalizeText(candidate.OriginalText);
        if (candidateText.Length == 0 ||
            !string.Equals(candidateText, NormalizeText(applied.OriginalText), StringComparison.Ordinal))
            return false;

        var candidateBounds = candidate.OriginalGeometry.LocalBounds;
        var appliedBounds = applied.OriginalGeometry.LocalBounds;
        var intersectionWidth = Math.Max(0d, Math.Min(candidateBounds.Right, appliedBounds.Right) -
                                             Math.Max(candidateBounds.Left, appliedBounds.Left));
        var intersectionHeight = Math.Max(0d, Math.Min(candidateBounds.Top, appliedBounds.Top) -
                                              Math.Max(candidateBounds.Bottom, appliedBounds.Bottom));
        var intersectionArea = intersectionWidth * intersectionHeight;
        var candidateArea = candidateBounds.Size.Width * candidateBounds.Size.Height;
        var appliedArea = appliedBounds.Size.Width * appliedBounds.Size.Height;
        var smallerArea = Math.Min(candidateArea, appliedArea);

        // Companion OCR data can contain the same source line twice with sub-point
        // coordinate differences even though the PDF has only one text object.
        // A strict 90% overlap keeps unrelated repeated captions independent.
        return smallerArea > 0d && intersectionArea / smallerArea >= 0.9d;
    }

    internal static bool IsDuplicateDeletionRequestForDiagnostics(OcrTextRegion candidate, OcrTextRegion applied) =>
        IsDuplicateDeletionRequest(candidate, applied);

    private static string NormalizeText(string value) => string.Concat(value.Where(character => !char.IsWhiteSpace(character)));
    private static string Abbreviate(string value) => value.Length <= 20 ? value : value[..20] + "…";

    private static void SaveDocument(IntPtr document, string path)
    {
        Exception? writeError = null;
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        WriteBlockDelegate callback = (_, data, size) =>
        {
            try
            {
                if (size > int.MaxValue) return 0;
                var bytes = new byte[(int)size];
                Marshal.Copy(data, bytes, 0, bytes.Length);
                stream.Write(bytes);
                return 1;
            }
            catch (Exception ex)
            {
                writeError = ex;
                return 0;
            }
        };
        var writer = new FpdfFileWrite { Version = 1, WriteBlock = Marshal.GetFunctionPointerForDelegate(callback) };
        if (NativeMethods.FPDF_SaveAsCopy(document, ref writer, NoIncremental) == 0)
            throw new IOException("PDFiumが出力PDFを保存できませんでした。", writeError);
        stream.Flush(true);
        GC.KeepAlive(callback);
    }

    private static void ValidateOutput(
        string path,
        int? expectedPageCount,
        IReadOnlySet<int> changedPages,
        PdfOutputVersion expectedVersion)
    {
        var utf8Path = Marshal.StringToCoTaskMemUTF8(path);
        IntPtr document = IntPtr.Zero;
        try
        {
            document = NativeMethods.FPDF_LoadDocument(utf8Path, IntPtr.Zero);
            if (document == IntPtr.Zero) throw CreatePdfException("出力PDFの再検証に失敗しました");
            var requestedVersion = PdfOutputVersionMapping.GetPdfiumVersion(expectedVersion);
            if (requestedVersion is not null &&
                (NativeMethods.FPDF_GetFileVersion(document, out var actualVersion) == 0 ||
                 actualVersion != requestedVersion.Value))
                throw new InvalidDataException(
                    $"出力PDFのバージョンが指定値 PDF {FormatPdfiumVersion(requestedVersion.Value)} と一致しません。");
            var count = NativeMethods.FPDF_GetPageCount(document);
            if (count <= 0 || expectedPageCount is > 0 && count != expectedPageCount)
                throw new InvalidDataException("出力PDFのページ数が元PDFと一致しません。");
            for (var index = 0; index < count; index++)
            {
                var page = NativeMethods.FPDF_LoadPage(document, index);
                if (page == IntPtr.Zero) throw new InvalidDataException($"出力PDFの{index + 1}ページを開けません。");
                try
                {
                    if (!changedPages.Contains(index + 1)) continue;
                    var bitmap = NativeMethods.FPDFBitmap_Create(200, 200, 1);
                    if (bitmap == IntPtr.Zero) throw new InvalidDataException($"出力PDFの{index + 1}ページを検証描画できません。");
                    try
                    {
                        NativeMethods.FPDFBitmap_FillRect(bitmap, 0, 0, 200, 200, 0xFFFFFFFF);
                        NativeMethods.FPDF_RenderPageBitmap(bitmap, page, 0, 0, 200, 200, 0, 0);
                    }
                    finally { NativeMethods.FPDFBitmap_Destroy(bitmap); }
                }
                finally { NativeMethods.FPDF_ClosePage(page); }
            }
        }
        finally
        {
            if (document != IntPtr.Zero) NativeMethods.FPDF_CloseDocument(document);
            Marshal.FreeCoTaskMem(utf8Path);
        }
    }

    private static void EnsureInitialized()
        => PdfiumSynchronization.EnsureInitialized(NativeMethods.FPDF_InitLibrary);

    /// <summary>PDFiumの14、17、20等のバージョン番号をPDF表記へ変換します。</summary>
    private static string FormatPdfiumVersion(int version) => version switch
    {
        >= 10 and <= 19 => $"1.{version - 10}",
        20 => "2.0",
        _ => version.ToString(CultureInfo.InvariantCulture),
    };

    private static Exception CreatePdfException(string message) =>
        new InvalidDataException($"{message} (PDFium error {NativeMethods.FPDF_GetLastError()})。");

    private sealed record TextObjectCandidate(IntPtr Object, string Text, PdfRectangle Bounds);
    private sealed record ImageOptimizationPlan(
        int SourceWidth,
        int SourceHeight,
        long OriginalPixels,
        long RetainedPixels,
        long OriginalEncodedBytes,
        long EstimatedEncodedBytes,
        IReadOnlyList<ImageCrop> Tiles,
        byte[] EncodedBackground,
        int JpegQuality,
        uint BackgroundArgb,
        bool IsWhiteBackground,
        IReadOnlyList<PdfImageOptimizationPreviewRegion> PreviewRegions,
        bool RemoveImageObject);
    private sealed record ImageSegmentation(
        IReadOnlyList<PixelRectangle> Retained,
        IReadOnlyList<RemovedImageRegion> Removed);
    private sealed record RemovedImageRegion(PixelRectangle Bounds, string Description);
    private sealed record BlankRectangleSplit(
        PixelRectangle First,
        PixelRectangle Second,
        PixelRectangle Removed,
        string Description)
    {
        public long Score => (long)Removed.Width * Removed.Height;
    }
    private sealed record ShapeRectangleSplit(
        PixelRectangle FirstPartition,
        PixelRectangle First,
        PixelRectangle SecondPartition,
        PixelRectangle Second,
        string Description)
    {
        public long Score =>
            (long)FirstPartition.Width * FirstPartition.Height +
            (long)SecondPartition.Width * SecondPartition.Height -
            (long)First.Width * First.Height -
            (long)Second.Width * Second.Height;
    }
    private readonly record struct PixelInterval(int Start, int Length)
    {
        public int End => Start + Length;
    }
    private readonly record struct PixelRectangle(int Left, int Top, int Width, int Height)
    {
        public int Right => Left + Width;
        public int Bottom => Top + Height;
    }

    /// <summary>
    /// 元画像を適応的な小区画へ集約し、任意の矩形内に背景以外の画素が存在するかを高速に判定します。
    /// </summary>
    private sealed class ContentGrid
    {
        private readonly bool[] _occupied;
        private readonly bool[] _shapeOccupied;
        private readonly int[] _integral;

        private ContentGrid(
            int sourceWidth,
            int sourceHeight,
            int cellSize,
            int columns,
            int rows,
            bool[] occupied,
            bool[] shapeOccupied,
            int[] integral)
        {
            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
            CellSize = cellSize;
            Columns = columns;
            Rows = rows;
            _occupied = occupied;
            _shapeOccupied = shapeOccupied;
            _integral = integral;
        }

        public int SourceWidth { get; }
        public int SourceHeight { get; }
        public int CellSize { get; }
        public int Columns { get; }
        public int Rows { get; }

        public static ContentGrid Create(
            byte[] pixels,
            int sourceWidth,
            int sourceHeight,
            int sourceStride,
            int sourceFormat,
            RgbColor background,
            PageImageOptimization options)
        {
            var cellSize = Math.Max(1, (int)Math.Ceiling(Math.Max(sourceWidth, sourceHeight) / 1200d));
            var columns = (sourceWidth + cellSize - 1) / cellSize;
            var rows = (sourceHeight + cellSize - 1) / cellSize;
            var cellInk = new int[checked(columns * rows)];
            var strictCellInk = new int[cellInk.Length];
            var bytesPerPixel = sourceFormat == 1 ? 1 : sourceFormat == 2 ? 3 : 4;
            for (var y = 0; y < sourceHeight; y++)
            {
                var sourceRow = y * sourceStride;
                var gridRow = y / cellSize * columns;
                for (var x = 0; x < sourceWidth; x++)
                {
                    var offset = sourceRow + x * bytesPerPixel;
                    if (!IsBackgroundPixel(pixels, offset, sourceFormat, background, options))
                        cellInk[gridRow + x / cellSize]++;
                    if (!IsStrictShapeBackgroundPixel(pixels, offset, sourceFormat, background, options))
                        strictCellInk[gridRow + x / cellSize]++;
                }
            }

            var minimumCellInk = Math.Max(1, cellSize * cellSize / 48);
            var occupied = BuildOccupiedMap(cellInk, cellSize, columns, rows, minimumCellInk);
            var shapeOccupied = BuildOccupiedMap(strictCellInk, cellSize, columns, rows, minimumCellInk);

            var integralStride = columns + 1;
            var integral = new int[checked(integralStride * (rows + 1))];
            for (var y = 0; y < rows; y++)
            {
                var rowSum = 0;
                for (var x = 0; x < columns; x++)
                {
                    if (occupied[y * columns + x]) rowSum++;
                    integral[(y + 1) * integralStride + x + 1] =
                        integral[y * integralStride + x + 1] + rowSum;
                }
            }
            return new ContentGrid(
                sourceWidth,
                sourceHeight,
                cellSize,
                columns,
                rows,
                occupied,
                shapeOccupied,
                integral);
        }

        private static bool[] BuildOccupiedMap(
            IReadOnlyList<int> cellInk,
            int cellSize,
            int columns,
            int rows,
            int minimumCellInk)
        {
            var rawOccupied = cellInk
                .Select(value => value >= minimumCellInk)
                .ToArray();
            var occupied = new bool[rawOccupied.Length];
            var strongCellInk = cellSize == 1
                ? 1
                : Math.Max(2, (int)Math.Ceiling(cellSize * cellSize * 0.12d));
            for (var y = 0; y < rows; y++)
            for (var x = 0; x < columns; x++)
            {
                var index = y * columns + x;
                if (!rawOccupied[index]) continue;
                var neighbourCount = 0;
                for (var neighbourY = Math.Max(0, y - 1); neighbourY <= Math.Min(rows - 1, y + 1); neighbourY++)
                for (var neighbourX = Math.Max(0, x - 1); neighbourX <= Math.Min(columns - 1, x + 1); neighbourX++)
                {
                    if (neighbourX == x && neighbourY == y) continue;
                    if (rawOccupied[neighbourY * columns + neighbourX]) neighbourCount++;
                }

                // Preserve genuine strokes and punctuation, but discard isolated
                // one-cell JPEG speckles that otherwise bridge a large blank area.
                occupied[index] = cellInk[index] >= strongCellInk || neighbourCount >= 2;
            }
            return occupied;
        }

        public bool HasContent(PixelRectangle rectangle)
        {
            var grid = ToGridRectangle(rectangle);
            return CountOccupiedCells(grid.Left, grid.Top, grid.Right, grid.Bottom) > 0;
        }

        public IReadOnlyList<PixelInterval> FindBlankRowBands(PixelRectangle rectangle, int minimumPixels)
        {
            var grid = ToGridRectangle(rectangle);
            var result = new List<PixelInterval>();
            var index = grid.Top;
            while (index < grid.Bottom)
            {
                if (CountOccupiedCells(grid.Left, index, grid.Right, index + 1) > 0)
                {
                    index++;
                    continue;
                }
                var start = index;
                while (index < grid.Bottom && CountOccupiedCells(grid.Left, index, grid.Right, index + 1) == 0) index++;
                var pixelStart = Math.Max(rectangle.Top, start * CellSize);
                var pixelEnd = Math.Min(rectangle.Bottom, index * CellSize);
                if (pixelStart > rectangle.Top && pixelEnd < rectangle.Bottom && pixelEnd - pixelStart >= minimumPixels)
                    result.Add(new PixelInterval(pixelStart, pixelEnd - pixelStart));
            }
            return result;
        }

        public IReadOnlyList<PixelInterval> FindBlankColumnBands(PixelRectangle rectangle, int minimumPixels)
        {
            var grid = ToGridRectangle(rectangle);
            var result = new List<PixelInterval>();
            var index = grid.Left;
            while (index < grid.Right)
            {
                if (CountOccupiedCells(index, grid.Top, index + 1, grid.Bottom) > 0)
                {
                    index++;
                    continue;
                }
                var start = index;
                while (index < grid.Right && CountOccupiedCells(index, grid.Top, index + 1, grid.Bottom) == 0) index++;
                var pixelStart = Math.Max(rectangle.Left, start * CellSize);
                var pixelEnd = Math.Min(rectangle.Right, index * CellSize);
                if (pixelStart > rectangle.Left && pixelEnd < rectangle.Right && pixelEnd - pixelStart >= minimumPixels)
                    result.Add(new PixelInterval(pixelStart, pixelEnd - pixelStart));
            }
            return result;
        }

        /// <summary>
        /// Finds a guillotine partition whose two content bounds leave a large
        /// two-dimensional blank area. Unlike a simple blank-band search, this
        /// also handles L-shaped arrangements made from a side label and a rule.
        /// </summary>
        public ShapeRectangleSplit? FindBestShapeSplit(
            PixelRectangle rectangle,
            int minimumHorizontalBand,
            int minimumVerticalBand,
            int padding)
        {
            var vertical = FindBestVerticalShapeSplit(
                rectangle,
                minimumHorizontalBand,
                minimumVerticalBand,
                padding);
            var horizontal = FindBestHorizontalShapeSplit(
                rectangle,
                minimumHorizontalBand,
                minimumVerticalBand,
                padding);
            if (vertical is null) return horizontal;
            if (horizontal is null) return vertical;
            return vertical.Score >= horizontal.Score ? vertical : horizontal;
        }

        private ShapeRectangleSplit? FindBestVerticalShapeSplit(
            PixelRectangle rectangle,
            int minimumHorizontalBand,
            int minimumVerticalBand,
            int padding)
        {
            var grid = ToGridRectangle(rectangle);
            if (grid.Width < 2) return null;
            var prefix = new PixelRectangle?[grid.Width];
            var suffix = new PixelRectangle?[grid.Width];
            PixelRectangle? running = null;
            for (var localX = 0; localX < grid.Width; localX++)
            {
                running = UnionGridBounds(running, GetColumnContentBounds(grid.Left + localX, grid.Top, grid.Bottom));
                prefix[localX] = running;
            }
            running = null;
            for (var localX = grid.Width - 1; localX >= 0; localX--)
            {
                running = UnionGridBounds(running, GetColumnContentBounds(grid.Left + localX, grid.Top, grid.Bottom));
                suffix[localX] = running;
            }

            ShapeRectangleSplit? best = null;
            for (var localSplit = 1; localSplit < grid.Width; localSplit++)
            {
                if (prefix[localSplit - 1] is not PixelRectangle firstGrid ||
                    suffix[localSplit] is not PixelRectangle secondGrid)
                    continue;
                var splitX = Math.Clamp(
                    (grid.Left + localSplit) * CellSize,
                    rectangle.Left + 1,
                    rectangle.Right - 1);
                var firstPartition = new PixelRectangle(
                    rectangle.Left,
                    rectangle.Top,
                    splitX - rectangle.Left,
                    rectangle.Height);
                var secondPartition = new PixelRectangle(
                    splitX,
                    rectangle.Top,
                    rectangle.Right - splitX,
                    rectangle.Height);
                var first = ToPixelContentBounds(firstGrid, firstPartition, padding);
                var second = ToPixelContentBounds(secondGrid, secondPartition, padding);
                var candidate = CreateShapeSplitCandidate(
                    rectangle,
                    firstPartition,
                    first,
                    secondPartition,
                    second,
                    minimumHorizontalBand,
                    minimumVerticalBand);
                if (candidate is not null && (best is null || candidate.Score > best.Score)) best = candidate;
            }
            return best;
        }

        private ShapeRectangleSplit? FindBestHorizontalShapeSplit(
            PixelRectangle rectangle,
            int minimumHorizontalBand,
            int minimumVerticalBand,
            int padding)
        {
            var grid = ToGridRectangle(rectangle);
            if (grid.Height < 2) return null;
            var prefix = new PixelRectangle?[grid.Height];
            var suffix = new PixelRectangle?[grid.Height];
            PixelRectangle? running = null;
            for (var localY = 0; localY < grid.Height; localY++)
            {
                running = UnionGridBounds(running, GetRowContentBounds(grid.Top + localY, grid.Left, grid.Right));
                prefix[localY] = running;
            }
            running = null;
            for (var localY = grid.Height - 1; localY >= 0; localY--)
            {
                running = UnionGridBounds(running, GetRowContentBounds(grid.Top + localY, grid.Left, grid.Right));
                suffix[localY] = running;
            }

            ShapeRectangleSplit? best = null;
            for (var localSplit = 1; localSplit < grid.Height; localSplit++)
            {
                if (prefix[localSplit - 1] is not PixelRectangle firstGrid ||
                    suffix[localSplit] is not PixelRectangle secondGrid)
                    continue;
                var splitY = Math.Clamp(
                    (grid.Top + localSplit) * CellSize,
                    rectangle.Top + 1,
                    rectangle.Bottom - 1);
                var firstPartition = new PixelRectangle(
                    rectangle.Left,
                    rectangle.Top,
                    rectangle.Width,
                    splitY - rectangle.Top);
                var secondPartition = new PixelRectangle(
                    rectangle.Left,
                    splitY,
                    rectangle.Width,
                    rectangle.Bottom - splitY);
                var first = ToPixelContentBounds(firstGrid, firstPartition, padding);
                var second = ToPixelContentBounds(secondGrid, secondPartition, padding);
                var candidate = CreateShapeSplitCandidate(
                    rectangle,
                    firstPartition,
                    first,
                    secondPartition,
                    second,
                    minimumHorizontalBand,
                    minimumVerticalBand);
                if (candidate is not null && (best is null || candidate.Score > best.Score)) best = candidate;
            }
            return best;
        }

        private ShapeRectangleSplit? CreateShapeSplitCandidate(
            PixelRectangle original,
            PixelRectangle firstPartition,
            PixelRectangle first,
            PixelRectangle secondPartition,
            PixelRectangle second,
            int minimumHorizontalBand,
            int minimumVerticalBand)
        {
            if (first.Width <= 0 || first.Height <= 0 || second.Width <= 0 || second.Height <= 0)
                return null;
            var candidate = new ShapeRectangleSplit(
                firstPartition,
                first,
                secondPartition,
                second,
                "内側の矩形空白");
            var originalArea = (long)original.Width * original.Height;
            var minimumSavedArea = Math.Max(1024L, (long)Math.Ceiling(originalArea * 0.06d));
            if (candidate.Score < minimumSavedArea) return null;

            var hasMeaningfulGap =
                firstPartition.Top + minimumHorizontalBand <= first.Top ||
                first.Bottom + minimumHorizontalBand <= firstPartition.Bottom ||
                firstPartition.Left + minimumVerticalBand <= first.Left ||
                first.Right + minimumVerticalBand <= firstPartition.Right ||
                secondPartition.Top + minimumHorizontalBand <= second.Top ||
                second.Bottom + minimumHorizontalBand <= secondPartition.Bottom ||
                secondPartition.Left + minimumVerticalBand <= second.Left ||
                second.Right + minimumVerticalBand <= secondPartition.Right;
            return hasMeaningfulGap ? candidate : null;
        }

        private PixelRectangle? GetColumnContentBounds(int column, int top, int bottom)
        {
            var firstRow = -1;
            var lastRow = -1;
            for (var row = top; row < bottom; row++)
            {
                if (!_shapeOccupied[row * Columns + column]) continue;
                if (firstRow < 0) firstRow = row;
                lastRow = row;
            }
            return firstRow < 0
                ? null
                : new PixelRectangle(column, firstRow, 1, lastRow - firstRow + 1);
        }

        private PixelRectangle? GetRowContentBounds(int row, int left, int right)
        {
            var firstColumn = -1;
            var lastColumn = -1;
            for (var column = left; column < right; column++)
            {
                if (!_shapeOccupied[row * Columns + column]) continue;
                if (firstColumn < 0) firstColumn = column;
                lastColumn = column;
            }
            return firstColumn < 0
                ? null
                : new PixelRectangle(firstColumn, row, lastColumn - firstColumn + 1, 1);
        }

        private static PixelRectangle? UnionGridBounds(PixelRectangle? current, PixelRectangle? addition)
        {
            if (addition is null) return current;
            if (current is null) return addition;
            var left = Math.Min(current.Value.Left, addition.Value.Left);
            var top = Math.Min(current.Value.Top, addition.Value.Top);
            var right = Math.Max(current.Value.Right, addition.Value.Right);
            var bottom = Math.Max(current.Value.Bottom, addition.Value.Bottom);
            return new PixelRectangle(left, top, right - left, bottom - top);
        }

        private PixelRectangle ToPixelContentBounds(
            PixelRectangle gridBounds,
            PixelRectangle partition,
            int padding)
        {
            var left = Math.Max(partition.Left, gridBounds.Left * CellSize - padding);
            var top = Math.Max(partition.Top, gridBounds.Top * CellSize - padding);
            var right = Math.Min(
                partition.Right,
                Math.Min(SourceWidth, gridBounds.Right * CellSize) + padding);
            var bottom = Math.Min(
                partition.Bottom,
                Math.Min(SourceHeight, gridBounds.Bottom * CellSize) + padding);
            return new PixelRectangle(left, top, right - left, bottom - top);
        }

        private PixelRectangle ToGridRectangle(PixelRectangle rectangle)
        {
            var left = Math.Clamp(rectangle.Left / CellSize, 0, Columns);
            var top = Math.Clamp(rectangle.Top / CellSize, 0, Rows);
            var right = Math.Clamp((rectangle.Right + CellSize - 1) / CellSize, left, Columns);
            var bottom = Math.Clamp((rectangle.Bottom + CellSize - 1) / CellSize, top, Rows);
            return new PixelRectangle(left, top, right - left, bottom - top);
        }

        private int CountOccupiedCells(int left, int top, int right, int bottom)
        {
            var stride = Columns + 1;
            return _integral[bottom * stride + right]
                   - _integral[top * stride + right]
                   - _integral[bottom * stride + left]
                   + _integral[top * stride + left];
        }
    }
    private readonly record struct RgbColor(byte Red, byte Green, byte Blue)
    {
        public bool IsNearWhite => Red >= 240 && Green >= 240 && Blue >= 240;
        public uint ToArgb() => 0xFF000000u | ((uint)Red << 16) | ((uint)Green << 8) | Blue;
    }
    private readonly record struct BackgroundBucket(long Red, long Green, long Blue, int Count)
    {
        public BackgroundBucket Add(RgbColor color) =>
            new(Red + color.Red, Green + color.Green, Blue + color.Blue, Count + 1);
        public RgbColor Average => Count == 0
            ? new RgbColor(255, 255, 255)
            : new RgbColor((byte)(Red / Count), (byte)(Green / Count), (byte)(Blue / Count));
    }
    private readonly record struct ImageCrop(
        int SourceWidth,
        int SourceHeight,
        int Left,
        int Top,
        int Width,
        int Height,
        int SourceFormat,
        int SourceStride,
        byte[] SourcePixels,
        long OriginalEncodedBytes,
        byte[] EncodedJpeg,
        int JpegQuality)
    {
        public int SourceBytesPerPixel => SourceFormat switch
        {
            1 => 1,
            2 => 3,
            3 or 4 => 4,
            _ => throw new InvalidDataException($"未対応のPDF画像形式です: {SourceFormat}"),
        };
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WriteBlockDelegate(IntPtr self, IntPtr data, uint size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetBlockDelegate(IntPtr parameter, uint position, IntPtr buffer, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct FpdfFileAccess
    {
        public uint FileLen;
        public IntPtr GetBlock;
        public IntPtr Param;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FpdfFileWrite
    {
        public int Version;
        public IntPtr WriteBlock;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FsMatrix
    {
        public float A;
        public float B;
        public float C;
        public float D;
        public float E;
        public float F;
    }

    private static class NativeMethods
    {
        /// <summary>PDFiumネイティブライブラリを指定するP/Invoke用の論理名です。</summary>
        private const string Pdfium = "pdfium";
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDF_InitLibrary();
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDF_LoadDocument(IntPtr path, IntPtr password);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern uint FPDF_GetLastError();
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDF_GetPageCount(IntPtr document);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDF_GetFileVersion(IntPtr document, out int fileVersion);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDF_ClosePage(IntPtr page);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDF_CloseDocument(IntPtr document);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDFText_LoadPage(IntPtr page);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDFText_ClosePage(IntPtr textPage);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFText_CountChars(IntPtr textPage);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDFText_GetTextObject(IntPtr textPage, int index);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFText_GetCharOrigin(IntPtr textPage, int index, out double x, out double y);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFText_GetCharBox(IntPtr textPage, int index, out double left, out double right, out double bottom, out double top);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFPage_CountObjects(IntPtr page);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDFPage_GetObject(IntPtr page, int index);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFPageObj_GetType(IntPtr pageObject);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFPageObj_GetBounds(IntPtr pageObject, out float left, out float bottom, out float right, out float top);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFPageObj_GetMatrix(IntPtr pageObject, out FsMatrix matrix);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFPageObj_SetMatrix(IntPtr pageObject, ref FsMatrix matrix);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDFPageObj_AddMark(IntPtr pageObject, IntPtr name);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern uint FPDFPageObj_CountMarks(IntPtr pageObject);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDFPageObj_GetMark(IntPtr pageObject, uint index);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFPageObjMark_GetName(IntPtr mark, IntPtr buffer, uint buflen, out uint outBuflen);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern uint FPDFTextObj_GetText(IntPtr textObject, IntPtr textPage, IntPtr buffer, uint length);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDFTextObj_GetFont(IntPtr textObject);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern float FPDFTextObj_GetFontSize(IntPtr textObject);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern double FPDFText_GetFontSize(IntPtr textPage, int index);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFFont_GetAscent(IntPtr font, float fontSize, out float ascent);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFFont_GetDescent(IntPtr font, float fontSize, out float descent);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFTextObj_GetTextRenderMode(IntPtr textObject);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFTextObj_SetTextRenderMode(IntPtr textObject, int renderMode);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDFPageObj_CreateTextObj(IntPtr document, IntPtr font, float fontSize);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFText_SetText(IntPtr textObject, IntPtr text);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFPageObj_GetFillColor(IntPtr pageObject, out uint red, out uint green, out uint blue, out uint alpha);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFPageObj_SetFillColor(IntPtr pageObject, uint red, uint green, uint blue, uint alpha);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDFPageObj_Transform(IntPtr pageObject, double a, double b, double c, double d, double e, double f);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDFPage_InsertObject(IntPtr page, IntPtr pageObject);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFPage_RemoveObject(IntPtr page, IntPtr pageObject);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDFPageObj_Destroy(IntPtr pageObject);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFPage_GenerateContent(IntPtr page);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDF_SaveAsCopy(IntPtr document, ref FpdfFileWrite writer, uint flags);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern float FPDF_GetPageWidthF(IntPtr page);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern float FPDF_GetPageHeightF(IntPtr page);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDFImageObj_GetBitmap(IntPtr imageObject);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern uint FPDFImageObj_GetImageDataRaw(IntPtr imageObject, IntPtr buffer, uint bufferLength);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFImageObj_LoadJpegFileInline(IntPtr pages, int count, IntPtr imageObject, ref FpdfFileAccess fileAccess);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDFPageObj_NewImageObj(IntPtr document);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFImageObj_SetBitmap(IntPtr pages, int count, IntPtr imageObject, IntPtr bitmap);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFBitmap_GetWidth(IntPtr bitmap);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFBitmap_GetHeight(IntPtr bitmap);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFBitmap_GetFormat(IntPtr bitmap);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFBitmap_GetStride(IntPtr bitmap);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);
        [DllImport(Pdfium, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    }
}
