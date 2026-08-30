using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App.ViewModels;

/// <summary>
/// Undo/Redoで復元できるOCR領域の編集状態を表します。
/// </summary>
/// <param name="Text">スナップショット取得時の文字列。</param>
/// <param name="Left">プレビュー画像上の左端位置。</param>
/// <param name="Top">プレビュー画像上の上端位置。</param>
/// <param name="Width">領域幅。</param>
/// <param name="Height">領域高。</param>
/// <param name="RotationDegrees">時計回りの回転角度。</param>
/// <param name="ReadingOrder">ページ内の読み順番号。</param>
/// <param name="WordReadingsText">単語と読みを行単位で表した編集文字列。</param>
/// <param name="CharacterAdvancesText">文字ごとの送り量を表した編集文字列。</param>
/// <param name="ReviewStatus">確認・修正状態。</param>
/// <param name="IsDeleted">PDF出力時に削除する領域の場合は<c>true</c>。</param>
/// <param name="IsVertical">縦書き・横書きの明示指定。未指定の場合は<c>null</c>。</param>
/// <param name="IsGeometryLocked">領域全体の位置、寸法、回転を固定するか。</param>
/// <param name="CharacterLocksText">各Unicodeテキスト要素の固定状態をセミコロン区切りで表した値。</param>
public sealed record OverlayRegionSnapshot(
    string Text,
    double Left,
    double Top,
    double Width,
    double Height,
    double RotationDegrees,
    int ReadingOrder = 0,
    string WordReadingsText = "",
    string CharacterAdvancesText = "",
    ReviewStatus ReviewStatus = ReviewStatus.Unreviewed,
    bool IsDeleted = false,
    bool? IsVertical = null,
    bool IsGeometryLocked = false,
    string CharacterLocksText = "");

/// <summary>
/// 文字編集モードで表示する、1テキスト要素分のローカル座標セルです。
/// </summary>
/// <param name="Index">行内で0から始まるテキスト要素番号。</param>
/// <param name="Text">セルに表示するUnicodeテキスト要素。</param>
/// <param name="Left">親領域左上を基準とするセル左端。</param>
/// <param name="Top">親領域左上を基準とするセル上端。</param>
/// <param name="Width">セル幅。</param>
/// <param name="Height">セル高。</param>
/// <param name="IsSelected">文字単位選択に含まれる場合は<c>true</c>。</param>
/// <param name="IsLocked">文字送り位置とサイズが固定されている場合は<c>true</c>。</param>
/// <param name="IsSearchMatch">現在表示中の検索結果に含まれる場合は<c>true</c>。</param>
public sealed record CharacterOverlayCell(
    int Index,
    string Text,
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsSelected,
    bool IsLocked = false,
    bool IsSearchMatch = false);

/// <summary>
/// 画面上の1つのOCR行または領域について、文字列、幾何情報、文字送り、選択状態を管理します。
/// </summary>
/// <remarks>
/// 文字位置は領域全体の座標と、Unicodeテキスト要素ごとの送り量に分けて保持します。
/// 結合文字やサロゲートペアを分断しないよう、文字境界の計算には
/// <see cref="StringInfo.ParseCombiningCharacters(string)"/> を使用します。
/// </remarks>
public sealed class OverlayRegionViewModel : INotifyPropertyChanged
{
    /// <summary>利用者が修正できる現在のOCR文字列です。</summary>
    private string _text;
    /// <summary>プレビュー画像座標における領域左端です。</summary>
    private double _left;
    /// <summary>プレビュー画像座標における領域上端です。</summary>
    private double _top;
    /// <summary>回転前のローカル座標における領域幅です。</summary>
    private double _width;
    /// <summary>回転前のローカル座標における領域高さです。</summary>
    private double _height;
    /// <summary>領域中心を基準にした時計回りの回転角度です。</summary>
    private double _rotationDegrees;
    /// <summary>検索・コピー・読み上げで使用するページ内の順序です。</summary>
    private int _readingOrder;
    /// <summary>「表記=よみ」を1行ずつ保持する編集用文字列です。</summary>
    private string _wordReadingsText;
    /// <summary>複数領域の整列とサイズ統一でこの領域を基準にするかを示します。</summary>
    private bool _isAlignmentReference;
    /// <summary>文字選択のうち、プロパティ編集対象になる主文字のインデックスです。</summary>
    private int _selectedCharacterIndex = -1;
    /// <summary>Shiftによる連続文字選択を開始した文字位置です。</summary>
    private int _characterSelectionAnchor = -1;
    /// <summary>未確認・修正済みなど、人による確認作業の状態です。</summary>
    private ReviewStatus _reviewStatus;
    /// <summary>PDF出力時にこの領域を除外する削除予定フラグです。</summary>
    private bool _isDeleted;
    /// <summary>文字送りの主軸が上から下であるかを示します。</summary>
    private bool _isVertical;
    /// <summary>領域全体の位置、寸法、回転を固定するかを示します。</summary>
    private bool _isGeometryLocked;
    /// <summary>Unicodeテキスト要素ごとの、書字方向に沿った送り量です。</summary>
    private readonly List<double> _characterAdvances = [];
    /// <summary>各Unicodeテキスト要素の位置と送り量を固定するフラグです。</summary>
    private readonly List<bool> _characterLocks = [];
    /// <summary>文字編集モードで複数選択されているテキスト要素のインデックスです。</summary>
    private readonly SortedSet<int> _selectedCharacterIndices = [];
    /// <summary>検索結果として一時強調する、OCR領域文字列内のUTF-16開始位置です。</summary>
    private int _searchHighlightStart = -1;
    /// <summary>検索結果として一時強調するUTF-16文字数です。</summary>
    private int _searchHighlightLength;

    public OverlayRegionViewModel(PdfTextOverlayRegion source, int readingOrder = 0, string wordReadingsText = "")
        : this(
            Guid.NewGuid(),
            source.Text,
            new OverlayRegionSnapshot(source.Text, source.Left, source.Top, source.Width, source.Height, source.RotationDegrees, readingOrder, wordReadingsText,
                CreateCharacterAdvancesText(source.Text, source.IsVertical ? source.Height : source.Width, source.CharacterAdvances),
                IsVertical: source.IsVertical),
            new OverlayRegionSnapshot(source.Text, source.Left, source.Top, source.Width, source.Height, source.RotationDegrees, readingOrder, wordReadingsText,
                CreateCharacterAdvancesText(source.Text, source.IsVertical ? source.Height : source.Width, source.CharacterAdvances),
                IsVertical: source.IsVertical),
            source.IsInvisible,
            source.IsVertical,
            source.ProviderId,
            source.Confidence,
            false,
            false)
    {
    }

