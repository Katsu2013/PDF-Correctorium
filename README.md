# PDF Correctorium

[日本語](#japanese) | [English](#english)

<a id="japanese"></a>

## 概要（日本語）

PDF Correctoriumは、**OCRでPDFに付けられた透明テキストを確認・修正するWindowsアプリ**です。文字の読み取り間違いや位置のずれを修正できるほか、ページの追加・削除・並べ替えなどの簡単なページ編集、しおりの修正、文書情報の編集にも対応しています。

元のPDFを残したまま作業でき、編集内容を保存して後から再開したり、修正結果を別のPDFファイルとして出力したりできます。

### できること

- **文字の修正**：OCRの読み取り間違いを直し、文字を追加・削除できます。
- **位置・サイズの調整**：透明テキストの位置、大きさ、回転、文字ごとの間隔を調整できます。
- **縦書き・横書きの設定**：日本語の縦書きを含め、文字方向や読み順を編集できます。
- **文字領域の整理**：領域の分割・結合や、不要な領域の削除ができます。
- **検索・一括置換**：同じ読み取り間違いを探し、まとめて修正できます。
- **校正・確認**：未確認・要再確認などの状態で対象を絞り込み、順番に確認できます。
- **ページの編集**：ページの追加・削除・並べ替え・回転ができ、元に戻す／やり直しにも対応します。
- **しおりの編集**：しおりの追加・修正・削除や、階層・順序の整理ができます。
- **文書情報の編集**：タイトル、作者、文書の言語、出力PDFのバージョンなどを変更できます。
- **作業の保存・PDF出力**：プロジェクトとして保存して編集を再開し、修正結果を別のPDFとして出力できます。

現在は開発版です。アプリ内で画像から新たにOCRを実行する機能など、未実装の項目もあります。詳しくは「現在の開発状況」と「残る制限」をご確認ください。

## 現在の開発状況

現在のリポジトリは、開発版`v1.0.0-dev.132`に対応しています。以下を実装しています。

- C# / .NET 8 / WPFによるソリューション構成
- 縦書き・横書きと、文字方向とは独立した回転に対応するOCR領域モデル
- 変更しない元のOCR値と、編集可能な重ね合わせ表示用の値の分離
- 確認状態とPDF出力用の属性
- 状態による絞り込み、ページをまたぐ対象移動、確認済みにして次へ進む操作、位置・サイズの直接編集を防ぐ専用の「校正・確認」モード
- OCR文字列、位置・サイズ、文字ごとの送り幅、読み順、確認状態、検索・置換、複数領域編集の「元に戻す／やり直し」
- ZIP互換の`.pdfocrproj`形式による安全なプロジェクト保存・読み込み・検証（展開件数・容量・圧縮率・埋込PDF整合性の上限検査を含む）
- SHA-256による元PDFの同一性確認
- ポータブル版とインストール版それぞれのデータ保存先の解決
- 構造化された診断ログの基盤
- PDFとプロジェクトを開くWPFアプリケーション画面
- 最近開いたPDF・プロジェクトの一覧から再読み込み、表示件数の設定、履歴クリア
- 再利用可能な別プロセスのPDFiumワーカーによるページ描画・文字抽出・文書情報読込と、スクロール可能なプレビュー
- ページ数表示、非同期サムネイル、前後ページへの移動、ページの挿入・削除・並べ替え、90度単位の回転、および各ページ構成操作のUndo/Redo。作業PDFは履歴の寿命に合わせて回収
- プロジェクト外部のPDFと、プロジェクトに埋め込まれたPDFのプレビュー
- PDFの文字オブジェクトから抽出したOCR文字列の半透明表示（不可視描画モードや透明度ゼロの文字を含む）
- NDLOCR-Liteの関連ファイル（JSON、XML、TXT、TEI）の自動検出
- NDLOCR-LiteのJSON・XMLからの座標付きOCR領域の取り込みと、手動取り込み
- OCR領域の選択と、文字列・位置・サイズ・回転・文字方向・確認状態・分割／結合・ロック・読み順の編集
- マウスによる移動、8方向のサイズ変更ハンドル、回転操作、整列、文字ごとの送り幅の編集
- 25～400%の表示倍率、幅・高さ・ページ全体・選択範囲に合わせる表示、ツールバー操作、Ctrl＋マウスホイール
- OCR文字列の検索・置換、繰り返し領域への変更反映、文書全体のOCR品質分析
- PDFのしおり、文書情報（タイトル・作者・件名・キーワード・作成アプリ・PDF変換ツール）、文書言語、出力PDFバージョン、PDFを開いたときの表示設定の編集
- 元PDFとは別のPDFへの安全な出力、出力後の検証、検証後の保存確定
- プロジェクトの自動保存、世代別バックアップ、バックアップからの復元、ページサムネイルのキャッシュ
- 「設定 → 表示 → 表示言語」で切り替えられる日本語・英語の画面表示
- ポータブル版・インストール版の両方で、表示言語の選択を次回起動時まで保持
- パネル、サムネイルサイズ、OCRの重ね合わせ表示、編集ハンドル、ショートカット、自動保存、バックアップ保持数を設定できるコンパクトな作業画面
- 外部のテストフレームワークに依存しない契約テスト実行機能

実装済みの範囲は、古い設計資料に記載された初期の基盤段階より広がっています。Version 1.0に向けた未実装項目や既知の不具合は、[実装状況](IMPLEMENTATION_STATUS.md)と設計資料内の実装状況欄で管理しています。上記の機能一覧は、読み込み・保存・再出力時の完全な情報保持や、Version 1.0の全要件の達成を保証するものではありません。

## 安全性の修正と残る制限（dev.132）

dev.132では、文書情報辞書を持たないPDFへしおりと文書情報を同時追加した際に、両者の内部オブジェクト番号が衝突する不具合を修正しました。

dev.131では、qpdfを12.4.1、PDFiumを154.0.8035へ更新しました。すべてのqpdf経路とPDF出力ワーカーに期限・標準出力量上限・プロセスツリー終了を適用し、PDFiumワーカーを含む外部PDF処理はWindowsジョブでプロセス数とメモリ量を制限します。OCR付随ファイル、しおり交換ファイル、ワーカーのJSON・PNG・通信行、圧縮済みプロジェクトにも上限を設け、XMLのDTDと許可外一時出力先を拒否します。ネイティブ依存物は`DEPENDENCIES.lock.json`のSHA-256と照合し、配布記録にも含めます。dev.130で追加したZIP展開上限、危険なエントリ名・重複・内包PDF整合性検証、一時PDF回収、別プロセス化も引き続き有効です。

Windowsジョブは同一利用者権限で動く処理の停止・資源制限であり、AppContainerや権限縮小を行う完全なOSサンドボックスではありません。悪意ある入力に対する敵対的コーパス／ファジング、資源使用量の画面表示、修復・救出UIは引き続き未実装です。

dev.122の監査で再現した5件を修正しました。文書を切り替える際に保存・破棄・キャンセルを確認し、読み込み失敗時は現在の文書を保持します。意図的に空にした文字列をプロジェクトの保存・再読み込みとPDF出力で保持します。親領域・フィット・出力関連の属性を保持し、校正・確認モードでの文字幅補正を制限し、一括置換した領域を要再確認にします。

自動保存は設定した間隔、または約30秒間入力がない場合に実行します（5秒ごとに判定）。一度も保存していないプロジェクトは、元PDFを埋め込んだ復旧用ファイルを`workspaces/recovery/<project-id>.autosave.pdfocrproj`に保存します。復旧するには、このファイルを明示的に開いてください。起動時に復旧データを自動検出する機能は未実装です。復旧用ファイルへの保存だけでは、プロジェクトを保存済み扱いにはしません。

**プロジェクトの互換性：** 新しく保存する形式は1.1です。dev.123は形式1.0と1.1を読み込めます。dev.123より前のアプリでは新しい形式1.1のファイルを開けません。旧版との互換性が必要な場合は、元のバックアップを保持してください。プロジェクト内の管理情報には、保存に使用したアプリのビルドバージョンも記録します。

外部サービス連携／アプリ内でのOCR実行、ルビ・コメント・タグ・差分、階層別の進捗、修復・救出画面、パネルのドッキングなど、Version 1.0の要件には未実装のものがあります。[残る実装項目](IMPLEMENTATION_STATUS.md#remaining-version-10-gaps)を参照してください。この改訂で残りの全機能が完成したわけではありません。

## 最近開いたファイル

「ファイル → 最近開いたファイル」から、PDFやプロジェクトを新しい順の一覧から開けます。「編集 → 設定 → 管理」で表示件数（既定10件、0～30件）と履歴クリアを設定できます。0件では表示・記録を停止し、履歴クリアは「保存」で確定します。PDFやプロジェクト自体は削除しません。履歴は設定の書き出しに含めません。詳しくは[操作ガイド](RECENT-FILES.md)を参照してください。

## 設定の持ち運び・配置プリセット

「編集 → 設定 → 管理」から、設定とショートカットのJSON書き出し・取り込み、パネル幅・表示状態の名前付きプリセットを利用できます。最大20件のプリセットを登録・更新・削除できます。取り込みや配置の適用は「保存」で確定し、「キャンセル」で破棄します。PDFの編集内容や手動倍率は変更しません。詳しくは[操作ガイド](SETTINGS-WORKSPACES.md)を参照してください。

## 表示言語

画面表示は日本語（`ja-JP`）と英語（`en-US`）に対応しています。設定画面の「表示」タブで変更できます。メイン画面、メニュー、ツールチップ、プロパティの項目名、ページ名、確認状態や文字方向の選択肢、主要なダイアログへ即座に反映され、次回起動時も選択した言語を使用します。

言語の切り替え対象はアプリの画面表示だけです。元PDF、取り込んだOCRデータ、コメント、しおり、プロジェクト内容の文字列を翻訳・書き換えすることはありません。

## キーボード操作

アクセスキーは連番ではなく、Open=O、Save=S、Save As=Aなど操作名に基づいて割り当てます。ファイルメニューの操作例はAlt+F → O（PDFを開く）です。設定画面の保存はAlt+Sで実行できます。

入力項目・ボタン・タブの末尾に表示する英数字はAltアクセスキーです。Tab／Shift+Tabで部品を移動し、メイン画面ではF6／Shift+F6で主要な領域を移動できます。OK・キャンセルにはアクセスキーを追加しません。設定で変更可能な8種類の編集ショートカットは、現在のキーまたは割り当てなしをツールチップへ表示します。詳しくは[キーボード操作ガイド](KEYBOARD-ACCESSIBILITY.md)を参照してください。

## 校正・確認モード

ツールバーのモード選択で「校正・確認」を選びます。右側の一覧に、現在のページで条件に合う領域を読み順で表示します。初期の絞り込み条件は「未確認・要再確認」で、未確認のみ・要再確認のみ・全状態も選択できます。削除した領域は確認対象に含めません。それ以外のOCR領域も、周囲の文脈を確認できるようプレビューに表示します。

「前の対象」「次の対象」は、ページ順、その中で読み順に対象を移動し、必要な場合だけ別のページを読み込みます。文書の端から反対側の端へは循環しません。「確認済みにして次へ」は、選択中の単一領域を確認済みにして次の対象へ移動します。文字列を直すとその領域は修正済みになり、絞り込み条件から外れても編集欄は開いたままです。確認済み・修正済み・対象外・保留の領域を再確認する場合は全状態を選んでください。対象の検索はキャンセルでき、モード・絞り込み条件・ページ・文書を変更した場合も、処理中の移動をキャンセルします。

文字列、単語の読み方、確認状態は編集できます。一方、位置の直接移動、サイズ変更、回転、整列、文字幅調整、領域の作成・削除・分割・結合は無効になります。既存の位置・サイズのロック設定は書き換えません。品質分析からの補正にも同じ制限を適用します。ただし、文字列の修正には通常の文字枠調整規則を使用するため、文字の追加・削除に必要なレイアウト変更は発生します。レイアウトを直接調整する場合はOCR編集モードへ戻してください。確認状態と修正した文字列は、空文字や領域属性を含め、プロジェクト保存と「元に戻す／やり直し」に対応しています。絞り込み条件と選択中のモードは一時的な画面状態であり、文書情報としては保存しません。

確認対象の一覧から選択した場合や前後の対象へ移動した場合は、対象が見える位置へプレビューをスクロールします。プレビュー上のOCR領域を直接クリックした場合は、スクロール位置を変えません。現在のページの対象件数は、文書全体や階層別の確認進捗を表すものではありません。これらの集計や、コメント・タグ・差分は未実装です。

## ビルド

### バージョン管理方針

アプリの版番号は`Directory.Build.props`だけで定義します。開発リビジョン132では、ソリューション全体の製品バージョンが`1.0.0-dev.132`、アセンブリ／ファイルバージョンが`1.0.0.132`になります。タイトルバー、バージョン情報、起動ログ、保存プロジェクト内の管理情報もこのビルドバージョンを使用します。必須の改訂・検証手順は[VERSIONING.md](VERSIONING.md)、今後の作業に適用するルールは[AGENTS.md](AGENTS.md)を参照してください。

変更したアプリのソースやビルドツールを配布する前に、`DevelopmentRevision`を増やします。ポータブル版の発行処理は、版番号の不一致、ローカルでのリビジョン巻き戻し、検証済みリビジョンを変更済みの入力で再利用する操作を拒否し、実際のEXE／DLLの版情報を検査して`build-info.json`を記録します。同一ソースの再検証ビルドではリビジョンを維持できますが、出力先は毎回新しい日時付きフォルダーにします。プロジェクトの保存形式は1.1、読み込みに必要な最小版はdev.123のままです。この変更時点の作業フォルダーではGit管理情報を利用できませんでした。ローカルのビルド記録はGit履歴の代わりにはなりません。

### 前提条件と実行手順

Version 1.0の互換性基準として実行対象を.NET 8に設定し、Visual Studio 2026 / .NET 10の開発ツールでビルドしています。

Windowsのファイル関連付けから`.pdf`や`.pdfocrproj`を開くことに対応しています。アプリはファイル引数を1つ受け取り、「ファイル」メニューと同じ読み込み処理で先頭ページを表示します。日本語名、空白を含む名前、相対パス、大文字の拡張子も扱えます。起動のたびに新しいウィンドウを開き、すでに起動中のアプリへ要求を転送することはありません。ファイルが存在しない場合や破損している場合は、読み込み成功と扱わずエラーを表示します。

ポータブル版には、`Icons/PdfDocument.ico`、`Icons/PdfCorrectoriumProject.ico`と、引用符付き起動コマンド・アイコンの割り当てを説明する[FILE-ASSOCIATIONS.md](FILE-ASSOCIATIONS.md)を同梱します。Windowsの関連付けや既定のアプリを自動変更することはありません。

`global.json`は.NET SDK `10.0.302`と`latestFeature`へのロールフォワードを指定しているため、インストール済みの.NET 10 SDK 10.0.400でもリポジトリ直下から通常のコマンドを実行できます。アプリの実行対象は.NET 8のままです。

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet-cli"
$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"
dotnet build PdfCorrectorium.sln
dotnet run --project tests/PdfCorrectorium.ContractTests
```

既存のビルドを上書きせず、新しい日時付きフォルダーへポータブル版を発行するには、以下を実行します。配布前のReleaseビルドと検証の必須手順は[VERSIONING.md](VERSIONING.md)を参照してください。

```powershell
.\tools\BuildPortable.ps1
```

出力先は`outputs/PdfCorrectorium-Builds`配下で、フォルダー名は`PdfCorrectorium-<version>-win-x64-<yyyyMMdd-HHmmss>`です。ソリューションのビルドで依存パッケージを復元済みの場合、オフラインでは`-NoRestore`を指定できます。

dev.122で発生していた起動スモークテストの設定不一致はdev.123で修正済みです。現在の検証結果は[テスト戦略](outputs/PdfCorrectorium-Documentation/docs/11_Test/11-01_TestStrategy.md)に記録し、過去の監査結果は履歴として残しています。

ステータスバーの表示倍率スライダーは、中央の目印を100%としています。左半分は25～100%、右半分は100～400%をそれぞれ線形に割り当てます。軸をクリックするとその位置へ直接移動します。矢印キーは1パーセントポイント、PageUp／PageDownは10パーセントポイントずつ変更し、ドロップダウンとツールバーの表示も同期します。

ステータスバーの倍率ボタンは枠なしで、マウスを重ねたときや押したときは背景で反応を示し、キーボード操作時はフォーカスを表示します。ツールバーは余白を詰め、ボタンの内側余白を2 DIP、外側余白を1 DIPとしています（DIPは表示倍率に依存しない画面上の単位です）。保存したサイズ設定とアイコン寸法を維持しつつ、メインボタンの外寸を4 DIP縮小し、最小24 DIPとします。区切り線やツールバー自体の余白も狭くしています。

文書の有無に応じた画面状態の専用テストは、ウィンドウを表示せずに実行します。PDFを開く前のメニュー・コマンドの無効化、ダイアログやショートカットからの操作制限、PDF／プロジェクト読み込み後の再有効化、読み込み失敗、ページ数・倍率の制限、単色のスライダー軸と中央100%の左右別スケール、スライダー・ツールバー・ドロップダウン・手入力による倍率表示の同期を確認します。毎回新しい出力フォルダーを指定してください。テストは2ページの検証用PDF、画面画像、`checks.txt`を作成し、失敗時はゼロ以外の終了コードを返します。

```powershell
$uiTestOutput = Join-Path $PWD ("outputs/.verification/document-ui-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--document-ui-test", ('"' + $uiTestOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

2026-08-30のこの専用テストでは136項目すべてに合格しました。倍率文字列のデータ連携の維持、25～400%の相互変換、中央位置と目印の一致、中央をまたぐキー操作、フラットボタンの状態、サイズ設定28・36・64におけるラベル表示あり／なしのコンパクトなツールバー寸法を含みます。これは従来の起動スモークテストとは別のテストであり、スモークテスト側の設定不一致はdev.123で修正しました。

ファイルからの起動に関する結合テストでは、新しいアプリのプロセスを13個起動し、先頭ページのプレビュー、プロジェクトデータ、埋め込み／外部の元PDF、エラー処理、入力ファイルが変更されないこと、同梱アイコンの解像度を確認します（67項目）。

```powershell
$launchTestOutput = Join-Path $PWD ("outputs/.verification/file-launch-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--file-launch-tests", ('"' + $launchTestOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

校正・確認モードの専用テストは、2ページのPDFとプロジェクトを自ら作成し、非表示のWPF画面を描画します。2026-08-30には、絞り込み、読み順、ページをまたぐ移動、確認操作、文字修正、元に戻す／やり直し、保存・再読み込み、キャンセル、位置・サイズ関連のコマンドと処理の制限、言語別のドロップダウン表示と選択状態について、69項目に合格しました。

```powershell
$reviewTestOutput = Join-Path $PWD ("outputs/.verification/review-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--review-mode-test", ('"' + $reviewTestOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

子プロセス用の`--startup-file-test <new-report-path>`は、ウィンドウと操作を止めるエラーダイアログの表示だけを抑え、読み込み後の文書状態を記録します。通常起動と同じファイル読み込み処理を呼び出します。これらのテストはWindowsのファイル関連付けを変更しません。

dev.123で修正する前の2026-08-30の監査では、文書UI 136項目、ファイル起動67項目、校正・確認69項目、契約13項目の合計285項目を再実行し、すべて合格しました。それでも別の人工データによる検査では、今回修正した5件の問題を再現し、従来の起動スモークテストも失敗していました。これらの件数は確認した経路を表すもので、全体の受け入れ完了を意味しません。検証範囲の不足と必要な回帰テストは[テスト戦略](outputs/PdfCorrectorium-Documentation/docs/11_Test/11-01_TestStrategy.md)を参照してください。

常設の`--persistence-test <new-output-directory>`では、文書切り替え・キャンセル・失敗時の保持、プロジェクト保存とPDF出力での空文字、読み込み済み／未表示領域の属性、校正モードの制限、一括処理後の状態、無操作時の自動保存、元PDFを埋め込んだ復旧データを追加検証します。他の診断モードと同様に、新しい出力フォルダーを指定して実行してください。

dev.132の`--page-history-test <new-output-directory>`では、生成したPDFを使ってページ追加・削除・並べ替え・90度回転をUndo/Redoし、作業PDF、OCRページ、選択状態、操作前のOCR履歴、Redo後のプロジェクト保存・再読込に加え、作業PDF回収、PDFiumワーカー分離・資源制限、外部処理の期限・出力量、OCR／しおり取込上限を29項目で検証します。

```powershell
$pageHistoryOutput = Join-Path $PWD ("outputs/.verification/page-history-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--page-history-test", ('"' + $pageHistoryOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

## ドキュメント

[設計資料の目次](outputs/PdfCorrectorium-Documentation/README.md)から、仕様の正本となるMarkdownと更新済みの5点の図版を参照できます。図版には校正・確認画面と文書プロパティ画面も含みます。今後の修復機能は未実装と明記しています。`PDF-Correctorium-Design-Documentation.pdf`は2026-08-09時点の内容のままで、再生成していません。

## ライセンス

Apache License 2.0です。第三者コンポーネントは`THIRD-PARTY-NOTICES.md`で管理しています。安定版の配布前に、ソフトウェア部品表（SBOM）を追加する予定です。

[Englishへ移動](#english) | [日本語の先頭へ戻る](#japanese)

---

<a id="english"></a>

## Overview (English)

PDF Correctorium is a **Windows application for reviewing and correcting invisible OCR text layers in PDFs**. Alongside fixing recognition errors and misplaced text, it supports basic page editing such as adding, deleting, and reordering pages, as well as editing bookmarks and document information.

You can work without changing the source PDF, save your edits to resume later, and export the corrected result as a separate PDF file.

### What you can do

- **Correct text**: Fix OCR recognition errors and add or remove characters.
- **Adjust position and size**: Change the position, size, rotation, and character spacing of invisible text.
- **Set writing direction**: Edit writing direction and reading order, including vertical Japanese text.
- **Organize text regions**: Split or merge regions and delete unnecessary ones.
- **Search and replace**: Find repeated recognition errors and correct them in bulk.
- **Proofread and review**: Filter regions by states such as unreviewed or needs-review and check them in sequence.
- **Edit pages**: Add, delete, reorder, and rotate pages.
- **Edit bookmarks**: Add, modify, and delete bookmarks, and organize their hierarchy and order.
- **Edit document information**: Change the title, author, document language, output PDF version, and other properties.
- **Save your work and export PDFs**: Save a project to resume editing later and export the corrected result as a separate PDF.

This is a development version. Some features, including running new OCR on images within the application, are not yet implemented. See "Current milestone" and "Safety fixes and remaining limitations" below for details.

## Current milestone

The current repository snapshot corresponds to the `v1.0.0-dev.132` development line. It includes:

- C# / .NET 8 / WPF solution structure
- Core OCR region model with vertical/horizontal writing and independent rotation
- Immutable original OCR values and editable overlay values
- Review states and output attributes
- Dedicated proofreading/review mode with status filters, cross-page target navigation, verify-and-next, and protection from direct geometry edits
- Undo/redo for OCR text, geometry, character advances, reading order, review state, search/replace, multi-region edits, and page insertion/deletion/reordering/rotation
- Safe ZIP-compatible `.pdfocrproj` save/open/validation with bounded entries, expansion, compression ratios, JSON, thumbnails, and embedded PDFs
- Source PDF SHA-256 fingerprinting
- Portable and installed data-path resolution
- Structured diagnostic log foundation
- WPF application shell for opening PDFs and projects
- Reusable out-of-process PDFium worker for page rendering, text extraction and document-property inspection, with a scrollable preview
- Page count, asynchronous thumbnail navigation, previous/next controls, page insertion/deletion/reordering, and 90-degree page rotation
- External and embedded project source-PDF preview support
- Semi-transparent OCR text overlays extracted from PDF text objects, including invisible render mode and zero-alpha text
- Automatic NDLOCR-Lite companion discovery for JSON, XML, TXT, and TEI files
- Coordinate-based overlay import from NDLOCR-Lite JSON and XML, with a manual import fallback
- Selectable OCR regions with text, position, size, rotation, writing-direction, review-state, split/merge, lock, and reading-order editing
- Mouse movement, eight-direction resize handles, rotation controls, alignment, and character-level advance editing
- 25-400% zoom, fit-width, fit-height, fit-page, fit-selection, toolbar controls, and Ctrl+mouse-wheel
- OCR search/replace, repeated-region propagation, and whole-document OCR quality analysis
- Editable PDF bookmarks, document metadata (title, author, subject, keywords, creator, and producer), document language, output PDF version, and viewer preferences
- Safe isolated export to a separately saved PDF, followed by validation and output commit
- Project autosave, versioned backups, backup restoration, and cached page thumbnails
- Japanese and English UI, switchable from `Settings > Display > Display language`
- Persistent UI-language selection for both portable and installed operation
- Compact preview workspace with configurable panels, thumbnail size, overlay appearance, edit handles, shortcuts, autosave, and backup retention
- Dependency-free contract test runner

The implemented application is broader than the original foundation milestone described in older design snapshots. Remaining Version 1.0 gaps and known defects are tracked in [Implementation status](IMPLEMENTATION_STATUS.md) and in the implementation-status sections of the design documentation. Features listed above are not a guarantee of complete round-trip preservation or of meeting every Version 1.0 requirement.

## Safety fixes and remaining limitations (dev.132)

Dev.132 fixes an object-number collision when adding both bookmarks and a new document-information dictionary to a PDF that had no existing `/Info` dictionary.

Dev.131 updates qpdf to 12.4.1 and PDFium to 154.0.8035. Every qpdf path and the PDF-export worker now has a deadline, bounded captured output and whole-process-tree termination. Windows Job Objects cap process count and memory for external PDF processing, including the reusable PDFium worker. NDLOCR companions, bookmark exchange files, worker JSON/PNG/protocol lines and the compressed project archive are bounded; XML DTDs and worker output paths outside the designated temporary root are rejected. `DEPENDENCIES.lock.json` pins native-file SHA-256 values and publication records include the verified native files. Dev.130's ZIP expansion limits, unsafe/duplicate-name rejection, embedded-PDF integrity checks, temporary-file reclamation and process separation remain in force.

Job Objects provide termination and resource containment under the same user account; they are not AppContainer or reduced-privilege isolation. Hostile-input corpora/fuzzing, resource telemetry and repair/rescue UI remain future work.

The five issues reproduced in the dev.122 audit are now addressed: document switching prompts to save/discard/cancel and preserves the current document on failed loads; intentional empty text survives project save/reload and PDF export; parent/fit/output metadata is retained; review-mode width correction is guarded; bulk replacements receive needs-review status.

Autosave runs at the configured interval or after about 30 seconds without input (checked every 5 seconds). Never-saved projects receive a source-embedded recovery package under `workspaces/recovery/<project-id>.autosave.pdfocrproj`. Open that file explicitly to recover; automatic recovery discovery at startup is not implemented. Recovery writes do not mark the project saved.

**Project compatibility:** new saves use format 1.1; versions 1.0 and 1.1 can be read by dev.123. Older application builds cannot open new 1.1 packages. Keep a backup when older-build compatibility is needed. The manifest now records the build version.

External/in-app OCR, ruby/comments/tags/diffs, hierarchical progress, repair/rescue UI, docking and other full Version 1.0 requirements remain unfinished. See [implementation status](IMPLEMENTATION_STATUS.md#remaining-version-10-gaps). This increment is not completion of every remaining feature.

## Recent files

Use File > Recent Files to reopen PDFs and projects in most-recently-opened order. Edit > Settings > Manage provides the display count (default 10, range 0–30) and Clear History. Zero stops displaying and recording history; clearing takes effect only on Settings Save, and never deletes documents. File paths are excluded from settings export. See the [usage guide (Japanese)](RECENT-FILES.md).

## Settings transfer and workspace presets

Use Edit > Settings > Manage to export/import settings and custom shortcuts as JSON, and register up to 20 named panel-layout presets. Import/apply updates the dialog draft; Save commits changes and Cancel discards them. Document edits and manual zoom are preserved. Storage information is now a collapsible section in Manage. See the [usage and format guide (Japanese)](SETTINGS-WORKSPACES.md).

## Display language

The application UI supports Japanese (`ja-JP`) and English (`en-US`). Change the language from the Display tab in the application settings window. The main window, menus, tooltips, property labels, page names, status choices, writing-direction choices, and principal dialogs update immediately; the selection is restored the next time the application starts.

Localization affects application chrome only. It never translates or rewrites text contained in the source PDF, imported OCR data, comments, bookmarks, or project content.

## Keyboard navigation

Mnemonics follow operation names, such as Open=O, Save=S and Save As=A, rather than alphabetical allocation. Menu mnemonics are scoped to their popup: use Alt+F, then O to open a PDF. Settings uses Alt+S for Save.

The letters or digits shown after input labels, actions, and tab captions are Alt access keys. Use Tab/Shift+Tab to move between controls and F6/Shift+F6 to move between main workspace panes. OK/Cancel do not receive new mnemonics. All eight customizable editing commands show their current shortcut or unassigned state in tooltips. See the [keyboard operation guide (Japanese)](KEYBOARD-ACCESSIBILITY.md) for details and limitations.

## Proofreading / review mode

Choose `校正・確認` in the toolbar mode selector. The right pane lists matching regions on the current page, in reading order. The default filter is `未確認・要再確認`; unreviewed-only, needs-review-only, and all-status filters are also available. Deleted regions are never review targets. Other OCR overlays remain visible for context.

`前の対象` / `次の対象` move through matching regions in page order, then reading order, loading additional pages only when necessary. They do not wrap at the document ends. `確認済みにして次へ` marks the single selected region verified and moves to the next target. A text correction marks the region modified; its editor remains open even if it no longer matches the filter. Choose all statuses to revisit verified, modified, excluded or deferred regions. Target search can be canceled, and changing the mode, filter, page or document cancels pending navigation.

Text, word readings, and review status remain editable. Ordinary direct movement, resize, rotation, alignment, character-width adjustment, region creation/deletion and split/merge commands are disabled in this mode; existing geometry-lock settings are not rewritten. The quality-analysis correction path is also guarded. Text corrections still use the normal character-cell reconciliation rules, including layout changes needed for inserting/removing text. Return to OCR editing for direct layout adjustments. Review states and corrected text support project save and Undo/Redo, including intentional empty text and preserved region metadata. Review filters and the selected mode are temporary UI state, not saved document metadata.

Selecting a review-list entry or using target navigation scrolls the preview to reveal the target. Ordinary selection by clicking an OCR region in the preview leaves the scroll position unchanged. The current-page target count is not a document-wide or hierarchical review-progress report; those aggregate reports, comments, tags and diffs remain unimplemented.

## Build

### Version policy

`Directory.Build.props` is the sole source of application version inputs. Development revision 132 produces product version `1.0.0-dev.132` and assembly/file version `1.0.0.132` across the solution. The title bar, About dialog, startup log and saved project manifest use the build version. [VERSIONING.md](VERSIONING.md) defines the mandatory revision-increment and verification rules; [AGENTS.md](AGENTS.md) applies them to future repository work.

Before delivering changed source/build tools, advance `DevelopmentRevision`. Portable publication rejects version mismatches, local revision rollback and changed inputs reusing a certified revision, checks the actual EXE/DLL metadata, and writes `build-info.json`. Same-source verification rebuilds may retain a revision but always use a new timestamped folder. The project data format remains 1.1; its minimum reader remains dev.123. Git metadata was unavailable in this working folder at the time of this change; local build records are not a substitute for Git history.

### Commands and prerequisites

The application targets .NET 8 for the Version 1.0 compatibility baseline and is built with the Visual Studio 2026 / .NET 10 toolchain.

Opening a `.pdf` or `.pdfocrproj` through a Windows file association is supported: the application consumes the single file argument and loads the first page, using the same loader as the File menu. Japanese names, spaces, relative paths, and uppercase extensions are supported. Each launch creates a new window; existing instances do not receive forwarded requests. Missing/corrupt files report an error without silently claiming a successful open.

The portable build includes `Icons/PdfDocument.ico`, `Icons/PdfCorrectoriumProject.ico`, and [FILE-ASSOCIATIONS.md](FILE-ASSOCIATIONS.md) with the quoted launch command and icon mapping. No Windows association or default application is changed automatically.

`global.json` specifies .NET SDK `10.0.302` with `latestFeature` roll-forward, allowing the normal repository-root commands to use installed .NET 10 SDK 10.0.400. The runtime target remains .NET 8.

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet-cli"
$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"
dotnet build PdfCorrectorium.sln
dotnet run --project tests/PdfCorrectorium.ContractTests
```

Create a portable build in a new timestamped folder without overwriting an earlier build:

```powershell
.\tools\BuildPortable.ps1
```

Builds are stored under `outputs/PdfCorrectorium-Builds` using the name
`PdfCorrectorium-<version>-win-x64-<yyyyMMdd-HHmmss>`. After a solution build has already restored dependencies, use `-NoRestore` when working offline.

The earlier dev.122 smoke-test settings mismatch is fixed in dev.123. Current verification is recorded in the [test strategy](outputs/PdfCorrectorium-Documentation/docs/11_Test/11-01_TestStrategy.md); the old audit results remain historical evidence.

The status-bar zoom slider places 100% at its center marker: the left half maps linearly from 25% to 100%, and the right half from 100% to 400%. Clicking the track moves directly to that position. Arrow keys still change zoom by 1 percentage point, and PageUp/PageDown by 10; the dropdown and toolbar stay synchronized.

Status-bar zoom buttons are frameless, with background-only hover/press feedback and a keyboard-focus indicator. Toolbar spacing is compact: button padding is 2 DIP and margins are 1 DIP. The saved size preference and icon dimensions are preserved; the main button's outer size is reduced by 4 DIP (minimum 24 DIP), with tighter separators and toolbar padding.

The focused document-availability UI test runs without displaying a window. It checks disabled menus/commands before opening a PDF, dialog/shortcut guards, re-enabling after PDF/project loading, failed loads, page/zoom limits, the single-color status-bar slider track and centered two-scale mapping, and zoom-display synchronization after slider/toolbar operations, dropdown selections, and manual input. Supply a new output folder; the test writes its own two-page PDF fixture, screenshots, and `checks.txt`, and returns a nonzero exit code on failure:

```powershell
$uiTestOutput = Join-Path $PWD ("outputs/.verification/document-ui-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--document-ui-test", ('"' + $uiTestOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

On 2026-08-30 this focused test passed all 136 checks, including preservation of the zoom-text binding, conversion round-trips across 25–400%, midpoint/marker alignment, keyboard steps across the midpoint, flat status-button states, and compact toolbar sizing with labels on/off at size preferences 28, 36, and 64. It is separate from the legacy smoke test, whose settings mismatch was corrected in dev.123.

File-launch integration tests start 13 fresh application processes and verify the first-page preview, project data, embedded/external sources, error handling, unchanged input files, and packaged icon resolutions (67 checks):

```powershell
$launchTestOutput = Join-Path $PWD ("outputs/.verification/file-launch-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--file-launch-tests", ('"' + $launchTestOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

The dedicated review test creates its own two-page PDF/project fixture and renders the hidden WPF view. On 2026-08-30 it passed 69 checks covering filters, reading order, cross-page navigation, confirmation, text correction, Undo/Redo, save/reload, cancellation, geometry command/handler guards, and localized dropdown labels/selections:

```powershell
$reviewTestOutput = Join-Path $PWD ("outputs/.verification/review-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--review-mode-test", ('"' + $reviewTestOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

The child-test option `--startup-file-test <new-report-path>` suppresses only windows/modal error dialogs and writes the resulting document state; it invokes the same startup file-opening path as normal launches. These tests do not modify Windows file associations.

Before the dev.123 fixes, the 2026-08-30 audit re-ran the 136 document-UI, 67 file-launch, 69 review-mode and 13 contract checks: all 285 passed. Separate synthetic-data probes nevertheless reproduced the five now-addressed audit problems, and the legacy smoke test still failed. These counts describe the tested paths, not complete acceptance. See the [test strategy](outputs/PdfCorrectorium-Documentation/docs/11_Test/11-01_TestStrategy.md) for coverage gaps and required regression cases.

The permanent `--persistence-test <new-output-directory>` additionally checks switching/cancel/failure preservation, empty OCR text through project and PDF output, loaded/unvisited region metadata, review restrictions, bulk status, idle autosave and embedded recovery. Run it like the other diagnostic modes, with a new output directory.

The dev.132 `--page-history-test <new-output-directory>` uses generated PDFs to exercise Undo/Redo for page insertion, deletion, reordering and 90-degree rotation. Its 29 checks also cover working-PDF reclamation, PDFium worker isolation/resource limits, external-process deadline/output caps, OCR/bookmark import caps, OCR history continuity, and project save/reload after redo.

```powershell
$pageHistoryOutput = Join-Path $PWD ("outputs/.verification/page-history-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Start-Process -FilePath ".\src\PdfCorrectorium.App\bin\Release\net8.0-windows7.0\PdfCorrectorium.exe" -ArgumentList @("--page-history-test", ('"' + $pageHistoryOutput + '"')) -WindowStyle Hidden -Wait -PassThru
```

## Documentation

The [design documentation index](outputs/PdfCorrectorium-Documentation/README.md) links the normative Markdown and five updated diagrams, including the new review and document-properties views. Future repair functionality is explicitly labeled unimplemented. `PDF-Correctorium-Design-Documentation.pdf` remains the 2026-08-09 snapshot and has not been regenerated.

## License

Apache License 2.0. Third-party components are tracked in `THIRD-PARTY-NOTICES.md`; an SBOM will be added before a stable distribution.
