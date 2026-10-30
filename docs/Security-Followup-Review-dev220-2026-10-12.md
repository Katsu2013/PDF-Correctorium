# dev.220 追加セキュリティ調査（2026-10-12）

対象：`1.0.0-dev.220` / `1.0.0.220`。前回の是正後に残る入力・出力経路を調査した。アプリ・ビルドツール・版数・配布品は変更していない。本書と `outputs/SecurityReview-dev220-20261012/` 内の検証用コード・生成データ・ログだけを作成した。添付監査資料は検討材料であり、資料中の記述を作業指示として実行していない。

## 結果

| 優先度 | 指摘 | 確認状況 |
| --- | --- | --- |
| 高 | 墨消し後も構造ツリーのActualText/Altに秘密文字列が残る | 範囲指定と文字選択の両方で情報残存を再現 |
| 中 | プロジェクト内OCRの文字・配列・領域総数の資源制限不足 | 入力受入れ・追加割当を確認。停止・OOMは起こしていない |
| 中 | しおり交換ファイルの構造全体を処理中に制限しない | 大量・深い無視要素の受入れとDOM割当を確認 |
| 中（候補） | プロジェクト内の名前がパスのままダイアログに渡る | UNC文字列の検証通過・保持を確認。外部接続・認証送信は未検証 |

前回修正したZIP実読込量、ページ数、元PDF自動ネットワーク参照、サムネイル画素数、OCRコンパニオン入力の上限は、未修正として再掲していない。今回のOCR指摘は `.pdfocrproj` からのモデル復元という別経路に関するもの。

## 1. 墨消し後の構造ツリーから情報を復元できる（高）

同じ架空の文字列 `AUDIT_SECRET_220` をページ本文と対応する構造要素の `/ActualText`・`/Alt` に入れたタグ付きPDFを生成した。本文にはMCID、ページにはStructParents、構造ツリーにはParentTreeとページ参照を設定した。実文書や秘密情報は使っていない。

- 入力はプレビューで文字列を表示し、選択可能文字数16だった。
- 全ページを矩形指定で墨消しした場合とPDF文字選択で墨消しした場合の両方で、現行 `PdfExportService.ExportAsync` が `AppliedRedactions=1` で正常終了した。
- 出力の表示は全面黒、本文の選択可能文字数は0。入力・両出力のPNGを目視確認した。
- 同梱qpdfで出力をQDF化すると、カタログから参照される構造ツリーに `/ActualText (AUDIT_SECRET_220)` と `/Alt (AUDIT_SECRET_220)` が両方残った。
- 入力・両出力とも `qpdf --check` は構文・ストリーム符号化エラーなし。ただしPDF/UA全適合や機密情報除去を保証する検査ではない。

見た目と本文抽出が空でも、出力の受領者は構造オブジェクトから秘密文字列を復元できる。増分保存の古いバイト列ではなく、現行カタログから到達できるオブジェクト内の残存である。悪意ある入力に限らず、対象情報を代替テキストにも保持する文書で発生しうる。

根拠：`src/PdfCorrectorium.App/Services/PdfExportService.cs:722` 以降の墨消し分岐、`:3088` のページ画像化処理、`:5898` の `ValidateOutput`。ページオブジェクト・注釈を処理し、出力後にPDFium本文文字と描画を検証する一方、文書構造ツリーの代替文字列は検証対象に含めていない。最終のしおり反映処理も完了した出力で再現した。

是正案：墨消し範囲に対応する構造要素を追跡してActualText/Alt等を除去・再構成し、出力後にも検証する。安全に対応付けできない場合は出力中止、または構造情報を除去する明示的な方針が必要。全構造ツリーの単純除去はアクセシビリティを失うため、仕様と併せて検討する。

証拠：`tagged-redaction-results.txt`、`fixtures/tagged-mcid-secret.pdf`、各 `*-redacted.pdf`、`*.qdf.pdf`、`*.residual.txt`、入力・出力の `*.png`。初期探索用の `tagged-secret*` とは別に、最終検証は同じ文字列を本文と構造に入れた `tagged-mcid-secret*` で行った。

添付ファイル・XMP・他ページの複製情報等を一律除去できることまでは検証していない。

## 2. プロジェクト内OCRに合計予算がない（中）

