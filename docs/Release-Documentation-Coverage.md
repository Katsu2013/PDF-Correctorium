# dev.199以降の機能・文書確認表

2026-10-30、現行ソースdev.274のコミット前確認。比較元は公開済みdev.199。以下の対応は実装と利用者向け説明の範囲を確認するためのもので、未公開の機能を公開済みと案内しない。

| 変更・実装の範囲 | 操作ガイド | 設計・判断の正本 |
|---|---|---|
| 表示ページ優先・背景のリンク確認・進捗 | [導入](../wiki/Getting-Started.md) | [アーキテクチャ](../outputs/PdfCorrectorium-Documentation/docs/04_Architecture/04-01_Architecture.md) |
| OCRの段落／行／文字、枠表示・透過度、画像調整 | [OCR編集](../wiki/Ocr-Editing.md) | [OCR仕様](../outputs/PdfCorrectorium-Documentation/docs/07_OCR/07-01_OcrSpecification.md) |
| 選択横書き行のRapidOCR・余白・手修正 | [再認識](../wiki/RapidOCR.md) | [行再認識](RapidOCR-Line-Recognition.md) |
| 密度外れ値・類似語・並べ替え・差分・部分修正 | [品質と辞書](../wiki/OCR-Quality-And-Lexicon.md) | [類似語](Similar-Word-Analysis.md) |
| 資料別OCR辞書・正解語・同文字数ルール | [品質と辞書](../wiki/OCR-Quality-And-Lexicon.md) | [共通辞書](Shared-OCR-Lexicon-Implementation-Plan.md) |
| 既存リンク・文字指定・整列・サイズ・ロック | [リンク](../wiki/Comments-Tags.md) | [画面仕様](../outputs/PdfCorrectorium-Documentation/docs/03_Screens/03-01_ScreenSpecification.md) |
| 自動ページリンク候補・文字／範囲一括墨消し | [墨消し](../wiki/Redaction-Guide.md) | [PDF仕様](../outputs/PdfCorrectorium-Documentation/docs/06_PDF/06-01_PdfEditing.md) |
| TXT・CSV作業データと仕上がりPNG／JPEG | [出力](../wiki/Data-Exports.md) | [処理と秘匿性](Local-Processing-And-Redaction-Scope.md) |
| ページ番号・文字ヘッダー／フッター・透かし | [ページ編集](../wiki/Page-Editing.md) | [PDF仕様](../outputs/PdfCorrectorium-Documentation/docs/06_PDF/06-01_PdfEditing.md) |
| テーマ・モード保存・項目表示・共通アイコン | [設定](../wiki/Settings-Workspaces.md) | [アイコン規約](../outputs/PdfCorrectorium-Documentation/docs/02_UIUX/02-02_IconDesignAndUsage.md) |
| 独立プロパティ・読み上げドック・左右別収納 | [画面](../wiki/Ui-Overview.md) | [収納仕様](Chapter-Grouping-And-Side-AutoHide.md) |
| Windows読み上げ・前後移動・0.5速度 | [読み上げ](../wiki/Read-Aloud.md) | [段落・音声](Speech-Navigation-And-Paragraphs.md) |
| 手動段落、共通読み方・行例外・確認用ルビ | [読み上げ](../wiki/Read-Aloud.md) | [共通読み方](Shared-Readings-And-Docked-Speech.md) |
| PDFの読み方保存、検索・コピーの原文維持 | [読み上げ](../wiki/Read-Aloud.md) | [相互運用](Pdf-Reading-Interoperability.md) |
| プロジェクト1.14／最小dev.262・設定21 | [保存](../wiki/Project-Files.md) | [データ形式](../outputs/PdfCorrectorium-Documentation/docs/08_ProjectFormat/08-01_ProjectFormat.md) |
| .NET 10・PDFium／qpdf・自己完結型ZIP | [導入](../wiki/Getting-Started.md) | [依存監査](../DEPENDENCIES.lock.json)、[運用](Distribution-Operations.md) |
| 上限・隔離・安全な確定・全件梱包・Git記録 | [FAQ](../wiki/FAQ.md) | [版規則](../VERSIONING.md)、[SECURITY](../SECURITY.md) |
| MSIX・App Installer・Store・署名の準備 | [変更と制約](../wiki/Release-Changes.md) | [配布設計](Distribution-Installer-Store-Update-Plan.md)、[署名](../CODE_SIGNING.md) |

## 確認対象と整理方法

README、Web紹介、Wikiの全ページ、ルートのロードマップ・設定・版規則・配布／署名／セキュリティ文書、設計書の全章とADR、設計PDF、dev.199からの累積リリース文書を対象にする。新旧仕様が混在した見出しは「当時」と区別し、過去の試験数・失敗・リビジョンは書き換えない。Markdownのローカル参照、Wiki参照、Web紹介のリンク、PDFの本文・図・ページ外へのはみ出しを確認する。

旧READMEの履歴と旧設計PDFを保持し、現行のREADMEを案内と章構成へ整理した。新しいワークスペース模式図を追加した。他の模式図は参考図として扱う。Wikiの原稿はこのリポジトリ内で更新し、GitHub Wikiへの別途反映・公開は実行していない。

## 実装済みと混同しないもの

- 選択行RapidOCRは対応済み。未OCR範囲・ページ／PDF全体の新規OCRと縦書き・混在の自動検出は未実装。
- 共通読み方と確認用ルビは対応済み。PDFに描く本来のルビや、任意の外部音声への完全な発音強制は未実装。
- 自己完結型ZIPはランタイムを同梱する。公開済みdev.199は.NET 8 Desktop Runtimeを別途必要とする。
- 直接MSIXの署名と実導入／更新、Store掲載、SignPathの申請・実署名、独立レビューは未完了。構成ファイルの存在をサービス稼働と扱わない。
- 文書処理は完全ローカル。利用者操作のWebリンクと更新の通信を許可する。処理上限を「無制限」と表現せず、墨消し範囲以外の秘密情報や編集プロジェクトまで除去したとは主張しない。

実行した検証と配布物は[今回のリリース文書](releases/v1.0.0-dev.274.md)に記録する。ローカル成功をGitHub Actions成功や署名・公開と読み替えない。
最終参照確認：文書191件、ローカル参照896件、Wiki参照41件。未解決の参照・置換文字（文字化け）検出は0件。Web紹介の重複ID・未解決ページ内リンクも0件。
