# 校正モード診断の失敗：.NET 8での原因調査

> 以下はdev.247の調査記録です。画面のLoaded処理と専用.appstateの修正を、その後dev.248で本体へ反映しました。.NET 8と.NET 10の両方で元の69項目を維持したまま成功しています。最終配布の結果は[dev.249](releases/v1.0.0-dev.249.md)を参照してください。

調査日：2026-10-27。対象：現行dev.247のソースと.NET 8.0.33。現行アプリのコード・版数・利用者設定は変更せず、独立したコピーで調査した。

## 結論

今回の失敗の原因は、**診断側がウィンドウの表示初期化を行わず、まだ生成されていないドッキング内の描画部品を検索・検査していたこと**だった。

確認ステータスの値、選択肢、データバインディングは失敗時にも正常だった。同じ.NET 8構成で通常の画面初期化を通すと、確認ステータスの日本語・英語表示を含め、元の校正モード診断69項目がすべて成功した。期待値の緩和・検査の削除・例外の握りつぶしは行っていない。

実アプリで確認ステータスが欠ける現象、校正データが壊れる現象は今回の試験では再現しなかった。

## 失敗していた箇所

- `src/PdfCorrectorium.App/App.ReviewDiagnostics.cs:139`
- 実行入口：`--review-mode-test <新しい診断出力ディレクトリ>`
- 例外：`InvalidOperationException: Sequence contains no matching element`

対象の診断は、`window.Content` 以下の**ビジュアルツリー**をたどり、ItemsSourceがReviewStatusOptionsと同一のComboBoxをSingleで取得していた。

一方、診断のLayoutAsyncはルート要素にMeasure / Arrange / UpdateLayoutを実行するだけで、Window.ShowやLoaded処理を通していなかった。AvalonDockが管理する内容は論理ツリー上に存在していても、その時点ではビジュアルツリーに接続されていない。

製品側の関連処理：

- `MainWindow.AvalonDockPreview.cs:84` 前後で、PropertiesDockPaneなどを従来のGridから取り外し、AvalonDockのContentへ移している。
- 同ファイルの `manager.Loaded`（172行前後）で、ペインのテンプレート、タブ、復元配置、表示同期などを初期化する。
- `MainWindow.PropertyPanes.cs` で機能別の内容を分離している。
- PropertiesDockPaneのDataContextは明示的に保持されており、今回それが失われていたわけではない。

従来の「ルートだけレイアウトすればすべての部品を検索できる」という診断の前提が、現在のドッキング構造と合っていなかった。いつのリビジョンでこの不一致が入り込んだかは、今回特定していない。

## 観測した状態

| 項目 | 元の未表示条件 | 画面初期化後 |
| --- | --- | --- |
| 実ランタイム | 8.0.33 | 8.0.33 |
| Window.IsLoaded | false | true |
| ルート以下で見つかった対象ComboBox | 0個 | 1個 |
| 対象ComboBoxの描画サイズ | 0 × 0 | 132 × 30 DIP |
| 対象のDataContext | 正しいViewModelと同一 | 同一 |
| ItemsSource | 正しい6件の選択肢と同一 | 同一 |
| ItemsSource / SelectedValue のBinding | Active / Active | Active / Active |
| SelectedValue | Unreviewed | Unreviewed |
| 選択肢の表示名 | 未確認 | 未確認 |
| SelectionContent | 未生成 | 生成済み |
| 実際の描画TextBlock | 未生成 | 「未確認」 |

未表示のときも、対象ComboBoxは名前から取得でき、論理ツリーにも存在した。しかし描画ツリーの親はOcrPropertyContentで途切れ、ウィンドウのルートからたどれなかった。

このため、検索結果0件は「ステータスの選択肢がない」「バインドが壊れた」という意味ではなかった。

## 前回の追加実験が失敗した理由

前回は、部品を名前で直接取得し、ApplyTemplateを呼ぶ追加実験でも、次の検査に失敗していた。

`The closed status dropdown displays its localized name, not the option record's type name.`

今回、その内部状態も比較した。

| 実験 | 結果 |
| --- | --- |
| 元と同じ未表示条件 | 対象ComboBoxを描画ツリーから取得できず、11項目通過後に停止 |
| 名前で直接取得＋ApplyTemplate | SelectionContentは生成されたがサイズ0、表示文字の子要素が未生成。11項目通過後に停止 |
| 対象ComboBoxだけ明示的にMeasure / Arrange | 「未確認」の表示を確認でき、日本語・英語切替も成功。ただし別の未生成フィルター部品で15項目通過後に停止 |
| ウィンドウを画面外でShowし、通常のLoaded処理を通す | 元の診断69項目すべて成功 |

