using System.Windows.Input;

namespace PdfCorrectorium.App.ViewModels;

/// <summary>
/// 非同期処理の実行中に再入を抑止するWPFコマンドです。
/// </summary>
public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    /// <summary>非同期処理の完了前に同じコマンドが再実行されることを防ぎます。</summary>
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    // ICommand.Executeはvoid契約のためasync voidになる。例外は呼び出し元のUI同期コンテキストへ返し、
    // finallyで再実行抑止状態を必ず解除する。
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(); }
        finally
        {
            _running = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// コマンドの実行可否をWPFへ再評価させます。
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// 同期処理をWPFの<see cref="ICommand"/>として公開する軽量コマンドです。
/// </summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) execute();
    }
    /// <summary>
    /// コマンドの実行可否をWPFへ再評価させます。
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
