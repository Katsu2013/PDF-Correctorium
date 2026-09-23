# PDF Correctorium 開発ドキュメント

現行dev.174では、リボン左端の「ファイル」からQATを残した全画面Backstageへ切り替える。PDF／プロジェクト／OCRデータの読込、保存、別名保存、PDF出力、文書プロパティ、設定、バージョン情報、終了、最近開いたファイルを集約し、ホームからファイル操作を撤去した。戻る／Escで直前タブへ復帰し、Alt+F、F6／Shift+F6、日本語／英語、Automation名を診断する。

現行dev.173では、「ホーム」「編集・校正」「表示・検証」の3タブと最上部QATを拡大・整理した。「編集・校正」はOCR編集、読み順、校正・確認、墨消しの現在モードに必要な実在コマンドだけを表示し、「表示・検証」には表紙単独表示、綴じ方向、OCR品質、文書情報、検証、復旧を配置する。リボン／クラシックの選択は設定形式17へ保存し次回起動時に復元する。QATとタブにはAutomation名、Altアクセスキー、ツールチップ、明瞭なキーボードフォーカスを備え、F6/Shift+F6巡回を両UIで維持する。現行仕様は[リボンUI・UI切り替え仕様 (RIBBON-UI.md)](../../RIBBON-UI.md)、検討経緯と未決事項は[リボンUI検討履歴](../../ribbon_discussion_history.md)を参照する。

現行dev.169では、矩形とOCR領域に加えて、元PDFの可視／不可視文字、多角形、フリーハンドから墨消しを指定できる。通常PDFの文字選択は、文字中心で対象を確定し、元の`Tj`／`TJ`命令から選択符号だけを除去する。出力矩形は対象文字のPDF実座標から再計算し、隣文字を覆わず、元の画像・図形・フォント・範囲外テキスト命令を保持する。1文字ずつ選択しても行全体を基準に同じ高さへそろえ、同一行で重なる帯と同色で隣接する帯は1本へ統合する。Iビームで横方向だけにドラッグしても行を選択でき、選択中は細いマーカー帯を表示して確定後に自動選択解除する。安全に処理できない場合は、全面画像化せず出力を中止する。新規保存はプロジェクト形式1.6で、現行版は形式1.0～1.6を読み込める。

本書群は、PDF Correctorium Version 1.0 の実装・レビュー・OSS公開に用いる設計基準である。

現行dev.161では、墨消しページを元の表示内容から直接300 DPI・JPEG品質99で画像化し、墨消し範囲外の不可視OCR文字を検索・コピー用レイヤーとして保持する。部分的に重なる文字行は行全体を落とさず、安全余白と交差した文字だけを除去して左右の文字列断片を再構成する。出力後は文字境界単位で範囲内に抽出可能な文字がないことを検証する。画像最適化では画素判定を一度の走査へ統合して並列化し、長文書の中間確定回数もメモリ上限を保ったまま削減する。[非機能要求](docs/01_Requirements/01-02_NonFunctionalAndPlatform.md)、[PDF編集仕様](docs/06_PDF/06-01_PdfEditing.md)、[最新検証](docs/11_Test/11-01_TestStrategy.md)を参照する。

dev.141ではOCR文字描画を領域単位へ集約し、文字セルの再計算を抑制した。領域サイズ変更ハンドルも選択中だけ生成する。[描画方式と検証範囲](../../OCR-RENDERING.md)を参照する。

dev.140では、読み順番号を選択枠やサイズ変更ハンドルに隠されない独立した最前面レイヤーへ表示する。番号は不透明な背景と白い輪郭を持ち、倍率にかかわらず判読できる。dev.139の「ポータブルモード／通常モード」表記と、dev.138でアプリデータ保存モードを設定画面だけに分離した仕様も維持している。内部の`Embedded`／`Relative`値とプロジェクト形式1.3は変更しない。

dev.129ではページ追加・削除・並べ替え・90度回転をOCR編集と共通のUndo/Redo履歴へ統合した。dev.128の[最近開いたファイル](../../RECENT-FILES.md)、dev.127の設定JSON移出入と固定パネルの名前付きプリセットも維持している。[設定操作・保存形式ガイド](../../SETTINGS-WORKSPACES.md)、[画面設計](docs/03_Screens/03-01_ScreenSpecification.md)、[最新の検証記録](docs/11_Test/11-01_TestStrategy.md)を参照。dev.139ではメイン画面、文書プロパティ、保存設定の模式図を新しいモード名へ更新し、dev.140では読み順番号の重なり順をMarkdown仕様へ追記した。設定画面等の後続変更は旧設計PDFには含まれない。