ApplyTemplateはテンプレートの生成を行うが、それだけで全データテンプレートの測定・描画が完了したことにはならなかった。失敗メッセージの「型名が表示されている」という想定も、実際に型名を描画したと観測したものではなく、期待するTextBlockを見つけられなかった際の説明文だった。

ComboBoxひとつだけを測定して先へ進めても、別の部品で同じ初期化不足に当たる。画面全体を通常と同じライフサイクルで用意する必要がある。

## 設定隔離について見つかった追加の不備

`App.xaml.cs:243` 前後のstateDiagnosticIndexには、校正診断のオプションが含まれていない。このため、校正診断だけはMainWindowのdiagnosticDirectoryに専用の保存先が渡らず、実行元アプリの設定・ワークスペースを読みうる。

今回の再現は新しい独立フォルダの既定設定でも発生しており、設定の混入は今回の直接原因ではない。ただし、保存された非表示・浮動ペインなどにより診断結果が変わりうるため、診断側で是正すべき点である。

校正診断の出力先へ `.appstate` を使うよう統合する際には、RunReviewModeTestAsyncの「出力ディレクトリが既に存在したら無条件に失敗する」判定も合わせる必要がある。MainWindowの構築時に作られた `.appstate` だけを許し、ほかの診断結果が存在する保存先は引き続き拒否する。

## 修正方針とコピー内での裏付け

実ソースに反映する場合の対象は、主に診断2ファイルである。

1. App.ReviewDiagnostics.csで、他の画面診断と同様に画面外・非アクティブ・タスクバー非表示でWindow.Showを行い、Dispatcherの初期処理を待つ。
2. App.xaml.csで、校正診断にも専用のdiagnosticDirectoryを渡す。
3. 校正診断の出力先チェックを、初期作成された.appstateを受け入れる形式へ合わせる。
4. 元の69項目の判定は保つ。固定時間のDelayや、表示文字検査の削除で通す方法は採らない。

観測用ログや部品単体へのApplyTemplate / 強制測定を取り除いた、上記の最小限の診断準備だけでも、.NET 8で診断を再実行した。**独立した設定保存先で2回実行し、ともに69項目すべて成功、終了コード0**だった。最終結果は `minimal-results.json` に記録した。

検査対象には以下を含む。

- 対象の絞り込み、一覧とページ上の選択同期。
- 確認ステータスの日本語・英語表示と言語変更後の状態保持。
- 校正モードでの枠移動・回転・文字幅操作の禁止。
- 確認済みにして次へ、ページをまたぐ前後移動。
- 文字修正、Undo / Redo、保存・再読込。
- 取消、モード変更・文書変更中のナビゲーション停止。

今回の原因調査は.NET 8で実施した。診断修正を現行ソースに正式反映した結果や、修正後の.NET 10での再検証結果ではない。

## 証跡

ルート：

`C:\Users\katsu\Documents\Codex\PDF-Correctorium\outputs\review-mode-investigation-20261027-122028`

| 証跡 | 内容 |
| --- | --- |
| results.json | 未表示・直接取得・単体測定・表示初期化の比較 |
| runs/observe/result/before-locator.json | 失敗時にもモデルとBindingが正常である記録 |
| runs/direct/result/after-locator.json | ApplyTemplateだけではサイズ0・表示文字未生成 |
| runs/measured/result/after-locator.json | 単体測定後に「未確認」が描画される記録 |
| runs/shown/result/after-locator.json | Loaded済みの対象部品、Binding、描画文字 |
| runs/shown/result/checks.txt | 元の69項目成功 |
| runs/shown/result/review-first-target.png | 初回選択時の実画面描画。確認ステータス「未確認」を視認 |
| runs/shown/result/review-next-page.png | ページ移動後の実画面描画 |
| minimal-review-fixture.diff | 観測用処理を除いた診断準備の変更差分 |
| minimal-startup-isolation.diff | 校正診断の設定保存先を分離する起動時の変更差分 |
| minimal-results.json | 最小限の診断準備での反復確認 |
| original-fingerprint-before.txt | 実リポジトリの調査前指紋 |
| original-fingerprint-after.txt | 実リポジトリの調査後指紋 |

実験用コピーの識別はdev.248、対象TFMはnet8.0 / net8.0-windows7.0。正式配布を作成したものではない。現行のDirectory.Build.propsはdev.247のままとし、Gitのコミット・履歴変更も行っていない。

