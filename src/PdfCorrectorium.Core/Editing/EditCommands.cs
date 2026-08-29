using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.Core.Editing;

/// <summary>
/// プロジェクトモデルへ適用でき、同じ単位で取り消せる編集操作を定義します。
/// </summary>
public interface IEditCommand
{
    /// <summary>履歴画面へ表示する操作の説明です。</summary>
    string Description { get; }
    /// <summary>編集操作を適用した新しいプロジェクト状態を返します。</summary>
    PdfCorrectoriumProject Execute(PdfCorrectoriumProject project);
    /// <summary>編集前のプロジェクト状態を復元します。</summary>
    PdfCorrectoriumProject Undo(PdfCorrectoriumProject project);
}

/// <summary>
/// 不変な<see cref="PdfCorrectoriumProject"/>に対するUndo／Redoスタックを管理します。
/// </summary>
/// <param name="capacity">保持するUndo操作の最大件数。</param>
public sealed class EditHistory(int capacity = 100)
{
    private readonly Stack<IEditCommand> _undo = new();
    private readonly Stack<IEditCommand> _redo = new();
    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>操作を実行してUndoスタックへ追加し、Redoスタックを破棄します。</summary>
    public PdfCorrectoriumProject Execute(PdfCorrectoriumProject project, IEditCommand command)
    {
        var result = command.Execute(project);
        _undo.Push(command);
        _redo.Clear();
        Trim();
        return result;
    }

    /// <summary>直前の操作を取り消します。履歴が空の場合は入力状態をそのまま返します。</summary>
    public PdfCorrectoriumProject Undo(PdfCorrectoriumProject project)
    {
        if (!_undo.TryPop(out var command)) return project;
        var result = command.Undo(project);
        _redo.Push(command);
        return result;
    }

    /// <summary>直前に取り消した操作を再実行します。履歴が空の場合は入力状態をそのまま返します。</summary>
    public PdfCorrectoriumProject Redo(PdfCorrectoriumProject project)
    {
        if (!_redo.TryPop(out var command)) return project;
        var result = command.Execute(project);
        _undo.Push(command);
        return result;
    }

    private void Trim()
    {
        if (_undo.Count <= Capacity) return;
        var retained = _undo.Take(Capacity).Reverse().ToArray();
        _undo.Clear();
        foreach (var item in retained) _undo.Push(item);
    }
}