dev.124では版番号を共通ビルド設定へ一元化し、数値版・画面表示・保存情報・配布名を揃えた。[版管理ルール](../../VERSIONING.md)に今後の更新・検証手順を定めている。以下のdev.122/123の記録は、各更新時点の履歴である。

2026-08-30の現行ソース（dev.123へ更新する前の配布物dev.122）との整合性監査を反映し、その後の改訂状況を各文書へ追記している。要求文はVersion 1.0の目標を表し、実装済みであることを意味しない。各文書の「現行実装」または「実装状況」を実装判定の根拠とし、全体の既知問題はリポジトリ直下の[実装状況](../../IMPLEMENTATION_STATUS.md)で管理する。

Markdownファイルを正本とする。`PDF-Correctorium-Design-Documentation.pdf`は2026-09-15にdev.163のMarkdownと12点のSVG図版から再生成し、代表ページと全ページの描画成立を確認した発行スナップショットである。再生成にはリポジトリの`tools/BuildDocumentationPdf.py`を用いる。

## 2026-08-30の反映内容

- 機能要求・UX・画面設計: 校正・確認モードの対象一覧、状態絞り込み、ページをまたぐ移動、確認済みにして次へ、選択時のスクロールと操作制限。階層別レビュー集計等は未実装として区別した。
- 画面設計: 文書プロパティ、共通タブ・入力部品、しおり一覧、文書未読込時の無効化、同期する倍率表示、中央100%の2段階スライダー、フラットボタンとツールバー余白。
- テスト戦略: 契約13件、画面136件、ファイル起動67件、校正69件の実績と実行方法、および未検出だった5件の回帰テスト課題。
- 運用・配布: ファイル引数からの起動、関連付け用アイコン、毎回別の日時付きフォルダーにビルドする運用と自動保存の制限。
- 既知問題: 文書切替時の未保存編集消失、空文字編集の復活、再保存時の領域属性欠落、校正モードの幅補正経路、一括置換後の状態不一致を未修正として記載した。

上記は本文追記時点の記録。その後dev.123で図版5件を整備し、上記5件を修正、操作停止後の自動保存と未保存プロジェクトの復旧用保存を追加した。現在の結果は[実装状況](../../IMPLEMENTATION_STATUS.md)と[テスト戦略](docs/11_Test/11-01_TestStrategy.md)を参照する。未実装の大規模機能は引き続き残件である。

新規保存はプロジェクト形式1.6となり、輪郭付き墨消しを理解しないdev.162以前では開けない。現行dev.169は旧形式1.0～1.5も読み込める。旧ビルドを使う場合はバックアップを保持する。

> OCR済みPDFの透明テキスト、配置、読み順、文字方向、回転、ルビおよび文書構造を、安全かつ効率よく編集できるプロジェクト管理型PDF編集ソフト。

## 文書の読み順

2026-09-01の追加要望4件は[追加機能の検討一覧](docs/13_Roadmap/13-02_FutureFeatureRequests.md)に集約した。FUT-001の墨消しはdev.152、FUT-002の確定範囲はdev.133、FUT-004の単一／見開きと切り替え／連続の4通り・表紙・左右綴じはdev.149までに実装済みで、FUT-003と各項目の残余範囲は未実装として区別する。

