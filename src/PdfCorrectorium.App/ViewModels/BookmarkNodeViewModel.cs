using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App.ViewModels;

/// <summary>
/// 編集可能なしおり階層を画面へ公開し、変更を文書の未保存状態へ通知します。
/// </summary>
public sealed class BookmarkNodeViewModel : INotifyPropertyChanged
{
    /// <summary>しおり編集を文書の未保存状態へ反映する通知処理です。</summary>
    private readonly Action _changed;
    /// <summary>しおり一覧へ表示する現在のタイトルです。</summary>
    private string _title;
    /// <summary>しおり選択時に移動する1から始まるページ番号です。</summary>
    private int _pageNumber;
    /// <summary>子しおりを展開表示する現在の状態です。</summary>
    private bool _isExpanded;
    /// <summary>しおり名のインライン編集欄を一時的に表示するUI状態です。</summary>
    private bool _isEditing;

    /// <summary>
    /// モデルの子階層を再帰的にビュー・モデルへ変換します。
    /// </summary>
    /// <param name="bookmark">変換元のしおり。</param>
    /// <param name="changed">タイトル、ページ、展開状態の変更時に呼ぶ通知。</param>
    public BookmarkNodeViewModel(PdfBookmark bookmark, Action changed)
    {
        _changed = changed;
        Id = bookmark.Id;
        _title = bookmark.Title;
        _pageNumber = Math.Max(1, bookmark.PageNumber);
        _isExpanded = bookmark.IsExpanded;
        foreach (var child in bookmark.Children)
            Children.Add(new BookmarkNodeViewModel(child, changed));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid Id { get; }
    public ObservableCollection<BookmarkNodeViewModel> Children { get; } = [];

    public string Title
    {
        get => _title;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "無題のしおり" : value.Trim();
            if (_title == normalized) return;
            _title = normalized;
            OnPropertyChanged();
            _changed();
        }
    }

    public int PageNumber
    {
        get => _pageNumber;
        set
        {
            var normalized = Math.Max(1, value);
            if (_pageNumber == normalized) return;
            _pageNumber = normalized;
            OnPropertyChanged();
            _changed();
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>
    /// しおり名をインライン編集しているかを取得または設定します。
    /// </summary>
    /// <remarks>
    /// この値は画面だけの一時状態であり、プロジェクトの未保存変更には含めません。
    /// </remarks>
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value) return;
            _isEditing = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 現在の編集値と子階層を永続化用モデルへ変換します。
    /// </summary>
    public PdfBookmark ToModel() => new()
    {
        Id = Id,
        Title = Title,
        PageNumber = PageNumber,
        IsExpanded = IsExpanded,
        Children = Children.Select(child => child.ToModel()).ToArray(),
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