    public OverlayRegionViewModel(
        Guid id,
        string originalText,
        OverlayRegionSnapshot original,
        OverlayRegionSnapshot current,
        bool isInvisible,
        bool isVertical,
        string providerId,
        double? confidence,
        bool isAdded = false,
        bool isDeleted = false)
    {
        Id = id;
        OriginalText = originalText;
        _isVertical = current.IsVertical ?? isVertical;
        LoadedIsVertical = _isVertical;
        _text = current.Text;
        _left = current.Left;
        _top = current.Top;
        _width = current.Width;
        _height = current.Height;
        _rotationDegrees = current.RotationDegrees;
        _readingOrder = current.ReadingOrder;
        _wordReadingsText = current.WordReadingsText;
        _reviewStatus = current.ReviewStatus;
        _isGeometryLocked = current.IsGeometryLocked;
        IsInvisible = isInvisible;
        ProviderId = providerId;
        Confidence = confidence;
        IsAdded = isAdded;
        Original = original with { IsVertical = original.IsVertical ?? isVertical };
        _isDeleted = isDeleted;
        // Restore locks first. Reconciliation of the advances must know which
        // character boundaries are immutable; otherwise loading a project can
        // normalize and silently change a locked character before its lock is read.
        ApplyCharacterLocks(current.CharacterLocksText);
        ApplyCharacterAdvances(current.CharacterAdvancesText);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid Id { get; }
    public string OriginalText { get; }
    public bool IsInvisible { get; }
    /// <summary>読込時の自動判定を、利用者による方向変更と区別する基準です。</summary>
    internal bool LoadedIsVertical { get; }
    public bool IsVertical
    {
        get => _isVertical;
        set
        {
            if (_isVertical == value) return;
            _isVertical = value;
            (_width, _height) = (_height, _width);
            ReconcileCharacterAdvances();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(Height));
            OnPropertyChanged(nameof(IsModified));
            NotifyCharacterSelection();
        }
    }
    public string ProviderId { get; }
    public double? Confidence { get; }
    public bool IsAdded { get; }
    /// <summary>領域全体の移動、サイズ変更、回転、および自動調整を禁止するかを示します。</summary>
    public bool IsGeometryLocked
    {
        get => _isGeometryLocked;
        set
        {
            if (!Set(ref _isGeometryLocked, value)) return;
            OnPropertyChanged(nameof(IsModified));
            OnPropertyChanged(nameof(CanAutomaticallyAdjust));
        }
    }
    public bool IsDeleted
    {
        get => _isDeleted;
        set
        {
            if (!Set(ref _isDeleted, value)) return;
            OnPropertyChanged(nameof(IsModified));
        }
    }
    public OverlayRegionSnapshot Original { get; }
    public string Text
    {
        get => _text;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_text, normalized, StringComparison.Ordinal)) return;
            // Capture the existing cell geometry before replacing the text.  A plain
            // count-based normalization would resize every preceding character even
            // when the user only corrected one character near the end of the line.
            ReconcileCharacterAdvances();
            ReconcileCharacterLocks();
            var previousText = _text;
            var previousAdvances = _characterAdvances.ToArray();
            var previousLocks = _characterLocks.ToArray();
            _text = normalized;
            _searchHighlightStart = -1;
            _searchHighlightLength = 0;
            ReconcileCharacterStateAfterTextEdit(previousText, previousAdvances, previousLocks);
            ClampCharacterSelection();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsModified));
            NotifyCharacterSelection();
        }
    }
    public double Left { get => _left; set => Set(ref _left, Math.Max(0, value)); }
    public double Top { get => _top; set => Set(ref _top, Math.Max(0, value)); }
    public double Width
    {
        get => _width;
        set
        {
            var normalized = Math.Max(4, value);
            var previous = _width;
            if (Math.Abs(previous - normalized) < 0.000001) return;
            _width = normalized;
            if (!IsVertical) ScaleCharacterAdvances(previous, normalized);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsModified));
            NotifyCharacterSelection();
        }
    }
    public double Height
    {
        get => _height;
        set
        {
            var normalized = Math.Max(4, value);
            var previous = _height;
            if (Math.Abs(previous - normalized) < 0.000001) return;
            _height = normalized;
            if (IsVertical) ScaleCharacterAdvances(previous, normalized);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsModified));
            NotifyCharacterSelection();
        }
    }
    public double RotationDegrees { get => _rotationDegrees; set => Set(ref _rotationDegrees, NormalizeDegrees(value)); }
    public int ReadingOrder { get => _readingOrder; set => Set(ref _readingOrder, Math.Max(1, value)); }
    public string WordReadingsText
    {
        get => _wordReadingsText;
        set
        {
            if (!Set(ref _wordReadingsText, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(WordReadingsValidationMessage));
            OnPropertyChanged(nameof(HasWordReadingErrors));
        }
    }
    public string WordReadingsValidationMessage => ValidateWordReadings(WordReadingsText);
    public bool HasWordReadingErrors => WordReadingsValidationMessage.Length > 0;
    public bool IsAlignmentReference { get => _isAlignmentReference; set => Set(ref _isAlignmentReference, value); }
    public ReviewStatus ReviewStatus { get => _reviewStatus; set => Set(ref _reviewStatus, value); }
    public int SelectedCharacterIndex
    {
        get => _selectedCharacterIndex;
        set
        {
            var count = TextElementCount;
            var normalized = count == 0 || value < 0 ? -1 : Math.Clamp(value, 0, count - 1);
            if (normalized < 0)
            {
                if (_selectedCharacterIndex < 0 && _selectedCharacterIndices.Count == 0) return;
                _selectedCharacterIndex = -1;
                _characterSelectionAnchor = -1;
                _selectedCharacterIndices.Clear();
            }
            else
            {
                if (_selectedCharacterIndex == normalized && _selectedCharacterIndices.Count == 1 && _selectedCharacterIndices.Contains(normalized)) return;
                _selectedCharacterIndex = normalized;
                _characterSelectionAnchor = normalized;
                _selectedCharacterIndices.Clear();
                _selectedCharacterIndices.Add(normalized);
            }
            NotifyCharacterSelection();
        }
    }
    public IReadOnlyList<int> SelectedCharacterIndices => _selectedCharacterIndices.ToArray();
    public int SelectedCharacterCount => _selectedCharacterIndices.Count;
    public bool HasCharacterSelection => _selectedCharacterIndices.Count > 0 && TextElementCount > 0;
    public bool HasSingleCharacterSelection => SelectedCharacterCount == 1;
    public bool HasMultipleCharacterSelection => SelectedCharacterCount > 1;
    public bool HasHorizontalCharacterSelection => HasCharacterSelection && !IsVertical;
    public bool HasVerticalCharacterSelection => HasCharacterSelection && IsVertical;
    /// <summary>位置または送り量を固定した文字が1つ以上あるかを示します。</summary>
    public bool HasLockedCharacters => _characterLocks.Any(value => value);
    /// <summary>自動調整可能な未固定文字が1つ以上あるかを示します。</summary>
    public bool HasUnlockedCharacters => _characterLocks.Any(value => !value);
    /// <summary>選択中のすべての文字が固定済みかを示します。</summary>
    public bool AreSelectedCharactersLocked =>
        HasCharacterSelection && _selectedCharacterIndices.All(index => index < _characterLocks.Count && _characterLocks[index]);
    /// <summary>選択中に固定済みの文字が1つ以上含まれるかを示します。</summary>
    public bool HasLockedSelectedCharacters =>
        HasCharacterSelection && _selectedCharacterIndices.Any(index => index < _characterLocks.Count && _characterLocks[index]);
    /// <summary>選択中に手動調整可能な未固定文字が含まれるかを示します。</summary>
    public bool HasUnlockedSelectedCharacters =>
        HasCharacterSelection && _selectedCharacterIndices.Any(index => index < _characterLocks.Count && !_characterLocks[index]);
    /// <summary>固定境界を保持しながら文字送りを自動調整できるかを示します。</summary>
    public bool CanAutomaticallyAdjust => !IsGeometryLocked && HasUnlockedCharacters && TextElementCount > 1;

    /// <summary>書字方向と直交する、行の見た目上の太さを返します。</summary>
    public double LineThickness => IsVertical ? Width : Height;

    /// <summary>書字方向に沿った、行領域の長さを返します。</summary>
    public double WritingExtent => IsVertical ? Height : Width;

    /// <summary>
    /// Unicode テキスト要素ごとの、書字方向に沿った現在の送り量を返します。
    /// </summary>
    /// <remarks>
    /// 呼び出し側が内部状態を変更できないよう、正規化後の値をコピーして返します。
    /// </remarks>
    public IReadOnlyList<double> CharacterAdvances
    {
        get
        {
            ReconcileCharacterAdvances();
            return _characterAdvances.ToArray();
        }
    }

    /// <summary>
    /// 文字送りを変更せずに、行頭側および行末側へ領域を広げます。
    /// </summary>
    /// <param name="leadingAmount">行頭側へ加えるプレビュー画像座標の長さ。</param>
    /// <param name="trailingAmount">行末側へ加えるプレビュー画像座標の長さ。</param>
    /// <param name="pageWidth">領域を収めるページ画像の幅。</param>
    /// <param name="pageHeight">領域を収めるページ画像の高さ。</param>
    /// <returns>領域が実際に変更された場合は <c>true</c>。</returns>
    public bool ExpandWritingBoundsPreservingAdvances(
        double leadingAmount,
        double trailingAmount,
        double pageWidth,
        double pageHeight)
    {
        if (IsGeometryLocked) return false;
        leadingAmount = Math.Max(0, leadingAmount);
        trailingAmount = Math.Max(0, trailingAmount);
        if (leadingAmount + trailingAmount < 0.001) return false;

        var oldWidth = Width;
        var oldHeight = Height;
        var oldExtent = WritingExtent;
        var newExtent = oldExtent + leadingAmount + trailingAmount;
        var localCenterShift = (trailingAmount - leadingAmount) / 2d;
        var radians = RotationDegrees * Math.PI / 180d;
        var axisX = IsVertical ? -Math.Sin(radians) : Math.Cos(radians);
        var axisY = IsVertical ? Math.Cos(radians) : Math.Sin(radians);
        var centerX = Left + oldWidth / 2d + axisX * localCenterShift;
        var centerY = Top + oldHeight / 2d + axisY * localCenterShift;
        var newWidth = IsVertical ? oldWidth : newExtent;
        var newHeight = IsVertical ? newExtent : oldHeight;

        _width = newWidth;
        _height = newHeight;
        _left = Math.Clamp(centerX - newWidth / 2d, 0, Math.Max(0, pageWidth - newWidth));
        _top = Math.Clamp(centerY - newHeight / 2d, 0, Math.Max(0, pageHeight - newHeight));
        NotifyGeometryChanged();
        return Math.Abs(oldWidth - newWidth) > 0.001 || Math.Abs(oldHeight - newHeight) > 0.001;
    }

    /// <summary>
    /// 行の中心と文字送りを維持したまま、行の太さだけを変更します。
    /// </summary>
    /// <param name="targetThickness">変更後の行の太さ。</param>
    /// <param name="pageWidth">領域を収めるページ画像の幅。</param>
    /// <param name="pageHeight">領域を収めるページ画像の高さ。</param>
    /// <returns>領域が実際に変更された場合は <c>true</c>。</returns>
    public bool SetLineThicknessPreservingAdvances(
        double targetThickness,
        double pageWidth,
        double pageHeight)
    {
        if (IsGeometryLocked || !double.IsFinite(targetThickness)) return false;
        targetThickness = Math.Max(4, targetThickness);
        if (Math.Abs(LineThickness - targetThickness) < 0.001) return false;

        var centerX = Left + Width / 2d;
        var centerY = Top + Height / 2d;
        if (IsVertical) _width = targetThickness;
        else _height = targetThickness;
        _left = Math.Clamp(centerX - _width / 2d, 0, Math.Max(0, pageWidth - _width));
        _top = Math.Clamp(centerY - _height / 2d, 0, Math.Max(0, pageHeight - _height));
        NotifyGeometryChanged();
        return true;
    }

    /// <summary>
    /// 指定した文字以降に、自動調整できる未固定文字があるかを返します。
    /// </summary>
    /// <param name="startIndex">確認を開始する Unicode テキスト要素の位置。</param>
    public bool HasUnlockedCharacterAtOrAfter(int startIndex)
    {
        ReconcileCharacterLocks();
        return startIndex >= 0 && startIndex < _characterLocks.Count &&
               _characterLocks.Skip(startIndex).Any(isLocked => !isLocked);
    }
    public bool CanEqualizeCharacterAdvances => !IsGeometryLocked && HasUnlockedCharacters && TextElementCount > 1;
    public bool CanRestoreOriginalCharacterAdvances =>
        !IsGeometryLocked && HasUnlockedCharacters &&
        ParseCharacterAdvances(Original.CharacterAdvancesText).Count == TextElementCount && TextElementCount > 0;
    public int TextElementCount => StringInfo.ParseCombiningCharacters(Text).Length;
    public double CharacterSelectionLeft => SelectedCells().Select(cell => cell.Left).DefaultIfEmpty(0).Min();
    public double CharacterSelectionTop => SelectedCells().Select(cell => cell.Top).DefaultIfEmpty(0).Min();
    public double CharacterSelectionWidth
    {
        get
        {
            var cells = SelectedCells();
            return cells.Count == 0 ? 0 : cells.Max(cell => cell.Left + cell.Width) - cells.Min(cell => cell.Left);
        }
    }
    public double CharacterSelectionHeight
    {
        get
        {
            var cells = SelectedCells();
            return cells.Count == 0 ? 0 : cells.Max(cell => cell.Top + cell.Height) - cells.Min(cell => cell.Top);
        }
    }
    public double CharacterSelectionRight => CharacterSelectionLeft + CharacterSelectionWidth;
    public double CharacterSelectionBottom => CharacterSelectionTop + CharacterSelectionHeight;
    public double SelectedCharacterAdvance
    {
        get
        {
            var selected = _selectedCharacterIndices.Where(index => index < _characterAdvances.Count && !_characterLocks[index]).ToArray();
            return selected.Length == 0 ? 0 : selected.Average(index => _characterAdvances[index]);
        }
        set
        {
            var selected = _selectedCharacterIndices.Where(index => index < _characterAdvances.Count && !_characterLocks[index]).ToArray();
            if (selected.Length == 0) return;
            var normalized = Math.Max(1, Math.Round(value, 3));
            if (selected.All(index => Math.Abs(_characterAdvances[index] - normalized) < 0.0005)) return;
            foreach (var index in selected) _characterAdvances[index] = normalized;
            UpdateExtentFromCharacterAdvances();
        }
    }
    /// <summary>
    /// 現在の文字送りと書字方向から計算した、各テキスト要素の表示セルを取得します。
    /// </summary>
    public IReadOnlyList<CharacterOverlayCell> CharacterCells
    {
        get
        {
            var indexes = StringInfo.ParseCombiningCharacters(Text);
            if (indexes.Length == 0) return [];
            ReconcileCharacterAdvances();
            var cells = new CharacterOverlayCell[indexes.Length];
            var offset = 0d;
            for (var index = 0; index < indexes.Length; index++)
            {
                var start = indexes[index];
                var end = index + 1 < indexes.Length ? indexes[index + 1] : Text.Length;
                var advance = _characterAdvances[index];
                var cellWidth = IsVertical ? Width : advance;
                var cellHeight = IsVertical ? advance : Height;
                var isSearchMatch = _searchHighlightStart >= 0 &&
                                    end > _searchHighlightStart &&
                                    start < _searchHighlightStart + _searchHighlightLength;
                cells[index] = new CharacterOverlayCell(
                    index,
                    Text[start..end],
                    IsVertical ? 0 : offset,
                    IsVertical ? offset : 0,
                    cellWidth,
                    cellHeight,
                    _selectedCharacterIndices.Contains(index),
                    _characterLocks[index],
                    isSearchMatch);
                offset += advance;
            }
            return cells;
        }
    }

    /// <summary>文字列内の一致範囲を、Unicodeテキスト要素のセル単位で一時強調します。</summary>
    public void SetSearchHighlightByTextOffset(int startIndex, int length)
    {
        var normalizedStart = Math.Clamp(startIndex, 0, Text.Length);
        var normalizedLength = Math.Clamp(length, 0, Text.Length - normalizedStart);
        if (_searchHighlightStart == normalizedStart && _searchHighlightLength == normalizedLength) return;
        _searchHighlightStart = normalizedLength > 0 ? normalizedStart : -1;
        _searchHighlightLength = normalizedLength;
        OnPropertyChanged(nameof(CharacterCells));
    }

    /// <summary>検索結果へ移動した際の一時的な文字強調を解除します。</summary>
    public void ClearSearchHighlight()
    {
        if (_searchHighlightStart < 0 && _searchHighlightLength == 0) return;
        _searchHighlightStart = -1;
        _searchHighlightLength = 0;
        OnPropertyChanged(nameof(CharacterCells));
    }
    /// <summary>
    /// 取込時の状態または追加直後の状態から変更されているかを取得します。
    /// </summary>
    public bool IsModified =>
        IsAdded ||
        Capture() with { ReviewStatus = Original.ReviewStatus } != Original;

    /// <summary>
    /// 現在の編集状態をUndo/Redo用の不変スナップショットとして取得します。
    /// </summary>
    public OverlayRegionSnapshot Capture() => new(Text, Left, Top, Width, Height, RotationDegrees, ReadingOrder, WordReadingsText, SerializeCharacterAdvances(), ReviewStatus, IsDeleted, IsVertical, IsGeometryLocked, SerializeCharacterLocks());

    /// <summary>主選択文字を後半領域の先頭として、この領域を安全に2分割できるかを示します。</summary>
    public bool CanSplitAtSelectedCharacter =>
        !IsGeometryLocked && HasSingleCharacterSelection && SelectedCharacterIndex > 0 && SelectedCharacterIndex < TextElementCount;

    /// <summary>
    /// 主選択文字を境界として、前半・後半のOCR領域スナップショットを生成します。
    /// </summary>
    /// <remarks>
    /// 選択文字は後半領域の先頭になります。文字送りと固定状態はそのまま分配し、
    /// 回転している領域でも書字方向の軸上で位置が連続するように中心座標を求めます。
    /// </remarks>
    public (OverlayRegionSnapshot Leading, OverlayRegionSnapshot Trailing) CreateSplitSnapshots()
    {
        if (!CanSplitAtSelectedCharacter)
            throw new InvalidOperationException("OCR領域を分割できる文字境界が選択されていません。");

        ReconcileCharacterAdvances();
        ReconcileCharacterLocks();
        var elements = GetTextElements(Text);
        var splitIndex = SelectedCharacterIndex;
        var leadingAdvances = _characterAdvances.Take(splitIndex).ToArray();
        var trailingAdvances = _characterAdvances.Skip(splitIndex).ToArray();
        var leadingLocks = _characterLocks.Take(splitIndex).ToArray();
        var trailingLocks = _characterLocks.Skip(splitIndex).ToArray();
        var leadingExtent = leadingAdvances.Sum();
        var trailingExtent = trailingAdvances.Sum();
        var totalExtent = leadingExtent + trailingExtent;
        var radians = RotationDegrees * Math.PI / 180d;
        var axisX = IsVertical ? -Math.Sin(radians) : Math.Cos(radians);
        var axisY = IsVertical ? Math.Cos(radians) : Math.Sin(radians);
        var centerX = Left + Width / 2d;
        var centerY = Top + Height / 2d;
        var startX = centerX - axisX * totalExtent / 2d;
        var startY = centerY - axisY * totalExtent / 2d;

        OverlayRegionSnapshot CreatePart(
            IReadOnlyList<string> partElements,
            IReadOnlyList<double> partAdvances,
            IReadOnlyList<bool> partLocks,
            double precedingExtent,
            int readingOrder,
            string readings)
        {
            var extent = partAdvances.Sum();
            var partCenterX = startX + axisX * (precedingExtent + extent / 2d);
            var partCenterY = startY + axisY * (precedingExtent + extent / 2d);
            var partWidth = IsVertical ? Width : extent;
            var partHeight = IsVertical ? extent : Height;
            return new OverlayRegionSnapshot(
                string.Concat(partElements),
                Math.Max(0, partCenterX - partWidth / 2d),
                Math.Max(0, partCenterY - partHeight / 2d),
                Math.Max(4, partWidth),
                Math.Max(4, partHeight),
                RotationDegrees,
                readingOrder,
                readings,
                SerializeCharacterAdvances(partAdvances),
                ReviewStatus.Modified,
                false,
                IsVertical,
                false,
                SerializeCharacterLocks(partLocks));
        }

        return (
            CreatePart(elements.Take(splitIndex).ToArray(), leadingAdvances, leadingLocks, 0, ReadingOrder, WordReadingsText),
            CreatePart(elements.Skip(splitIndex).ToArray(), trailingAdvances, trailingLocks, leadingExtent, ReadingOrder + 1, string.Empty));
    }

    /// <summary>指定領域と、同一行上の隣接領域として結合できるかを判定します。</summary>
    public bool CanMergeWith(OverlayRegionViewModel? other)
    {
        if (other is null || ReferenceEquals(this, other) || IsDeleted || other.IsDeleted ||
            IsGeometryLocked || other.IsGeometryLocked || IsVertical != other.IsVertical)
            return false;
        var angleDifference = Math.Abs(((RotationDegrees - other.RotationDegrees + 540d) % 360d) - 180d);
        if (angleDifference > 3d) return false;

        var radians = RotationDegrees * Math.PI / 180d;
        var axisX = IsVertical ? -Math.Sin(radians) : Math.Cos(radians);
        var axisY = IsVertical ? Math.Cos(radians) : Math.Sin(radians);
        var crossX = -axisY;
        var crossY = axisX;
        var dx = other.Left + other.Width / 2d - (Left + Width / 2d);
        var dy = other.Top + other.Height / 2d - (Top + Height / 2d);
        var crossDistance = Math.Abs(dx * crossX + dy * crossY);
        var crossSize = Math.Max(IsVertical ? Width : Height, other.IsVertical ? other.Width : other.Height);
        if (crossDistance > Math.Max(6d, crossSize * 0.8d)) return false;

        var longitudinalDistance = Math.Abs(dx * axisX + dy * axisY);
        var combinedHalfExtent = ((IsVertical ? Height : Width) + (other.IsVertical ? other.Height : other.Width)) / 2d;
        var gap = longitudinalDistance - combinedHalfExtent;
        return gap <= Math.Max(12d, crossSize * 3d);
    }

    /// <summary>この領域と同一行上の隣接領域を、1つの文字列・文字送りへ結合します。</summary>
    public OverlayRegionSnapshot CreateMergedSnapshotWith(OverlayRegionViewModel other)
    {
        if (!CanMergeWith(other))
            throw new InvalidOperationException("選択したOCR領域は同一行上で隣接していません。");

        ReconcileCharacterAdvances();
        ReconcileCharacterLocks();
        other.ReconcileCharacterAdvances();
        other.ReconcileCharacterLocks();
        var radians = RotationDegrees * Math.PI / 180d;
        var axisX = IsVertical ? -Math.Sin(radians) : Math.Cos(radians);
        var axisY = IsVertical ? Math.Cos(radians) : Math.Sin(radians);
        var thisProjection = (Left + Width / 2d) * axisX + (Top + Height / 2d) * axisY;
        var otherProjection = (other.Left + other.Width / 2d) * axisX + (other.Top + other.Height / 2d) * axisY;
        var first = thisProjection <= otherProjection ? this : other;
        var second = ReferenceEquals(first, this) ? other : this;
        var firstAdvances = first._characterAdvances.ToArray();
        var secondAdvances = second._characterAdvances.ToArray();
        var firstExtent = firstAdvances.Sum();
        var secondExtent = secondAdvances.Sum();
        var firstCenterProjection = (first.Left + first.Width / 2d) * axisX + (first.Top + first.Height / 2d) * axisY;
        var secondCenterProjection = (second.Left + second.Width / 2d) * axisX + (second.Top + second.Height / 2d) * axisY;
        var gap = Math.Max(0d, secondCenterProjection - secondExtent / 2d - (firstCenterProjection + firstExtent / 2d));
        if (firstAdvances.Length > 0) firstAdvances[^1] += gap;
        var mergedAdvances = firstAdvances.Concat(secondAdvances).ToArray();
        var mergedLocks = first._characterLocks.Concat(second._characterLocks).ToArray();
        var totalExtent = mergedAdvances.Sum();
        var leadingProjection = firstCenterProjection - firstExtent / 2d;
        var mergedCenterProjection = leadingProjection + totalExtent / 2d;
        var firstCross = (first.Left + first.Width / 2d) * -axisY + (first.Top + first.Height / 2d) * axisX;
        var secondCross = (second.Left + second.Width / 2d) * -axisY + (second.Top + second.Height / 2d) * axisX;
        var mergedCross = (firstCross + secondCross) / 2d;
        var centerX = axisX * mergedCenterProjection - axisY * mergedCross;
        var centerY = axisY * mergedCenterProjection + axisX * mergedCross;
        var mergedWidth = IsVertical ? Math.Max(Width, other.Width) : totalExtent;
        var mergedHeight = IsVertical ? totalExtent : Math.Max(Height, other.Height);
        var readings = string.Join(Environment.NewLine,
            new[] { first.WordReadingsText, second.WordReadingsText }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return new OverlayRegionSnapshot(
            first.Text + second.Text,
            Math.Max(0, centerX - mergedWidth / 2d),
            Math.Max(0, centerY - mergedHeight / 2d),
            Math.Max(4, mergedWidth),
            Math.Max(4, mergedHeight),
            RotationDegrees,
            Math.Min(ReadingOrder, other.ReadingOrder),
            readings,
            SerializeCharacterAdvances(mergedAdvances),
            ReviewStatus.Modified,
            false,
            IsVertical,
            false,
            SerializeCharacterLocks(mergedLocks));
    }

    /// <summary>
    /// 保存済みスナップショットの全編集値をこの領域へ復元します。
    /// </summary>
    /// <param name="snapshot">復元する状態。</param>
    public void Apply(OverlayRegionSnapshot snapshot)
    {
        Text = snapshot.Text;
        Left = snapshot.Left;
        Top = snapshot.Top;
        if (snapshot.IsVertical is bool isVertical) IsVertical = isVertical;
        Width = snapshot.Width;
        Height = snapshot.Height;
        RotationDegrees = snapshot.RotationDegrees;
        ReadingOrder = snapshot.ReadingOrder;
        WordReadingsText = snapshot.WordReadingsText;
        ReviewStatus = snapshot.ReviewStatus;
        IsDeleted = snapshot.IsDeleted;
        // Apply the lock map before advances for the same reason as construction:
        // a locked advance is persisted data and must never be normalized away.
        ApplyCharacterLocks(snapshot.CharacterLocksText);
        ApplyCharacterAdvances(snapshot.CharacterAdvancesText);
        IsGeometryLocked = snapshot.IsGeometryLocked;
        ClampCharacterSelection();
        OnPropertyChanged(nameof(IsModified));
    }

    /// <summary>
    /// 主選択されているUnicodeテキスト要素を返します。
    /// </summary>
    /// <returns>選択文字。選択がない場合は空文字列。</returns>
    public string GetSelectedCharacter()
    {
        if (!HasSingleCharacterSelection) return string.Empty;
        var indexes = StringInfo.ParseCombiningCharacters(Text);
        var start = indexes[SelectedCharacterIndex];
        var end = SelectedCharacterIndex + 1 < indexes.Length ? indexes[SelectedCharacterIndex + 1] : Text.Length;
        return Text[start..end];
    }

    /// <summary>
    /// 主選択中の1文字セルを、指定された1個以上のUnicodeテキスト要素へ置き換えます。
    /// </summary>
    /// <remarks>
    /// 複数文字へ置換した場合は、選択セルが占めていた送り幅だけを新しい文字数で等分します。
    /// したがって、選択セルより前後にある文字の位置、手動調整済みの送り幅、およびロック状態は変化しません。
    /// 新しいセルは元セルのロック状態を引き継ぎ、置換後は追加された文字をまとめて選択します。
    /// 空文字列を指定した場合は、選択セルを削除します。
    /// </remarks>
    /// <param name="replacement">置換後の文字列。複数文字および空文字列を指定できます。</param>
    public void ReplaceSelectedCharacter(string replacement)
    {
        if (!HasSingleCharacterSelection) return;
        ReconcileCharacterAdvances();
        ReconcileCharacterLocks();

        // Keep the leading edge and every unaffected character cell stable.  In
        // particular, deleting a character must not make the normalizer stretch
        // all cells that preceded it merely to fill the old line width.
        var oldExtent = IsVertical ? Height : Width;

        var selectedIndex = SelectedCharacterIndex;
        var previousElements = GetTextElements(_text);
        if (selectedIndex < 0 || selectedIndex >= previousElements.Count) return;

        var replacementElements = GetTextElements(replacement ?? string.Empty);
        var selectedAdvance = _characterAdvances[selectedIndex];
        var selectedLock = _characterLocks[selectedIndex];
        var updatedElements = previousElements
            .Take(selectedIndex)
            .Concat(replacementElements)
            .Concat(previousElements.Skip(selectedIndex + 1))
            .ToArray();

        _characterAdvances.RemoveAt(selectedIndex);
        _characterLocks.RemoveAt(selectedIndex);
        if (replacementElements.Count > 0)
        {
            var splitAdvance = selectedAdvance / replacementElements.Count;
            _characterAdvances.InsertRange(
                selectedIndex,
                Enumerable.Repeat(splitAdvance, replacementElements.Count));
            _characterLocks.InsertRange(
                selectedIndex,
                Enumerable.Repeat(selectedLock, replacementElements.Count));
        }

        _text = string.Concat(updatedElements);
        _selectedCharacterIndices.Clear();
        if (replacementElements.Count > 0)
        {
            for (var index = 0; index < replacementElements.Count; index++)
                _selectedCharacterIndices.Add(selectedIndex + index);
            _selectedCharacterIndex = selectedIndex;
            _characterSelectionAnchor = selectedIndex;
        }
        else if (updatedElements.Length > 0)
        {
            _selectedCharacterIndex = Math.Min(selectedIndex, updatedElements.Length - 1);
            _characterSelectionAnchor = _selectedCharacterIndex;
            _selectedCharacterIndices.Add(_selectedCharacterIndex);
        }
        else
        {
            _selectedCharacterIndex = -1;
            _characterSelectionAnchor = -1;
        }

        UpdateExtentKeepingLeadingBoundary(oldExtent, Math.Max(4, _characterAdvances.Sum()));

        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(IsModified));
        NotifyCharacterSelection();
    }

    /// <summary>
    /// 領域内のローカル座標を含む文字セルのインデックスを検索します。
    /// </summary>
    /// <returns>該当する文字セルのインデックス。範囲外の場合は-1。</returns>
    public int FindCharacterIndexAt(double localX, double localY)
    {
        var position = IsVertical ? localY : localX;
        var cells = CharacterCells;
        for (var index = 0; index < cells.Count; index++)
        {
            var cell = cells[index];
            var start = IsVertical ? cell.Top : cell.Left;
            var end = start + (IsVertical ? cell.Height : cell.Width);
            if (position >= start && (position < end || index == cells.Count - 1)) return index;
        }
        return cells.Count == 0 ? -1 : Math.Clamp(position < 0 ? 0 : cells.Count - 1, 0, cells.Count - 1);
    }

    /// <summary>
    /// 文字を単独選択、追加選択、または直前の主選択から範囲選択します。
    /// </summary>
    /// <param name="index">選択する文字セルのインデックス。</param>
    /// <param name="toggle">現在の選択状態を反転するか。</param>
    /// <param name="extendRange">主選択との間を連続選択するか。</param>
    public void SelectCharacter(int index, bool toggle, bool extendRange)
    {
        var count = TextElementCount;
        if (count == 0 || index < 0)
        {
            ClearCharacterSelection();
            return;
        }
        index = Math.Clamp(index, 0, count - 1);
        if (extendRange && _characterSelectionAnchor >= 0)
        {
            _selectedCharacterIndices.Clear();
            var start = Math.Min(_characterSelectionAnchor, index);
            var end = Math.Max(_characterSelectionAnchor, index);
            for (var current = start; current <= end; current++) _selectedCharacterIndices.Add(current);
            _selectedCharacterIndex = index;
        }
        else if (toggle)
        {
            if (!_selectedCharacterIndices.Add(index)) _selectedCharacterIndices.Remove(index);
            _selectedCharacterIndex = _selectedCharacterIndices.Contains(index)
                ? index
                : _selectedCharacterIndices.Count == 0 ? -1 : _selectedCharacterIndices.Max;
            _characterSelectionAnchor = _selectedCharacterIndex;
        }
        else
        {
            _selectedCharacterIndices.Clear();
            _selectedCharacterIndices.Add(index);
            _selectedCharacterIndex = index;
            _characterSelectionAnchor = index;
        }
        NotifyCharacterSelection();
    }

    /// <summary>
    /// UTF-16文字列内の検索一致範囲を、画面上のUnicode文字セル選択へ変換します。
    /// </summary>
    /// <param name="textOffset">一致範囲のUTF-16開始位置。</param>
    /// <param name="textLength">一致範囲のUTF-16長。</param>
    public void SelectCharacterRangeByTextOffset(int textOffset, int textLength)
    {
        _selectedCharacterIndices.Clear();
        var starts = StringInfo.ParseCombiningCharacters(Text);
        if (starts.Length == 0 || textLength <= 0)
        {
            _selectedCharacterIndex = -1;
            _characterSelectionAnchor = -1;
            NotifyCharacterSelection();
            return;
        }

        var rangeStart = Math.Clamp(textOffset, 0, Text.Length);
        var rangeEnd = Math.Clamp(textOffset + textLength, rangeStart, Text.Length);
        for (var index = 0; index < starts.Length; index++)
        {
            var elementStart = starts[index];
            var elementEnd = index + 1 < starts.Length ? starts[index + 1] : Text.Length;
            if (elementEnd > rangeStart && elementStart < rangeEnd)
                _selectedCharacterIndices.Add(index);
        }

        _selectedCharacterIndex = _selectedCharacterIndices.Count == 0 ? -1 : _selectedCharacterIndices.Min;
        _characterSelectionAnchor = _selectedCharacterIndex;
        NotifyCharacterSelection();
    }

    /// <summary>
    /// この領域内の文字選択をすべて解除します。
    /// </summary>
    public void ClearCharacterSelection() => SelectedCharacterIndex = -1;

    /// <summary>
    /// 選択された文字の送り量を増減し、後続文字の位置を再配置します。
    /// </summary>
    public void AdjustSelectedCharacterAdvances(double delta)
    {
        ReconcileCharacterLocks();
        var selected = _selectedCharacterIndices
            .Where(index => index < _characterAdvances.Count && !_characterLocks[index])
            .ToArray();
        if (selected.Length == 0 || Math.Abs(delta) < 0.000001) return;
        foreach (var index in selected)
            _characterAdvances[index] = Math.Max(1, Math.Round(_characterAdvances[index] + delta, 3));
        UpdateExtentFromCharacterAdvances();
    }

    /// <summary>
    /// 領域内の全文字送りへ同じ差分を加えます。
    /// </summary>
    public void AdjustAllCharacterAdvances(double delta)
    {
        if (_characterAdvances.Count == 0 || Math.Abs(delta) < 0.000001) return;
        ReconcileCharacterLocks();
        for (var index = 0; index < _characterAdvances.Count; index++)
        {
            if (_characterLocks[index]) continue;
            _characterAdvances[index] = Math.Max(1, Math.Round(_characterAdvances[index] + delta, 3));
        }
        UpdateExtentFromCharacterAdvances();
    }

    /// <summary>選択中の文字について、位置と送り量の固定状態を設定します。</summary>
    /// <param name="isLocked"><c>true</c> の場合は自動調整の対象から除外します。</param>
    public void SetSelectedCharacterLocks(bool isLocked)
    {
        ReconcileCharacterLocks();
        var changed = false;
        foreach (var index in _selectedCharacterIndices.Where(index => index >= 0 && index < _characterLocks.Count))
        {
            if (_characterLocks[index] == isLocked) continue;
            _characterLocks[index] = isLocked;
            changed = true;
        }
        if (!changed) return;
        OnPropertyChanged(nameof(IsModified));
        NotifyCharacterSelection();
    }

    /// <summary>
    /// 領域の書字方向の長さを文字数で等分し、等幅配置へ戻します。
    /// </summary>
    public void EqualizeCharacterAdvances()
    {
        var count = TextElementCount;
        if (count <= 1) return;
        var extent = Math.Max(4, IsVertical ? Height : Width);
        var equalAdvance = extent / count;
        ReconcileCharacterLocks();
        if (HasLockedCharacters)
        {
            ApplyEstimatedAdvancesRespectingLocks(Enumerable.Repeat(equalAdvance, count).ToArray());
            return;
        }
        _characterAdvances.Clear();
        for (var index = 0; index < count; index++) _characterAdvances.Add(equalAdvance);
        OnPropertyChanged(nameof(IsModified));
        NotifyCharacterSelection();
    }

    /// <summary>
    /// 取込時に保持していた文字送りへ復元します。
    /// </summary>
    public void RestoreOriginalCharacterAdvances()
    {
        var originalAdvances = ParseCharacterAdvances(Original.CharacterAdvancesText);
        if (originalAdvances.Count != TextElementCount || originalAdvances.Count == 0) return;
        ReconcileCharacterLocks();
        if (HasLockedCharacters)
        {
            ApplyEstimatedAdvancesRespectingLocks(originalAdvances);
            return;
        }
        _characterAdvances.Clear();
        _characterAdvances.AddRange(originalAdvances);
        var extent = Math.Max(4, originalAdvances.Sum());
        if (IsVertical)
        {
            _height = extent;
            OnPropertyChanged(nameof(Height));
        }
        else
        {
            _width = extent;
            OnPropertyChanged(nameof(Width));
        }
        OnPropertyChanged(nameof(IsModified));
        NotifyCharacterSelection();
    }

    /// <summary>
    /// 全文字の送り量を設定し、領域長に合うよう正規化します。
    /// </summary>
    /// <param name="advances">Unicodeテキスト要素と同じ件数の送り量。</param>
    public void SetCharacterAdvances(IReadOnlyList<double> advances)
    {
        if (advances.Count != TextElementCount || advances.Count == 0 ||
            advances.Any(value => !double.IsFinite(value) || value <= 0))
            throw new ArgumentException("文字数と文字幅の推定結果が一致しません。", nameof(advances));
        var targetExtent = Math.Max(4, IsVertical ? Height : Width);
        var scale = targetExtent / advances.Sum();
        _characterAdvances.Clear();
        _characterAdvances.AddRange(advances.Select(value => Math.Max(1, value * scale)));
        var correction = targetExtent / _characterAdvances.Sum();
        for (var index = 0; index < _characterAdvances.Count; index++) _characterAdvances[index] *= correction;
        OnPropertyChanged(nameof(IsModified));
        NotifyCharacterSelection();
    }

    /// <summary>
    /// 文字列中の指定範囲だけを、指定した送り幅の合計へ比例補正します。
    /// </summary>
    /// <param name="startTextOffset">UTF-16 文字列上の開始位置。</param>
    /// <param name="textLength">UTF-16 文字列上の対象長。</param>
    /// <param name="targetExtent">補正後の対象範囲の送り幅合計。</param>
    /// <returns>送り幅を変更できた場合は <c>true</c>。</returns>
    /// <remarks>
    /// 行または文字の固定が1つでもある領域は変更しません。品質分析からの一括補正が、
    /// 手作業で確定した文字位置を崩さないようにするためです。
    /// </remarks>
    public bool TrySetCharacterRangeExtent(int startTextOffset, int textLength, double targetExtent)
    {
        if (IsGeometryLocked || HasLockedCharacters || startTextOffset < 0 || textLength <= 0 ||
            !double.IsFinite(targetExtent) || targetExtent <= 0)
            return false;

        var offsets = StringInfo.ParseCombiningCharacters(Text);
        if (offsets.Length == 0 || startTextOffset + textLength > Text.Length) return false;
        var firstElement = Array.IndexOf(offsets, startTextOffset);
        if (firstElement < 0) return false;
        var rangeEnd = startTextOffset + textLength;
        var lastExclusive = firstElement;
        while (lastExclusive < offsets.Length && offsets[lastExclusive] < rangeEnd) lastExclusive++;
        if (lastExclusive <= firstElement) return false;

        ReconcileCharacterAdvances();
        var currentExtent = _characterAdvances
            .Skip(firstElement)
            .Take(lastExclusive - firstElement)
            .Sum();
        if (!double.IsFinite(currentExtent) || currentExtent <= 0 ||
            Math.Abs(currentExtent - targetExtent) < 0.001)
            return false;

        var scale = targetExtent / currentExtent;
        for (var index = firstElement; index < lastExclusive; index++)
            _characterAdvances[index] = Math.Max(1, _characterAdvances[index] * scale);
        UpdateExtentFromCharacterAdvances();
        OnPropertyChanged(nameof(IsModified));
        NotifyCharacterSelection();
        return true;
    }

    /// <summary>
    /// 画像から推定した先頭余白と文字送りを領域へ反映します。
    /// </summary>
    /// <param name="estimation">文字送り推定サービスの結果。</param>
    public void ApplyCharacterAdvanceEstimation(CharacterAdvanceEstimationResult estimation)
    {
        if (estimation.Advances.Count != TextElementCount || estimation.Extent < 4 ||
            estimation.Advances.Any(value => !double.IsFinite(value) || value <= 0))
            throw new ArgumentException("文字幅の推定結果が不正です。", nameof(estimation));

        if (IsGeometryLocked) return;
        ReconcileCharacterLocks();
        if (HasLockedCharacters)
        {
            ApplyEstimatedAdvancesRespectingLocks(estimation.Advances);
            return;
        }

        var oldWidth = Width;
        var oldHeight = Height;
        var oldCenterX = Left + oldWidth / 2d;
        var oldCenterY = Top + oldHeight / 2d;
        var oldExtent = IsVertical ? oldHeight : oldWidth;
        var localShift = estimation.LeadingOffset + estimation.Extent / 2d - oldExtent / 2d;
        var radians = RotationDegrees * Math.PI / 180d;
        var centerShiftX = IsVertical ? -Math.Sin(radians) * localShift : Math.Cos(radians) * localShift;
        var centerShiftY = IsVertical ? Math.Cos(radians) * localShift : Math.Sin(radians) * localShift;
        var newWidth = IsVertical ? oldWidth : estimation.Extent;
        var newHeight = IsVertical ? estimation.Extent : oldHeight;

        _left = Math.Max(0, oldCenterX + centerShiftX - newWidth / 2d);
        _top = Math.Max(0, oldCenterY + centerShiftY - newHeight / 2d);
        _width = newWidth;
        _height = newHeight;
        _characterAdvances.Clear();
        _characterAdvances.AddRange(estimation.Advances);
        OnPropertyChanged(nameof(Left));
        OnPropertyChanged(nameof(Top));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(IsModified));
        NotifyCharacterSelection();
    }

    /// <summary>
    /// 指定文字から行末までを再推定するための一時的な部分領域を生成します。
    /// </summary>
    /// <param name="startIndex">再推定を開始するUnicodeテキスト要素のインデックス。</param>
    /// <returns>元領域と同じ回転・書字方向を持つ部分領域。</returns>
    public OverlayRegionViewModel CreateCharacterSuffixEstimationRegion(int startIndex)
    {
        var indexes = StringInfo.ParseCombiningCharacters(Text);
        if (startIndex < 0 || startIndex >= indexes.Length - 1)
            throw new ArgumentOutOfRangeException(nameof(startIndex), "自動調整する範囲には2文字以上必要です。");

        ReconcileCharacterAdvances();
        var suffixText = Text[indexes[startIndex]..];
        var prefixExtent = _characterAdvances.Take(startIndex).Sum();
        var fullExtent = IsVertical ? Height : Width;
        var suffixExtent = Math.Max(4, fullExtent - prefixExtent);
        var localCenterShift = prefixExtent + suffixExtent / 2d - fullExtent / 2d;
        var radians = RotationDegrees * Math.PI / 180d;
        var centerX = Left + Width / 2d;
        var centerY = Top + Height / 2d;
        var suffixCenterX = centerX + (IsVertical ? -Math.Sin(radians) : Math.Cos(radians)) * localCenterShift;
        var suffixCenterY = centerY + (IsVertical ? Math.Cos(radians) : Math.Sin(radians)) * localCenterShift;
        var suffixWidth = IsVertical ? Width : suffixExtent;
        var suffixHeight = IsVertical ? suffixExtent : Height;

        return new OverlayRegionViewModel(new PdfTextOverlayRegion(
            suffixText,
            suffixCenterX - suffixWidth / 2d,
            suffixCenterY - suffixHeight / 2d,
            suffixWidth,
            suffixHeight,
            IsInvisible,
            IsVertical,
            ProviderId,
            Confidence,
            RotationDegrees));
    }

    /// <summary>
    /// 指定文字より前の配置を維持し、行末側だけに推定結果を適用します。
    /// </summary>
    /// <param name="startIndex">置換を開始するUnicodeテキスト要素のインデックス。</param>
    /// <param name="estimation">部分領域に対する推定結果。</param>
    public bool ApplyCharacterSuffixAdvanceEstimation(int startIndex, CharacterAdvanceEstimationResult estimation)
    {
        ReconcileCharacterAdvances();
        if (startIndex < 0 || startIndex >= _characterAdvances.Count - 1 ||
            estimation.Advances.Count != _characterAdvances.Count - startIndex ||
            estimation.Extent < 2 ||
            estimation.Advances.Any(value => !double.IsFinite(value) || value <= 0))
            throw new ArgumentException("選択文字以降の文字幅推定結果が不正です。", nameof(estimation));

        if (IsGeometryLocked) return false;
        ReconcileCharacterLocks();
        if (HasLockedCharacters)
        {
            var candidates = _characterAdvances.ToArray();
            for (var index = startIndex; index < candidates.Length; index++)
                candidates[index] = estimation.Advances[index - startIndex];
            // The estimator measures the ink after the selected cell boundary.
            // Preserve that boundary by including the detected leading side bearing
            // in the first adjustable character, just as in the no-lock path below.
            candidates[startIndex] += Math.Max(0, estimation.LeadingOffset);
            return ApplyEstimatedAdvancesRespectingLocks(
                candidates,
                startIndex,
                allowTrailingExtentChange: true);
        }

        var previousAdvances = _characterAdvances.ToArray();
        var prefixAdvances = _characterAdvances.Take(startIndex).ToArray();
        var suffixAdvances = estimation.Advances.ToArray();
        // Keep the selected character's leading boundary fixed. Any leading side
        // bearing found by the estimator belongs to that character's advance.
        suffixAdvances[0] += Math.Max(0, estimation.LeadingOffset);
        var oldWidth = Width;
        var oldHeight = Height;
        var oldExtent = IsVertical ? oldHeight : oldWidth;
        var newExtent = Math.Max(4, prefixAdvances.Sum() + suffixAdvances.Sum());
        var centerShift = (newExtent - oldExtent) / 2d;
        var radians = RotationDegrees * Math.PI / 180d;
        var oldCenterX = Left + oldWidth / 2d;
        var oldCenterY = Top + oldHeight / 2d;
        var newCenterX = oldCenterX + (IsVertical ? -Math.Sin(radians) : Math.Cos(radians)) * centerShift;
        var newCenterY = oldCenterY + (IsVertical ? Math.Cos(radians) : Math.Sin(radians)) * centerShift;
        var newWidth = IsVertical ? oldWidth : newExtent;
        var newHeight = IsVertical ? newExtent : oldHeight;

        _left = Math.Max(0, newCenterX - newWidth / 2d);
        _top = Math.Max(0, newCenterY - newHeight / 2d);
        _width = newWidth;
        _height = newHeight;
        _characterAdvances.Clear();
        _characterAdvances.AddRange(prefixAdvances);
        _characterAdvances.AddRange(suffixAdvances);
        OnPropertyChanged(nameof(Left));
        OnPropertyChanged(nameof(Top));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(IsModified));
        NotifyCharacterSelection();
        return Math.Abs(oldWidth - newWidth) > 0.001 ||
               Math.Abs(oldHeight - newHeight) > 0.001 ||
               !previousAdvances.SequenceEqual(_characterAdvances);
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(IsModified));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static double NormalizeDegrees(double value)
    {
        if (!double.IsFinite(value)) return 0;
        var normalized = value % 360;
        if (normalized > 180) normalized -= 360;
        if (normalized <= -180) normalized += 360;
        return Math.Round(normalized, 2);
    }

    private static string ValidateWordReadings(string value)
    {
        var lineNumber = 0;
        foreach (var rawLine in value.Replace("\r\n", "\n").Split('\n'))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1)
                return $"{lineNumber}行目は「表記=よみ」で入力してください。";
        }
        return string.Empty;
    }

    private void ClampCharacterSelection()
    {
        var count = TextElementCount;
        _selectedCharacterIndices.RemoveWhere(index => index < 0 || index >= count);
        if (count == 0 || _selectedCharacterIndices.Count == 0)
        {
            _selectedCharacterIndex = -1;
            _characterSelectionAnchor = -1;
            _selectedCharacterIndices.Clear();
        }
        else if (!_selectedCharacterIndices.Contains(_selectedCharacterIndex))
        {
            _selectedCharacterIndex = _selectedCharacterIndices.Max;
            _characterSelectionAnchor = _selectedCharacterIndex;
        }
    }

    /// <summary>
    /// 固定文字の左右境界を維持し、固定文字間にある未固定文字だけを推定値の比率で再配分します。
    /// </summary>
    private bool ApplyEstimatedAdvancesRespectingLocks(
        IReadOnlyList<double> estimated,
        int firstAdjustableIndex = 0,
        bool allowTrailingExtentChange = false)
    {
        ReconcileCharacterAdvances();
        ReconcileCharacterLocks();
        if (estimated.Count != _characterAdvances.Count) return false;

        var original = _characterAdvances.ToArray();
        var result = original.ToArray();
        var index = Math.Clamp(firstAdjustableIndex, 0, result.Length);
        while (index < result.Length)
        {
            if (_characterLocks[index])
            {
                index++;
                continue;
            }

            var start = index;
            while (index < result.Length && !_characterLocks[index]) index++;
            var end = index;
            var targetExtent = _characterAdvances.Skip(start).Take(end - start).Sum();
            var sourceExtent = estimated.Skip(start).Take(end - start).Sum();
            if (!double.IsFinite(sourceExtent) || sourceExtent <= 0) continue;

            // A fixed character after this run is an anchor, so keep the run's
            // original total width/height. The final unlocked run has no following
            // anchor; suffix adjustment may therefore use its natural estimated
            // extent and move only the line's trailing edge.
            if (allowTrailingExtentChange && end == result.Length)
            {
                for (var current = start; current < end; current++)
                    result[current] = Math.Max(1, estimated[current]);
                continue;
            }

            var scale = targetExtent / sourceExtent;
            for (var current = start; current < end; current++)
                result[current] = Math.Max(1, estimated[current] * scale);
            result[end - 1] += targetExtent - result.Skip(start).Take(end - start).Sum();
        }

        // This is deliberately defensive. The run calculation above already skips
        // locked items, but restoring their exact values here prevents future
        // estimation changes and floating-point remainder correction from moving a
        // fixed boundary.
        for (var lockedIndex = 0; lockedIndex < result.Length; lockedIndex++)
            if (_characterLocks[lockedIndex])
                result[lockedIndex] = original[lockedIndex];

        var changed = result.Where((value, itemIndex) =>
                Math.Abs(value - _characterAdvances[itemIndex]) > 0.001)
            .Any();
        if (!changed) return false;

        var oldExtent = IsVertical ? Height : Width;
        _characterAdvances.Clear();
        _characterAdvances.AddRange(result);
        if (allowTrailingExtentChange)
            UpdateExtentKeepingLeadingBoundary(oldExtent, Math.Max(4, result.Sum()));
        OnPropertyChanged(nameof(IsModified));
        NotifyCharacterSelection();
        return true;
    }

    /// <summary>
    /// 文字列の先頭境界を維持したまま、送り方向の全長だけを変更します。
    /// </summary>
    private void UpdateExtentKeepingLeadingBoundary(double oldExtent, double newExtent)
    {
        if (Math.Abs(oldExtent - newExtent) <= 0.001) return;

        var oldWidth = Width;
        var oldHeight = Height;
        var oldCenterX = Left + oldWidth / 2d;
        var oldCenterY = Top + oldHeight / 2d;
        var centerShift = (newExtent - oldExtent) / 2d;
        var radians = RotationDegrees * Math.PI / 180d;
        var newCenterX = oldCenterX + (IsVertical ? -Math.Sin(radians) : Math.Cos(radians)) * centerShift;
        var newCenterY = oldCenterY + (IsVertical ? Math.Cos(radians) : Math.Sin(radians)) * centerShift;
        var newWidth = IsVertical ? oldWidth : newExtent;
        var newHeight = IsVertical ? newExtent : oldHeight;

        _left = Math.Max(0, newCenterX - newWidth / 2d);
        _top = Math.Max(0, newCenterY - newHeight / 2d);
        _width = newWidth;
        _height = newHeight;
        OnPropertyChanged(nameof(Left));
        OnPropertyChanged(nameof(Top));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
    }

    private void ReconcileCharacterLocks()
    {
        var count = TextElementCount;
        if (_characterLocks.Count > count)
            _characterLocks.RemoveRange(count, _characterLocks.Count - count);
        while (_characterLocks.Count < count) _characterLocks.Add(false);
    }

    /// <summary>
    /// 文字列編集前後の共通部分を対応付け、変更位置より前の送り幅と既存の文字ロックを保持します。
    /// </summary>
    /// <param name="previousText">編集前の文字列。</param>
    /// <param name="previousAdvances">編集前のUnicodeテキスト要素ごとの送り幅。</param>
    /// <param name="previousLocks">編集前のUnicodeテキスト要素ごとのロック状態。</param>
    private void ReconcileCharacterStateAfterTextEdit(
        string previousText,
        IReadOnlyList<double> previousAdvances,
        IReadOnlyList<bool> previousLocks)
    {
        var oldExtent = Math.Max(4, IsVertical ? Height : Width);
        var previousElements = GetTextElements(previousText);
        var currentElements = GetTextElements(Text);
        if (currentElements.Count == 0)
        {
            _characterAdvances.Clear();
            _characterLocks.Clear();
            UpdateExtentKeepingLeadingBoundary(oldExtent, 4);
            return;
        }

        var commonPrefix = 0;
        while (commonPrefix < previousElements.Count && commonPrefix < currentElements.Count &&
               string.Equals(previousElements[commonPrefix], currentElements[commonPrefix], StringComparison.Ordinal))
            commonPrefix++;

        var commonSuffix = 0;
        while (commonSuffix < previousElements.Count - commonPrefix &&
               commonSuffix < currentElements.Count - commonPrefix &&
               string.Equals(
                   previousElements[previousElements.Count - 1 - commonSuffix],
                   currentElements[currentElements.Count - 1 - commonSuffix],
                   StringComparison.Ordinal))
            commonSuffix++;

        var advances = Enumerable.Repeat(double.NaN, currentElements.Count).ToArray();
        var locks = new bool[currentElements.Count];

        // Everything before the first changed text element keeps its exact advance,
        // even when it was not explicitly locked.  This is the stable editing anchor.
        for (var index = 0; index < commonPrefix; index++)
        {
            advances[index] = previousAdvances[index];
            locks[index] = previousLocks[index];
        }

        // An unchanged suffix also keeps its exact advance, regardless of its lock
        // state.  The suffix may move towards the prefix when text between them is
        // deleted, but neither the prefix nor the suffix is resized.
        for (var offset = 0; offset < commonSuffix; offset++)
        {
            var previousIndex = previousElements.Count - 1 - offset;
            var currentIndex = currentElements.Count - 1 - offset;
            locks[currentIndex] = previousLocks[previousIndex];
            advances[currentIndex] = previousAdvances[previousIndex];
        }

        var previousMiddleCount = previousElements.Count - commonPrefix - commonSuffix;
        var currentMiddleCount = currentElements.Count - commonPrefix - commonSuffix;
        // A one-for-one correction (for example "1" -> "7") represents the same
        // edited cells.  Preserve their advances and locks, not just explicit locks.
        if (previousMiddleCount == currentMiddleCount)
        {
            for (var offset = 0; offset < currentMiddleCount; offset++)
            {
                var previousIndex = commonPrefix + offset;
                var currentIndex = commonPrefix + offset;
                advances[currentIndex] = previousAdvances[previousIndex];
                locks[currentIndex] = previousLocks[previousIndex];
            }
        }
        else if (currentMiddleCount > 0)
        {
            // Replacement text shares the extent of the replaced cells.  A pure
            // insertion has no replaced extent, so use a neighbouring/line average
            // and grow the line instead of compressing unrelated characters.
            var replacedExtent = previousAdvances
                .Skip(commonPrefix)
                .Take(previousMiddleCount)
                .Sum();
            var fallbackAdvance = previousElements.Count > 0
                ? previousAdvances.Sum() / previousElements.Count
                : oldExtent / currentElements.Count;
            var insertedExtent = replacedExtent > 0.001
                ? replacedExtent
                : Math.Max(1, fallbackAdvance) * currentMiddleCount;
            var insertedAdvance = Math.Max(1, insertedExtent / currentMiddleCount);
            for (var offset = 0; offset < currentMiddleCount; offset++)
                advances[commonPrefix + offset] = insertedAdvance;
        }

        // Every slot is assigned by prefix, suffix, or changed-middle handling.
        // Be defensive about malformed legacy data without falling back to scaling
        // the complete line.
        var fallback = Math.Max(1, oldExtent / Math.Max(1, currentElements.Count));
        for (var index = 0; index < advances.Length; index++)
            if (!double.IsFinite(advances[index])) advances[index] = fallback;

        _characterAdvances.Clear();
        _characterAdvances.AddRange(advances);
        _characterLocks.Clear();
        _characterLocks.AddRange(locks);
        UpdateExtentKeepingLeadingBoundary(oldExtent, Math.Max(4, advances.Sum()));
    }

    /// <summary>書字方向の領域長だけを変更し、保持済み文字送りを再スケールしません。</summary>
    private void SetWritingExtentDirect(double extent)
    {
        if (IsVertical)
        {
            _height = Math.Max(4, extent);
            OnPropertyChanged(nameof(Height));
        }
        else
        {
            _width = Math.Max(4, extent);
            OnPropertyChanged(nameof(Width));
        }
    }

    /// <summary>結合文字とサロゲートペアを分割せず、文字列を表示文字単位へ分解します。</summary>
    private static IReadOnlyList<string> GetTextElements(string text)
    {
        var indexes = StringInfo.ParseCombiningCharacters(text);
        var result = new string[indexes.Length];
        for (var index = 0; index < indexes.Length; index++)
        {
            var end = index + 1 < indexes.Length ? indexes[index + 1] : text.Length;
            result[index] = text[indexes[index]..end];
        }
        return result;
    }

    private void ReconcileCharacterAdvances()
    {
        var count = TextElementCount;
        if (count == 0)
        {
            if (_characterAdvances.Count > 0) _characterAdvances.Clear();
            if (_characterLocks.Count > 0) _characterLocks.Clear();
            return;
        }

        ReconcileCharacterLocks();
        var hasValidAdvances = _characterAdvances.Count == count &&
                               _characterAdvances.All(value => double.IsFinite(value) && value > 0);
        if (hasValidAdvances && _characterLocks.Any(isLocked => isLocked))
        {
            // CharacterCells calls this method while merely rendering. Once any
            // character is locked, rendering and later automatic adjustment must
            // not normalize the stored widths. Keep the exact boundaries and make
            // the line extent follow them instead.
            var lockedExtent = Math.Max(4, _characterAdvances.Sum());
            if (Math.Abs((IsVertical ? Height : Width) - lockedExtent) > 0.001)
                SetWritingExtentDirect(lockedExtent);
            return;
        }

        var extent = Math.Max(4, IsVertical ? Height : Width);
        var normalized = NormalizeCharacterAdvances(_characterAdvances, count, extent);
        if (_characterAdvances.Count == normalized.Count &&
            _characterAdvances.Zip(normalized).All(pair => Math.Abs(pair.First - pair.Second) < 0.000001))
        {
            ReconcileCharacterLocks();
            return;
        }

        _characterAdvances.Clear();
        _characterAdvances.AddRange(normalized);
        ReconcileCharacterLocks();
    }

    private void ScaleCharacterAdvances(double previousExtent, double newExtent)
    {
        var count = TextElementCount;
        if (count == 0)
        {
            _characterAdvances.Clear();
            return;
        }

        ReconcileCharacterLocks();
        var hasValidAdvances = _characterAdvances.Count == count &&
                               _characterAdvances.All(value => double.IsFinite(value) && value > 0);
        if (hasValidAdvances && _characterLocks.Any(isLocked => isLocked))
        {
            // A locked character fixes both its own advance and its leading
            // boundary. Therefore only the unlocked suffix after the final lock can
            // absorb a requested line-size change without moving a locked item.
            var lastLockedIndex = _characterLocks.FindLastIndex(isLocked => isLocked);
            var suffixStart = lastLockedIndex + 1;
            var fixedExtent = _characterAdvances.Take(suffixStart).Sum();
            var suffixCount = count - suffixStart;

            if (suffixCount > 0)
            {
                var requestedSuffixExtent = Math.Max(suffixCount, newExtent - fixedExtent);
                var normalizedSuffix = NormalizeCharacterAdvances(
                    _characterAdvances.Skip(suffixStart).ToArray(),
                    suffixCount,
                    requestedSuffixExtent);
                for (var index = 0; index < suffixCount; index++)
                    _characterAdvances[suffixStart + index] = normalizedSuffix[index];
            }

            SetWritingExtentDirect(Math.Max(4, _characterAdvances.Sum()));
            return;
        }

        // Width/Height has already been changed by the caller. Scale from the
        // actual sum instead of the previous line extent so imported glyph
        // bounds (which omit side bearings and spacing) cannot stay shorter
        // than the line box.
        var normalized = NormalizeCharacterAdvances(_characterAdvances, count, Math.Max(4, newExtent));
        _characterAdvances.Clear();
        _characterAdvances.AddRange(normalized);
    }

    private void ApplyCharacterAdvances(string serialized)
    {
        _characterAdvances.Clear();
        _characterAdvances.AddRange(ParseCharacterAdvances(serialized));
        ReconcileCharacterAdvances();
        NotifyCharacterSelection();
    }

    private void ApplyCharacterLocks(string serialized)
    {
        _characterLocks.Clear();
        foreach (var part in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _characterLocks.Add(part is "1" or "true" or "True");
        ReconcileCharacterLocks();
        NotifyCharacterSelection();
    }

    private IReadOnlyList<CharacterOverlayCell> SelectedCells() =>
        CharacterCells.Where(cell => cell.IsSelected).ToArray();

    private void UpdateExtentFromCharacterAdvances()
    {
        var extent = Math.Max(4, _characterAdvances.Sum());
        if (IsVertical) _height = extent;
        else _width = extent;
        OnPropertyChanged(IsVertical ? nameof(Height) : nameof(Width));
        OnPropertyChanged(nameof(IsModified));
        NotifyCharacterSelection();
    }

    /// <summary>
    /// 文字送りを直接変更しない前処理で領域の位置・寸法を更新したことを、
    /// 画面表示、変更判定、および文字セル表示へまとめて通知します。
    /// </summary>
    private void NotifyGeometryChanged()
    {
        OnPropertyChanged(nameof(Left));
        OnPropertyChanged(nameof(Top));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(LineThickness));
        OnPropertyChanged(nameof(WritingExtent));
        OnPropertyChanged(nameof(IsModified));
        NotifyCharacterSelection();
    }

    private static IReadOnlyList<double> ParseCharacterAdvances(string serialized)
    {
        var result = new List<double>();
        foreach (var part in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value > 0)
                result.Add(value);
        return result;
    }

    private string SerializeCharacterAdvances() => SerializeCharacterAdvances(_characterAdvances);

    private static string SerializeCharacterAdvances(IEnumerable<double> values) =>
        string.Join(';', values.Select(value => value.ToString("0.######", CultureInfo.InvariantCulture)));

    private string SerializeCharacterLocks()
    {
        ReconcileCharacterLocks();
        // An empty value is the canonical representation of an entirely
        // unlocked line. Persisting "0;0;..." here would make a freshly
        // imported region differ from its original snapshot even though the
        // effective lock state had not changed.
        return SerializeCharacterLocks(_characterLocks);
    }

    private static string SerializeCharacterLocks(IEnumerable<bool> values)
    {
        var locks = values.ToArray();
        if (locks.All(value => !value)) return string.Empty;
        return string.Join(';', locks.Select(value => value ? "1" : "0"));
    }

    private static string CreateCharacterAdvancesText(string text, double extent, IReadOnlyList<double>? source)
    {
        var count = StringInfo.ParseCombiningCharacters(text).Length;
        var advances = NormalizeCharacterAdvances(source ?? [], count, Math.Max(4, extent));
        return string.Join(';', advances.Select(value => value.ToString("0.######", CultureInfo.InvariantCulture)));
    }

    private static IReadOnlyList<double> NormalizeCharacterAdvances(
        IReadOnlyList<double> source,
        int count,
        double extent)
    {
        if (count <= 0) return [];
        var target = Math.Max(4, extent);
        if (source.Count != count || source.Any(value => !double.IsFinite(value) || value <= 0))
            return Enumerable.Repeat(target / count, count).ToArray();

        var sum = source.Sum();
        if (!double.IsFinite(sum) || sum <= 0)
            return Enumerable.Repeat(target / count, count).ToArray();

        var scale = target / sum;
        var result = source.Select(value => Math.Max(0.000001, value * scale)).ToArray();
        // Remove floating-point drift at the trailing edge. This guarantees
        // that the final character cell and the line box share the same edge.
        result[^1] += target - result.Sum();
        return result;
    }

    private void NotifyCharacterSelection()
    {
        OnPropertyChanged(nameof(SelectedCharacterIndex));
        OnPropertyChanged(nameof(SelectedCharacterIndices));
        OnPropertyChanged(nameof(SelectedCharacterCount));
        OnPropertyChanged(nameof(HasCharacterSelection));
        OnPropertyChanged(nameof(HasSingleCharacterSelection));
        OnPropertyChanged(nameof(HasMultipleCharacterSelection));
        OnPropertyChanged(nameof(HasHorizontalCharacterSelection));
        OnPropertyChanged(nameof(HasVerticalCharacterSelection));
        OnPropertyChanged(nameof(HasLockedCharacters));
        OnPropertyChanged(nameof(HasUnlockedCharacters));
        OnPropertyChanged(nameof(AreSelectedCharactersLocked));
        OnPropertyChanged(nameof(HasLockedSelectedCharacters));
        OnPropertyChanged(nameof(HasUnlockedSelectedCharacters));
        OnPropertyChanged(nameof(CanAutomaticallyAdjust));
        OnPropertyChanged(nameof(CanEqualizeCharacterAdvances));
        OnPropertyChanged(nameof(CanRestoreOriginalCharacterAdvances));
        OnPropertyChanged(nameof(TextElementCount));
        OnPropertyChanged(nameof(CharacterSelectionLeft));
        OnPropertyChanged(nameof(CharacterSelectionTop));
        OnPropertyChanged(nameof(CharacterSelectionWidth));
        OnPropertyChanged(nameof(CharacterSelectionHeight));
        OnPropertyChanged(nameof(CharacterSelectionRight));
        OnPropertyChanged(nameof(CharacterSelectionBottom));
        OnPropertyChanged(nameof(SelectedCharacterAdvance));
        OnPropertyChanged(nameof(CharacterCells));
    }
}