1. [プロジェクト概要](docs/00_Project/00-01_ProjectOverview.md)
2. [設計思想](docs/00_Project/00-02_DesignPhilosophy.md)
3. [スコープとロードマップ](docs/00_Project/00-03_ScopeAndRoadmap.md)
4. [機能要求](docs/01_Requirements/01-01_FunctionalRequirements.md)
5. [非機能・プラットフォーム要求](docs/01_Requirements/01-02_NonFunctionalAndPlatform.md)
6. [UX原則と操作フロー](docs/02_UIUX/02-01_UXAndWorkflows.md)
7. [画面設計](docs/03_Screens/03-01_ScreenSpecification.md)
8. [アーキテクチャ](docs/04_Architecture/04-01_Architecture.md)
9. [データモデル](docs/05_DataModel/05-01_DataModel.md)
10. [PDF編集仕様](docs/06_PDF/06-01_PdfEditing.md)
11. [OCR仕様](docs/07_OCR/07-01_OcrSpecification.md)
12. [`.pdfocrproj`仕様](docs/08_ProjectFormat/08-01_ProjectFormat.md)
13. [プラグイン仕様](docs/09_Plugins/09-01_PluginArchitecture.md)
14. [ログ・診断](docs/10_Logging/10-01_LoggingAndDiagnostics.md)
15. [テスト戦略](docs/11_Test/11-01_TestStrategy.md)
16. [運用・配布](docs/12_Operations/12-01_Operations.md)
17. [ロードマップ](docs/13_Roadmap/13-01_Roadmap.md)
18. [ADR索引](docs/90_ADR/README.md)
19. [用語集](docs/99_Glossary/99-01_Glossary.md)

## 要求の表記

- `MUST`: Version 1.0で必須。
- `SHOULD`: 原則実装。技術上の理由で外す場合はADRが必要。
- `MAY`: 任意または将来拡張。
- `TBD`: 利用者確認または技術検証が必要。

文書間で矛盾した場合は、承認済みADR、要求仕様、詳細設計、補足資料の順で優先する。

## 図版

dev.139ではメイン画面と校正画面のステータス表示を`PDF保存: ポータブルモード`へ変更し、文書プロパティ図と保存設定図にも「ポータブルモード／通常モード」を反映した。メイン画面の模式図には、入力PDF注意と文書注釈（コメント／タグ、ページリンク）も反映している。

2026-08-30に現行画面の模式図を更新した。スクリーンショットではなく、主要部品と操作の配置を説明する図である。未実装機能は現行画面へ混ぜず、将来案に明示する。本文の既知制限も併せて参照する。

- [メイン画面ワイヤーフレーム (v1.0.0-dev.165)](assets/svg/SCR-001_MainWindow_Wireframe-v1.0.0-dev165.svg) / [旧版](assets/svg/SCR-001_MainWindow_Wireframe.svg)
- [OCR編集モックアップ (v1.0.0-dev.165)](assets/svg/SCR-002_OcrEdit_Mockup-v1.0.0-dev165.svg) / [旧版](assets/svg/SCR-002_OcrEdit_Mockup.svg)
- [校正・確認モード (v1.0.0-dev.165)](assets/svg/SCR-009_ReviewMode_Wireframe-v1.0.0-dev165.svg) / [旧版](assets/svg/SCR-009_ReviewMode_Wireframe.svg)
- [文書プロパティ](assets/svg/SCR-010_DocumentProperties_Wireframe.svg)
- [dev.139 プロジェクト保存・注釈機能](assets/svg/SCR-011_ProjectAnnotations-dev133.svg)
- [dev.147 見開き表示の表紙・左右綴じ](assets/svg/SCR-012_FacingPageLayouts-dev147.svg)
- [プロジェクト診断・修復（将来案・未実装）](assets/svg/SCR-003_ProjectDiagnostics_Wireframe.svg)
- [墨消し画面と安全なPDF出力 (v1.0.0-dev.165)](assets/svg/SCR-013_Redaction-v1.0.0-dev165.svg) / [旧版 (dev.152)](assets/svg/SCR-013_Redaction-dev152.svg)
- [OCR編集／墨消しの排他モード切替（dev.153）](assets/svg/SCR-014_EditingModeSwitch-dev153.svg)
- [墨消し範囲の移動・サイズ変更（dev.154）](assets/svg/SCR-015_RedactionResize-dev154.svg)
- [墨消し範囲の色・表示・削除（dev.156）](assets/svg/SCR-016_RedactionAppearance-dev156.svg)
- [墨消しのスポイト・Delete・ホイール操作（dev.157）](assets/svg/SCR-017_RedactionEyedropperAndWheel-dev157.svg)

## 文書の状態

初版要求ベースラインに、dev.163までの実装差分・検証結果と残件を追記した状態。ライブラリの正確なバージョン、PDF適合範囲、未完了のUI・復旧・OCRプロバイダー境界は、実装とADRの更新に合わせて確定する。要求を現行の不具合に合わせて緩和したものではない。