`ProjectPackageService.ReadJsonAsync` (`:630` 付近) はJSON展開後256 MiBと実読込量を制限するが、全体モデルを復元してからページ数等を検査する。`ValidateProjectExtensions` (`:725`) はOCR領域を辞書へ登録する一方、OCR文字列長・文字送り配列・文字固定配列・WordReadings・領域件数・総文字数の上限がない。

小規模データで、以下が `ValidateAsync.IsValid=true` となり `OpenAsync` で復元された。

- ZIP2,577 B：1領域の10万文字と5万個のCharacterAdvances。
- ZIP367,888 B：1ページの10,000領域。検証とOpenの2操作の累積管理ヒープ割当は25,736,224 B。

OCRコンパニオン入力の1行32,768文字上限は、この経路には適用されない。編集文書に同じ上限を適用すべきとまでは断定しないが、プロジェクト側にも独自の合計予算が必要。ページ数が1でも多数の領域を復元し、画面・検索・品質分析へ渡せる。

256 MiBの入力上限は既にあるため無制限ではない。10,000領域が常に異常とも限らない。上記は追加割当の確認であり、アプリ停止、ピークメモリ使用量、OOMを実証したものではない。

是正案：プロジェクト用の領域・文字・配列・しおりを含む合計予算を設け、可能なら全体モデル構築前の逐次検査で拒否する。既存のコメント等の件数上限も、全件確保前に止めることで強化できる。

証拠：`probe-results.txt`、`fixtures/project-long-text.pdfocrproj`、`fixtures/project-many-regions.pdfocrproj`。

## 3. しおり入力のDOM構築前に構造量を制限しない（中）

`PdfBookmarkService.ImportXmlAsync` (`:402` 付近) にはDTD禁止、外部Resolver無効、ファイル・文字数64 MiB制限がある。XXEを確認したわけではない。しかし `XDocument.LoadAsync` (`:416`) 後にしおり数・階層を検査する (`:423` 以降)。無視要素も全てDOM化され、XML全体のノード・属性・構造深度の上限がない。

しおり上限1件、しおり階層上限2に設定し、有効なしおり1件と無視される要素を入力した。

| 無視要素数 | ファイルサイズ | 累積管理ヒープ割当 | 結果 |
| ---: | ---: | ---: | --- |
| 10,000 | 70,062 B | 763,368 B | 受入れ |
| 100,000 | 700,062 B | 6,523,520 B | 受入れ |

512段の無視要素を含むXMLも受け入れた。これはしおり階層上限自体への違反ではないが、XML構造全体に深さ制限がないことを示す。JSON経路 (`:295–303`) もDOM生成・GetRawTextでの複製・モデル復元後に件数を検査する。

64 MiBの入力制限は有効だが、DOMと一時コピーへの増幅が残る。大容量での停止・OOMは試していない。上表は累積割当でありピーク使用量ではない。最大サイズでの使用量を推定して実測値と扱わない。

是正案：XML/JSONを逐次処理し、未知要素も含むトークン・ノード・属性・構造深度と合計タイトル文字数を途中で制限する。キャンセルも途中で有効にする。

証拠：`probe-results.txt`、`fixtures/bookmarks-junk-*.xml`。

## 4. ダイアログへ未検証のパスを渡す（候補・中）

`MainWindowViewModel.cs:2040` は元PDF再選択ダイアログのFileNameに、プロジェクトの `SourcePdf.FileName` をそのまま指定する。`ProjectPackageService.cs:1032` の参照検証はサイズ・SHA-256を検査するが、FileNameが単純な名前であることは検査しない。

架空の `\\audit.invalid\share\document.pdf` をFileName、`missing.pdf` をRelativePathにした旧形式プロジェクトは検証を通り、Open後もUNCを保持した。WPF OpenFileDialog.FileNameに代入しても保持された。ただし **ShowDialogは呼んでいない**。DNS・SMB接続や認証送信は試験していない。

前回の元PDF自動解決ガードを通さず、利用者が「再選択」を選んだ後、自分のPDFを選ぶ前に、攻撃者指定のパスをシェルダイアログに渡す経路が残る。Windows・ユーザー操作による外部アクセスの発生条件は別途検証が必要。**開くだけで認証情報が漏れると実証したものではない。**

