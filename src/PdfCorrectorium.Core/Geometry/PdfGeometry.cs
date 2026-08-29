namespace PdfCorrectorium.Core.Geometry;

/// <summary>PDF座標系上の点を表します。</summary>
/// <param name="X">水平方向のPDFポイント座標。</param>
/// <param name="Y">垂直方向のPDFポイント座標。</param>
public readonly record struct PdfPoint(double X, double Y)
{
    /// <summary>XとYの両方が計算可能な有限値かを示します。</summary>
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

/// <summary>PDFポイント単位の幅と高さを表します。</summary>
/// <param name="Width">幅。</param>
/// <param name="Height">高さ。</param>
public readonly record struct PdfSize(double Width, double Height)
{
    /// <summary>幅と高さが有限かつ正であるかを示します。</summary>
    public bool IsValid => double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0;
}

/// <summary>PDF座標系上の軸平行矩形を表します。</summary>
/// <param name="Origin">左下原点。</param>
/// <param name="Size">幅と高さ。</param>
public readonly record struct PdfRectangle(PdfPoint Origin, PdfSize Size)
{
    /// <summary>矩形の左端X座標です。</summary>
    public double Left => Origin.X;
    /// <summary>矩形の下端Y座標です。</summary>
    public double Bottom => Origin.Y;
    /// <summary>矩形の右端X座標です。</summary>
    public double Right => Origin.X + Size.Width;
    /// <summary>矩形の上端Y座標です。</summary>
    public double Top => Origin.Y + Size.Height;
    /// <summary>原点と寸法がPDF出力に使用できる値かを示します。</summary>
    public bool IsValid => Origin.IsFinite && Size.IsValid;
}

/// <summary>
/// OCR文字列の位置、変形、回転および文字ごとの送り量を保持します。
/// </summary>
/// <remarks>
/// <see cref="LocalBounds"/>は回転前のローカル矩形です。画面またはPDFへ配置する際は、
/// 拡大縮小と文字送りを適用した後、<see cref="RotationCenter"/>を中心に回転します。
/// </remarks>
public sealed record TextGeometry
{
    /// <summary>回転とページ配置を適用する前の文字領域です。</summary>
    public required PdfRectangle LocalBounds { get; init; }
    /// <summary>ページ座標上で文字領域を回転させる中心点です。</summary>
    public required PdfPoint RotationCenter { get; init; }
    /// <summary>時計回りの回転角度です。</summary>
    public double RotationDegrees { get; init; }
    /// <summary>文字形状へ適用する水平方向倍率です。</summary>
    public double HorizontalScale { get; init; } = 1d;
    /// <summary>文字形状へ適用する垂直方向倍率です。</summary>
    public double VerticalScale { get; init; } = 1d;
    /// <summary>各文字の既定送りへ加える間隔補正値です。</summary>
    public double CharacterSpacing { get; init; }
    /// <summary>Unicodeテキスト要素ごとの書字方向に沿った送り量です。</summary>
    public IReadOnlyList<double> CharacterAdvances { get; init; } = [];
    /// <summary>領域全体の位置、寸法、回転を自動処理や誤操作から保護する場合は <c>true</c>。</summary>
    public bool IsGeometryLocked { get; init; }
    /// <summary>各Unicodeテキスト要素の位置と送り量を固定するフラグです。</summary>
    public IReadOnlyList<bool> CharacterLocks { get; init; } = [];

    /// <summary>PDFへ安全に書き出せる有限かつ正の幾何値であることを検証します。</summary>
    /// <returns>検出した問題を表す安定したエラーコードの一覧。</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!LocalBounds.IsValid) errors.Add("bounds.invalid");
        if (!RotationCenter.IsFinite) errors.Add("rotationCenter.invalid");
        if (!double.IsFinite(RotationDegrees)) errors.Add("rotation.invalid");
        if (!double.IsFinite(HorizontalScale) || HorizontalScale <= 0) errors.Add("horizontalScale.invalid");
        if (!double.IsFinite(VerticalScale) || VerticalScale <= 0) errors.Add("verticalScale.invalid");
        if (!double.IsFinite(CharacterSpacing)) errors.Add("characterSpacing.invalid");
        if (CharacterAdvances.Any(advance => !double.IsFinite(advance) || advance <= 0))
            errors.Add("characterAdvances.invalid");
        return errors;
    }

    /// <summary>浮動小数点誤差を考慮して別の幾何情報と等価かを判定します。</summary>
    /// <param name="other">比較対象。</param>
    /// <param name="tolerance">各数値を同一とみなす許容差。</param>
    /// <returns>全項目が許容差内で一致する場合は<c>true</c>。</returns>
    public bool IsEquivalentTo(TextGeometry other, double tolerance = 0.000001)
    {
        if (Math.Abs(LocalBounds.Left - other.LocalBounds.Left) > tolerance ||
            Math.Abs(LocalBounds.Bottom - other.LocalBounds.Bottom) > tolerance ||
            Math.Abs(LocalBounds.Size.Width - other.LocalBounds.Size.Width) > tolerance ||
            Math.Abs(LocalBounds.Size.Height - other.LocalBounds.Size.Height) > tolerance ||
            Math.Abs(RotationCenter.X - other.RotationCenter.X) > tolerance ||
            Math.Abs(RotationCenter.Y - other.RotationCenter.Y) > tolerance ||
            Math.Abs(RotationDegrees - other.RotationDegrees) > tolerance ||
            Math.Abs(HorizontalScale - other.HorizontalScale) > tolerance ||
            Math.Abs(VerticalScale - other.VerticalScale) > tolerance ||
            Math.Abs(CharacterSpacing - other.CharacterSpacing) > tolerance ||
            CharacterAdvances.Count != other.CharacterAdvances.Count ||
            IsGeometryLocked != other.IsGeometryLocked ||
            CharacterLocks.Count != other.CharacterLocks.Count)
            return false;

        return CharacterAdvances.Zip(other.CharacterAdvances).All(pair => Math.Abs(pair.First - pair.Second) <= tolerance) &&
               CharacterLocks.SequenceEqual(other.CharacterLocks);
    }
}
