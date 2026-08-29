using System.ComponentModel;
using System.Runtime.CompilerServices;
using PdfCorrectorium.App.Services;

namespace PdfCorrectorium.App.ViewModels;

/// <summary>定型ヘッダー／フッターへ反映する編集の種類です。</summary>
public enum RepeatedRegionPropagationMode
{
    /// <summary>参照ページの分割数、位置、寸法、文字送りを再現します。</summary>
    ReplaceStructure,
    /// <summary>一致した領域を削除予定にします。</summary>
    DeleteMatches,
}

/// <summary>他ページへ編集を反映するときの検索条件です。</summary>
public sealed record RepeatedRegionPropagationOptions(
    IReadOnlyList<int> TargetPageNumbers,
    double MinimumSimilarity,
    bool PreserveTargetText,
    RepeatedRegionPropagationMode Mode);

/// <summary>定型領域をページ横断で検索している間の進捗情報です。</summary>
public sealed record RepeatedRegionSearchProgress(
    int CurrentPageNumber,
    int CompletedPages,
    int TotalPages,
    int CandidateCount)
{
    /// <summary>0～100で表した完了率です。</summary>
    public double Percentage => TotalPages <= 0 ? 100d : CompletedPages * 100d / TotalPages;
}

/// <summary>候補プレビューで表示する領域の状態です。</summary>
public enum RepeatedRegionPreviewKind
{
    /// <summary>反映前から対象ページに存在する領域です。</summary>
    Existing,
    /// <summary>反映によって新しく配置される領域です。</summary>
    Replacement,
    /// <summary>反映によって削除される領域です。</summary>
    Deleted,
    /// <summary>固定されているため変更されない領域です。</summary>
    Locked,
}

/// <summary>定型領域の反映前後を画像上へ重ねるための読み取り専用データです。</summary>
public sealed record RepeatedRegionPreviewRegion(
    double Left,
    double Top,
    double Width,
    double Height,
    double RotationDegrees,
    string Text,
    bool IsVertical,
    IReadOnlyList<double> CharacterAdvances,
    RepeatedRegionPreviewKind Kind);

/// <summary>他ページで見つかった、反映前に確認できる候補です。</summary>
public sealed class RepeatedRegionCandidate : INotifyPropertyChanged
{
    private bool _isSelected = true;

    internal IReadOnlyList<OverlayRegionViewModel> MatchedRegions { get; init; } = [];

    /// <summary>1から始まる対象ページ番号です。</summary>
    public required int PageNumber { get; init; }

    /// <summary>位置と文字列から計算した0～100%の一致度です。</summary>
    public required double Similarity { get; init; }

    /// <summary>対象ページで現在保持されている文字列です。</summary>
    public required string TargetText { get; init; }

    /// <summary>位置・寸法または文字送りが固定され、変更対象外であるかを示します。</summary>
    public required bool IsLocked { get; init; }

    /// <summary>利用者が今回の反映対象として選択したかを示します。</summary>
    public bool IsSelected
    {
        get => _isSelected && !IsLocked;
        set
        {
            var normalized = value && !IsLocked;
            if (_isSelected == normalized) return;
            _isSelected = normalized;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    /// <summary>候補一覧へ表示するページ表記です。</summary>
    public string PageDisplay => LocalizationService.IsEnglish ? $"Page {PageNumber}" : $"{PageNumber} ページ";

    /// <summary>候補一覧へ表示する一致度です。</summary>
    public string SimilarityDisplay => $"{Similarity:0}%";

    /// <summary>候補領域を囲む矩形の、ページ画像上での位置と寸法です。</summary>
    public string PositionDisplay
    {
        get
        {
            if (MatchedRegions.Count == 0) return "-";
            var left = MatchedRegions.Min(region => region.Left);
            var top = MatchedRegions.Min(region => region.Top);
            var right = MatchedRegions.Max(region => region.Left + region.Width);
            var bottom = MatchedRegions.Max(region => region.Top + region.Height);
            return $"X {left:0.0}, Y {top:0.0}, {right - left:0.0} × {bottom - top:0.0}";
        }
    }

    /// <summary>固定状態を説明する短い表示です。</summary>
    public string StatusDisplay => LocalizationService.Translate(IsLocked ? "固定済み（対象外）" : "反映可能");

    /// <summary>選択状態が変更されたときに発生します。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;
}