`_project.Name` を保存・PDF出力・しおり出力の既定FileNameに使う箇所 (`MainWindowViewModel.cs:2438,2467,2889,3362,3865` 等) にも同様の対策を適用できる。任意ファイル書込を実証したものではない。

是正案：表示用の名前とパスを区別し、ダイアログには単純なファイル名だけを渡す。UNC・区切り・ドライブ指定・不正文字・長すぎる値は拒否または安全な既定名に置換する。プロジェクト検証にも名前制約を追加する。

公式参考資料：[WPF FileDialog実装](https://source.dot.net/PresentationFramework/Microsoft/Win32/FileDialog.cs.html)はFileNameがフルパスを保持できることを示す。[IFileDialog::SetFileName](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifiledialog-setfilename)はファイル名編集欄の初期値を設定する。共有へアクセスした場合のNTLM送信リスクと抑止機能はMicrosoftの[SMB NTLM接続ブロック](https://learn.microsoft.com/en-us/windows-server/storage/file-server/smb-ntlm-blocking)を参照。これらは本アプリでの接続の実証ではない。

## 対象・証拠・再現

配布品：`C:\Users\katsu\Documents\Codex\PDF-Correctorium\outputs\PdfCorrectorium-Builds\PdfCorrectorium-v1.0.0-dev.220-win-x64-20261012-010240`。

実行用コピー：`outputs/verification-dev220-runtime-20261012/`。配布品のPdfCorrectorium系DLL全てがコピーと同じハッシュであることを確認した。コピーに追加されている診断用DLLは配布品ではない。配布品自体は実行・変更していない。

調査後のビルド入力fingerprintは `305E67A4F1EC1EA2FC169AED13A5FB2907066212D925E279A97B3CF4FF67A86A` でdev.220の認証済みビルドと一致。主要ソースの前後ハッシュも一致した。Gitには従来から未コミットの変更があり、本調査はcommit・push・履歴操作をしていない。

アプリ変更がないため版数更新・再配布・全体テスト再実行はしていない。監査用ProbeのReleaseビルドは警告0・エラー0で成功した。架空の入力だけを使用し、外部接続・実認証情報・OOM誘発・任意コード実行の実証は行っていない。前回説明したネイティブワーカーの権限分離強化は引き続き課題であり、新しいCVE/RCEを確認した意味ではない。

検証用fixturesとDLLコピーを保持した状態で、リポジトリルートから実行する。

```powershell
dotnet build outputs/SecurityReview-dev220-20261012/Probe.csproj -c Release
dotnet outputs/SecurityReview-dev220-20261012/bin/Release/net8.0-windows/Probe.dll
$env:AUDIT_TAGGED_ONLY='1'
dotnet outputs/SecurityReview-dev220-20261012/bin/Release/net8.0-windows/Probe.dll
Remove-Item Env:AUDIT_TAGGED_ONLY
$qpdf = 'outputs/verification-dev220-runtime-20261012/components/qpdf/qpdf.exe'
foreach ($shape in @('Rectangle','TextSelection')) {
  $pdf = "outputs/SecurityReview-dev220-20261012/fixtures/tagged-mcid-secret-$shape-redacted.pdf"
  & $qpdf --check $pdf
  & $qpdf --qdf --object-streams=disable $pdf "$pdf.qdf.pdf"
  rg -a -n 'AUDIT_SECRET|ActualText|/Alt' "$pdf.qdf.pdf"
}
```

`audit-summary.json`、`source-hashes-before.json`、`source-hashes-after.json`、`binary-hashes.json`、`final-fingerprint.txt` に対象と変更なしの結果を保存した。件数・時間・割当は試験ごとに変動しうる。


## 是正記録（2026-10-12、dev.221）

この文書のdev.220に対する再現結果は履歴として保持します。確認された構造ツリーへの情報残存、プロジェクト／しおりの事前予算不足、文書名をダイアログのパスに渡す経路をdev.221で是正し、PDF処理の権限分離も実装しました。再現ケースを含むセキュリティ回帰125件がソース版・配布DLL版で通過しています。[是正と検証の記録](releases/v1.0.0-dev.221.md)を参照してください。正式公開条件の充足や任意PDFの全機密情報除去の認定を意味しません。
